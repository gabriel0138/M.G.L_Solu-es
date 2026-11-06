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

        private static object DraftReply(ApprovedRoadPreview draft,string status="Preview",TruckRoutePlan? previous=null)
        {
            var p=draft.Plan;var view=RoutePresentation.From(p);var available=view.HasGeometry&&draft.ApprovalId!=null;
            return new{status,state=view.State.ToString(),label=view.HasGeometry&&!available?"Prévia indisponível; gere novamente":view.Label,available,
                distanceKm=available?view.DistanceKm:null,durationSeconds=available?view.DurationSeconds:null,
                coordinates=available?p.TrajetoCoordenadas:[],provider=p.ProviderName,profile=p.ProviderProfile,geometrySource=p.GeometrySource,
                draft.ApprovalId,draft.ExpiresAtUtc,draft.BaseRevision,stopCount=available?(int?)(p.RequestedStops.Count-2):null,
                previous=previous==null?null:new{distanceKm=previous.DistanciaKm,durationSeconds=previous.ProviderDurationSeconds}};
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
                var draft=await _planning.CreateApprovalAsync(entregaId,scope.Actor.Id,stops,binding,ct);
                if(draft==null)return Conflict("Entrega indisponível para prévia.");
                if((await _access!.ResolveAsync(User,entregaId,ct))?.Admin!=true)return NotFound();
                return Json(DraftReply(draft));
            }catch(PlanningContextChangedException){return Conflict("Prévia expirada, já utilizada, versão ou vínculo alterado. Atualize o planejador e gere uma nova prévia.");}
        }

        [HttpPost,ValidateAntiForgeryToken]
        public async Task<IActionResult> Planejar(int entregaId,string? coordenadas,CancellationToken ct)
        {
            var scope=_access==null?null:await _access.ResolveAsync(User,entregaId,ct);
            if(scope?.Admin!=true||_planning==null)return NotFound();
            return Conflict("Publicação direta desativada. Confirme os pontos, gere a prévia e aprove o percurso no planejador.");
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
        public async Task<IActionResult> Confirmar(int entregaId,string? confirmedStops,string? binding,string? approvalId,CancellationToken ct)
        {
            var scope=_access==null?null:await _access.ResolveAsync(User,entregaId,ct);
            if(scope?.Admin!=true||_planning==null)return NotFound();
            if(confirmedStops==null||confirmedStops.Length>16000||string.IsNullOrWhiteSpace(binding))return BadRequest("Confirme os pontos e o vínculo do pedido.");
            var stops=ConfirmedPoints(confirmedStops);
            if(stops==null)return BadRequest("Confirme endereço e posição de cada parada no mapa.");
            try {
                var outcome=await _planning.PublishApprovedAsync(entregaId,scope.Actor.Id,approvalId,stops,binding,ct);
                if(outcome.Status!="Published")return StatusCode(409,DraftReply(outcome.Replacement!,outcome.Status,outcome.Previous));
            }catch(PlanningContextChangedException){return Conflict("Prévia expirada, já utilizada, versão ou vínculo alterado. Atualize o planejador e gere uma nova prévia.");}
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
