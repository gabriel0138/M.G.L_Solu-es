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
        private readonly OsrmRoadRouter? _roads;
        private readonly DeliveryRoadPlanning? _planning;
        private readonly GpsHistoryAccess? _access;

        public RoteamentoController(
            MglDbContext context, 
            ITruckRoutingService routingService,
            IFuelCostService fuelService, OsrmRoadRouter? roads = null, DeliveryRoadPlanning? planning = null, GpsHistoryAccess? access = null)
        {
            _context = context;
            _routingService = routingService;
            _fuelService = fuelService;_roads=roads;_planning=planning;_access=access;
        }

        private static List<ConfirmedRouteStop>? ConfirmedPoints(string json)
        {
            try {
                var points=System.Text.Json.JsonSerializer.Deserialize<List<ConfirmedRouteStop>>(json,new System.Text.Json.JsonSerializerOptions{PropertyNameCaseInsensitive=true});
                return points is {Count: >=2 and <=10} && points.All(p=>p!=null&&p.IsValid())?points:null;
            }catch(System.Text.Json.JsonException){return null;}
        }

        [HttpPost,ValidateAntiForgeryToken]
        [ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
        public async Task<IActionResult> Previa(int entregaId,string? confirmedStops,string? binding,CancellationToken ct)
        {
            var scope=_access==null?null:await _access.ResolveAsync(User,entregaId,ct);
            if(scope?.Admin!=true||_planning==null)return NotFound();
            if(confirmedStops==null||confirmedStops.Length>16000||string.IsNullOrWhiteSpace(binding))return BadRequest("Confirme os pontos e o vínculo do pedido.");
            var stops=ConfirmedPoints(confirmedStops);
            if(stops==null)return BadRequest("Confirme endereço e posição de cada parada no mapa.");
            try {
                var plan=await _planning.PreviewAsync(entregaId,stops,binding,ct);
                if(plan==null)return Conflict("Entrega indisponível para prévia.");
                if((await _access!.ResolveAsync(User,entregaId,ct))?.Admin!=true)return NotFound();
                var view=RoutePresentation.From(plan);
                return Json(new{state=view.State.ToString(),label=view.Label,available=view.HasGeometry,view.DistanceKm,view.DurationSeconds,
                    coordinates=view.HasGeometry?plan.TrajetoCoordenadas:[],provider=plan.ProviderName,profile=plan.ProviderProfile,geometrySource=plan.GeometrySource});
            }catch(PlanningContextChangedException){return Conflict("Pedido, endereço ou atribuição mudou. Atualize a página e confirme os pontos novamente.");}
        }

        [HttpPost,ValidateAntiForgeryToken]
        public async Task<IActionResult> Planejar(int entregaId,string? coordenadas,CancellationToken ct)
        {
            var scope=_access==null?null:await _access.ResolveAsync(User,entregaId,ct);
            if(scope?.Admin!=true||_planning==null)return NotFound();
            if(!OsrmRoadRouter.TryLocations(coordenadas,out var points))return BadRequest("Informe de 2 a 10 coordenadas válidas, longitude/latitude.");
            if(await _planning.CalculateAsync(entregaId,points,ct)==null)return Conflict("Entrega indisponível para planejamento.");
            return RedirectToAction(nameof(Index),new {entregaId});
        }

        [HttpPost,ValidateAntiForgeryToken]
        [ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
        public async Task<IActionResult> Sugestoes(int entregaId,string? endereco,[FromServices] PhotonGeocoder geocoder,CancellationToken ct)
        {
            var scope=_access==null?null:await _access.ResolveAsync(User,entregaId,ct);
            if(scope?.Admin!=true)return NotFound();
            return Json(await geocoder.SearchAsync(endereco,ct));
        }
        [HttpPost,ValidateAntiForgeryToken]
        public async Task<IActionResult> Confirmar(int entregaId,string? confirmedStops,string? binding,CancellationToken ct)
        {
            var scope=_access==null?null:await _access.ResolveAsync(User,entregaId,ct);
            if(scope?.Admin!=true||_planning==null)return NotFound();
            if(confirmedStops==null||confirmedStops.Length>16000||string.IsNullOrWhiteSpace(binding))return BadRequest("Confirme os pontos e o vínculo do pedido.");
            var stops=ConfirmedPoints(confirmedStops);
            if(stops==null)return BadRequest("Confirme endereço e posição de cada parada no mapa.");
            try {
                var plan=await _planning.CalculateAsync(entregaId,stops.Select(p=>new MglConnect.Web.Services.RouteTrust.RouteLocation(p.Latitude,p.Longitude)).ToList(),ct,confirmedStops:stops,expectedBinding:binding);
                if(plan==null)return Conflict("Entrega indisponível para planejamento.");
            }catch(PlanningContextChangedException){return Conflict("Pedido, endereço ou atribuição mudou. Atualize a página e confirme os pontos novamente.");}
            return RedirectToAction(nameof(Index),new {entregaId});
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
            decimal? consumoKmL, bool calcular = false, string? coordenadas = null)
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

            ViewBag.EntregaSelecionadaId=entregaId;
            TruckRoutePlan plano;
            decimal valorPedido = 0m;

            if(entregaId.HasValue && !await _context.Entregas.AnyAsync(e=>e.Id==entregaId.Value))return NotFound();
            bool requested=calcular || entregaId.HasValue || !string.IsNullOrWhiteSpace(origem) || !string.IsNullOrWhiteSpace(destino) || !string.IsNullOrWhiteSpace(coordenadas);
            if (precoCombustivel is <= 0 || consumoKmL is <= 0) ModelState.AddModelError("", "Preço e consumo devem ser positivos.");
            if(eixos is <1 or >20)ModelState.AddModelError("eixos","Informe entre 1 e 20 eixos.");
            if(calcular && !entregaId.HasValue && string.IsNullOrWhiteSpace(coordenadas) && (string.IsNullOrWhiteSpace(origem)||string.IsNullOrWhiteSpace(destino)))
                ModelState.AddModelError("", "Informe origem e destino, coordenadas ou uma entrega própria.");
            if (!requested || !ModelState.IsValid)
            {
                plano=new TruckRoutePlan { PerfilVeiculo=perfil,EixosVeiculo=eixos??5 };
                requested=false;
            }
            else if(!string.IsNullOrWhiteSpace(coordenadas))
            {
                if(!OsrmRoadRouter.TryLocations(coordenadas,out var points)) { ModelState.AddModelError("coordenadas","Informe entre 2 e 10 coordenadas válidas na ordem longitude,latitude.");plano=new(){PerfilVeiculo=perfil};requested=false; }
                else if(_roads==null)plano=new(){PerfilVeiculo=perfil,ProviderFailure=MglConnect.Web.Services.RouteTrust.ProviderFailure.Disabled};
                else {ViewBag.RouteStops=points;plano=(await _roads.RouteAsync(points,HttpContext.RequestAborted)).ToPlan();plano.PerfilVeiculo=perfil;plano.EixosVeiculo=eixos??5;plano.Origem="Origem informada por coordenadas";plano.Destino="Destino informado por coordenadas";}
            }
            else if (entregaId.HasValue && entregaId.Value > 0)
            {
                plano = await _routingService.CalcularRotaParaEntregaAsync(entregaId.Value, perfil);
                ViewBag.EntregaSelecionadaId = entregaId.Value;

                var entrega = await _context.Entregas.Include(e => e.Pedido).FirstOrDefaultAsync(e => e.Id == entregaId.Value);
                valorPedido = entrega?.Pedido?.ValorTotal ?? 0m;
            }
            else
            {
                var alt = altura ?? 0m;
                var pes = peso ?? 0m;
                var larg = largura ?? 0m;
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

            ViewBag.GeographyContext=entregaId.HasValue&&_planning!=null?await _planning.ContextAsync(entregaId.Value,HttpContext.RequestAborted):null;
            ViewBag.HistoricalCaptureEnabled=_roads?.HistoricalCaptureEnabled==true;
            ViewBag.RoutePresentation = RoutePresentation.From(plano,custoViagem,requested);
            ViewBag.PerfilVeiculo = perfil;
            ViewBag.CustoViagem = custoViagem;
            ViewBag.PrecoCombustivelCustom = precoCombustivel;
            ViewBag.ConsumoCustom = consumoKmL;

            return View(plano);
        }
    }
}
