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

        public async Task<IActionResult> Index(string? status)
        {
            var query = _context.Pedidos
                .Include(p => p.Cliente)
                .Include(p => p.Entrega)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(status))
            {
                query = query.Where(p => p.Status == status);
            }

            ViewBag.StatusSelecionado = status;
            var pedidos = await query.OrderByDescending(p => p.DataPedido).ToListAsync();
            return View(pedidos);
        }

        public async Task<IActionResult> Create()
        {
            ViewBag.ClienteId = new SelectList(await _context.Clientes.Where(c => c.Ativo).OrderBy(c => c.RazaoSocial).ToListAsync(), "Id", "RazaoSocial");
            var proximoNumero = $"PED-{DateTime.UtcNow.Year}-{(await _context.Pedidos.CountAsync() + 1):D4}";
            return View(new Pedido { NumeroPedido = proximoNumero });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(Pedido pedido)
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
