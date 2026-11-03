using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MglConnect.Web.Data;
using MglConnect.Web.Models;
using MglConnect.Web.Services;
using MglConnect.Web.ViewModels;

namespace MglConnect.Web.Controllers
{
    [Authorize(Roles = "Cliente,Intermediador")]
    [Route("Cliente")]
    public class ClientePortalController : Controller
    {
        private readonly MglDbContext _context;

        public ClientePortalController(MglDbContext context)
        {
            _context = context;
        }

        private async Task<Cliente?> ObterClienteAtualAsync()
        {
            // Se logado com perfil de cliente, busca pelo ClienteId do usuário
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (int.TryParse(userIdClaim, out int userId))
            {
                var usuario = await _context.Usuarios
                    .Include(u => u.Cliente)
                    .AsNoTracking()
                    .FirstOrDefaultAsync(u => u.Id == userId && u.Ativo
                        && (u.Perfil == "Cliente" || u.Perfil == "Intermediador"));

                if (usuario?.Cliente is { Ativo: true } cliente)
                {
                    return cliente;
                }

                // Sem vínculo explícito com Cliente: não aplicar fallback
                return null;
            }

            return null;
        }

        [HttpGet("Pedido/Novo")]
        public async Task<IActionResult> NovoPedido()
        {
            if (await ObterClienteAtualAsync() == null)
            {
                return View("SemCliente");
            }

            return View(new NovoPedidoClienteViewModel());
        }

        [HttpPost("Pedido/Novo")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> NovoPedido(NovoPedidoClienteViewModel model)
        {
            var cliente = await ObterClienteAtualAsync();
            if (cliente == null)
            {
                return Forbid();
            }

            if (!ModelState.IsValid)
            {
                return View(model);
            }

            if (!Guid.TryParse(model.ChaveEnvio, out var submissionId))
            {
                ModelState.AddModelError(nameof(model.ChaveEnvio), "A solicitação expirou. Recarregue o formulário e tente novamente.");
                return View(model);
            }

            var submissionKey = submissionId.ToString("N");
            var priorSubmission = await _context.Pedidos.AsNoTracking()
                .FirstOrDefaultAsync(p => p.ChaveIdempotencia == submissionKey);
            if (priorSubmission != null)
            {
                if (priorSubmission.ClienteId != cliente.Id)
                {
                    return Forbid();
                }

                return RedirectToAction(nameof(PedidoDetalhes), new { id = priorSubmission.Id });
            }

            var pedido = new Pedido
            {
                NumeroPedido = GerarNumeroPedido(),
                ChaveIdempotencia = submissionKey,
                ClienteId = cliente.Id,
                Descricao = model.Descricao.Trim(),
                ValorTotal = model.ValorTotal,
                PesoKg = model.PesoKg,
                TipoCarga = model.TipoCarga.Trim(),
                OrigemEndereco = model.OrigemEndereco.Trim(),
                OrigemNumero = model.OrigemNumero.Trim(),
                OrigemBairro = model.OrigemBairro?.Trim(),
                OrigemCidade = model.OrigemCidade.Trim(),
                OrigemEstado = model.OrigemEstado.Trim().ToUpperInvariant(),
                OrigemCep = model.OrigemCep?.Trim(),
                DestinoEndereco = model.DestinoEndereco.Trim(),
                DestinoNumero = model.DestinoNumero.Trim(),
                DestinoBairro = model.DestinoBairro?.Trim(),
                DestinoCidade = model.DestinoCidade.Trim(),
                DestinoEstado = model.DestinoEstado.Trim().ToUpperInvariant(),
                DestinoCep = model.DestinoCep?.Trim(),
                AlturaCargaMetros = model.AlturaCargaMetros,
                LarguraCargaMetros = model.LarguraCargaMetros,
                ComprimentoCargaMetros = model.ComprimentoCargaMetros,
                DataPedido = DateTime.UtcNow,
                Status = "Registrado"
            };

            _context.Pedidos.Add(pedido);
            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                _context.Entry(pedido).State = EntityState.Detached;
                var existing = await _context.Pedidos.AsNoTracking()
                    .FirstOrDefaultAsync(p => p.ChaveIdempotencia == submissionKey);
                if (existing?.ClienteId == cliente.Id)
                {
                    return RedirectToAction(nameof(PedidoDetalhes), new { id = existing.Id });
                }

                throw;
            }

            TempData["MensagemSucesso"] = $"Pedido {pedido.NumeroPedido} enviado para validação operacional.";
            return RedirectToAction(nameof(PedidoDetalhes), new { id = pedido.Id });
        }

