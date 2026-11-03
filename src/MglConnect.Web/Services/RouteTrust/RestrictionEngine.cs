namespace MglConnect.Web.Services.RouteTrust;

// Pure deterministic evaluation. No legal limits, network calls or proximity matching.
public sealed class RestrictionEngine
{
    public RouteTrustResult Evaluate(OperationalTruckProfile vehicle,TruckRoutePlan plan,RouteEvidence evidence,DateTimeOffset passage)
    {
        var coords=plan.TrajetoCoordenadas??[];var rules=evidence.Rules??[];
        bool geometry=plan.RotaDisponivel&&coords.Count is >=2 and <=20000
            &&coords.All(p=>p!=null&&double.IsFinite(p.Latitude)&&p.Latitude is >=-90 and <=90&&double.IsFinite(p.Longitude)&&p.Longitude is >=-180 and <=180)
            &&coords.Zip(coords.Skip(1)).Any(p=>p.First.Latitude!=p.Second.Latitude||p.First.Longitude!=p.Second.Longitude);
        var hash=geometry?RouteFingerprint.Geometry(coords):"";
        var result=new RouteTrustResult{Vehicle=vehicle,CalculatedAtUtc=passage.UtcDateTime,Provider=evidence.Provider,RoutingProfile=evidence.RoutingProfile,
            GeographicVersion=evidence.GeographicVersion,SourceReference=evidence.SourceReference,Territory=evidence.Territory,
            RulesVersion=RouteFingerprint.Rules(evidence),GeometrySha256=hash,Synthetic=evidence.Synthetic,
            Confidence=geometry?RouteConfidence.Estimada:RouteConfidence.Indisponivel,
            Geometry=geometry?coords:[],Steps=geometry?plan.PassosNavegacao:[],EstimatedDistanceKm=geometry&&plan.DistanciaKm>0?plan.DistanciaKm:null,
            EstimatedMinutes=geometry&&plan.TempoEstimadoMinutos>0?plan.TempoEstimadoMinutos:null};
        var invalid=vehicle.Validate();result.Reasons.AddRange(invalid);result.MissingData=vehicle.Missing();
        if(result.MissingData.Count>0)result.Reasons.Add("Perfil operacional incompleto: "+string.Join(", ",result.MissingData));
        if(string.IsNullOrWhiteSpace(evidence.GeographicVersion)||string.IsNullOrWhiteSpace(evidence.SourceReference)||string.IsNullOrWhiteSpace(evidence.Territory))result.Reasons.Add("Fonte geográfica, versão ou cobertura territorial não comprovadas.");
        if(rules.Count>2000)result.Reasons.Add("Fonte excede o limite de regras; avaliação incompleta.");
        if(rules.Any(r=>r==null))result.Checks.Add(new("fonte-malformada",RestrictionKind.Access,RestrictionOutcome.Desconhecido,"Regra malformada na fonte.","Não informada","Não informada",0,0));
        if(!geometry)result.Reasons.Add("Trajeto sem geometria válida; orientação e ETA operacional suspensos.");
        var signatures=rules.Where(r=>r!=null).Take(2000).Select(rule=>new{Rule=rule,Fingerprint=RuleSignature(rule)}).ToArray();
        foreach(var entry in signatures)
        {
            var rule=entry.Rule;
            var outcome=RestrictionOutcome.Desconhecido;var reason="Fonte, validade ou associação ao segmento não comprovadas.";
            bool applies=Enum.IsDefined(rule.Kind)&&geometry&&rule.GeometrySha256==hash&&rule.FirstEdge>=0&&rule.LastEdge>=rule.FirstEdge&&rule.LastEdge<coords.Count-1
                &&!string.IsNullOrWhiteSpace(rule.Id)&&!string.IsNullOrWhiteSpace(rule.Source)&&!string.IsNullOrWhiteSpace(rule.Version)
                &&!string.IsNullOrWhiteSpace(rule.Territory)&&rule.Territory==evidence.Territory&&rule.ValidFrom<=passage&&passage<=rule.ValidUntil;
            if(applies)
            {
                var conflicts=signatures.Any(other=>!ReferenceEquals(other.Rule,rule)&&other.Rule.Kind==rule.Kind&&other.Rule.GeometrySha256==hash
                    &&other.Rule.Territory==rule.Territory&&other.Rule.FirstEdge<=rule.LastEdge&&other.Rule.LastEdge>=rule.FirstEdge
                    &&other.Rule.ValidFrom<=passage&&passage<=other.Rule.ValidUntil&&other.Fingerprint!=entry.Fingerprint);
                if(conflicts)reason="Conflito entre fontes aplicáveis; não é possível aprovar o segmento.";
                else if(invalid.Count>0)reason="Perfil operacional inconsistente.";
                else (outcome,reason)=Check(vehicle,rule,passage);
            }
            result.Checks.Add(new(rule.Id,rule.Kind,outcome,reason,rule.Source,rule.Version,rule.FirstEdge,rule.LastEdge));
        }
        // Every edge and every required dimension must have explicit, applicable evidence.
        foreach(var kind in Enum.GetValues<RestrictionKind>())
        {
            int through=-1;
            foreach(var interval in result.Checks.Where(c=>c.Kind==kind&&c.Outcome!=RestrictionOutcome.Desconhecido).OrderBy(c=>c.FirstEdge))
            {if(interval.FirstEdge>through+1)break;through=Math.Max(through,interval.LastEdge);}
            bool covered=geometry&&through>=coords.Count-2;
            if(!covered)result.Checks.Add(new("cobertura-"+kind,kind,RestrictionOutcome.Desconhecido,"Evidência insuficiente ou conflitante nos segmentos para esta restrição.",evidence.SourceReference??"Não informada",evidence.GeographicVersion??"Não informada",0,Math.Max(0,coords.Count-2)));
        }
        if(result.Checks.Any(c=>c.Outcome==RestrictionOutcome.Incompativel))result.Reasons.Add("Restrição incompatível: não iniciar orientação por este trajeto.");
        if(result.Checks.Any(c=>c.Outcome==RestrictionOutcome.Desconhecido))result.Reasons.Add("Informações insuficientes: consulte a operação.");
        bool sufficient=geometry&&invalid.Count==0&&result.MissingData.Count==0&&rules.Count<=2000&&result.Checks.All(c=>c.Outcome==RestrictionOutcome.Compativel)
            &&!string.IsNullOrWhiteSpace(evidence.Provider)&&!string.IsNullOrWhiteSpace(evidence.GeographicVersion)&&!string.IsNullOrWhiteSpace(evidence.SourceReference)&&!string.IsNullOrWhiteSpace(evidence.Territory);
        if(sufficient)result.Confidence=RouteConfidence.Validada;
        result.NavigationAllowed=sufficient&&!evidence.Synthetic&&!rules.Any(r=>r.Kind==RestrictionKind.Time&&!r.ExplicitlyUnrestricted);
        if(sufficient&&rules.Any(r=>r.Kind==RestrictionKind.Time&&!r.ExplicitlyUnrestricted))result.Reasons.Add("Janela verificada somente no instante informado; horários futuros de passagem não comprovados.");
        if(evidence.Synthetic)result.Reasons.Add("ENSAIO SINTÉTICO: restrições de teste; não autoriza navegação real.");
        return result;
    }
    private static string RuleSignature(RestrictionRule r)=>RouteFingerprint.Hash(new{
        Maximum=r.Maximum?.ToString("G29",System.Globalization.CultureInfo.InvariantCulture),r.Unit,
        Allowed=r.Allowed?.Select(s=>s?.ToUpperInvariant()).Distinct().Order().ToArray(),r.RequiredPermit,
        r.WindowStartMinute,r.WindowEndMinute,r.TimeZone,r.ExplicitlyUnrestricted});
    private static (RestrictionOutcome,string) Check(OperationalTruckProfile p,RestrictionRule r,DateTimeOffset at)
    {
        (RestrictionOutcome,string) Known(bool allowed)=>(allowed?RestrictionOutcome.Compativel:RestrictionOutcome.Incompativel,allowed?"Compatível com a regra informada, neste segmento e instante.":"Perfil incompatível com a regra informada.");
        if(r.Allowed?.Any(string.IsNullOrWhiteSpace)==true)return(RestrictionOutcome.Desconhecido,"Categorias inválidas na fonte.");
        if(r.ExplicitlyUnrestricted)return r.Maximum.HasValue||r.Allowed!=null||r.RequiredPermit!=null||r.WindowStartMinute.HasValue||r.WindowEndMinute.HasValue?(RestrictionOutcome.Desconhecido,"Fonte contraditória: ausência de restrição junto com condição explícita."):Known(true); // Must still pass source, geometry, time and complete coverage checks.
        Measure? value=r.Kind switch{RestrictionKind.Height=>p.Height,RestrictionKind.Width=>p.Width,RestrictionKind.Length=>p.Length,
            RestrictionKind.GrossWeight=>p.Gross,RestrictionKind.AxleLoad=>p.AxleLoads.Count>0?p.AxleLoads.MaxBy(m=>m.Value):null,_=>null};
        if(r.Kind is RestrictionKind.Height or RestrictionKind.Width or RestrictionKind.Length or RestrictionKind.GrossWeight or RestrictionKind.AxleLoad)
        {
            var unit=r.Kind is RestrictionKind.Height or RestrictionKind.Width or RestrictionKind.Length?"m":"kg";
            return value!=null&&r.Maximum>0&&r.Unit==unit&&value.Unit==unit?Known(value.Value<=r.Maximum):(RestrictionOutcome.Desconhecido,"Medida/unidade do veículo ou limite não informado.");
        }
        if(r.Kind==RestrictionKind.AxleCount)return p.Axles.HasValue&&r.Maximum>0&&decimal.Truncate(r.Maximum.Value)==r.Maximum.Value&&r.Unit=="eixos"?Known(p.Axles<=r.Maximum):(RestrictionOutcome.Desconhecido,"Número de eixos/limite não informado.");
        if(r.Kind==RestrictionKind.Category)return !string.IsNullOrWhiteSpace(p.Category)&&r.Allowed?.Length>0?Known(r.Allowed.Contains(p.Category,StringComparer.OrdinalIgnoreCase)):(RestrictionOutcome.Desconhecido,"Categoria/possibilidades de acesso desconhecidas.");
        if(r.Kind==RestrictionKind.Access)return p.PermitsDeclared==true&&!string.IsNullOrWhiteSpace(r.RequiredPermit)?Known(p.Permits.Contains(r.RequiredPermit,StringComparer.Ordinal)):(RestrictionOutcome.Desconhecido,"Condição de acesso não especificada.");
        if(r.Kind==RestrictionKind.Hazardous)return p.Hazardous.HasValue&&r.Allowed?.Length>0
            ?Known(r.Allowed.Contains(p.Hazardous.Value?p.HazardClass??"desconhecida":"nao-perigosa",StringComparer.OrdinalIgnoreCase)):(RestrictionOutcome.Desconhecido,"Carga perigosa/regra não classificada.");
        if(r.Kind==RestrictionKind.Time)
        {
            if(r.WindowStartMinute is not (>=0 and <1440)||r.WindowEndMinute is not (>=0 and <1440)||string.IsNullOrWhiteSpace(r.TimeZone))return(RestrictionOutcome.Desconhecido,"Janela/fuso não informados.");
            try{var local=TimeZoneInfo.ConvertTime(at,TimeZoneInfo.FindSystemTimeZoneById(r.TimeZone));var minute=local.Hour*60+local.Minute;
                return Known(r.WindowStartMinute<=r.WindowEndMinute?minute>=r.WindowStartMinute&&minute<r.WindowEndMinute:minute>=r.WindowStartMinute||minute<r.WindowEndMinute);}
            catch(TimeZoneNotFoundException){return(RestrictionOutcome.Desconhecido,"Fuso horário não reconhecido.");}
            catch(InvalidTimeZoneException){return(RestrictionOutcome.Desconhecido,"Fuso horário inválido.");}
        }
        return(RestrictionOutcome.Desconhecido,"Regra não suportada.");
    }
}
