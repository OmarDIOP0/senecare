using Npgsql;

namespace SeneCare.Infrastructure.Persistence;

/// <summary>
/// Accepts both the Npgsql form (« Host=…;Database=… ») and the URL form given by hosts such as Render
/// (« postgresql://user:password@host:5432/database »), and always returns the Npgsql form.
/// </summary>
public static class PostgresConnectionString
{
    public static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)
            || !(value.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase) || value.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase)))
        {
            return value;
        }

        var uri = new Uri(value);
        var credentials = uri.UserInfo.Split(':', 2);
        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.IsDefaultPort || uri.Port <= 0 ? 5432 : uri.Port,
            Database = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/')),
            Username = Uri.UnescapeDataString(credentials[0]),
            Password = credentials.Length > 1 ? Uri.UnescapeDataString(credentials[1]) : null,
            // Internal network of the host: TLS when offered, plain otherwise.
            SslMode = SslMode.Prefer,
        };
        return builder.ConnectionString;
    }
}
