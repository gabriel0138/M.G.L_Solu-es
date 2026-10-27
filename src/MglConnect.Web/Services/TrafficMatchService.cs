using System;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace MglConnect.Web.Services
{
    public class TrafficMatchService : ITrafficMatchService
    {
        private readonly IConfiguration _configuration;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<TrafficMatchService> _logger;

        public TrafficMatchService(
            IConfiguration configuration,
            IHttpClientFactory httpClientFactory,
            ILogger<TrafficMatchService> logger)
        {
            _configuration = configuration;
            _httpClientFactory = httpClientFactory;
            _logger = logger;
        }

        public async Task<TrafficMatchResult> ObterCondicaoTrafegoAsync(
            double latAtual, 
            double lonAtual, 
            double destLat, 
            double destLon, 
            int tempoBaseMinutos,
            string perfilVeiculo = "Pesado")
        {
            var apiKey = _configuration["GoogleMaps:ApiKey"] 
                      ?? _configuration["GOOGLE_MAPS_API_KEY"] 
                      ?? Environment.GetEnvironmentVariable("GOOGLE_MAPS_API_KEY");

            if (!string.IsNullOrWhiteSpace(apiKey) && apiKey != "SUA_CHAVE_AQUI")
            {
                try
                {
                    var resultGoogle = await ConsultarGoogleRoutesTrafficAsync(apiKey, latAtual, lonAtual, destLat, destLon, tempoBaseMinutos);
                    if (resultGoogle != null)
                    {
                        return resultGoogle;
                    }
                }
                catch (HttpRequestException ex)
                {
                    _logger.LogWarning(
                        "Falha ao consultar Google Routes API Traffic ({ErrorType}); não serão inferidas condições ao vivo.",
                        ex.GetType().Name);
                }
                catch (TaskCanceledException ex)
                {
                    _logger.LogWarning("Tempo esgotado ao consultar Google Routes API Traffic ({ErrorType}).", ex.GetType().Name);
                }
                catch (JsonException ex)
                {
                    _logger.LogWarning("Resposta inválida da Google Routes API Traffic ({ErrorType}).", ex.GetType().Name);
                }
            }

            return new TrafficMatchResult
            {
                Condicao = "Indisponível",
                Severidade = "Sem dados de trânsito em tempo real",
                CorHex = "#64748B",
                FatorAtraso = 1.0,
                AtrasoMinutos = 0,
                TempoEstimadoMinutosComTrafego = tempoBaseMinutos,
                DescricaoGargalo = "O tempo exibido é uma estimativa de rota sem dados de trânsito ao vivo. Configure uma chave válida do Google Routes API para consultar tráfego atual.",
                Fonte = "Sem provedor de trânsito ao vivo configurado"
            };
        }

        private async Task<TrafficMatchResult?> ConsultarGoogleRoutesTrafficAsync(
            string apiKey, 
            double latAtual, 
            double lonAtual, 
            double destLat, 
            double destLon, 
            int tempoBaseMinutos)
        {
            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(5);

            var requestBody = new
            {
                origin = new { location = new { latLng = new { latitude = latAtual, longitude = lonAtual } } },
                destination = new { location = new { latLng = new { latitude = destLat, longitude = destLon } } },
                travelMode = "DRIVE",
                routingPreference = "TRAFFIC_AWARE",
                computeAlternativeRoutes = false
            };

            var request = new HttpRequestMessage(HttpMethod.Post, "https://routes.googleapis.com/directions/v2:computeRoutes")
            {
                Content = new StringContent(JsonSerializer.Serialize(requestBody), System.Text.Encoding.UTF8, "application/json")
            };
            request.Headers.Add("X-Goog-Api-Key", apiKey);
            request.Headers.Add("X-Goog-FieldMask", "routes.duration,routes.staticDuration,routes.description");

            var response = await client.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Google Routes API retornou status {StatusCode}", response.StatusCode);
                return null;
            }

            using var stream = await response.Content.ReadAsStreamAsync();
            using var doc = await JsonDocument.ParseAsync(stream);
            var root = doc.RootElement;

            if (root.TryGetProperty("routes", out var routes) && routes.GetArrayLength() > 0)
            {
                var route = routes[0];
                var durationStr = route.GetProperty("duration").GetString()?.Replace("s", "") ?? "0";
                var staticDurationStr = route.TryGetProperty("staticDuration", out var sd) ? sd.GetString()?.Replace("s", "") ?? durationStr : durationStr;

                double.TryParse(durationStr, NumberStyles.Any, CultureInfo.InvariantCulture, out double durationSec);
                double.TryParse(staticDurationStr, NumberStyles.Any, CultureInfo.InvariantCulture, out double staticDurationSec);

                int duracaoMinComTrafego = (int)Math.Max(5, Math.Round(durationSec / 60.0));
                int duracaoEstaticaMin = (int)Math.Max(5, Math.Round(staticDurationSec / 60.0));
                int atrasoMinutos = Math.Max(0, duracaoMinComTrafego - duracaoEstaticaMin);

                double fator = staticDurationSec > 0 ? (durationSec / staticDurationSec) : 1.0;

                string condicao = "Fluido";
                string severidade = "Normal";
                string corHex = "#10B981"; // Verde

                if (fator >= 1.35)
                {
                    condicao = "Congestionado";
                    severidade = "Congestionamento Intenso";
                    corHex = "#EF4444"; // Vermelho
                }
                else if (fator >= 1.15)
                {
                    condicao = "Moderado";
                    severidade = "Lentidão Moderada";
                    corHex = "#F59E0B"; // Laranja / Âmbar
                }

                return new TrafficMatchResult
                {
                    DadosAoVivoDisponiveis = true,
                    Condicao = condicao,
                    Severidade = severidade,
                    CorHex = corHex,
                    FatorAtraso = Math.Round(fator, 2),
                    AtrasoMinutos = atrasoMinutos,
                    TempoEstimadoMinutosComTrafego = Math.Max(tempoBaseMinutos, tempoBaseMinutos + atrasoMinutos),
                    DescricaoGargalo = atrasoMinutos > 0 
                        ? $"Retenção de +{atrasoMinutos} min detectada pela Google Routes API." 
                        : "Fluxo rodoviário livre sem retenções.",
                    Fonte = "Google Routes API v2 (Traffic Layer Match)"
                };
            }

            return null;
        }
    }
}
