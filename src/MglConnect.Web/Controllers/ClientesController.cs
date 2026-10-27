using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MglConnect.Web.Data;
using MglConnect.Web.Models;

namespace MglConnect.Web.Controllers
{
    [Authorize(Roles = "Admin")]
    public class ClientesController : Controller
    {
        private readonly MglDbContext _context;

        public ClientesController(MglDbContext context)
        {
            _context = context;
        }

        public async Task<IActionResult> Index(string? termoBusca)
        {
            var query = _context.Clientes.AsQueryable();

            if (!string.IsNullOrWhiteSpace(termoBusca))
            {
                var termo = termoBusca.Trim().ToLower();
                query = query.Where(c => c.RazaoSocial.ToLower().Contains(termo) 
                                      || (c.NomeFantasia != null && c.NomeFantasia.ToLower().Contains(termo)) 
                                      || c.Cnpj.Contains(termo)
                                      || (c.Cidade != null && c.Cidade.ToLower().Contains(termo)));
            }

            ViewBag.TermoBusca = termoBusca;
            var clientes = await query.OrderByDescending(c => c.DataCadastro).ToListAsync();
            return View(clientes);
        }

        public IActionResult Create()
        {
            return View(new Cliente());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(Cliente cliente)
        {
            if (ModelState.IsValid)
            {
                if (await _context.Clientes.AnyAsync(c => c.Cnpj == cliente.Cnpj))
                {
                    ModelState.AddModelError("Cnpj", "Já existe um cliente cadastrado com este CNPJ.");
                    return View(cliente);
                }

                cliente.DataCadastro = DateTime.UtcNow;
                _context.Clientes.Add(cliente);
                await _context.SaveChangesAsync();
                TempData["MensagemSucesso"] = "Cliente cadastrado com sucesso!";
                return RedirectToAction(nameof(Index));
            }
            return View(cliente);
        }

        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null) return NotFound();

            var cliente = await _context.Clientes.FindAsync(id);
            if (cliente == null) return NotFound();

            return View(cliente);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, Cliente cliente)
        {
            if (id != cliente.Id) return NotFound();

            if (ModelState.IsValid)
            {
                if (await _context.Clientes.AnyAsync(c => c.Cnpj == cliente.Cnpj && c.Id != cliente.Id))
                {
                    ModelState.AddModelError("Cnpj", "Já existe outro cliente com este CNPJ.");
                    return View(cliente);
                }

                try
                {
                    _context.Update(cliente);
                    await _context.SaveChangesAsync();
                    TempData["MensagemSucesso"] = "Cliente atualizado com sucesso!";
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!await _context.Clientes.AnyAsync(e => e.Id == cliente.Id)) return NotFound();
                    throw;
                }
                return RedirectToAction(nameof(Index));
            }
            return View(cliente);
        }

        public async Task<IActionResult> Details(int? id)
        {
            if (id == null) return NotFound();

            var cliente = await _context.Clientes
                .Include(c => c.Pedidos)
                    .ThenInclude(p => p.Entrega)
                .FirstOrDefaultAsync(m => m.Id == id);

            if (cliente == null) return NotFound();

            return View(cliente);
        }
    }
}
