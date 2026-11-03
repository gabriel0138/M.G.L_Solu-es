using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using MglConnect.Web.Models;

namespace MglConnect.Web.Controllers;

public class HomeController : Controller
{
    private readonly ILogger<HomeController> _logger;

    public HomeController(ILogger<HomeController> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Rota raiz: redireciona automaticamente conforme estado de autenticação e perfil RBAC.
    /// Admin → /Dashboard | Motorista → /Entregas | Cliente → /Cliente | Anônimo → /Account/Login
    /// </summary>
    public IActionResult Index()
    {
        if (User.Identity != null && User.Identity.IsAuthenticated)
        {
            if (User.IsInRole("Admin"))
                return RedirectToAction("Index", "Dashboard");

            if (User.IsInRole("Motorista"))
                return RedirectToAction("Index", "Entregas");

            if (User.IsInRole("Cliente") || User.IsInRole("Intermediador"))
                return Redirect("/Cliente");
        }

        // Anônimo → tela de login corporativo
        return RedirectToAction("Login", "Account");
    }

    public IActionResult Privacy()
    {
        return View();
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = HttpContext.Items["Mgl.CorrelationId"] as string ?? Activity.Current?.TraceId.ToHexString() ?? HttpContext.TraceIdentifier });
    }
}
