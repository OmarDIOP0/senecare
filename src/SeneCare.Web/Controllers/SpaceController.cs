using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using SeneCare.Application.Business;
using SeneCare.Application.Properties;
using SeneCare.Application.Requests;
using SeneCare.Application.Subscriptions;
using SeneCare.Application.Users;
using SeneCare.Infrastructure.Identity;
using SeneCare.Web.Infrastructure;
using SeneCare.Web.Localization;
using SeneCare.Web.ViewModels;

namespace SeneCare.Web.Controllers;

/// <summary>Home of the signed-in space (content per role) and guided-tour progress.</summary>
[Authorize]
public sealed class SpaceController(
    GetUserProfileHandler getProfile,
    TourProgressHandler tours,
    PropertyQueries properties,
    RequestQueries requests,
    SubscriptionQueries subscriptions,
    BusinessDashboardQuery businessDashboard,
    IStringLocalizer<SharedResource> localizer,
    TimeProvider clock) : Controller
{
    [HttpGet("/espace")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var userId = User.GetUserId()!.Value;
        var profile = await getProfile.HandleAsync(userId, cancellationToken);
        if (profile is null)
        {
            return Challenge();
        }

        var nav = AppNavigation.For(User);
        var runTour = !await tours.HasCompletedAsync(userId, nav.TourKey, cancellationToken);
        return View(new SpaceViewModel
        {
            FirstName = profile.FullName?.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault(),
            Space = nav.TourKey,
            AccountName = profile.Memberships.Count > 0 ? profile.Memberships[0].AccountName : null,
            Month = Formatting.ToDakar(clock.GetUtcNow()).Month,
            TourJson = runTour ? TourJson(nav.TourKey) : null,
            Client = nav.TourKey == TourKeys.Client ? await ClientHomeAsync(userId, cancellationToken) : null,
            Business = nav.TourKey == TourKeys.Business ? await businessDashboard.GetAsync(userId, cancellationToken) : null,
        });
    }

    /// <summary>Customer home: subscription, the request in progress with its timeline, neighborhood progress.</summary>
    private async Task<ClientHomeModel> ClientHomeAsync(Guid userId, CancellationToken cancellationToken)
    {
        var homes = await properties.ListAsync(userId, cancellationToken);
        var active = (await requests.ListAsync(userId, cancellationToken)).FirstOrDefault(r => r.IsOpen);
        var timeline = active is null
            ? []
            : RequestTimeline.Build(
                active.Status,
                (await requests.DetailAsync(userId, active.Id, cancellationToken))?.History ?? [],
                active.AppointmentAt,
                localizer);

        return new ClientHomeModel
        {
            PropertyCount = homes.Count,
            Subscription = (await subscriptions.ListAsync(userId, cancellationToken)) is { Count: > 0 } subs ? subs[0] : null,
            ActiveRequest = active,
            ActiveTimeline = timeline,
            Neighborhood = await subscriptions.NeighborhoodAsync(userId, cancellationToken),
        };
    }

    /// <summary>Called by the tour script when the user finishes or skips it.</summary>
    [HttpPost("/visite/{tourKey}/terminee")]
    public async Task<IActionResult> CompleteTour(string tourKey, CancellationToken cancellationToken)
    {
        var result = await tours.CompleteAsync(User.GetUserId()!.Value, tourKey, cancellationToken);
        return result.IsSuccess ? NoContent() : BadRequest();
    }

    /// <summary>« Revoir la visite guidée » (profile page).</summary>
    [HttpPost("/visite/rejouer")]
    public async Task<IActionResult> ReplayTour(CancellationToken cancellationToken)
    {
        await tours.ResetAsync(User.GetUserId()!.Value, AppNavigation.For(User).TourKey, cancellationToken);
        return LocalRedirect("/espace");
    }

    private string TourJson(string tourKey) => JsonSerializer.Serialize(new
    {
        key = tourKey,
        completeUrl = $"/visite/{tourKey}/terminee",
        labels = new
        {
            next = localizer["Tour_Next"].Value,
            done = localizer["Tour_Done"].Value,
            skip = localizer["Tour_Skip"].Value,
            progress = localizer["Tour_Progress"].Value,
        },
        steps = TourDefinitions.For(tourKey).Select(s => new
        {
            target = s.Target,
            title = localizer[s.TitleKey].Value,
            text = localizer[s.TextKey].Value,
        }),
    });
}

/// <summary>
/// Sections of the signed-in space that later phases will build. They already exist so that
/// navigation, guided tours and PWA shortcuts work end to end.
/// </summary>
[Authorize]
public sealed class SectionsController : Controller
{
    [HttpGet("/technicien/rapports")]
    [Authorize(Policy = Policies.Technician)]
    public IActionResult Reports() => Soon("rapports", "Tab_Reports", EmptyIllustration.Tools);

    [HttpGet("/admin/dispatch")]
    [Authorize(Policy = Policies.AdminDispatcher)]
    public IActionResult Dispatch() => Soon("dispatch", "Tab_Dispatch", EmptyIllustration.Tools);

    private ViewResult Soon(string navKey, string titleKey, EmptyIllustration illustration)
    {
        ViewData["NavActive"] = navKey;
        ViewData["TitleKey"] = titleKey;
        return View("Soon", illustration);
    }
}

/// <summary>Four illustrated welcome screens, shown on first launch; replayable from the profile.</summary>
[AllowAnonymous]
[Route("/bienvenue")]
public sealed class OnboardingController : Controller
{
    public const string CookieName = "sc_bienvenue";

    [HttpGet("")]
    public IActionResult Index(bool rejouer = false) => View(rejouer);

    /// <summary>« Commencer » or « Passer »: remember it on this device (functional cookie, 1 year).</summary>
    [HttpPost("")]
    public IActionResult Finish(bool rejouer = false)
    {
        Response.Cookies.Append(CookieName, "1", new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Lax,
            IsEssential = true,
            MaxAge = TimeSpan.FromDays(365),
        });
        if (rejouer)
        {
            return LocalRedirect("/compte");
        }

        return User.Identity?.IsAuthenticated == true ? LocalRedirect("/espace") : LocalRedirect("/connexion");
    }
}
