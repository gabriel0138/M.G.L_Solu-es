using System.Threading.Tasks;

namespace MglConnect.Web.Services
{
    public class TrafficMatchResult
    {
        public bool DadosAoVivoDisponiveis { get; set; }
        public string Condicao { get; set; } = "Indisponível"; // "Fluido", "Moderado", "Congestionado"
        public string Severidade { get; set; } = "Normal";
        public string CorHex { get; set; } = "#10B981"; // Verde Waze / Google
        public double FatorAtraso { get; set; } = 1.0;
        public int AtrasoMinutos { get; set; } = 0;
        public int TempoEstimadoMinutosComTrafego { get; set; }
        public string DescricaoGargalo { get; set; } = "Sem dados de trânsito em tempo real.";
        public string Fonte { get; set; } = "Sem provedor de trânsito ao vivo configurado";
    }

    public interface ITrafficMatchService
    {
        Task<TrafficMatchResult> ObterCondicaoTrafegoAsync(
            double latAtual, 
            double lonAtual, 
            double destLat, 
            double destLon, 
            int tempoBaseMinutos,
            string perfilVeiculo = "Pesado");
    }
}
