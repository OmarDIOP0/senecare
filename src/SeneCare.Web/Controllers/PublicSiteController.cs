using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SeneCare.Application.Neighborhoods;
using SeneCare.Application.Public;
using SeneCare.Web.Infrastructure;

namespace SeneCare.Web.Controllers;

/// <summary>
/// Public pages of the service company website: trades, households, businesses, neighbourhoods, pricing,
/// FAQ and legal pages. Texts come from the resources and <c>Content/site-content.json</c>; prices and
/// response times from the database.
/// </summary>
[AllowAnonymous]
public sealed class PublicSiteController(SiteContent site, GetPublicHomeHandler publicHome, NeighborhoodCommunity community) : Controller
{
    [HttpGet("/metiers")]
    public IActionResult Trades() => View();

    [HttpGet("/metiers/{slug}")]
    public async Task<IActionResult> Trade(string slug, CancellationToken cancellationToken)
    {
        var trade = site.TradeBySlug(slug);
        if (trade is null)
        {
            return NotFound();
        }

        ViewData["Home"] = await publicHome.HandleAsync(cancellationToken);
        return View(trade);
    }

    [HttpGet("/particuliers")]
    public async Task<IActionResult> Households(CancellationToken cancellationToken) =>
        View(await publicHome.HandleAsync(cancellationToken));

    [HttpGet("/entreprises")]
    public async Task<IActionResult> Businesses(CancellationToken cancellationToken) =>
        View(await publicHome.HandleAsync(cancellationToken));

    [HttpGet("/quartiers")]
    public async Task<IActionResult> Neighborhoods(CancellationToken cancellationToken)
    {
        ViewData["Ranking"] = await community.RankingAsync(cancellationToken);
        return View(await publicHome.HandleAsync(cancellationToken));
    }

    [HttpGet("/tarifs")]
    public async Task<IActionResult> Pricing(CancellationToken cancellationToken) =>
        View(await publicHome.HandleAsync(cancellationToken));

    [HttpGet("/faq")]
    public IActionResult Faq() => View();

    [HttpGet("/mentions-legales")]
    public IActionResult Legal() => View();

    [HttpGet("/cgu")]
    public IActionResult Terms() => View();
}
