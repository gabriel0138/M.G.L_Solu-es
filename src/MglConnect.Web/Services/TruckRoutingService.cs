using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using MglConnect.Web.Data;
using MglConnect.Web.Services.RouteTrust;

namespace MglConnect.Web.Services
{
    public class TruckRoutingService : ITruckRoutingService
    {
        private const double CajamarDepotLatitude = -23.355797;
        private const double CajamarDepotLongitude = -46.877658;
        private static readonly PublicApiThrottle PhotonThrottle = new(TimeSpan.FromSeconds(1));
        private ProviderRouteReply? _lastOsrmReply;
        private readonly OsrmRoadRouter? _osrm;

        private readonly IConfiguration _configuration;
        private readonly MglDbContext _context;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IMemoryCache _routeCache;
        private readonly ILogger<TruckRoutingService> _logger;
        private readonly IRouteDeviationEngine _deviationEngine;
        private readonly RouteTrustService? _trust;
        private readonly ControlledRouteDemo? _demo;
        private readonly IExperimentalTruckRouter? _experimental;
        private readonly IHttpContextAccessor? _request;
        private CancellationToken Abort=>_request?.HttpContext?.RequestAborted??CancellationToken.None;

        public TruckRoutingService(
            MglDbContext context, 
            IHttpClientFactory httpClientFactory, 
            IMemoryCache routeCache,
            IConfiguration configuration,
            ILogger<TruckRoutingService> logger,
            IRouteDeviationEngine deviationEngine, RouteTrustService? trust = null,IHttpContextAccessor? request = null,IExperimentalTruckRouter? experimental = null, ControlledRouteDemo? demo = null, OsrmRoadRouter? osrm = null)
        {
            _context = context;
            _httpClientFactory = httpClientFactory;
            _routeCache = routeCache;
            _configuration = configuration;
            _logger = logger;
            _deviationEngine = deviationEngine;
            _trust = trust;_request=request;_experimental=experimental;_demo=demo;_osrm=osrm;

            // Validar gabarito físico imediatamente após injeção de dependência
            // (para garantir que parâmetros padrão sejam consistentes)
            ValidarGabaritoPesado();
        }

        private async Task<(double Latitude, double Longitude)?> GeocodificarEnderecoAsync(string endereco)
        {
            if (string.IsNullOrWhiteSpace(endereco))
                return null;

            if (endereco.Contains("CD", StringComparison.OrdinalIgnoreCase)
                && endereco.Contains("Cajamar", StringComparison.OrdinalIgnoreCase))
            {
                return (CajamarDepotLatitude, CajamarDepotLongitude);
            }

            var apiKey = _configuration["GoogleMaps:GeocodingApiKey"]
                ?? _configuration["GoogleMaps:ApiKey"]
                ?? _configuration["GOOGLE_MAPS_API_KEY"]
                ?? Environment.GetEnvironmentVariable("GOOGLE_MAPS_API_KEY");
            var usarGoogle = !string.IsNullOrWhiteSpace(apiKey) && apiKey != "SUA_CHAVE_AQUI";
            var photonEnabled = bool.TryParse(_configuration["Geocoding:PhotonEnabled"], out var enabled)
                && enabled;
            if (!usarGoogle && !photonEnabled)
            {
                _logger.LogInformation("Geocodificação indisponível. Habilite Photon apenas para desenvolvimento ou configure Google Geocoding.");
                return null;
            }

            var provider = usarGoogle ? "google" : "photon";
            var cacheKey = $"geocoding_{provider}_{endereco.Trim().ToUpperInvariant()}";
            if (_routeCache.TryGetValue<(double Latitude, double Longitude)>(cacheKey, out var cachedCoordinate))
            {
                return cachedCoordinate;
            }

            var coordinate = usarGoogle
                ? await GeocodificarComGoogleAsync(endereco, apiKey!)
                : await GeocodificarComPhotonAsync(endereco);

            if (coordinate.HasValue)
            {
                _routeCache.Set(cacheKey, coordinate.Value, TimeSpan.FromDays(7));
            }

            return coordinate;
        }

        private async Task<(double Latitude, double Longitude)?> GeocodificarComGoogleAsync(string endereco, string apiKey)
        {
            try
            {
                var client = _httpClientFactory.CreateClient();
                client.Timeout = TimeSpan.FromSeconds(8);
                var url = "https://maps.googleapis.com/maps/api/geocode/json?region=br&components=country:BR&address="
                    + Uri.EscapeDataString(endereco.Trim())
                    + "&key="
                    + Uri.EscapeDataString(apiKey);
                using var response = await client.GetAsync(url,Abort);

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Google Geocoding API retornou HTTP {StatusCode} para o endereço da rota.", response.StatusCode);
                    return null;
                }

                using var stream = await response.Content.ReadAsStreamAsync();
                using var document = await JsonDocument.ParseAsync(stream);
                var root = document.RootElement;
                var status = root.TryGetProperty("status", out var statusElement) ? statusElement.GetString() : null;
                if (status != "OK" || !root.TryGetProperty("results", out var results) || results.GetArrayLength() == 0)
                {
                    _logger.LogWarning("Google Geocoding API não encontrou coordenadas. Status: {Status}", status);
                    return null;
                }

                var first = results[0];
                if (!first.TryGetProperty("geometry", out var geometry)
                    || !geometry.TryGetProperty("location", out var location)
                    || !location.TryGetProperty("lat", out var latitudeElement)
                    || !location.TryGetProperty("lng", out var longitudeElement)
                    || !latitudeElement.TryGetDouble(out var latitude)
                    || !longitudeElement.TryGetDouble(out var longitude)
                    || !double.IsFinite(latitude) || latitude is < -34 or > 6
                    || !double.IsFinite(longitude) || longitude is < -75 or > -34)
                {
                    _logger.LogWarning("Google Geocoding API retornou coordenadas inválidas para o endereço informado.");
                    return null;
                }

                return (latitude, longitude);
            }
            catch (HttpRequestException ex)
            {
                _logger.LogWarning("Não foi possível consultar Google Geocoding API ({ErrorType}).", ex.GetType().Name);
            }
            catch (TaskCanceledException ex)
            {
                _logger.LogWarning("Tempo esgotado ao consultar Google Geocoding API ({ErrorType}).", ex.GetType().Name);
            }
            catch (JsonException ex)
            {
                _logger.LogWarning("Resposta inválida da Google Geocoding API ({ErrorType}).", ex.GetType().Name);
            }

            return null;
        }

