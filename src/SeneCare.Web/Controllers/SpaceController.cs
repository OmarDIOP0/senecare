using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using SeneCare.Application.Admin;
using SeneCare.Application.Business;
using SeneCare.Application.Neighborhoods;
using SeneCare.Application.Properties;
using SeneCare.Application.Requests;
using SeneCare.Application.Subscriptions;
using SeneCare.Application.Users;
using SeneCare.Domain.Billing;
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
    AdviceManagement advice,
    NeighborhoodCommunity community,
    ReferralService referrals,
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
        if (nav.TourKey == TourKeys.Technician)
        {
            return Redirect("/technicien");
        }

        if (nav.TourKey == TourKeys.Admin)
        {
            return Redirect("/admin");
        }

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
        var all = await requests.ListAsync(userId, cancellationToken);
        var active = all.FirstOrDefault(r => r.IsOpen);
        var timeline = active is null
            ? []
            : RequestTimeline.Build(
                active.Status,
                (await requests.DetailAsync(userId, active.Id, cancellationToken))?.History ?? [],
                active.AppointmentAt,
                localizer);

        var subs = await subscriptions.ListAsync(userId, cancellationToken);
        var due = subs
            .SelectMany(sub => sub.Invoices.Where(i => i.Status is InvoiceStatus.Issued or InvoiceStatus.Overdue).Select(i => (Sub: sub, Invoice: i)))
            .OrderBy(x => x.Invoice.DueDate)
            .FirstOrDefault();
        return new ClientHomeModel
        {
            PropertyCount = homes.Count,
            RequestCount = all.Count,
            Subscription = subs.Count > 0 ? subs[0] : null,
            Due = due.Invoice,
            DueStartsSubscription = due.Sub?.Status == SubscriptionStatus.PendingRegistration,
            ActiveRequest = active,
            ActiveTimeline = timeline,
            Neighborhood = await subscriptions.NeighborhoodAsync(userId, cancellationToken),
            Advice = await advice.ForMonthAsync(Formatting.ToDakar(clock.GetUtcNow()).Month, cancellationToken),
            Community = homes.Count > 0 ? await community.OfUserAsync(userId, cancellationToken) : null,
            Referral = homes.Count > 0 ? await referrals.StatsAsync(userId, cancellationToken) : null,
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

    private string TourJson(string tourKey) => BuildTourJson(tourKey, localizer);

    /// <summary>Guided tour configuration read by app.js (also used by the technician missions page).</summary>
    internal static string BuildTourJson(string tourKey, IStringLocalizer<SharedResource> localizer) => JsonSerializer.Serialize(new
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
