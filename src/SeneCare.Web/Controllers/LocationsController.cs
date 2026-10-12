using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SeneCare.Application.Locations;

namespace SeneCare.Web.Controllers;

/// <summary>HTMX fragments for the location pickers.</summary>
[AllowAnonymous]
public sealed class LocationsController(SearchNeighborhoodsHandler searchNeighborhoods) : Controller
{
    /// <summary>Results list for the neighborhood picker <paramref name="field"/>.</summary>
    [HttpGet("/quartiers/recherche")]
    [ResponseCache(Duration = 300, Location = ResponseCacheLocation.Any)]
    public async Task<IActionResult> Search(string? q, string field, CancellationToken cancellationToken)
    {
        ViewData["Field"] = field;
        return PartialView("Ui/_NeighborhoodResults", await searchNeighborhoods.HandleAsync(q, cancellationToken));
    }
}
