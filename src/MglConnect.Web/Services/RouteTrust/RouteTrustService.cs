using System.Data;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MglConnect.Web.Data;
using MglConnect.Web.Models;
namespace MglConnect.Web.Services.RouteTrust;

public interface IRouteEvidenceSource { RouteEvidence Read(); }
public sealed class ConfiguredRouteEvidenceSource(IConfiguration config,IWebHostEnvironment env,ILogger<ConfiguredRouteEvidenceSource> logger):IRouteEvidenceSource
{
    public RouteEvidence Read()
    {
        if(config.GetValue<bool>("Routing:Valhalla:Enabled"))
            return new(){Provider="Valhalla experimental",RoutingProfile="truck",SourceReference="Adaptador 7B.1; configuração "+RouteFingerprint.Hash(config["Routing:Valhalla:BaseUrl"])+"; sem correspondência viária/cobertura verificadas."};
        var path=config["RouteTrust:FixturePath"];
        if(env.IsDevelopment()&&!string.IsNullOrWhiteSpace(path))
        {
            try
            {
                if(new FileInfo(path).Length>2*1024*1024)throw new InvalidDataException();
                var evidence=JsonSerializer.Deserialize<RouteEvidence>(File.ReadAllText(path))??throw new InvalidDataException();
                if(!evidence.Synthetic||evidence.Rules==null||evidence.Rules.Count>2000||evidence.Rules.Any(r=>r==null))throw new InvalidDataException();
                if(evidence.RecordedPlan!=null)
                {
                    if(evidence.RecordedPlan.TrajetoCoordenadas==null||evidence.RecordedPlan.TrajetoCoordenadas.Count>20000)throw new InvalidDataException();
                    if(evidence.RecordedPlan.TrajetoCoordenadas.Any(p=>p==null||!double.IsFinite(p.Latitude)||!double.IsFinite(p.Longitude)))throw new InvalidDataException();
                    var capturedHash=RouteFingerprint.Geometry(evidence.RecordedPlan.TrajetoCoordenadas);
                    // Explicit fixture binding, never used by a production adapter.
                    evidence.Rules=evidence.Rules.Select(r=>r.GeometrySha256=="__FIXTURE_CAPTURE__"?r with{GeometrySha256=capturedHash}:r).ToList();
                }
                return evidence;
            }
            catch(Exception ex) when(ex is IOException or JsonException or InvalidDataException or UnauthorizedAccessException)
            {logger.LogWarning("Fonte de restrições de ensaio indisponível ({ErrorType}).",ex.GetType().Name);}
        }
        var host=Uri.TryCreate(config["Routing:OsrmBaseUrl"],UriKind.Absolute,out var uri)?uri.Host:null;
        return new(){Provider=host==null?"Não configurado":"OSRM-compatível ("+host+")",SourceReference="Routing:OsrmBaseUrl; sem base de restrições verificável."};
    }
}
public sealed record RouteCalculationTicket(Guid Id,int Trip,Guid Tenant,int? Actor,int? Vehicle,string? Plate,int VehicleVersion,string? ProfileJson,string Binding,RouteEvidence Evidence,string? PermanentSnapshot=null);
public sealed class RouteTrustService(MglDbContext db,IRouteEvidenceSource source,RestrictionEngine engine,IHttpContextAccessor? http=null,TimeProvider? clock=null)
{
    private DateTime Now=>(clock??TimeProvider.System).GetUtcNow().UtcDateTime;
    public Task<Entrega?> Delivery(int id,CancellationToken ct=default)=>db.Entregas.AsNoTracking().Include(e=>e.Pedido).Include(e=>e.Veiculo).SingleOrDefaultAsync(e=>e.Id==id,ct);
    public async Task<RouteCalculationTicket> BeginAsync(int trip,int? actor=null,CancellationToken ct=default)
    {
        actor??=int.TryParse(http?.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier),out var user)?user:null;
        var evidence=JsonSerializer.Deserialize<RouteEvidence>(JsonSerializer.Serialize(source.Read()))!;
        await using var reservation=await db.Database.BeginTransactionAsync(IsolationLevel.Serializable,ct);
        var delivery=await Delivery(trip,ct)??throw new InvalidOperationException("Viagem indisponível.");
        if(actor.HasValue&&!await Allowed(actor.Value,delivery,ct))throw new UnauthorizedAccessException();
        var id=Guid.NewGuid();
        await db.Entregas.Where(e=>e.Id==trip&&e.TenantId==delivery.TenantId).ExecuteUpdateAsync(s=>s.SetProperty(e=>e.RotaCalculoAtualId,(Guid?)id),ct);
        if(actor.HasValue)db.AuditoriaOperacional.Add(new(){TenantId=delivery.TenantId,AtorUsuarioId=actor.Value,Evento="RotaCalculoSolicitado",TipoEntidade="Entrega",IdentificadorEntidade=trip.ToString(),Detalhes=$"Solicitação {id}",OcorridoEmUtc=Now});
        await db.SaveChangesAsync(ct);await reservation.CommitAsync(ct);
        return new(id,trip,delivery.TenantId,actor,delivery.VeiculoId,delivery.Veiculo?.Placa,TripProfiles.Revision(delivery),JsonSerializer.Serialize(TripProfiles.Read(delivery)),RouteFingerprint.Binding(delivery),evidence,delivery.Veiculo==null?null:TripProfiles.VehicleSnapshot(delivery.Veiculo));
    }
    public async Task<RouteTrustResult> FinishAsync(RouteCalculationTicket ticket,TruckRoutePlan plan,CancellationToken ct=default,ProviderRouteReply? providerReply=null)
    {
        await using var transaction=await db.Database.BeginTransactionAsync(IsolationLevel.Serializable,ct);
        var delivery=await Delivery(ticket.Trip,ct)??throw new InvalidOperationException("Viagem indisponível.");
        var profile=OperationalTruckProfile.Read(ticket.ProfileJson);
        if(profile.TripId!=ticket.Trip)profile=new();
        var evaluatedEvidence=providerReply?.Evidence()??ticket.Evidence;
        var result=engine.Evaluate(profile,plan,evaluatedEvidence,new DateTimeOffset(Now));result.CalculationId=ticket.Id;result.VehicleVersion=ticket.VehicleVersion;result.VehicleId=ticket.Vehicle;result.VehiclePlate=ticket.Plate;
        result.PermanentVehicleSnapshot=ticket.PermanentSnapshot;
        result.SourcePolicyHash=RouteFingerprint.Rules(ticket.Evidence);result.ProviderReply=providerReply;
        if(providerReply!=null){result.NavigationAllowed=false;result.Reasons.Add(providerReply.Detail);if(result.Confidence==RouteConfidence.Validada)result.Confidence=RouteConfidence.Estimada;}
        if(delivery.Pedido is {} cargo&&cargo.PesoKg>0&&profile.Payload is {} actual&&Math.Abs(cargo.PesoKg-actual.Value)>.01m)
        {
            result.Checks.Add(new("coerencia-carga",RestrictionKind.GrossWeight,RestrictionOutcome.Desconhecido,"Carga efetiva declarada diverge do peso do pedido; reconcilie os dados.","Pedido/perfil declarados","7A-1",0,Math.Max(0,result.Geometry.Count-2)));
            result.Reasons.Add("Massa do pedido e carga efetiva divergentes.");result.NavigationAllowed=false;
            if(result.Confidence==RouteConfidence.Validada)result.Confidence=RouteConfidence.Estimada;
        }
        result.Obsolete=delivery.TenantId!=ticket.Tenant||delivery.RotaCalculoAtualId!=ticket.Id||RouteFingerprint.Binding(delivery)!=ticket.Binding
            ||RouteFingerprint.Rules(source.Read())!=RouteFingerprint.Rules(ticket.Evidence)||(ticket.Actor.HasValue&&!await Allowed(ticket.Actor.Value,delivery,ct));
        if(result.Obsolete){result.NavigationAllowed=false;result.Reasons.Add("Avaliação obsoleta: dados/atribuição/fonte mudaram durante o cálculo.");}
        var expires=Now.AddMinutes(5);
        if(ticket.Evidence.Rules.Count>0)expires=new[]{expires,ticket.Evidence.Rules.Min(r=>r.ValidUntil.UtcDateTime)}.Min();
        result.ValidUntilUtc=expires;
        if(delivery.Veiculo?.Ativo!=true||!delivery.AtribuicaoAtiva||delivery.StatusEntrega!="Em transporte")
        {result.NavigationAllowed=false;result.Reasons.Add("Orientação exige viagem em transporte e veículo ativo.");}
        db.RotaAvaliacoes.Add(new(){Id=ticket.Id,TenantId=ticket.Tenant,EntregaId=ticket.Trip,VeiculoId=ticket.Vehicle,AtorUsuarioId=ticket.Actor,
            VinculoHash=ticket.Binding,RegrasHash=result.RulesVersion,CriadoEmUtc=Now,ExpiraEmUtc=expires,Obsoleta=result.Obsolete,ResultadoJson=JsonSerializer.Serialize(result)});
        if(ticket.Actor.HasValue)db.AuditoriaOperacional.Add(new(){TenantId=ticket.Tenant,AtorUsuarioId=ticket.Actor.Value,Evento="RotaAvaliada",TipoEntidade="Entrega",IdentificadorEntidade=ticket.Trip.ToString(),Detalhes=$"Avaliação {ticket.Id}; classe {result.Confidence}; obsoleta {result.Obsolete}",OcorridoEmUtc=Now});
        await db.SaveChangesAsync(ct);await transaction.CommitAsync(ct);return result;
    }
    public async Task<RouteTrustResult?> CurrentAsync(int trip,CancellationToken ct=default)
    {
        var delivery=await Delivery(trip,ct);if(delivery==null)return null;
        var record=await db.RotaAvaliacoes.AsNoTracking().Where(a=>a.EntregaId==trip&&a.TenantId==delivery.TenantId).OrderByDescending(a=>a.Id==delivery.RotaCalculoAtualId).ThenByDescending(a=>a.CriadoEmUtc).ThenByDescending(a=>a.Id).FirstOrDefaultAsync(ct);
        if(record==null)return null;
        RouteTrustResult result;
        try
        {
            result=JsonSerializer.Deserialize<RouteTrustResult>(record.ResultadoJson)??throw new JsonException();
            if(result.CalculationId!=record.Id||result.Geometry==null||result.Checks==null||result.Reasons==null||result.Vehicle==null||result.MissingData==null||result.Steps==null)throw new JsonException();
        }
        catch(JsonException){return new(){CalculationId=record.Id,Confidence=RouteConfidence.Indisponivel,Obsolete=true,Reasons=["Registro de avaliação não verificável; solicite novo cálculo."]};}
        if(record.Obsoleta||record.Id!=delivery.RotaCalculoAtualId||record.ExpiraEmUtc<=Now||record.VinculoHash!=RouteFingerprint.Binding(delivery)||(result.SourcePolicyHash??record.RegrasHash)!=RouteFingerprint.Rules(source.Read()))
        {result.Obsolete=true;result.NavigationAllowed=false;result.Reasons.Add("Avaliação vencida ou incompatível com os dados atuais. Solicite novo cálculo.");}
        if(!result.Obsolete)
        {
            var profile=TripProfiles.Read(delivery);
            var fresh=engine.Evaluate(profile,new TruckRoutePlan{RotaDisponivel=result.Geometry.Count>1,TrajetoCoordenadas=result.Geometry},result.ProviderReply?.Evidence()??source.Read(),new DateTimeOffset(Now));
            if(result.Confidence==RouteConfidence.Homologada||result.Synthetic!=fresh.Synthetic||result.Confidence==RouteConfidence.Validada&&fresh.Confidence!=RouteConfidence.Validada
                ||RouteFingerprint.Hash(profile)!=RouteFingerprint.Hash(result.Vehicle))
            {result.Obsolete=true;result.Reasons.Add("Verificação atual não corresponde ao registro persistido.");}
            result.NavigationAllowed=result.ProviderReply==null&&result.NavigationAllowed&&fresh.NavigationAllowed&&!result.Obsolete&&delivery.Veiculo?.Ativo==true&&delivery.AtribuicaoAtiva&&delivery.StatusEntrega=="Em transporte";
        }
        return result;
    }
    public async Task<RouteTrustResult?> CachedForReadAsync(int trip,CancellationToken ct=default)
    {
        if(http?.HttpContext?.Request.Method!="GET")return null;
        var current=await CurrentAsync(trip,ct);
        if(current==null||current.Obsolete||Now-current.CalculatedAtUtc>TimeSpan.FromSeconds(20))return null;
        var delivery=await Delivery(trip,ct);
        if(delivery==null||!int.TryParse(http.HttpContext.User.FindFirstValue(ClaimTypes.NameIdentifier),out var actor)||!await Allowed(actor,delivery,ct))return null;
        return current;
    }
    public async Task<bool> NavigationAllowedAsync(int trip,CancellationToken ct=default)
    {var current=await CurrentAsync(trip,ct);return current is {NavigationAllowed:true,Obsolete:false,Synthetic:false,Confidence:RouteConfidence.Validada};}
    private Task<bool> Allowed(int id,Entrega delivery,CancellationToken ct)=>db.Usuarios.AnyAsync(u=>u.Id==id&&u.Ativo&&u.TenantId==delivery.TenantId
        &&(u.Perfil=="Admin"||u.Perfil=="Administrador"||u.Perfil=="Motorista"&&delivery.MotoristaId==id&&db.TurnosMotorista.Any(t=>t.EntregaId==delivery.Id&&t.MotoristaId==id&&t.Ativo)),ct);
}