        [HttpGet("")]
        [HttpGet("Dashboard")]
        [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
        public async Task<IActionResult> Index()
        {
            var cliente = await ObterClienteAtualAsync();
            if (cliente == null) return View("SemCliente");
            return View(await CarregarPainelAsync(cliente));
        }

        [HttpGet("Painel")]
        [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
        public async Task<IActionResult> Painel()
        {
            var cliente = await ObterClienteAtualAsync();
            if (cliente == null) return Forbid();
            return PartialView("_ClientOperationsPanel", await CarregarPainelAsync(cliente));
        }

        private async Task<List<Pedido>> CarregarPainelAsync(Cliente cliente)
        {
            var query = _context.Pedidos.AsNoTracking().Where(p => p.ClienteId == cliente.Id);
            ViewBag.Cliente = cliente;
            ViewBag.TotalPedidos = await query.CountAsync();
            ViewBag.EmTransito = await query.CountAsync(p => p.Entrega != null && p.Entrega.StatusEntrega == "Em transporte");
            ViewBag.Entregues = await query.CountAsync(p => p.Entrega != null && p.Entrega.StatusEntrega == "Entregue");
            ViewBag.Pendentes = await query.CountAsync(p => p.Status == "Registrado" || p.Status == "EmProcessamento" || (p.Entrega != null && p.Entrega.StatusEntrega == "Pendente"));
            ViewBag.ValorTotal = Math.Round((decimal)(await query.Where(p => p.Status != "Cancelado").SumAsync(p => (double?)p.ValorTotal) ?? 0), 2);
            return await query.Include(p => p.Entrega).OrderByDescending(p => p.DataPedido).Take(5).ToListAsync();
        }

        [HttpGet("Pedidos")]
        public async Task<IActionResult> Pedidos(string? status, string? termo)
        {
            var cliente = await ObterClienteAtualAsync();
            if (cliente == null) return View("SemCliente");

            var query = _context.Pedidos
                .Include(p => p.Entrega)
                    .ThenInclude(e => e!.EventosRastreio)
                .Where(p => p.ClienteId == cliente.Id)
                .AsNoTracking()
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(status))
            {
                if (status == "Entregue" || status == "Em transporte" || status == "Pendente")
                {
                    query = query.Where(p => p.Entrega != null && p.Entrega.StatusEntrega == status);
                }
                else
                {
                    query = query.Where(p => p.Status == status);
                }
            }

            if (!string.IsNullOrWhiteSpace(termo))
            {
                var t = termo.Trim().ToLower();
                query = query.Where(p => p.NumeroPedido.ToLower().Contains(t) 
                                      || p.Descricao.ToLower().Contains(t)
                                      || (p.Entrega != null && p.Entrega.CodigoRastreio.ToLower().Contains(t)));
            }

            ViewBag.Cliente = cliente;
            ViewBag.StatusSelecionado = status;
            ViewBag.TermoBusca = termo;

            var lista = await query.OrderByDescending(p => p.DataPedido).ToListAsync();
            return View(lista);
        }

        [HttpGet("Pedido/{id:int}")]
        public async Task<IActionResult> PedidoDetalhes(int id)
        {
            var cliente = await ObterClienteAtualAsync();
            if (cliente == null) return View("SemCliente");

            var pedido = await _context.Pedidos
                .Include(p => p.Cliente)
                .Include(p => p.Entrega)
                    .ThenInclude(e => e!.Motorista)
                .Include(p => p.Entrega)
                    .ThenInclude(e => e!.Veiculo)
                .Include(p => p.Entrega)
                    .ThenInclude(e => e!.EventosRastreio.OrderByDescending(ev => ev.DataHora))
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == id && p.ClienteId == cliente.Id);

            if (pedido == null)
            {
                return NotFound();
            }

            ViewBag.Cliente = cliente;
            return View(pedido);
        }

        [HttpGet("Pedido/{id:int}/Comprovante")]
        [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
        public async Task<IActionResult> Comprovante(int id, [FromServices] IPodPdfService podPdfService)
        {
            var cliente = await ObterClienteAtualAsync();
            if (cliente == null) return Forbid();

            var pedido = await _context.Pedidos.AsNoTracking()
                .Include(p => p.Entrega)
                .FirstOrDefaultAsync(p => p.Id == id && p.ClienteId == cliente.Id);
            if (pedido?.Entrega is not { StatusEntrega: "Entregue" } entrega
                || string.IsNullOrWhiteSpace(entrega.NomeRecebedor)) return NotFound();

            var bytes = await podPdfService.GerarPodPdfAsync(entrega);
            return File(bytes, "application/pdf", $"Comprovante-Pedido-{id}.pdf");
        }

        private static string GerarNumeroPedido() =>
            $"PED-{DateTime.UtcNow:yyyy}-{Guid.NewGuid():N}"[..21].ToUpperInvariant();
    }
}
