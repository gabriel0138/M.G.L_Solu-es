using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MglConnect.Web.Data;

namespace MglConnect.Web.Services;

// A fictional test circuit, never a provider of operational truck navigation.
public sealed class ControlledRouteDemo(IConfiguration configuration, IWebHostEnvironment environment, MglDbContext db, DeliveryRoadPlanning? planning = null)
{
    public bool Enabled
    {
        get
        {
            if (!environment.IsDevelopment() || !configuration.GetValue<bool>("RoutingDemo:Enabled")
                || !configuration.GetValue<bool>("DemoData:Enabled") || !db.Database.IsSqlite()) return false;
            var file = new SqliteConnectionStringBuilder(db.Database.GetConnectionString()).DataSource;
            return Path.IsPathFullyQualified(file) && File.Exists(Path.Combine(Path.GetDirectoryName(file)!, ".mgl-local-preview"));
        }
    }

    // Authored road-like geometry: origin, curves, junction and destination.
    // Names deliberately identify fictional roads rather than real public streets.
    public static readonly (double Lat, double Lng)[] Circuit = [
        (-23.5505,-46.6333),(-23.5500,-46.6333),(-23.5496,-46.6331),
        (-23.5493,-46.6327),(-23.5493,-46.6322),(-23.5496,-46.6318),(-23.5500,-46.6318)];
    public static readonly (double Lat, double Lng)[] Detour = [
        (-23.5520,-46.6333),(-23.5520,-46.6326),(-23.5515,-46.6320),(-23.5508,-46.6318),Circuit[^1]];

    public static TruckRoutePlan Plan(bool detour = false, string? failure = null)
    {
        var points = detour ? Detour : Circuit;
        var plan = new TruckRoutePlan { ProviderName="Circuito local autorado",ProviderProfile="synthetic",GeometrySource="SyntheticCircuit",DemonstracaoControlada = true, Origem = "Origem do circuito fictício",
            Destino = "Destino do circuito fictício", PerfilVeiculo = "Demonstração", RotaDisponivel = failure == null,
            AvisoRota = failure ?? "Circuito e GPS sintéticos · sem homologação para caminhões." };
        if (failure != null) return plan;
        for (int i=0;i<points.Length;i++)
        {
            plan.TrajetoCoordenadas.Add(new() { Latitude=points[i].Lat, Longitude=points[i].Lng,
                Tipo=i==0?"Origem":i==points.Length-1?"Destino":"Waypoint", Nome=$"Ponto de ensaio {i+1}" });
            if (i>0) plan.DistanciaKm += (decimal)(Distance(points[i-1],points[i])/1000);
            plan.PassosNavegacao.Add(new() { Ordem=i,Latitude=points[i].Lat,Longitude=points[i].Lng,
                NomeVia=i==points.Length-1?"Destino fictício":$"Via de ensaio {i+1}",
                Instrucao=i==points.Length-1?"Fim do circuito de demonstração":"Siga o circuito sintético",
                ManobraTipo=i==0?"depart":i==points.Length-1?"arrive":"turn" });
        }
        // The only speed model used for this controlled estimate is 20 km/h.
        plan.TempoEstimadoMinutos = Math.Max(1,(int)Math.Ceiling((double)plan.DistanciaKm/20*60));
        return plan;
    }
    public static double Distance((double Lat,double Lng) a,(double Lat,double Lng) b)
    {
        const double rad=Math.PI/180;
        var h=Math.Pow(Math.Sin((b.Lat-a.Lat)*rad/2),2)+Math.Cos(a.Lat*rad)*Math.Cos(b.Lat*rad)*Math.Pow(Math.Sin((b.Lng-a.Lng)*rad/2),2);
        return 6371000*2*Math.Asin(Math.Sqrt(Math.Clamp(h,0,1)));
    }
    public async Task<GpsPositionPayload?> LatestAsync(int trip, CancellationToken ct=default)
    {
        var driver=await db.Entregas.Where(e=>e.Id==trip).Select(e=>e.MotoristaId).SingleAsync(ct);
        var row=await db.PosicoesGps.AsNoTracking().Where(p=>p.EntregaId==trip&&p.MotoristaId==driver&&p.Origem=="Simulacao"&&p.CapturaInformada)
            .OrderByDescending(p=>p.CapturadoEmUtc).ThenByDescending(p=>p.Id).FirstOrDefaultAsync(ct);
        if(row==null)return null;
        var code=await db.Entregas.Where(e=>e.Id==trip).Select(e=>e.CodigoRastreio).SingleAsync(ct);
        return GpsIngestionService.Receipt(row,code,"confirmado",false).Posicao;
    }
    public Task<TruckRoutePlan?> RecalculateRoadAsync(int trip,RouteTrust.RouteLocation position,RouteTrust.RouteLocation destination,int driver,CancellationToken ct)
        => planning==null?Task.FromResult<TruckRoutePlan?>(null):planning.CalculateAsync(trip,[position,destination],ct,expectedDriver:driver);
    public async Task<TruckRoutePlan> ForTripAsync(int trip, CancellationToken ct=default)
    {
        if(planning!=null && await planning.ReadAsync(trip,ct) is {} road) {
            road.SimulationEnabled=Enabled;
            return road;
        }
        // Shared destination does not erase the last selected circuit after reload.
        var driver=await db.Entregas.Where(e=>e.Id==trip).Select(e=>e.MotoristaId).SingleAsync(ct);
        var lastBranch=await db.PosicoesGps.AsNoTracking().Where(p=>p.EntregaId==trip&&p.MotoristaId==driver&&p.Origem=="Simulacao"&&p.CapturaInformada
            &&(p.Latitude!=Circuit[Circuit.Length-1].Lat||p.Longitude!=Circuit[Circuit.Length-1].Lng))
            .OrderByDescending(p=>p.CapturadoEmUtc).ThenByDescending(p=>p.Id).FirstOrDefaultAsync(ct);
        bool detour=lastBranch!=null&&Detour.Take(Detour.Length-1).Any(p=>p.Lat==lastBranch.Latitude&&p.Lng==lastBranch.Longitude);
        var fault=configuration["RoutingDemo:Failure"];
        return Plan(detour,fault=="missing"?"A demonstração está sem rota.":fault=="provider"?"Provedor de demonstração indisponível.":null);
    }
}
