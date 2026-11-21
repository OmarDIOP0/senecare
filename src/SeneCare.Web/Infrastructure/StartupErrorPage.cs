using System.Text.RegularExpressions;

namespace SeneCare.Web.Infrastructure;

/// <summary>
/// Hosted test site only (Staging): when the app fails to start and the host gives no log (shared IIS hosting), a
/// bare page shows the reason instead of a blank « HTTP Error 500.30 ». Passwords are masked. Never in Production.
/// </summary>
internal static partial class StartupErrorPage
{
    public static bool IsEnabled() => string.Equals(
        Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT"), "Staging", StringComparison.OrdinalIgnoreCase);

    public static async Task RunAsync(string[] args, Exception exception)
    {
        var text = "SeneCare n'a pas pu démarrer (diagnostic du site de test).\n\n" + Describe(exception);
        TryWriteFile(text);

        var app = WebApplication.Create(args);
        app.Run(context =>
        {
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            context.Response.ContentType = "text/plain; charset=utf-8";
            return context.Response.WriteAsync(text);
        });
        await app.RunAsync();
    }

    /// <summary>
    /// Type and message of each nested exception, plus the first frame of our own code: no full stack trace,
    /// secrets masked. Also used by the error page of the test site (shared hosting: no readable log).
    /// </summary>
    public static string Describe(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        var lines = new List<string>();
        for (var current = exception; current is not null; current = current.InnerException)
        {
            lines.Add(current.GetType().FullName + ": " + current.Message);
        }

        var where = exception.StackTrace?.Split('\n').Select(line => line.Trim())
            .FirstOrDefault(line => line.Contains("SeneCare.", StringComparison.Ordinal));
        if (where is not null)
        {
            lines.Add(where.Split(" in ", 2)[0]);
        }

        return Mask(string.Join("\n", lines));
    }

    private static string Mask(string text) => SecretPattern().Replace(text, "$1=***");

    private static void TryWriteFile(string text)
    {
        try
        {
            var folder = Path.Combine(AppContext.BaseDirectory, "logs");
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "startup-error.txt"), text);
        }
        catch (IOException)
        {
            // No write access: the page still shows the reason.
        }
        catch (UnauthorizedAccessException)
        {
            // Same.
        }
    }

    [GeneratedRegex(@"(?i)(password|pwd|user ?id|uid|secret|token|api_?key|accountkey|signingkey|hashkey|key)\s*[=:]\s*(""[^""]*""|[^;\s,}]*)")]
    private static partial Regex SecretPattern();
}
