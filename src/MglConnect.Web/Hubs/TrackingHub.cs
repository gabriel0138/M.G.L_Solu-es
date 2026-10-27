using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.SignalR;
using MglConnect.Web.Data;

namespace MglConnect.Web.Hubs
{
    public class TrackingHub : Hub
    {
        private readonly MglDbContext _context;

        public TrackingHub(MglDbContext context)
        {
            _context = context;
        }

        public async Task JoinTrackingGroup(string codigoRastreio)
        {
            if (string.IsNullOrWhiteSpace(codigoRastreio))
                throw new HubException("Código de rastreio inválido.");

            var key = codigoRastreio.Trim().ToUpperInvariant();
            if (!await _context.Entregas.AnyAsync(e => e.CodigoRastreio == key))
                throw new HubException("Rastreamento não localizado.");

            await Groups.AddToGroupAsync(Context.ConnectionId, key);
        }

        public async Task LeaveTrackingGroup(string codigoRastreio)
        {
            if (string.IsNullOrWhiteSpace(codigoRastreio)) return;

            var key = codigoRastreio.Trim().ToUpperInvariant();
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, key);
        }
    }
}
