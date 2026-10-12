using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using SeneCare.Web.ViewModels;

namespace SeneCare.Web.Controllers;

public sealed class HomeController : Controller
{
    public IActionResult Index() => View();

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error() =>
        View(new ErrorViewModel(Activity.Current?.Id ?? HttpContext.TraceIdentifier));
}
