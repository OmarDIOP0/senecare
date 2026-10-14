using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SeneCare.Application.Public;
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

    [HttpGet("/confidentialite")]
    public IActionResult Privacy() => View();

    [HttpGet("/acces-refuse")]
    public IActionResult AccessDenied() => View();

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error() =>
        View(new ErrorViewModel(Activity.Current?.Id ?? HttpContext.TraceIdentifier));
}
