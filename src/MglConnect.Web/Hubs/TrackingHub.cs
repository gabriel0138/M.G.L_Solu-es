using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.SignalR;
using MglConnect.Web.Data;
using MglConnect.Web.Services;

namespace MglConnect.Web.Hubs
{
    public class TrackingHub : Hub
    {
        private readonly MglDbContext _context;
        private readonly PublicTrackingRateLimiter _limiter;

        public TrackingHub(MglDbContext context, PublicTrackingRateLimiter limiter)
        {
            _context = context;
            _limiter = limiter;
        }

        public async Task JoinTrackingGroup(string codigoRastreio)
        {
            using var lease = _limiter.Acquire(Context.GetHttpContext());
            if (!lease.IsAcquired)
                throw new HubException("Muitas consultas. Aguarde antes de tentar novamente.");
            var key = PublicTrackingProjection.NormalizeCode(codigoRastreio);
            if (key == null || PublicTrackingProjection.IsWeakLegacyCode(key)
                || !await _context.Entregas.AnyAsync(e => e.CodigoRastreio.ToUpper() == key))
                throw new HubException("Rastreamento não localizado.");

            await Groups.AddToGroupAsync(Context.ConnectionId, key);
        }

        public async Task LeaveTrackingGroup(string codigoRastreio)
        {
            var key = PublicTrackingProjection.NormalizeCode(codigoRastreio);
            if (key == null) return;
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, key);
        }
    }
}
