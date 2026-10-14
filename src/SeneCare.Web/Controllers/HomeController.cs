using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SeneCare.Application.Public;
using SeneCare.Domain.Common;
using SeneCare.Web.ViewModels;

namespace SeneCare.Web.Controllers;

[AllowAnonymous]
public sealed class HomeController(GetPublicHomeHandler publicHome) : Controller
{
    /// <summary>
    /// Signed in → own space. First visit on this device → welcome screens. Otherwise the public home:
    /// offers, trades, steps, figures and advice, all read from the database.
    /// </summary>
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return LocalRedirect("/espace");
        }

        if (!Request.Cookies.ContainsKey(OnboardingController.CookieName))
        {
            return LocalRedirect("/bienvenue");
        }

        return View(await publicHome.HandleAsync(cancellationToken));
    }

    /// <summary>
    /// « Demander une intervention » (header and quick form): straight to the report form with the trade
    /// pre-selected; through the phone sign-in first when needed, with the number already typed.
    /// </summary>
    [HttpGet("/demander")]
    public IActionResult RequestJob(ServiceDomain? metier, string? telephone)
    {
        var report = metier is { } domain ? $"/signaler?domaine={domain}" : "/signaler";
        if (User.Identity?.IsAuthenticated == true)
        {
            return LocalRedirect(report);
        }

        var signIn = $"/connexion?returnUrl={Uri.EscapeDataString(report)}";
        if (!string.IsNullOrWhiteSpace(telephone))
        {
            signIn += $"&phone={Uri.EscapeDataString(telephone.Trim())}";
        }

        return LocalRedirect(signIn);
    }

    [HttpGet("/confidentialite")]
    public IActionResult Privacy() => View();

    [HttpGet("/acces-refuse")]
    public IActionResult AccessDenied() => View();

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error() =>
        View(new ErrorViewModel(Activity.Current?.Id ?? HttpContext.TraceIdentifier));
}
