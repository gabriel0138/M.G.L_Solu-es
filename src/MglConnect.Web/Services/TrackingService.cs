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
            // 80 random bits within the existing 20-character schema limit.
            // Previously issued decimal, GUID and prefixed codes remain valid.
            return Convert.ToHexString(RandomNumberGenerator.GetBytes(10));
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
                var publicEvent = PublicTrackingProjection.Event(evento.DataHora, evento.StatusAssociado);
                var payload = new
                {
                    codigoRastreio = entrega.CodigoRastreio.ToUpperInvariant(),
                    status = PublicTrackingProjection.Status(entrega.StatusEntrega),
                    titulo = publicEvent.Titulo,
                    descricao = publicEvent.Descricao,
                    localidade = publicEvent.Localidade,
                    icone = publicEvent.Icone,
                    dataHora = evento.DataHora
                };

                await _hubContext.Clients.Group(entrega.CodigoRastreio.ToUpperInvariant()).SendAsync("ReceiveCheckpointUpdate", payload);
            }
            catch (Exception exception)
            {
                _logger?.LogWarning(exception,
                    "Checkpoint persisted, but the live notification could not be sent.");
            }

            return evento;
        }

        public async Task<PublicTrackingDetails?> GetTrackingDetailsAsync(string codigoRastreio)
        {
            var cleanCode = PublicTrackingProjection.NormalizeCode(codigoRastreio);
            if (cleanCode == null) return null;

            var row = await _context.Entregas.AsNoTracking()
                .Where(e => e.CodigoRastreio.ToUpper() == cleanCode)
                .Select(e => new
                {
                    e.StatusEntrega, e.DestinatarioNome, e.CidadeDestino, e.EstadoDestino,
                    e.DataCriacao, e.DataPrevisaoEntrega, e.DataEntregaRealizada,
                    CidadeOrigem = e.Pedido != null ? e.Pedido.OrigemCidade : null,
                    EstadoOrigem = e.Pedido != null ? e.Pedido.OrigemEstado : null,
                    Eventos = e.EventosRastreio.OrderByDescending(ev => ev.DataHora)
                        .Select(ev => new { ev.DataHora, ev.StatusAssociado }).ToList()
                }).FirstOrDefaultAsync();
            if (row == null) return null;
            return new(cleanCode, PublicTrackingProjection.Status(row.StatusEntrega),
                PublicTrackingProjection.FirstName(row.DestinatarioNome), row.CidadeOrigem, row.EstadoOrigem,
                row.CidadeDestino, row.EstadoDestino, row.DataCriacao, row.DataPrevisaoEntrega, row.DataEntregaRealizada,
                row.Eventos.Select(ev => PublicTrackingProjection.Event(ev.DataHora, ev.StatusAssociado)).ToArray());
        }

        public async Task<bool> FinalizarEntregaPODAsync(int entregaId, string nomeRecebedor, string? documentoRecebedor, string? assinaturaBase64, string? fotoPath, string localidade)
        {
            var entrega = await _context.Entregas.FirstOrDefaultAsync(item => item.Id == entregaId);
            if (entrega == null) return false;
            if (entrega.StatusEntrega == "Entregue")
            {
                return PodContent.Matches(entrega, nomeRecebedor, documentoRecebedor, assinaturaBase64, fotoPath);
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
            entrega.NomeRecebedor = PodContent.Normalize(nomeRecebedor);
            entrega.DocumentoRecebedor = PodContent.Normalize(documentoRecebedor);
            entrega.AssinaturaDigitalBase64 = PodContent.Normalize(assinaturaBase64);
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
                var publicEvent = PublicTrackingProjection.Event(evento.DataHora, evento.StatusAssociado);
                var payload = new
                {
                    codigoRastreio = entrega.CodigoRastreio.ToUpperInvariant(),
                    status = "Entregue",
                    titulo = publicEvent.Titulo,
                    descricao = publicEvent.Descricao,
                    localidade = publicEvent.Localidade,
                    icone = publicEvent.Icone,
                    dataHora = agora
                };

                await _hubContext.Clients.Group(entrega.CodigoRastreio.ToUpperInvariant()).SendAsync("ReceiveCheckpointUpdate", payload);
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
