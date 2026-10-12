using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using SeneCare.Application.Abstractions;

namespace SeneCare.Infrastructure.Storage;

public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    /// <summary>Folder for uploaded files. Default: App_Data/uploads under the app (never under wwwroot).</summary>
    public string? Root { get; set; }
}

/// <summary>
/// Files on the server disk, outside wwwroot: they are only served through an authorised endpoint.
/// Keys are random (no user-provided names) and sharded by month. Object storage can replace it later.
/// </summary>
internal sealed class LocalFileStorage : IFileStorage
{
    private readonly string _root;

    public LocalFileStorage(IOptions<StorageOptions> options, IHostEnvironment environment)
    {
        _root = Path.GetFullPath(options.Value.Root ?? Path.Combine(environment.ContentRootPath, "App_Data", "uploads"));
        Directory.CreateDirectory(_root);
    }

    public async Task<string> SaveAsync(Stream content, string extension, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (extension.Length is 0 or > 5 || !extension.All(char.IsAsciiLetterOrDigit))
        {
            throw new ArgumentException("Invalid extension.", nameof(extension));
        }

        var key = $"{DateTime.UtcNow:yyyy-MM}/{Guid.NewGuid():N}.{extension}";
        var path = PathOf(key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);
        await content.CopyToAsync(file, cancellationToken);
        return key;
    }

    public Task<Stream?> OpenReadAsync(string storageKey, CancellationToken cancellationToken = default)
    {
        var path = PathOf(storageKey);
        Stream? stream = File.Exists(path)
            ? new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true)
            : null;
        return Task.FromResult(stream);
    }

    /// <summary>Resolves a key inside the root; refuses anything that would escape it.</summary>
    private string PathOf(string key)
    {
        var path = Path.GetFullPath(Path.Combine(_root, key));
        return path.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            ? path
            : throw new ArgumentException("Invalid storage key.", nameof(key));
    }
}
