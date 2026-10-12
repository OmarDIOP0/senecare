using SeneCare.Application.Abstractions;
using SeneCare.Application.Common;
using SeneCare.Domain.Common;

namespace SeneCare.Application.Media;

/// <summary>A file received from a form or the API (the stream is read once).</summary>
public sealed record UploadedFile(Stream Content, long Length);

/// <summary>
/// Accepts only real images and voice notes, recognised by their first bytes (never by name or
/// declared type), within size limits. Images are compressed on the phone (≈300 KB); the server
/// limit is higher for phones without JavaScript.
/// </summary>
public sealed class MediaUploader(IApplicationDbContext db, IFileStorage storage, TimeProvider clock)
{
    public const long MaxImageBytes = 6 * 1024 * 1024;
    public const long MaxAudioBytes = 2 * 1024 * 1024;

    private sealed record Format(string ContentType, string Extension, bool IsAudio);

    /// <summary>Stores the file and adds its <see cref="MediaFile"/> (saved with the caller's SaveChanges).</summary>
    public async Task<Result<MediaFile>> StoreAsync(
        UploadedFile file, MediaKind kind, Guid userId, Guid? accountId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(file);
        if (file.Length <= 0)
        {
            return Result<MediaFile>.Failure("media.empty");
        }

        var header = new byte[16];
        var read = await file.Content.ReadAtLeastAsync(header, header.Length, throwOnEndOfStream: false, cancellationToken);
        var format = Detect(header.AsSpan(0, read));
        var wantsAudio = kind == MediaKind.VoiceNote;
        if (format is null || format.IsAudio != wantsAudio)
        {
            return Result<MediaFile>.Failure("media.type");
        }

        if (file.Length > (format.IsAudio ? MaxAudioBytes : MaxImageBytes))
        {
            return Result<MediaFile>.Failure("media.too_large");
        }

        // Re-assemble the header with the rest of the stream.
        using var content = new MemoryStream();
        await content.WriteAsync(header.AsMemory(0, read), cancellationToken);
        await file.Content.CopyToAsync(content, cancellationToken);
        content.Position = 0;

        var key = await storage.SaveAsync(content, format.Extension, cancellationToken);
        var media = new MediaFile(key, format.ContentType, content.Length, kind, userId, accountId, clock.GetUtcNow());
        db.MediaFiles.Add(media);
        return Result<MediaFile>.Success(media);
    }

    private static Format? Detect(ReadOnlySpan<byte> h)
    {
        if (h.Length >= 3 && h[0] == 0xFF && h[1] == 0xD8 && h[2] == 0xFF)
        {
            return new Format("image/jpeg", "jpg", false);
        }

        if (h.Length >= 8 && h[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }))
        {
            return new Format("image/png", "png", false);
        }

        if (h.Length >= 12 && h[..4].SequenceEqual("RIFF"u8) && h[8..12].SequenceEqual("WEBP"u8))
        {
            return new Format("image/webp", "webp", false);
        }

        if (h.Length >= 4 && h[0] == 0x1A && h[1] == 0x45 && h[2] == 0xDF && h[3] == 0xA3)
        {
            return new Format("audio/webm", "webm", true); // Android Chrome MediaRecorder
        }

        if (h.Length >= 4 && h[..4].SequenceEqual("OggS"u8))
        {
            return new Format("audio/ogg", "ogg", true);
        }

        if (h.Length >= 12 && h[4..8].SequenceEqual("ftyp"u8))
        {
            return new Format("audio/mp4", "m4a", true); // iPhone MediaRecorder
        }

        return null;
    }
}
