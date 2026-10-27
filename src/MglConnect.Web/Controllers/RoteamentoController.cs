using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MglConnect.Web.Data;
using MglConnect.Web.Services;

namespace MglConnect.Web.Controllers
{
    [Authorize(Roles = "Admin")]
    public class RoteamentoController : Controller
    {
        private readonly MglDbContext _context;
        private readonly ITruckRoutingService _routingService;
        private readonly IFuelCostService _fuelService;

        public RoteamentoController(
            MglDbContext context, 
            ITruckRoutingService routingService,
            IFuelCostService fuelService)
        {
            _context = context;
            _routingService = routingService;
            _fuelService = fuelService;
        }

        [HttpGet]
        public async Task<IActionResult> Index(
            int? entregaId, 
            string? origem, 
            string? destino, 
            decimal? altura, 
            decimal? peso, 
            decimal? largura, 
            int? eixos, 
            string? tipoCarga,
            string? perfilVeiculo,
            decimal? precoCombustivel,
            decimal? consumoKmL)
        {
            ViewBag.Entregas = await _context.Entregas
                .Include(e => e.Pedido)
                .OrderByDescending(e => e.DataCriacao)
                .ToListAsync();

            ViewBag.Veiculos = await _context.Veiculos
                .Where(v => v.Ativo)
                .OrderBy(v => v.Modelo)
                .ToListAsync();

            var perfil = string.Equals(perfilVeiculo, "Leve", StringComparison.OrdinalIgnoreCase) 
                ? "Leve" 
                : "Pesado";

            TruckRoutePlan plano;
            decimal valorPedido = 0m;

            if (entregaId.HasValue && entregaId.Value > 0)
            {
                plano = await _routingService.CalcularRotaParaEntregaAsync(entregaId.Value, perfil);
                ViewBag.EntregaSelecionadaId = entregaId.Value;

                var entrega = await _context.Entregas.Include(e => e.Pedido).FirstOrDefaultAsync(e => e.Id == entregaId.Value);
                valorPedido = entrega?.Pedido?.ValorTotal ?? 0m;
            }
            else
            {
                var alt = altura ?? (perfil == "Leve" ? 2.10m : 4.20m);
                var pes = peso ?? (perfil == "Leve" ? 2.8m : 28.5m);
                var larg = largura ?? (perfil == "Leve" ? 2.00m : 2.60m);
                var eix = eixos ?? (perfil == "Leve" ? 1 : 5);
                var carg = tipoCarga ?? (perfil == "Leve" ? "Carga Expressa / Utilitário" : "Carga Geral Seca");

                plano = await _routingService.CalcularRotaPesadaAsync(
                    origem ?? "CD Logístico Cajamar - SP",
                    destino ?? "Av. Ana Costa, 200 - Santos/SP",
                    alt, pes, larg, eix, carg,
                    perfil
                );
            }

            // Cálculo Financeiro e Métrica de Combustível (Módulo 1)
            var custoViagem = _fuelService.CalcularCustoViagem(
                plano.DistanciaKm,
                perfil,
                plano.EixosVeiculo,
                valorPedido: valorPedido,
                precoCombustivelCustom: precoCombustivel,
                consumoCustom: consumoKmL
            );

            ViewBag.PerfilVeiculo = perfil;
            ViewBag.CustoViagem = custoViagem;
            ViewBag.PrecoCombustivelCustom = precoCombustivel;
            ViewBag.ConsumoCustom = consumoKmL;

            return View(plano);
        }
    }
}
