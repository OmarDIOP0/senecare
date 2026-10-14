using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SeneCare.Application.Locations;
using SeneCare.Infrastructure.Identity;
using SeneCare.Web.Infrastructure;

namespace SeneCare.Web.Controllers;

/// <summary>HTMX fragments for the location pickers, and « add my neighborhood » when it is missing.</summary>
public sealed class LocationsController(SearchNeighborhoodsHandler searchNeighborhoods, ListCommunesHandler communes) : Controller
{
    /// <summary>Results list for the neighborhood picker <paramref name="field"/>.</summary>
    [HttpGet("/quartiers/recherche")]
    [AllowAnonymous]
    [ResponseCache(Duration = 300, Location = ResponseCacheLocation.Any)]
    public async Task<IActionResult> Search(string? q, string field, CancellationToken cancellationToken)
    {
        ViewData["Field"] = field;
        var results = await searchNeighborhoods.HandleAsync(q, cancellationToken);
        if (results.Count == 0)
        {
            // Nothing found: the person may add it, attached to a commune of the list.
            ViewData["Communes"] = await communes.HandleAsync(cancellationToken);
        }

        return PartialView("Ui/_NeighborhoodResults", results);
    }

    /// <summary>Adds a neighborhood missing from the list; answers what the picker needs to select it.</summary>
    [HttpPost("/quartiers")]
    [Authorize(Policy = Policies.Authenticated)]
    public async Task<IActionResult> Suggest(Guid communeId, string? name, [FromServices] SuggestNeighborhoodHandler suggest, CancellationToken cancellationToken)
    {
        var result = await suggest.HandleAsync(new SuggestNeighborhoodCommand(User.GetUserId()!.Value, communeId, name), cancellationToken);
        return result.IsSuccess
            ? Json(new { id = result.Value.Id, label = $"{result.Value.Name} ({result.Value.Commune})" })
            : BadRequest(new { error = this.ErrorMessage(result.Error) });
    }
}
