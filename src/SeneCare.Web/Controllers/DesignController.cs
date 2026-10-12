using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace SeneCare.Web.Controllers;

/// <summary>Living style guide of the design system. Development only (404 elsewhere).</summary>
[AllowAnonymous]
public sealed class DesignController(IWebHostEnvironment environment) : Controller
{
    [HttpGet("/design")]
    public IActionResult Index() => environment.IsDevelopment() ? View() : NotFound();
}
