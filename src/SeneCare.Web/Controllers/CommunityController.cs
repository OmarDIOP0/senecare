using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SeneCare.Application.Neighborhoods;

namespace SeneCare.Web.Controllers;

/// <summary>
/// Public side of the Programme Quartiers: a page per neighborhood, made to be shared on WhatsApp
/// (Open Graph preview), the ranking, and the photos of published operations.
/// </summary>
[AllowAnonymous]
public sealed class CommunityController(NeighborhoodCommunity community) : Controller
{
    /// <summary>Cookie keeping the referral code from the shared link until the visitor registers.</summary>
    public const string ReferralCookie = "sc_parrain";

    [HttpGet("/quartier/{commune}/{quartier}")]
    public async Task<IActionResult> Neighborhood(string commune, string quartier, string? parrain, CancellationToken cancellationToken)
    {
        var page = await community.PageAsync(commune, quartier, cancellationToken);
        if (page is null)
        {
            return NotFound();
        }

        if (parrain is { Length: SeneCare.Domain.Users.User.ReferralCodeLength } && parrain.All(char.IsAsciiLetterOrDigit))
        {
            Response.Cookies.Append(ReferralCookie, parrain.ToUpperInvariant(), new CookieOptions
            {
                HttpOnly = true,
                Secure = Request.IsHttps,
                SameSite = SameSiteMode.Lax,
                MaxAge = TimeSpan.FromDays(30),
                IsEssential = true,
            });
        }

        return View(page);
    }

    [HttpGet("/classement-quartiers")]
    public async Task<IActionResult> Ranking(CancellationToken cancellationToken) => View(await community.RankingAsync(cancellationToken));

    [HttpGet("/quartier-photo/{id:guid}")]
    public async Task<IActionResult> Photo(Guid id, CancellationToken cancellationToken)
    {
        var photo = await community.PublicPhotoAsync(id, cancellationToken);
        if (photo is null)
        {
            return NotFound();
        }

        Response.Headers.CacheControl = "public, max-age=86400";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(photo.Content, photo.ContentType);
    }
}
