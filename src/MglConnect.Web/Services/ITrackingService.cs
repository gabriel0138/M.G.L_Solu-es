using System.Threading.Tasks;
using MglConnect.Web.Models;

namespace MglConnect.Web.Services
{
    public interface ITrackingService
    {
        string GenerateTrackingCode();
        Task<EventoRastreio> AddCheckpointAsync(int entregaId, string titulo, string? descricao, string localidade, string status, string icone);
        Task<Entrega?> GetTrackingDetailsAsync(string codigoRastreio);
        Task<bool> FinalizarEntregaPODAsync(int entregaId, string nomeRecebedor, string? documentoRecebedor, string? assinaturaBase64, string? fotoPath, string localidade);
    }
}
