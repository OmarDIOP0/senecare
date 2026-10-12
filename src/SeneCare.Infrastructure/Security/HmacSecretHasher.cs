using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using SeneCare.Application.Abstractions;

namespace SeneCare.Infrastructure.Security;

public sealed class SecurityOptions
{
    public const string SectionName = "Security";

    /// <summary>Base64 key (≥ 32 bytes) for hashing OTP codes and invitation tokens. Secret, never in the repository.</summary>
    public string? HashKey { get; set; }
}

/// <summary>
/// HMAC-SHA256 with a server key: deterministic (tokens can be looked up by hash) and useless
/// to an attacker who only steals the database.
/// </summary>
internal sealed class HmacSecretHasher : ISecretHasher
{
    private readonly byte[] _key;

    public HmacSecretHasher(IOptions<SecurityOptions> options)
    {
        var configured = options.Value.HashKey;
        _key = string.IsNullOrWhiteSpace(configured) ? [] : Convert.FromBase64String(configured);
        if (_key.Length < 32)
        {
            throw new InvalidOperationException("Security:HashKey must be a base64 key of at least 32 bytes (see README).");
        }
    }

    public string Hash(string secret)
    {
        ArgumentNullException.ThrowIfNull(secret);
        return Convert.ToBase64String(HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(secret)));
    }

    public bool Matches(string secret, string hash)
    {
        ArgumentNullException.ThrowIfNull(secret);
        ArgumentNullException.ThrowIfNull(hash);
        Span<byte> expected = stackalloc byte[32];
        if (!Convert.TryFromBase64String(hash, expected, out var written) || written != 32)
        {
            return false;
        }

        var actual = HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(secret));
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}
