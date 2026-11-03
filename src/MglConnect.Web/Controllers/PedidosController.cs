using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using MglConnect.Web.Data;
using MglConnect.Web.Models;

namespace MglConnect.Web.Controllers
{
    [Authorize(Roles = "Admin")]
    public class PedidosController : Controller
    {
        private readonly MglDbContext _context;

        public PedidosController(MglDbContext context)
        {
            _context = context;
        }

        public async Task<IActionResult> Index(string? status = null, string? termo = null)
        {
            status = status?.Trim();
            termo = termo?.Trim();
            var query = _context.Pedidos
                .AsNoTracking()
                .Include(p => p.Cliente)
                .Include(p => p.Entrega)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(status))
            {
                query = query.Where(p => p.Status == status);
            }

            if (!string.IsNullOrWhiteSpace(termo))
            {
                var busca = termo.ToLowerInvariant();
                query = query.Where(p => p.NumeroPedido.ToLower().Contains(busca)
                    || p.Descricao.ToLower().Contains(busca)
                    || (p.Cliente != null && (p.Cliente.RazaoSocial.ToLower().Contains(busca)
                        || (p.Cliente.NomeFantasia != null && p.Cliente.NomeFantasia.ToLower().Contains(busca)))));
            }

            ViewBag.StatusSelecionado = status;
            ViewBag.TermoBusca = termo;
            var pedidos = await query.OrderByDescending(p => p.DataPedido).ToListAsync();
            return View(pedidos);
        }

        public async Task<IActionResult> Create()
        {
            ViewBag.ClienteId = new SelectList(await _context.Clientes.Where(c => c.Ativo).OrderBy(c => c.RazaoSocial).ToListAsync(), "Id", "RazaoSocial");
            return View(new Pedido());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind("ClienteId,Descricao,ValorTotal,PesoKg,TipoCarga,OrigemEndereco,OrigemNumero,OrigemBairro,OrigemCidade,OrigemEstado,OrigemCep,DestinoEndereco,DestinoNumero,DestinoBairro,DestinoCidade,DestinoEstado,DestinoCep,AlturaCargaMetros,LarguraCargaMetros,ComprimentoCargaMetros")] Pedido pedido)
        {
            ModelState.Remove(nameof(Pedido.NumeroPedido));
            var clienteValido = await _context.Clientes
                .AnyAsync(c => c.Id == pedido.ClienteId && c.Ativo);
            if (!clienteValido)
            {
                ModelState.AddModelError(nameof(pedido.ClienteId), "Selecione um cliente ativo da sua operação.");
            }

            if (ModelState.IsValid)
            {
                pedido.NumeroPedido = GerarNumeroPedido();
                pedido.DataPedido = DateTime.UtcNow;
                pedido.Status = "Registrado";
                _context.Pedidos.Add(pedido);
                await _context.SaveChangesAsync();

                TempData["MensagemSucesso"] = $"Pedido {pedido.NumeroPedido} registrado com sucesso!";
                return RedirectToAction(nameof(Index));
            }

            ViewBag.ClienteId = new SelectList(await _context.Clientes.Where(c => c.Ativo).OrderBy(c => c.RazaoSocial).ToListAsync(), "Id", "RazaoSocial", pedido.ClienteId);
            return View(pedido);
        }

        public async Task<IActionResult> Details(int? id)
        {
            if (id == null) return NotFound();

            var pedido = await _context.Pedidos
                .Include(p => p.Cliente)
                .Include(p => p.Entrega)
                    .ThenInclude(e => e!.Motorista)
                .Include(p => p.Entrega)
                    .ThenInclude(e => e!.EventosRastreio)
                .FirstOrDefaultAsync(m => m.Id == id);

            if (pedido == null) return NotFound();

            return View(pedido);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AtualizarStatus(int id, string status)
        {
            var pedido = await _context.Pedidos.FirstOrDefaultAsync(item => item.Id == id);
            if (pedido == null) return NotFound();
            if (await _context.Entregas.AnyAsync(entrega => entrega.PedidoId == id))
            {
                TempData["MensagemErro"] = "O pedido já possui entrega vinculada. Gerencie o estado pela operação da entrega.";
                return RedirectToAction(nameof(Details), new { id });
            }

            var allowed = (pedido.Status, status) switch
            {
                ("Registrado", "EmProcessamento") => true,
                ("Registrado", "Cancelado") => true,
                ("EmProcessamento", "Cancelado") => true,
                _ => false
            };
            if (!allowed)
            {
                TempData["MensagemErro"] = "Transição de status do pedido inválida.";
                return RedirectToAction(nameof(Details), new { id });
            }

            pedido.Status = status;
            pedido.Versao++;
            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                TempData["MensagemErro"] = "O pedido foi alterado por outra operação. Atualize a página antes de tentar novamente.";
                return RedirectToAction(nameof(Details), new { id });
            }

            TempData["MensagemSucesso"] = $"Pedido atualizado para {status}.";
            return RedirectToAction(nameof(Details), new { id });
        }

        private static string GerarNumeroPedido() =>
            $"PED-{DateTime.UtcNow:yyyy}-{Guid.NewGuid():N}"[..21].ToUpperInvariant();
    }
}
