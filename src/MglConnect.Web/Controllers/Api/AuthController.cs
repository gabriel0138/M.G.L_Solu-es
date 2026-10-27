using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MglConnect.Web.Data;
using MglConnect.Web.Services;

namespace MglConnect.Web.Controllers.Api
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly MglDbContext _context;
        private readonly IPasswordHasher _hasher;
        private readonly IDataProtectionProvider _dataProtectionProvider;

        public AuthController(
            MglDbContext context,
            IPasswordHasher hasher,
            IDataProtectionProvider dataProtectionProvider)
        {
            _context = context;
            _hasher = hasher;
            _dataProtectionProvider = dataProtectionProvider;
        }

        public record LoginRequest(string Login, string Senha);

        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Login) || string.IsNullOrWhiteSpace(request.Senha))
            {
                return BadRequest(new { sucesso = false, mensagem = "Usuário e senha são obrigatórios." });
            }

            var usuario = await _context.Usuarios
                .FirstOrDefaultAsync(u => u.Login.ToLower() == request.Login.Trim().ToLower() && u.Ativo);

            if (usuario == null || !_hasher.VerifyPassword(request.Senha, usuario.SenhaHash))
            {
                return Unauthorized(new { sucesso = false, mensagem = "Credenciais inválidas." });
            }

            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, usuario.Id.ToString()),
                new Claim(ClaimTypes.Name, usuario.Nome),
                new Claim(ClaimTypes.Role,
                    usuario.Perfil.Equals("Administrador", StringComparison.OrdinalIgnoreCase)
                    ? "Admin"
                    : usuario.Perfil),
                new Claim("Login", usuario.Login),
                new Claim("TenantId", usuario.TenantId.ToString())
            };

            if (usuario.Perfil.Equals("Admin", StringComparison.OrdinalIgnoreCase)
                || usuario.Perfil.Equals("Administrador", StringComparison.OrdinalIgnoreCase))
            {
                claims.Add(new Claim(ClaimTypes.Role, "Administrador"));
            }

            var claimsIdentity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            var authProperties = new AuthenticationProperties
            {
                IsPersistent = false,
                IssuedUtc = DateTimeOffset.UtcNow,
                ExpiresUtc = DateTimeOffset.UtcNow.AddHours(8)
            };

            await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(claimsIdentity), authProperties);
            var principal = new ClaimsPrincipal(claimsIdentity);
            var token = ApiBearerToken.Protect(principal, authProperties, _dataProtectionProvider);

            return Ok(new
            {
                sucesso = true,
                mensagem = "Autenticação realizada com sucesso.",
                token,
                perfil = claimsIdentity.FindFirst(ClaimTypes.Role)?.Value,
                expiraEm = authProperties.ExpiresUtc
            });
        }
    }
}
