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
        private const string EditableFields = "RazaoSocial,NomeFantasia,Cnpj,Email,Telefone,Cep,Logradouro,Numero,Complemento,Bairro,Cidade,Estado,Ativo";
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
        public async Task<IActionResult> Create([Bind(EditableFields)] Cliente cliente)
        {
            if (ModelState.IsValid)
            {
                if (await _context.Clientes.IgnoreQueryFilters().AnyAsync(c => c.Cnpj == cliente.Cnpj))
                {
                    ModelState.AddModelError("Cnpj", "CNPJ indisponível para cadastro. Verifique os dados.");
                    return View(cliente);
                }

                cliente.DataCadastro = DateTime.UtcNow;
                _context.Clientes.Add(cliente);
                try { await _context.SaveChangesAsync(); }
                catch (DbUpdateException)
                {
                    // The global unique index can also reject concurrent submissions.
                    if (!await _context.Clientes.IgnoreQueryFilters().AnyAsync(c => c.Cnpj == cliente.Cnpj)) throw;
                    ModelState.AddModelError("Cnpj", "CNPJ indisponível para cadastro. Verifique os dados.");
                    return View(cliente);
                }
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
        public async Task<IActionResult> Edit(int id, [Bind("Id," + EditableFields)] Cliente cliente)
        {
            if (id != cliente.Id) return NotFound();

            // Query filters protect reads, not an UPDATE of a detached posted entity.
            // Load the authorized record first; never take ownership from the form.
            var existing = await _context.Clientes.FirstOrDefaultAsync(c => c.Id == id);
            if (existing == null) return NotFound();

            if (ModelState.IsValid)
            {
                if (await _context.Clientes.IgnoreQueryFilters().AnyAsync(c => c.Cnpj == cliente.Cnpj && c.Id != cliente.Id))
                {
                    ModelState.AddModelError("Cnpj", "CNPJ indisponível para cadastro. Verifique os dados.");
                    return View(cliente);
                }

                try
                {
                    existing.RazaoSocial = cliente.RazaoSocial;
                    existing.NomeFantasia = cliente.NomeFantasia;
                    existing.Cnpj = cliente.Cnpj;
                    existing.Email = cliente.Email;
                    existing.Telefone = cliente.Telefone;
                    existing.Cep = cliente.Cep;
                    existing.Logradouro = cliente.Logradouro;
                    existing.Numero = cliente.Numero;
                    existing.Complemento = cliente.Complemento;
                    existing.Bairro = cliente.Bairro;
                    existing.Cidade = cliente.Cidade;
                    existing.Estado = cliente.Estado;
                    existing.Ativo = cliente.Ativo;
                    await _context.SaveChangesAsync();
                    TempData["MensagemSucesso"] = "Cliente atualizado com sucesso!";
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!await _context.Clientes.AnyAsync(e => e.Id == cliente.Id)) return NotFound();
                    throw;
                }
                catch (DbUpdateException)
                {
                    if (!await _context.Clientes.IgnoreQueryFilters().AnyAsync(c => c.Cnpj == cliente.Cnpj && c.Id != id)) throw;
                    ModelState.AddModelError("Cnpj", "CNPJ indisponível para cadastro. Verifique os dados.");
                    return View(cliente);
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
