using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MglConnect.Web.Data;
using MglConnect.Web.Services;
using MglConnect.Web.ViewModels;

namespace MglConnect.Web.Controllers
{
    public class AccountController : Controller
    {
        private readonly MglDbContext _context;
        private readonly IPasswordHasher _hasher;

        public AccountController(MglDbContext context, IPasswordHasher hasher)
        {
            _context = context;
            _hasher = hasher;
        }

        [HttpGet]
        public IActionResult Login(string? returnUrl = null)
        {
            if (User.Identity != null && User.Identity.IsAuthenticated)
            {
                if (User.IsInRole("Motorista"))
                {
                    return RedirectToAction("Index", "Entregas");
                }
                if (User.IsInRole("Cliente"))
                {
                    return Redirect("/Cliente");
                }
                return RedirectToAction("Index", "Dashboard");
            }

            ViewData["ReturnUrl"] = returnUrl;
            return View(new LoginViewModel { ReturnUrl = returnUrl });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(LoginViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var usuario = await _context.Usuarios
                .FirstOrDefaultAsync(u => u.Login.ToLower() == model.Login.Trim().ToLower() && u.Ativo);

            if (usuario == null || !_hasher.VerifyPassword(model.Senha, usuario.SenhaHash))
            {
                ModelState.AddModelError(string.Empty, "Login ou senha inválidos.");
                return View(model);
            }

            var perfil = (usuario.Perfil.Equals("Administrador", System.StringComparison.OrdinalIgnoreCase) || 
                          usuario.Perfil.Equals("Admin", System.StringComparison.OrdinalIgnoreCase)) 
                         ? "Admin" 
                         : usuario.Perfil;

            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, usuario.Id.ToString()),
                new Claim(ClaimTypes.Name, usuario.Nome),
                new Claim(ClaimTypes.Role, perfil),
                new Claim("Login", usuario.Login),
                new Claim("TenantId", usuario.TenantId.ToString())
            };

            if (perfil == "Admin")
            {
                claims.Add(new Claim(ClaimTypes.Role, "Administrador"));
            }

            var claimsIdentity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            var authProperties = new AuthenticationProperties
            {
                IsPersistent = model.LembrarMe
            };

            await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(claimsIdentity), authProperties);

            if (!string.IsNullOrEmpty(model.ReturnUrl) && Url.IsLocalUrl(model.ReturnUrl))
            {
                return Redirect(model.ReturnUrl);
            }

            // Redirecionamento RBAC por perfil de usuário
            if (perfil.Equals("Motorista", System.StringComparison.OrdinalIgnoreCase))
            {
                return RedirectToAction("Index", "Entregas");
            }

            if (perfil.Equals("Cliente", System.StringComparison.OrdinalIgnoreCase))
            {
                return Redirect("/Cliente");
            }

            return RedirectToAction("Index", "Dashboard");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Logout()
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return RedirectToAction("Index", "Home");
        }

        [HttpGet]
        public IActionResult AccessDenied()
        {
            return View();
        }
    }
}
