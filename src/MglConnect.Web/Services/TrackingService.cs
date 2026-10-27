using System;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MglConnect.Web.Data;
using MglConnect.Web.Hubs;
using MglConnect.Web.Models;

namespace MglConnect.Web.Services
{
    public class TrackingService : ITrackingService
    {
        private readonly MglDbContext _context;
        private readonly IHubContext<TrackingHub> _hubContext;
        private readonly ILogger<TrackingService>? _logger;

        public TrackingService(
            MglDbContext context,
            IHubContext<TrackingHub> hubContext,
            ILogger<TrackingService>? logger = null)
        {
            _context = context;
            _hubContext = hubContext;
            _logger = logger;
        }

        public string GenerateTrackingCode()
        {
            // Padrão inspirado em Mercado Livre / Correios: MGL- + 8 dígitos + BR
            var randomBytes = new byte[4];
            using var rng = RandomNumberGenerator.Create();
            rng.GetBytes(randomBytes);
            var number = Math.Abs(BitConverter.ToInt32(randomBytes, 0)) % 100000000;
            return $"MGL-{number:D8}BR";
        }

        public async Task<EventoRastreio> AddCheckpointAsync(int entregaId, string titulo, string? descricao, string localidade, string status, string icone)
        {
            var entrega = await _context.Entregas.FindAsync(entregaId);
            if (entrega == null)
            {
                throw new ArgumentException("Entrega não encontrada.");
            }

            // REGRA CRÍTICA DE MÁQUINA DE ESTADOS: nao permitir reversao de "Entregue"
            if (entrega.StatusEntrega == "Entregue" && status != "Entregue")
            {
                throw new ArgumentException("Uma entrega com status 'Entregue' nao pode ser alterada para status anterior. Use o endpoint de POD para finalizar entrega.");
            }

            if (status == "Entregue")
            {
                throw new ArgumentException("O status 'Entregue' só pode ser definido ao registrar o POD.");
            }
            if (status is not ("Pendente" or "Em transporte") || status != entrega.StatusEntrega)
            {
                throw new ArgumentException("Checkpoint não pode alterar o estado operacional; use a transição autorizada do pedido ou o início de turno.");
            }

            var evento = new EventoRastreio
            {
                EntregaId = entregaId,
                DataHora = DateTime.UtcNow,
                Titulo = titulo,
                Descricao = descricao,
                Localidade = localidade,
                StatusAssociado = status,
                Icone = string.IsNullOrWhiteSpace(icone) ? "bi-truck" : icone
            };

            _context.EventosRastreio.Add(evento);
            await _context.SaveChangesAsync();

            // TRANSMISSÃO WEBSOCKET SIGNALR EM TEMPO REAL
            try
            {
                var payload = new
                {
                    codigoRastreio = entrega.CodigoRastreio,
                    status = entrega.StatusEntrega,
                    titulo = evento.Titulo,
                    descricao = evento.Descricao,
                    localidade = evento.Localidade,
                    icone = evento.Icone,
                    dataHora = evento.DataHora.ToString("dd/MM/yyyy HH:mm")
                };

                await _hubContext.Clients.Group(entrega.CodigoRastreio).SendAsync("ReceiveCheckpointUpdate", payload);
            }
            catch (Exception exception)
            {
                _logger?.LogWarning(exception,
                    "Checkpoint persisted, but the live notification could not be sent.");
            }

            return evento;
        }

        public async Task<Entrega?> GetTrackingDetailsAsync(string codigoRastreio)
        {
            if (string.IsNullOrWhiteSpace(codigoRastreio)) return null;

            var cleanCode = codigoRastreio.Trim().ToUpper();

            return await _context.Entregas
                .Include(e => e.Pedido)
                    .ThenInclude(p => p!.Cliente)
                .Include(e => e.Motorista)
                .Include(e => e.Veiculo)
                .Include(e => e.EventosRastreio.OrderByDescending(ev => ev.DataHora))
                .FirstOrDefaultAsync(e => e.CodigoRastreio == cleanCode);
        }

        public async Task<bool> FinalizarEntregaPODAsync(int entregaId, string nomeRecebedor, string? documentoRecebedor, string? assinaturaBase64, string? fotoPath, string localidade)
        {
            var entrega = await _context.Entregas.FirstOrDefaultAsync(item => item.Id == entregaId);
            if (entrega == null) return false;
            if (entrega.StatusEntrega == "Entregue")
            {
                return string.Equals(entrega.NomeRecebedor, nomeRecebedor, StringComparison.Ordinal);
            }
            if (entrega.StatusEntrega != "Em transporte")
            {
                return false;
            }

            var agora = DateTime.UtcNow;
            entrega.StatusEntrega = "Entregue";
            entrega.AtribuicaoAtiva = false;
            entrega.Versao++;
            entrega.DataEntregaRealizada = agora;
            entrega.DataHoraRecebimento = agora;
            entrega.NomeRecebedor = nomeRecebedor;
            entrega.DocumentoRecebedor = documentoRecebedor;
            entrega.AssinaturaDigitalBase64 = assinaturaBase64;
            entrega.FotoComprovantePath = fotoPath;

            var evento = new EventoRastreio
            {
                EntregaId = entregaId,
                DataHora = agora,
                Titulo = "Comprovante Digital de Entrega (POD) Confirmado",
                Descricao = $"Pacote recebido por {nomeRecebedor} (Doc: {documentoRecebedor ?? "Não informado"}). Assinatura digital registrada em sistema.",
                Localidade = localidade,
                StatusAssociado = "Entregue",
                Icone = "bi-patch-check-fill"
            };

            _context.EventosRastreio.Add(evento);
            var pedido = entrega.PedidoId > 0
                ? await _context.Pedidos.FirstOrDefaultAsync(item => item.Id == entrega.PedidoId)
                : null;
            if (pedido != null)
            {
                pedido.Status = "Concluido";
                pedido.Versao++;
            }

            var activeShifts = await _context.TurnosMotorista
                .Where(shift => shift.EntregaId == entregaId && shift.Ativo)
                .ToListAsync();
            foreach (var shift in activeShifts)
            {
                shift.Ativo = false;
                shift.EncerradoEmUtc = agora;
                _context.AuditoriaOperacional.Add(new AuditoriaOperacional
                {
                    AtorUsuarioId = shift.MotoristaId,
                    Evento = "TurnoEncerradoPorConclusao",
                    TipoEntidade = "Entrega",
                    IdentificadorEntidade = entregaId.ToString(),
                    Detalhes = $"Turno {shift.Id:N}.",
                    OcorridoEmUtc = agora
                });
            }

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                return false;
            }

            // Notificação SignalR em tempo real
            try
            {
                var payload = new
                {
                    codigoRastreio = entrega.CodigoRastreio,
                    status = "Entregue",
                    titulo = evento.Titulo,
                    descricao = evento.Descricao,
                    localidade = evento.Localidade,
                    icone = evento.Icone,
                    nomeRecebedor = nomeRecebedor,
                    dataHora = agora.ToString("dd/MM/yyyy HH:mm")
                };

                await _hubContext.Clients.Group(entrega.CodigoRastreio).SendAsync("ReceiveCheckpointUpdate", payload);
            }
            catch (Exception exception)
            {
                _logger?.LogWarning(exception,
                    "POD persisted, but the live notification could not be sent.");
            }

            return true;
        }
    }
}