        private async Task<(double Latitude, double Longitude)?> GeocodificarComPhotonAsync(string endereco)
        {
            var baseUrl = _configuration["Geocoding:PhotonBaseUrl"] ?? "https://photon.komoot.io";
            if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri)
                || baseUri.Scheme != Uri.UriSchemeHttps)
            {
                _logger.LogWarning("A URL do Photon deve ser uma URL HTTPS absoluta.");
                return null;
            }

            var url = $"{baseUrl.TrimEnd('/')}/api/?q={Uri.EscapeDataString(endereco.Trim())}"
                + "&limit=1&bbox=-75,-34,-34,6";

            try
            {
                var client = _httpClientFactory.CreateClient();
                client.Timeout = TimeSpan.FromSeconds(8);
                client.DefaultRequestHeaders.UserAgent.ParseAdd("MglConnect-LogisticsEngine/2.0");
                using var response = baseUri.Host.Equals("photon.komoot.io", StringComparison.OrdinalIgnoreCase)
                    ? await PhotonThrottle.GetAsync(client, url,Abort)
                    : await client.GetAsync(url,Abort);

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Photon retornou HTTP {StatusCode} para a geocodificação.", response.StatusCode);
                    return null;
                }

                using var stream = await response.Content.ReadAsStreamAsync();
                using var document = await JsonDocument.ParseAsync(stream);
                var root = document.RootElement;
                if (!root.TryGetProperty("features", out var features)
                    || features.ValueKind != JsonValueKind.Array
                    || features.GetArrayLength() == 0
                    || !features[0].TryGetProperty("geometry", out var geometry)
                    || !geometry.TryGetProperty("coordinates", out var coordinates)
                    || coordinates.ValueKind != JsonValueKind.Array
                    || coordinates.GetArrayLength() < 2
                    || !coordinates[0].TryGetDouble(out var longitude)
                    || !coordinates[1].TryGetDouble(out var latitude)
                    || !CoordenadaDentroDoBrasil(latitude, longitude))
                {
                    _logger.LogWarning("Photon não encontrou coordenadas válidas no Brasil para o endereço informado.");
                    return null;
                }

                return (latitude, longitude);
            }
            catch (HttpRequestException ex)
            {
                _logger.LogWarning("Não foi possível consultar o serviço público Photon ({ErrorType}).", ex.GetType().Name);
            }
            catch (TaskCanceledException ex)
            {
                _logger.LogWarning("Tempo esgotado ao consultar o serviço público Photon ({ErrorType}).", ex.GetType().Name);
            }
            catch (JsonException ex)
            {
                _logger.LogWarning("Resposta inválida do serviço público Photon ({ErrorType}).", ex.GetType().Name);
            }

            return null;
        }

        private static bool CoordenadaDentroDoBrasil(double latitude, double longitude) =>
            double.IsFinite(latitude) && latitude is >= -34 and <= 6
            && double.IsFinite(longitude) && longitude is >= -75 and <= -34;

        private sealed class PublicApiThrottle
        {
            private readonly SemaphoreSlim _gate = new(1, 1);
            private readonly TimeSpan _minimumInterval;
            private DateTimeOffset _lastRequestAt = DateTimeOffset.MinValue;

            public PublicApiThrottle(TimeSpan minimumInterval)
            {
                _minimumInterval = minimumInterval;
            }

            public async Task<HttpResponseMessage> GetAsync(HttpClient client, string url,CancellationToken ct=default)
            {
                await _gate.WaitAsync(ct);
                try
                {
                    var remainingDelay = _minimumInterval - (DateTimeOffset.UtcNow - _lastRequestAt);
                    if (remainingDelay > TimeSpan.Zero)
                    {
                        await Task.Delay(remainingDelay,ct);
                    }

                    _lastRequestAt = DateTimeOffset.UtcNow;
                    return await client.GetAsync(url,ct);
                }
                finally
                {
                    _gate.Release();
                }
            }
        }

        // This provider has no authoritative truck restriction dataset.
        private void ValidarGabaritoPesado()
        {
            _logger.LogInformation("Roteamento genérico: limites físicos e restrições locais exigem validação independente.");
        }

        /// <summary>
        /// Registra alertas físicos sem sugerir que o traçado genérico do OSRM os validou.
        /// </summary>
        /// <param name="plano">Plano de rota a ser validado e ajustado.</param>
        /// <param name="alturaMetros">Altura do veículo em metros.</param>
        /// <param name="pesoToneladas">Peso Bruto Total (PBT) em toneladas.</param>
        /// <param name="qtdEixos">Contagem de eixos do veículo.</param>
        /// <param name="tipoCarga">Tipo da carga (afeta classificacao de risco).</param>
        /// <returns>Plano de rota ajustado com restrições aplicadas.</returns>
        private TruckRoutePlan AplicarRestricoesGabarito(
            TruckRoutePlan plano,
            decimal alturaMetros,
            decimal pesoToneladas,
            decimal larguraMetros,
            int qtdEixos,
            string tipoCarga)
        {
            // A malha OSRM não valida restrições físicas de veículos.
            if (alturaMetros > 4.10m)
            {
                plano.Alertas.Add(new TruckRestrictionAlert
                {
                    Tipo = "Altura",
                    Severidade = "Perigo",
                    Mensagem = $"Veículo com {alturaMetros:F2}m. A rota não verifica altura livre de pontes, túneis ou viadutos; confirme o percurso em fonte homologada antes de dirigir.",
                    Localizacao = "Verificar restrições ao longo de toda a rota"
                });
            }

            // 2. VALIDACAO DE PESO E EIXOS: > 23t PBT + muitos eixos => Evitar ZERC zonas urbanas
            if (pesoToneladas > 23m || qtdEixos > 3)
            {
                // Adicionar alerta de peso e restrição de eixos
                string pesoMsg = pesoToneladas > 23m
                    ? $"Massa cadastrada ({pesoToneladas:F1}t; natureza não verificada) informado para conferência operacional. "
                    : string.Empty;

                string eixosMsg = qtdEixos > 3
                    ? $"Contagem de eixos ({qtdEixos}) informada para conferência operacional. "
                    : string.Empty;

                if (!string.IsNullOrWhiteSpace(pesoMsg) || !string.IsNullOrWhiteSpace(eixosMsg))
                {
                    plano.Alertas.Add(new TruckRestrictionAlert
                    {
                        Tipo = "Peso",
                        Severidade = "Atencao",
                        Mensagem = $"{pesoMsg}{eixosMsg}O traçado não verifica limites locais de peso, eixos ou zonas de circulação. Confirme as restrições aplicáveis antes de dirigir.",
                        Localizacao = "Verificar restrições ao longo de toda a rota"
                    });
                }
            }

            // 3. ALERTA DE LARGURA (sem validação da geometria das vias)
            if (larguraMetros > 2.50m)
            {
                plano.Alertas.Add(new TruckRestrictionAlert
                {
                    Tipo = "Largura",
                    Severidade = "Informativo",
                    Mensagem = $"Veículo com {larguraMetros:F2}m de largura. A rota não verifica largura da via; valide as restrições antes do deslocamento.",
                    Localizacao = "Verificar restrições ao longo de toda a rota"
                });
            }

            // 4. INSTRUCOES TURN-BY-TURN ENRIQUECIDAS (baseado nos steps do OSRM)
            this.EnriquecerInstrucoesTurnoPorTurno(plano);

            return plano;
        }

        /// <summary>
        /// Gera instruções de manobra detalhadas Turn-by-Turn baseadas na lista de steps do OSRM.
        /// Formato: "A 500m curve à direita na SP-160", "Siga em frente por 12 km".
        /// </summary>
        /// <param name="plano">Plano de rota a ser enriquecido com instrucoes.</param>
        private void EnriquecerInstrucoesTurnoPorTurno(TruckRoutePlan plano)
        {
            if (plano.PassosNavegacao == null || plano.PassosNavegacao.Count == 0)
            {
                // Fallback: instrucao basica a partir do primeiro step
                var primeiroStep = plano.PassosNavegacao?.FirstOrDefault();
                if (primeiroStep != null)
                {
                    plano.PassosNavegacao!.Clear();
                    plano.PassosNavegacao.Add(new RouteStep
                    {
                        Ordem = 1,
                        NomeVia = primeiroStep.NomeVia ?? "Via Principal",
                        Instrucao = $"Em {Math.Round(primeiroStep.DistanciaMetros / 1000.0, 1)} km, continue no percurso",
                        ManobraTipo = "depart",
                        ManobraModificador = "straight",
                        DistanciaMetros = primeiroStep.DistanciaMetros,
                        IconeBootstrap = primeiroStep.IconeBootstrap ?? "bi-arrow-up"
                    });
                }
                return;
            }

            // Transformar steps raw em instrucoes human-readables
            for (int i = 0; i < plano.PassosNavegacao.Count; i++)
            {
                var step = plano.PassosNavegacao[i];
                var distanciaRestanteKm = Math.Round((plano.DistanciaKm - (decimal)(i * 2.0)), 1); // approx

                // Determinar tipo de manobra a partir do ManobraModificador
                string instrucaoFormatada = step.ManobraModificador switch
                {
                    "right" => step.NomeVia != null && step.NomeVia.Length > 0
                                ? $"Curve à direita em {distanciaRestanteKm} km na {step.NomeVia}"
                                : $"Curve à direita em {distanciaRestanteKm} km",
                    "left" => step.NomeVia != null && step.NomeVia.Length > 0
                              ? $"Curve à esquerda em {distanciaRestanteKm} km na {step.NomeVia}"
                              : $"Curve à esquerda em {distanciaRestanteKm} km",
                    "on ramp" => step.NomeVia != null && step.NomeVia.Length > 0
                                 ? $"Acesse a via expressa {step.NomeVia} em {distanciaRestanteKm} km"
                                 : $"Acesse a via expressa em {distanciaRestanteKm} km",
                    "off ramp" => step.NomeVia != null && step.NomeVia.Length > 0
                                  ? $"Tome a saida {step.NomeVia} em {distanciaRestanteKm} km"
                                  : $"Tome a saida em {distanciaRestanteKm} km",
                    "fork" => step.NomeVia != null && step.NomeVia.Length > 0
                              ? $"Bifurque a direita em {distanciaRestanteKm} km na {step.NomeVia}"
                              : $"Bifurque à direita em {distanciaRestanteKm} km",
                    "merge" => step.NomeVia != null && step.NomeVia.Length > 0
                               ? $"Integre-se a {step.NomeVia} em {distanciaRestanteKm} km"
                               : $"Integre-se em {distanciaRestanteKm} km",
                    "depart" => step.NomeVia != null && step.NomeVia.Length > 0
                                ? $"Inicie a viagem acessando {step.NomeVia}"
                                : $"Inicie a viagem",
                    "arrive" => $"Voce chegou ao destino final da entrega",
                    _ => step.Instrucao ?? $"Continue no percurso por aproximadamente {distanciaRestanteKm} km"
                };

                // Atualizar a instrucao do step, mantendo dados originais
                step.Instrucao = instrucaoFormatada;
            }
        }

        // Método auxiliar: Converter coordenadas geodésicas complexas em polilinhas
        // fiéis às rodovias reais (sem linhas retas geodésicas puras).
        private List<RouteCoordinate> ConverterPolilinhaFielOSRM(List<RouteCoordinate> coordsBrutos)
        {
            if (coordsBrutos == null) return new List<RouteCoordinate>();
            if (coordsBrutos.Count <= 2) return coordsBrutos;

            var coordsFieis = new List<RouteCoordinate>();
            
            // Manter ponto de origem
            coordsFieis.Add(coordsBrutos[0]);

            // Amostragem inteligente: preservar curvas de rodovia, remover pontos colineares
            for (int i = 1; i < coordsBrutos.Count - 1; i++)
            {
                var pAnt = coordsBrutos[i - 1];
                var pAtual = coordsBrutos[i];
                var pProx = coordsBrutos[i + 1];

                // Calcular angulo entre pAnt-pAtual e pAtual-pProx
                double angulo1 = Math.Atan2(pAtual.Latitude - pAnt.Latitude, pAtual.Longitude - pAnt.Longitude) * 180 / Math.PI;
                double angulo2 = Math.Atan2(pProx.Latitude - pAtual.Latitude, pProx.Longitude - pAtual.Longitude) * 180 / Math.PI;

                // Se a mudança de direcao for menor que 15 graus, este ponto e colinear (pode ser removido)
                double diffAngulo = Math.Abs(angulo2 - angulo1);
                if (diffAngulo < 15.0)
                {
                    // Ponto colinear - pular (manter suavidade da polilinha)
                    continue;
                }

                // Ponto de curva - manter para fidelidade ao tracado rodoviario
                coordsFieis.Add(pAtual);
            }

            // Manter ponto de destino
            if (coordsBrutos.Count > 0)
                coordsFieis.Add(coordsBrutos[coordsBrutos.Count - 1]);

            // Se removemos tantos pontos que a polilina ficou muito simplificada, re-inserir alguns
            if (coordsFieis.Count < coordsBrutos.Count * 0.6)
            {
                // Retornar original se muito simplificado
                return coordsBrutos;
            }

            return coordsFieis;
        }

        public async Task<TruckRoutePlan> CalcularRotaParaEntregaAsync(int entregaId, string perfilVeiculo = "Pesado")
        {
            var entrega = await _context.Entregas
                .Include(e => e.Pedido)
                .Include(e => e.Veiculo)
                .FirstOrDefaultAsync(e => e.Id == entregaId);

            if (entrega == null)
            {
                throw new ArgumentException("Entrega não encontrada.");
            }

            if (_demo?.Enabled == true) return await _demo.ForTripAsync(entregaId, Abort);

            var cachedTrust=_trust==null?null:await _trust.CachedForReadAsync(entregaId);
            if(cachedTrust!=null)return new TruckRoutePlan{Confianca=cachedTrust,ProviderDurationSeconds=cachedTrust.ProviderDurationSeconds,ProviderResponseSha256=cachedTrust.ProviderResponseSha256,GeometrySourceReference=cachedTrust.GeometrySourceReference,ProviderName=cachedTrust.Provider,ProviderProfile=cachedTrust.RoutingProfile,GeometrySource=cachedTrust.GeometrySource??"Unavailable",PerfilVeiculo=perfilVeiculo,RotaDisponivel=cachedTrust.Geometry.Count>1,
                TrajetoCoordenadas=cachedTrust.Geometry,PassosNavegacao=cachedTrust.Steps,DistanciaKm=cachedTrust.EstimatedDistanceKm??0,TempoEstimadoMinutos=cachedTrust.EstimatedMinutes??0,
                AvisoRota=cachedTrust.NavigationAllowed?"Verificação limitada ao escopo indicado.":"Orientação suspensa: dados insuficientes, incompatíveis ou sintéticos."};
            var ticket = _trust == null ? null : await _trust.BeginAsync(entregaId,ct:Abort);
            var pedido = entrega.Pedido;
            var cidadeOrigem = pedido == null
                || string.IsNullOrWhiteSpace(pedido.OrigemCidade)
                || string.IsNullOrWhiteSpace(pedido.OrigemEstado)
                    ? null
                    : $"{pedido.OrigemCidade}/{pedido.OrigemEstado}";
            var origemPedido = string.Join(", ", new[]
            {
                pedido?.OrigemEndereco,
                pedido?.OrigemNumero,
                pedido?.OrigemBairro,
                cidadeOrigem,
                pedido?.OrigemCep,
                "Brasil"
            }.Where(parte => !string.IsNullOrWhiteSpace(parte)));
            var origem = string.IsNullOrWhiteSpace(pedido?.OrigemEndereco)
                ? "CD Logístico MGL Cajamar (SP)"
                : origemPedido;
            var destino = string.Join(", ", new[]
            {
                entrega.EnderecoDestino,
                entrega.NumeroDestino,
                entrega.BairroDestino,
                $"{entrega.CidadeDestino}/{entrega.EstadoDestino}",
                entrega.CepDestino,
                "Brasil"
            }.Where(parte => !string.IsNullOrWhiteSpace(parte)));

            var altura = entrega.Veiculo != null ? entrega.Veiculo.AlturaMetros : entrega.AlturaMetros;
            var peso = entrega.Veiculo != null ? entrega.Veiculo.PesoToneladas : entrega.PesoToneladas;
            var largura = entrega.Veiculo != null ? entrega.Veiculo.LarguraMetros : entrega.LarguraMetros;
            var eixos = entrega.Veiculo != null ? entrega.Veiculo.QtdEixos : entrega.QtdEixos;
            var carga = entrega.TipoCarga ?? "Carga Geral Seca";

            ProviderRouteReply? providerReply=null;
            TruckRoutePlan originalPlan;
            if(_experimental?.Enabled==true&&ticket!=null)
            {
                var physical=TruckPhysicalRequest.From(OperationalTruckProfile.Read(ticket.ProfileJson));
                if(physical==null)providerReply=new(){Failure=ProviderFailure.MissingVehicleData,Detail="Declare o perfil completo e as cargas por eixo; nenhum default será enviado ao provedor."};
                else
                {
                    var start=await GeocodificarEnderecoAsync(origem);var end=await GeocodificarEnderecoAsync(destino);
                    providerReply=start.HasValue&&end.HasValue
                        ?await _experimental.RouteAsync(new(new(start.Value.Latitude,start.Value.Longitude),new(end.Value.Latitude,end.Value.Longitude),physical),Abort)
                        :new(){Failure=ProviderFailure.MissingLocations,Detail="Geocodificação indisponível; não houve consulta ao Valhalla."};
                }
                originalPlan=providerReply.ToPlan();
            }
            else originalPlan = ticket?.Evidence.RecordedPlan ?? await CalcularRotaPesadaAsync(origem, destino, altura, peso, largura, eixos, carga, perfilVeiculo);
            // A cached raw geometry must never carry another trip's trust result.
            var plano = JsonSerializer.Deserialize<TruckRoutePlan>(JsonSerializer.Serialize(originalPlan))!;
            plano.RotaHomologadaParaVeiculo=false;plano.RotaAprovada=false;
            if(ticket!=null)plano.Confianca=await _trust!.FinishAsync(ticket,plano,Abort,providerReply);
            if(!string.Equals(perfilVeiculo,"Leve",StringComparison.OrdinalIgnoreCase)&&plano.Confianca?.NavigationAllowed!=true)
            {
                if(plano.Confianca?.Obsolete!=true)_deviationEngine.RemoveState(entrega.CodigoRastreio);
                plano.AvisoRota="Orientação de caminhão suspensa: avaliação de restrições insuficiente, incompatível ou sintética. Consulte a operação.";
                return plano;
            }

            // Registro automático no motor de detecção de desvios
            double destLat = -23.961794;
            double destLon = -46.332267;
            if (plano.TrajetoCoordenadas.Count > 0)
            {
                var ult = plano.TrajetoCoordenadas.Last();
                destLat = ult.Latitude;
                destLon = ult.Longitude;
            }

            _deviationEngine.RegisterActiveRoute(
                entrega.CodigoRastreio,
                entrega.Id,
                plano.PerfilVeiculo,
                plano.TrajetoCoordenadas,
                destLat,
                destLon,
                destino
            );

            return plano;
        }

        public async Task<TruckRoutePlan> ObterOuInicializarRotaAsync(string codigoRastreio, string perfilVeiculo = "Pesado")
        {
            var entrega = await _context.Entregas
                .FirstOrDefaultAsync(e => e.CodigoRastreio.ToUpper() == codigoRastreio.Trim().ToUpper());

            if (entrega == null)
            {
                throw new ArgumentException($"Entrega não encontrada para o código {codigoRastreio}.");
            }

            return await CalcularRotaParaEntregaAsync(entrega.Id, perfilVeiculo);
        }

        public async Task<TruckRoutePlan?> RecalcularRotaDinamicaAsync(
            string codigoRastreio, 
            double latAtual, 
            double lonAtual, 
            string perfilVeiculo = "Pesado")
        {
            if(_experimental?.Enabled==true)
            { _logger.LogWarning("Recálculo experimental suspenso: correspondência viária e cobertura ainda não verificadas."); return null; }
            var currentTrip=await _context.Entregas.AsNoTracking().Where(e=>e.CodigoRastreio==codigoRastreio).Select(e=>e.Id).SingleOrDefaultAsync(Abort);
            bool trusted=_trust!=null&&currentTrip>0&&await _trust.NavigationAllowedAsync(currentTrip,Abort);
            if((_trust!=null&&!trusted)||(!string.Equals(perfilVeiculo,"Leve",StringComparison.OrdinalIgnoreCase)&&!trusted))
            {
                _logger.LogWarning("Recálculo suspenso: não há avaliação atual suficiente para orientação operacional.");return null;
            }
            var ticket=_trust==null?null:await _trust.BeginAsync(currentTrip,ct:Abort);

            // Cache key for dynamic recalculation - includes current position for variance
            var cacheKey = $"rota_recalculo_{codigoRastreio.Trim().ToUpperInvariant()}_{latAtual:F4}_{lonAtual:F4}_{perfilVeiculo}";
            
            if (_trust==null&&_routeCache.TryGetValue(cacheKey, out TruckRoutePlan? cachedPlano))
            {
                _logger.LogInformation("Cache hit para recálculo de rota, perfil {Perfil}.", perfilVeiculo);
                return cachedPlano;
            }

            var key = codigoRastreio.Trim().ToUpperInvariant();
            var state = _deviationEngine.GetState(key);
            if (state == null || state.DestinoLatitude == 0)
            {
                _logger.LogWarning("Não foi possível recalcular a rota: destino não foi inicializado.");
                return null;
            }

            _logger.LogInformation("Solicitando recálculo de rota leve; coordenadas omitidas dos logs.");

            var waypoints = string.Format(CultureInfo.InvariantCulture,
                "{0:F6},{1:F6};{2:F6},{3:F6}",
                lonAtual, latAtual, state.DestinoLongitude, state.DestinoLatitude);

            var rotaGeo = await ObterRotaOsrmPorWaypointsAsync(waypoints);
            if (rotaGeo == null)
            {
                _logger.LogWarning("O provedor de roteamento não retornou uma rota alternativa.");
                return null;
            }

            bool isPesado = !string.Equals(perfilVeiculo, "Leve", StringComparison.OrdinalIgnoreCase);
            var plano = new TruckRoutePlan
            {
                Origem = $"Posição Atual ({latAtual:F4}, {lonAtual:F4})",
                Destino = state.DestinoNome,
                PerfilVeiculo = isPesado ? "Pesado" : "Leve",
                CorredorRodoviario = "Nova rota recalculada a partir da posição atual",
                DistanciaKm = rotaGeo.Value.distanciaKm,
                TempoEstimadoMinutos = rotaGeo.Value.tempoMinutos,
                ProviderName=_lastOsrmReply?.Provider??"OSRM",ProviderProfile="driving",GeometrySource=_lastOsrmReply?.GeometrySource??"RoadNetwork",ProviderDurationSeconds=_lastOsrmReply?.DurationSeconds,ProviderResponseSha256=_lastOsrmReply?.ResponseSha256,GeometrySourceReference=_lastOsrmReply?.SourceReference,
                TrajetoCoordenadas = rotaGeo.Value.coords,
                PassosNavegacao = rotaGeo.Value.passos,
                RotaDisponivel = true,
                RotaHomologadaParaVeiculo = false,
                RotaAprovada = false,
                AvisoRota = isPesado
                    ? "Percurso recalculado com perfil rodoviário genérico. Restrições de caminhões não são verificadas."
                    : "Percurso recalculado com perfil rodoviário genérico; confirme limites de altura, peso e acesso locais."
            };

            if(ticket!=null)
            {
                plano.Confianca=await _trust!.FinishAsync(ticket,plano,Abort);
                if(!plano.Confianca.NavigationAllowed||plano.Confianca.Obsolete)
                {if(!plano.Confianca.Obsolete)_deviationEngine.RemoveState(key);return null;}
            }
            // Atualiza a nova rota na engine de desvio
            _deviationEngine.UpdateRouteAfterReroute(key, plano.TrajetoCoordenadas);

            // Cache o resultado com expiração curta (5 minutos) para recálculos dinâmicos
            var cacheEntryOptions = new MemoryCacheEntryOptions()
                .SetSlidingExpiration(TimeSpan.FromMinutes(5))
                .SetAbsoluteExpiration(TimeSpan.FromMinutes(10));
            if(_trust==null)_routeCache.Set(cacheKey, plano, cacheEntryOptions);

            return plano;
        }

        public async Task<TruckRoutePlan> CalcularRotaPesadaAsync(
            string origem, 
            string destino, 
            decimal alturaMetros, 
            decimal pesoToneladas, 
            decimal larguraMetros, 
            int qtdEixos, 
            string tipoCarga,
            string perfilVeiculo = "Pesado")
        {
            // Cache key based on route parameters - hashed for stability
            _lastOsrmReply=null;
            var cacheKey = "rota_pesada_"+RouteFingerprint.Hash(new { origem=origem?.Trim().ToUpperInvariant(),destino=destino?.Trim().ToUpperInvariant(),perfilVeiculo,alturaMetros,pesoToneladas,larguraMetros,qtdEixos,tipoCarga,
                provider=_configuration["Routing:OsrmBaseUrl"],capture=_configuration["Routing:HistoricalGeometryPath"] });
            
            if (_routeCache.TryGetValue(cacheKey, out TruckRoutePlan? cachedPlano))
            {
                _logger.LogInformation("Cache hit para plano de rota, perfil {Perfil}.", perfilVeiculo);
                return cachedPlano!;
            }

            bool isPesado = !string.Equals(perfilVeiculo, "Leve", StringComparison.OrdinalIgnoreCase);

            var plano = new TruckRoutePlan
            {
                Origem = string.IsNullOrWhiteSpace(origem) ? "CD Logístico Cajamar - SP" : origem,
                Destino = string.IsNullOrWhiteSpace(destino) ? "Porto de Santos - SP" : destino,
                PerfilVeiculo = isPesado ? "Pesado" : "Leve",
                TipoCarga = string.IsNullOrWhiteSpace(tipoCarga) 
                    ? (isPesado ? "Carga Geral Seca" : "Carga Expressa / Utilitário") 
                    : tipoCarga,
                RotaAprovada = false,
                RotaHomologadaParaVeiculo = false
            };

            if (isPesado)
            {
                // PERFIL 1: CARGA PESADA (CAMINHÕES / CARRETAS)
                plano.CorredorRodoviario = "Percurso rodoviário entre os endereços informados; confirme as restrições do veículo.";
                plano.AlturaVeiculo = Math.Max(0,alturaMetros);
                plano.PesoVeiculo = Math.Max(0,pesoToneladas);
                plano.EixosVeiculo = qtdEixos > 0 ? qtdEixos : 5;
                
                // Pedágio multiplicado pelos eixos comerciais
                plano.CustoPedagioEstimado = null; // No route-specific toll provider.

                // 1. Alerta de Gabarito de Altura (evitamento de pontes e viadutos baixos do centro de SP)
                if (plano.AlturaVeiculo >= 4.10m)
                {
                    plano.Alertas.Add(new TruckRestrictionAlert
                    {
                        Tipo = "Altura",
                        Severidade = "Perigo",
                        Mensagem = $"Gabarito de altura elevado ({plano.AlturaVeiculo:F2}m). O traçado não verifica altura livre; confirme o percurso em fonte homologada.",
                        Localizacao = "Verificar restrições ao longo de toda a rota"
                    });
                }

                // 2. Alerta de Balança de Pesagem Dinâmica PBT
                plano.Alertas.Add(new TruckRestrictionAlert
                {
                    Tipo = "Peso",
                    Severidade = "Atencao",
                    Mensagem = plano.PesoVeiculo>0 ? $"Massa informada: {plano.PesoVeiculo:F1}t (natureza não verificada), {plano.EixosVeiculo} eixos. Limites locais e balanças não verificados." : "Massa não informada. Perfil físico e restrições não verificados.",
                    Localizacao = "Verificar restrições ao longo de toda a rota"
                });

                // 3. Alerta de ZMRC (Zona de Máxima Restrição de Circulação)
                plano.Alertas.Add(new TruckRestrictionAlert
                {
                    Tipo = "Horario",
                    Severidade = "Informativo",
                    Mensagem = "O traçado não verifica zonas de circulação, horários ou restrições municipais.",
                    Localizacao = "Verificar restrições ao longo de toda a rota"
                });

                // 4. Cargas Perigosas
                if (plano.TipoCarga.Contains("Perigosa", StringComparison.OrdinalIgnoreCase) || 
                    plano.TipoCarga.Contains("Química", StringComparison.OrdinalIgnoreCase))
                {
                    plano.Alertas.Add(new TruckRestrictionAlert
                    {
                        Tipo = "Carga",
                        Severidade = "Perigo",
                        Mensagem = "Carga perigosa. A rota não aplica restrições de túneis, vias ou produtos; valide o trajeto com orientação homologada.",
                        Localizacao = "Verificar restrições ao longo de toda a rota"
                    });
                }
            }
            else
            {
                // PERFIL 2: CARGA LEVE (UTILITÁRIOS / VANS / CARROS)
                plano.CorredorRodoviario = "Percurso rodoviário entre os endereços informados.";
                plano.AlturaVeiculo = Math.Max(0,alturaMetros);
                plano.PesoVeiculo = Math.Max(0,pesoToneladas);
                plano.EixosVeiculo = 1; // Tarifa base 1 eixo simples

                // Tarifa base de 1 eixo simples (veículo utilitário leve)
                plano.CustoPedagioEstimado = null;

                // Alerta informativo: isenção de pesagem e liberação de tráfego urbano
                plano.Alertas.Add(new TruckRestrictionAlert
                {
                    Tipo = "Tráfego",
                    Severidade = "Informativo",
                    Mensagem = "Perfil leve selecionado. Restrições de circulação locais também podem se aplicar e não são verificadas por este traçado.",
                    Localizacao = "Verificar regras locais para o veículo"
                });
            }

            var coordenadaOrigem = await GeocodificarEnderecoAsync(plano.Origem);
            var coordenadaDestino = await GeocodificarEnderecoAsync(plano.Destino);
            (List<RouteCoordinate> coords, decimal distanciaKm, int tempoMinutos, List<RouteStep> passos)? rotaGeo = null;
            if (coordenadaOrigem.HasValue && coordenadaDestino.HasValue)
            {
                var waypoints = string.Format(CultureInfo.InvariantCulture,
                    "{0:F6},{1:F6};{2:F6},{3:F6}",
                    coordenadaOrigem.Value.Longitude,
                    coordenadaOrigem.Value.Latitude,
                    coordenadaDestino.Value.Longitude,
                    coordenadaDestino.Value.Latitude);
                rotaGeo = await ObterRotaOsrmPorWaypointsAsync(waypoints);
            }

            if (rotaGeo.HasValue)
            {
                plano.TrajetoCoordenadas = rotaGeo.Value.coords;
                plano.PassosNavegacao = rotaGeo.Value.passos;
                plano.DistanciaKm = rotaGeo.Value.distanciaKm;
                plano.RotaDisponivel = true;
                plano.ProviderName=_lastOsrmReply?.Provider??"OSRM";plano.ProviderProfile="driving";
                plano.GeometrySource=_lastOsrmReply?.GeometrySource??"RoadNetwork";
                plano.ProviderDurationSeconds=_lastOsrmReply?.DurationSeconds;plano.ProviderResponseSha256=_lastOsrmReply?.ResponseSha256;plano.GeometrySourceReference=_lastOsrmReply?.SourceReference;plano.ProviderGraphVersion=_lastOsrmReply?.GraphVersion;
                plano.RotaHomologadaParaVeiculo = false;
                plano.RotaAprovada = false;
                plano.AvisoRota = isPesado
                    ? "Rota ilustrativa: o OSRM público usa perfil de carro e não verifica altura, peso, eixos, carga perigosa ou restrições locais. Confirme o percurso em fonte homologada antes de dirigir."
                    : "Rota calculada com perfil rodoviário genérico; confirme limites de altura, peso e acesso locais antes de dirigir.";
            }
            else
            {
                plano.RotaDisponivel = false;
                plano.ProviderFailure=_lastOsrmReply?.Failure??ProviderFailure.MissingLocations;
                plano.RotaHomologadaParaVeiculo = false;
                plano.RotaAprovada = false;
                plano.AvisoRota = "Não foi possível calcular a rota. Confira os endereços, habilite Photon em desenvolvimento ou configure Google Geocoding, e verifique Routing:OsrmBaseUrl.";
                plano.Alertas.Add(new TruckRestrictionAlert
                {
                    Tipo = "Rota",
                    Severidade = "Perigo",
                    Mensagem = plano.AvisoRota,
                    Localizacao = plano.Destino
                });
            }

            Abort.ThrowIfCancellationRequested();
            // Aplicar restrições de gabarito físico (altura > 4,10m, peso > 23t, eixos urbanos)
            // e enriquecer instruções Turn-by-Turn baseadas nos steps do OSRM
            plano = AplicarRestricoesGabarito(plano, alturaMetros, pesoToneladas, larguraMetros, qtdEixos, tipoCarga);

            if (!plano.RotaDisponivel)
            {
                plano.TempoEstimadoMinutos = 0;
            }
            else plano.TempoEstimadoMinutos=rotaGeo?.tempoMinutos??0;

            // Cache o resultado com expiração de 30 minutos
            if (plano.RotaDisponivel)
            {
                var cacheEntryOptions = new MemoryCacheEntryOptions()
                    .SetAbsoluteExpiration(TimeSpan.FromMinutes(5));
                _routeCache.Set(cacheKey, plano, cacheEntryOptions);
            }

            return plano;
        }

        private async Task<(List<RouteCoordinate> coords, decimal distanciaKm, int tempoMinutos, List<RouteStep> passos)?> ObterRotaOsrmPorWaypointsAsync(string waypoints)
        {
            if(!OsrmRoadRouter.TryLocations(waypoints,out var points))return null;
            _lastOsrmReply=await (_osrm??new OsrmRoadRouter(_httpClientFactory,_configuration,_routeCache)).RouteAsync(points,Abort);
            if(_lastOsrmReply.Failure!=ProviderFailure.None)return null;
            return (_lastOsrmReply.Geometry,_lastOsrmReply.DistanceKm??0,_lastOsrmReply.EstimatedMinutes??0,_lastOsrmReply.Steps);
        }

        private static (string icone, string instrucao) GerarInstrucaoManobra(string tipo, string mod, string via, double distM)
        {
            string nome = string.IsNullOrWhiteSpace(via) ? "na via indicada" : $"em {via}";

            if (tipo == "depart")
                return ("bi-geo-alt-fill", $"Inicie a viagem acessando {nome}");

            if (tipo == "arrive")
                return ("bi-flag-fill", "Você chegou ao destino final da entrega");

            if (tipo == "roundabout" || tipo == "rotary")
                return ("bi-arrow-repeat", $"Na rotatória, siga sentido {nome}");

            if (tipo == "fork" || tipo == "off ramp")
            {
                if (mod.Contains("right"))
                    return ("bi-arrow-up-right", $"Bifurcação à direita sentido {nome}");
                return ("bi-arrow-up-left", $"Bifurcação à esquerda sentido {nome}");
            }

            if (tipo == "merge" || tipo == "on ramp")
                return ("bi-arrow-up-right", $"Acesse a via expressa {nome}");

            if (mod.Contains("sharp left"))
                return ("bi-arrow-return-left", $"Curva fechada à esquerda {nome}");

            if (mod.Contains("slight left"))
                return ("bi-arrow-up-left", $"Mantenha-se ligeiramente à esquerda {nome}");

            if (mod.Contains("left"))
                return ("bi-arrow-left", $"Vire à esquerda {nome}");

            if (mod.Contains("sharp right"))
                return ("bi-arrow-return-right", $"Curva fechada à direita {nome}");

            if (mod.Contains("slight right"))
                return ("bi-arrow-up-right", $"Mantenha-se ligeiramente à direita {nome}");

            if (mod.Contains("right"))
                return ("bi-arrow-right", $"Vire à direita {nome}");

            return ("bi-arrow-up", $"Siga em frente {nome}");
        }

        private (List<RouteCoordinate> coords, decimal distanciaKm, int tempoMinutos, List<RouteStep> passos) ObterRotaFallback(bool isPesado)
        {
            var coords = new List<RouteCoordinate>();
            var passos = new List<RouteStep>();

            if (isPesado)
            {
                // Malha com 34 coordenadas reais ao longo de SP-330, SP-021 e SP-160
                var raw = new (double lat, double lon)[]
                {
                    (-23.355797, -46.877658), (-23.371041, -46.86914), (-23.369317, -46.847273),
                    (-23.391781, -46.837877), (-23.411604, -46.837969), (-23.430932, -46.847525),
                    (-23.450379, -46.852899), (-23.468200, -46.852400), (-23.491200, -46.841500),
                    (-23.518500, -46.829000), (-23.546000, -46.815000), (-23.578000, -46.801000),
                    (-23.605000, -46.789000), (-23.631000, -46.772500), (-23.655000, -46.751000),
                    (-23.678000, -46.726000), (-23.702800, -46.689200), (-23.721000, -46.661000),
                    (-23.738000, -46.634000), (-23.754000, -46.608000), (-23.771000, -46.582000),
                    (-23.791500, -46.592100), (-23.815000, -46.575000), (-23.839000, -46.558000),
                    (-23.861100, -46.541200), (-23.878000, -46.525000), (-23.892000, -46.495000),
                    (-23.903000, -46.471000), (-23.914000, -46.448000), (-23.924000, -46.425000),
                    (-23.935000, -46.388000), (-23.942000, -46.368000), (-23.951000, -46.348000),
                    (-23.961794, -46.332267)
                };

                for (int i = 0; i < raw.Length; i++)
                {
                    coords.Add(new RouteCoordinate
                    {
                        Latitude = raw[i].lat,
                        Longitude = raw[i].lon,
                        Tipo = (i == 0) ? "Origem" : (i == raw.Length - 1 ? "Destino" : "Waypoint"),
                        Nome = (i == 0) ? "Origem: CD Cajamar MGL" : (i == raw.Length - 1 ? "Destino: Santos (SP)" : null)
                    });
                }

                // Passos de navegação simulados de fallback pesado
                passos.Add(new RouteStep { Ordem = 1, NomeVia = "Rod. Anhanguera (SP-330)", Instrucao = "Inicie a rota saindo do CD Cajamar pela SP-330 Sul", ManobraTipo = "depart", DistanciaMetros = 12500, IconeBootstrap = "bi-geo-alt-fill", Latitude = -23.355797, Longitude = -46.877658 });
                passos.Add(new RouteStep { Ordem = 2, NomeVia = "Rodoanel Mário Covas (SP-021)", Instrucao = "Acesse a alça de entrada para o Rodoanel Trecho Oeste", ManobraTipo = "on ramp", ManobraModificador = "slight right", DistanciaMetros = 38000, IconeBootstrap = "bi-arrow-up-right", Latitude = -23.468200, Longitude = -46.852400 });
                passos.Add(new RouteStep { Ordem = 3, NomeVia = "Rodoanel Trecho Sul (SP-021)", Instrucao = "Mantenha-se à esquerda no Rodoanel Sul (Gabarito 5,50m)", ManobraTipo = "turn", ManobraModificador = "straight", DistanciaMetros = 42000, IconeBootstrap = "bi-arrow-up", Latitude = -23.702800, Longitude = -46.689200 });
                passos.Add(new RouteStep { Ordem = 4, NomeVia = "Rodovia dos Imigrantes (SP-160)", Instrucao = "Tome a saída à direita para a SP-160 sentido Baixada Santista", ManobraTipo = "fork", ManobraModificador = "right", DistanciaMetros = 28000, IconeBootstrap = "bi-arrow-right", Latitude = -23.791500, Longitude = -46.592100 });
                passos.Add(new RouteStep { Ordem = 5, NomeVia = "Balança Posto Fiscal (SP-160)", Instrucao = "Atenção: Balança de pesagem dinâmica PBT no km 41", ManobraTipo = "turn", ManobraModificador = "straight", DistanciaMetros = 15000, IconeBootstrap = "bi-speedometer", Latitude = -23.861100, Longitude = -46.541200 });
                passos.Add(new RouteStep { Ordem = 6, NomeVia = "Descida da Serra do Mar (SP-160)", Instrucao = "Inicie descida da serra em marcha reduzida para pesados", ManobraTipo = "turn", ManobraModificador = "slight right", DistanciaMetros = 12000, IconeBootstrap = "bi-arrow-down-right", Latitude = -23.892000, Longitude = -46.495000 });
                passos.Add(new RouteStep { Ordem = 7, NomeVia = "Polo Portuário / Santos", Instrucao = "Siga pela alça de acesso ao Porto de Santos / Centro", ManobraTipo = "turn", ManobraModificador = "right", DistanciaMetros = 8500, IconeBootstrap = "bi-arrow-right", Latitude = -23.935000, Longitude = -46.388000 });
                passos.Add(new RouteStep { Ordem = 8, NomeVia = "Destinatário Final", Instrucao = "Você chegou ao destino final da entrega em Santos", ManobraTipo = "arrive", DistanciaMetros = 0, IconeBootstrap = "bi-flag-fill", Latitude = -23.961794, Longitude = -46.332267 });

                return (coords, 147.0m, 160, passos);
            }
            else
            {
                // Malha urbana direta: SP-330 ➔ Marginal Tietê ➔ Corredor Central ➔ Via Anchieta
                var raw = new (double lat, double lon)[]
                {
                    (-23.355797, -46.877658), (-23.369412, -46.869763), (-23.371501, -46.849402),
                    (-23.395000, -46.839000), (-23.421000, -46.838500), (-23.455000, -46.853000),
                    (-23.485000, -46.805000), (-23.518000, -46.745000), (-23.522000, -46.711000),
                    (-23.525000, -46.678000), (-23.535000, -46.633300), (-23.558000, -46.629000),
                    (-23.585000, -46.618000), (-23.612000, -46.605000), (-23.630000, -46.592100),
                    (-23.660000, -46.575000), (-23.695000, -46.560000), (-23.732000, -46.545000),
                    (-23.780000, -46.525000), (-23.835000, -46.505000), (-23.882000, -46.480000),
                    (-23.918000, -46.435000), (-23.938000, -46.385000), (-23.955000, -46.350000),
                    (-23.961794, -46.332267)
                };

                for (int i = 0; i < raw.Length; i++)
                {
                    coords.Add(new RouteCoordinate
                    {
                        Latitude = raw[i].lat,
                        Longitude = raw[i].lon,
                        Tipo = (i == 0) ? "Origem" : (i == raw.Length - 1 ? "Destino" : "Waypoint"),
                        Nome = (i == 0) ? "Origem: CD Cajamar MGL" : (i == raw.Length - 1 ? "Destino: Santos (SP)" : null)
                    });
                }

                passos.Add(new RouteStep { Ordem = 1, NomeVia = "Rod. Anhanguera (SP-330)", Instrucao = "Inicie o percurso pela Rodovia Anhanguera sentido Capital", ManobraTipo = "depart", DistanciaMetros = 20000, IconeBootstrap = "bi-geo-alt-fill", Latitude = -23.355797, Longitude = -46.877658 });
                passos.Add(new RouteStep { Ordem = 2, NomeVia = "Marginal Tietê", Instrucao = "Continue na Marginal Tietê Pista Expressa", ManobraTipo = "turn", ManobraModificador = "straight", DistanciaMetros = 15000, IconeBootstrap = "bi-arrow-up", Latitude = -23.518000, Longitude = -46.745000 });
                passos.Add(new RouteStep { Ordem = 3, NomeVia = "Corredor Norte-Sul / 23 de Maio", Instrucao = "Tome a saída para a Av. 23 de Maio / Corredor Central", ManobraTipo = "turn", ManobraModificador = "right", DistanciaMetros = 18000, IconeBootstrap = "bi-arrow-right", Latitude = -23.535000, Longitude = -46.633300 });
                passos.Add(new RouteStep { Ordem = 4, NomeVia = "Via Anchieta (SP-150)", Instrucao = "Acesse a Rodovia Anchieta (SP-150) sentido Litoral", ManobraTipo = "on ramp", ManobraModificador = "slight right", DistanciaMetros = 45000, IconeBootstrap = "bi-arrow-up-right", Latitude = -23.630000, Longitude = -46.592100 });
                passos.Add(new RouteStep { Ordem = 5, NomeVia = "Entrada de Santos", Instrucao = "Siga em frente na entrada de Santos via Av. Martins Fontes", ManobraTipo = "turn", ManobraModificador = "straight", DistanciaMetros = 22000, IconeBootstrap = "bi-arrow-up", Latitude = -23.918000, Longitude = -46.435000 });
                passos.Add(new RouteStep { Ordem = 6, NomeVia = "Destinatário Final", Instrucao = "Você chegou ao destino final da entrega em Santos", ManobraTipo = "arrive", DistanciaMetros = 0, IconeBootstrap = "bi-flag-fill", Latitude = -23.961794, Longitude = -46.332267 });

                return (coords, 120.5m, 90, passos);
            }
        }

        private (List<RouteCoordinate> coords, decimal distanciaKm, int tempoMinutos, List<RouteStep> passos) ObterRotaFallbackDinamica(
            double latAtual, double lonAtual, double destLat, double destLon, bool isPesado)
        {
            var coords = new List<RouteCoordinate>
            {
                new RouteCoordinate { Latitude = latAtual, Longitude = lonAtual, Tipo = "Origem", Nome = "Posição Atual (Recálculo)" }
            };

            // Waypoints intermediários para contingência suave
            double midLat = (latAtual + destLat) / 2.0;
            double midLon = (lonAtual + destLon) / 2.0;
            coords.Add(new RouteCoordinate { Latitude = midLat, Longitude = midLon, Tipo = "Waypoint", Nome = "Conexão com Rodovia Principal" });
            coords.Add(new RouteCoordinate { Latitude = destLat, Longitude = destLon, Tipo = "Destino", Nome = "Destino Final" });

            double distMetros = RouteDeviationEngine.HaversineDistanceMeters(latAtual, lonAtual, destLat, destLon) * 1.25;
            decimal distKm = Convert.ToDecimal(distMetros / 1000.0);
            int tempoMin = (int)Math.Max(15, Math.Round((double)distKm / (isPesado ? 55.0 : 75.0) * 60.0));

            var passos = new List<RouteStep>
            {
                new RouteStep
                {
                    Ordem = 1,
                    NomeVia = "Alça de Acesso / Retorno",
                    Instrucao = "Retorne à via expressa conforme nova rota recalculada",
                    ManobraTipo = "turn",
                    ManobraModificador = "straight",
                    DistanciaMetros = 800,
                    IconeBootstrap = "bi-arrow-repeat",
                    Latitude = latAtual,
                    Longitude = lonAtual
                },
                new RouteStep
                {
                    Ordem = 2,
                    NomeVia = isPesado ? "SP-021 / SP-160" : "Via Anchieta (SP-150)",
                    Instrucao = "Siga em direção ao polo de destino",
                    ManobraTipo = "turn",
                    ManobraModificador = "straight",
                    DistanciaMetros = Math.Max(1000, distMetros - 800),
                    IconeBootstrap = "bi-arrow-up",
                    Latitude = midLat,
                    Longitude = midLon
                },
                new RouteStep
                {
                    Ordem = 3,
                    NomeVia = "Destino",
                    Instrucao = "Você chegou ao destino da entrega",
                    ManobraTipo = "arrive",
                    DistanciaMetros = 0,
                    IconeBootstrap = "bi-flag-fill",
                    Latitude = destLat,
                    Longitude = destLon
                }
            };

            return (coords, distKm, tempoMin, passos);
        }
    }
}
