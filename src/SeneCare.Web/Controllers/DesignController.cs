using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace SeneCare.Web.Controllers;

/// <summary>Living style guide of the design system. Development only (404 elsewhere).</summary>
[AllowAnonymous]
public sealed partial class DesignController(IWebHostEnvironment environment, ILogger<DesignController> logger) : Controller
{
    [HttpGet("/design")]
    public IActionResult Index() => environment.IsDevelopment() ? View() : NotFound();

    /// <summary>Target of the offline-queue demo form: proves a form saved offline arrives later.</summary>
    [HttpPost("/design/test-hors-ligne")]
    public IActionResult OfflineTest(string? message)
    {
        if (!environment.IsDevelopment())
        {
            return NotFound();
        }

        var replayed = Request.Headers.ContainsKey("X-SeneCare-Outbox");
        LogReceived(logger, message ?? string.Empty, replayed);
        TempData["flash.toast"] = $"Reçu : « {message} »" + (replayed ? " (envoyé depuis la file hors ligne)" : string.Empty);
        return LocalRedirect("/design");
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Offline test form received « {Message} » (replayed from outbox: {Replayed})")]
    private static partial void LogReceived(ILogger logger, string message, bool replayed);
}
