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
        // Type and message of each nested exception only: no stack trace, no file paths of the code.
        var lines = new List<string>();
        for (var current = exception; current is not null; current = current.InnerException)
        {
            lines.Add(current.GetType().FullName + ": " + current.Message);
        }

        var text = "SeneCare n'a pas pu démarrer (diagnostic du site de test).\n\n" + Mask(string.Join("\n", lines));
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
