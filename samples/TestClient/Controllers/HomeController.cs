using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using TestClient.Models;
using TestClient.Options;
using Microsoft.Extensions.Options;

namespace TestClient.Controllers;

public class HomeController : Controller
{
    private readonly OidcDemoOptions _options;

    public HomeController(IOptions<OidcDemoOptions> options)
    {
        _options = options.Value;
    }

    public IActionResult Index()
    {
        return View(new DemoHomeViewModel(_options.Authority, _options.ClientId, _options.Scopes));
    }

    public IActionResult Privacy()
    {
        return View();
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
