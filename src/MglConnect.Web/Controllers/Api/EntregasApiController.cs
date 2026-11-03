using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MglConnect.Web.Data;
using MglConnect.Web.Models;
using MglConnect.Web.Services;

namespace MglConnect.Web.Controllers.Api
{
    [ApiController]
    [Route("api/entregas")]
    [Authorize]
    public class EntregasApiController : ControllerBase
    {
        private readonly MglDbContext _context;
        private readonly ITrackingService _trackingService;
        private readonly ITrafficMatchService _trafficService;
        private readonly ITruckRoutingService _routingService;
        private readonly IPodPdfService _podPdfService;
        private readonly ICo2EsgService _co2Service;
        private readonly IDriverShiftService _shiftService;

        public EntregasApiController(
            MglDbContext context, 
            ITrackingService trackingService,
            ITrafficMatchService trafficService,
            ITruckRoutingService routingService,
            IPodPdfService podPdfService,
            ICo2EsgService co2Service,
            IDriverShiftService shiftService)
        {
            _context = context;
            _trackingService = trackingService;
            _trafficService = trafficService;
            _routingService = routingService;
            _podPdfService = podPdfService;
            _co2Service = co2Service;
            _shiftService = shiftService;
        }

        // GET: /api/entregas/motorista/{motoristaId}
        [HttpGet("motorista/{motoristaId:int}")]
        public async Task<IActionResult> GetEntregasMotorista(int motoristaId)
        {
            var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            var isAdmin = User.IsInRole("Admin") || User.IsInRole("Administrador");
            if (!isAdmin)
            {
                if (!User.IsInRole("Motorista")
                    || !int.TryParse(userIdClaim, out var currentUserId)
                    || currentUserId != motoristaId)
                {
                    return Forbid();
                }

                if (!await _context.Usuarios.AnyAsync(user =>
                    user.Id == currentUserId && user.Ativo && user.Perfil == "Motorista"))
                    return Forbid();
            }

            if (!isAdmin)
            {
                var trips = await _context.Entregas.AsNoTracking()
                    .Where(e => e.MotoristaId == motoristaId && e.AtribuicaoAtiva
                        && e.StatusEntrega != "Entregue" && e.StatusEntrega != "Cancelada")
                    .OrderBy(e => e.DataCriacao)
                    .Select(e => new
                    {
                        id = e.Id,
                        statusEntrega = e.StatusEntrega,
                        cidadeDestino = e.CidadeDestino,
                        estadoDestino = e.EstadoDestino,
                        veiculo = e.Veiculo == null ? null : e.Veiculo.Modelo,
                        placa = e.Veiculo == null ? null : e.Veiculo.Placa,
                        turnoAtivo = _context.TurnosMotorista.Any(t =>
                            t.MotoristaId == motoristaId && t.EntregaId == e.Id && t.Ativo)
                    })
                    .ToListAsync();
                return Ok(trips);
            }

            var entregas = await _context.Entregas
                .Include(e => e.Pedido)
                    .ThenInclude(p => p!.Cliente)
                .Where(e => e.MotoristaId == motoristaId)
                .OrderByDescending(e => e.DataCriacao)
                .Select(e => new
                {
                    id = e.Id,
                    codigoRastreio = e.CodigoRastreio,
                    pedidoId = e.PedidoId,
                    numeroPedido = e.Pedido != null ? e.Pedido.NumeroPedido : "",
                    clienteNome = e.Pedido != null && e.Pedido.Cliente != null ? e.Pedido.Cliente.RazaoSocial : "",
                    destinatarioNome = e.DestinatarioNome,
                    enderecoDestino = $"{e.EnderecoDestino}, {e.NumeroDestino} - {e.BairroDestino}, {e.CidadeDestino}/{e.EstadoDestino}",
                    cepDestino = e.CepDestino,
                    statusEntrega = e.StatusEntrega,
                    dataCriacao = e.DataCriacao,
                    dataPrevisao = e.DataPrevisaoEntrega
                })
                .ToListAsync();

            return Ok(entregas);
        }

        private async Task<Usuario?> ObterUsuarioAtualAsync()
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (int.TryParse(userIdClaim, out int currentUserId))
            {
                return await _context.Usuarios
                    .Include(u => u.Cliente)
                    .FirstOrDefaultAsync(u => u.Id == currentUserId && u.Ativo);
            }
            return null;
        }

        private async Task<bool> PodeAcessarEntregaAsync(Entrega entrega)
        {
            if (User.IsInRole("Admin") || User.IsInRole("Administrador"))
            {
                var admin = await ObterUsuarioAtualAsync();
                return admin != null && (admin.Perfil == "Admin" || admin.Perfil == "Administrador");
            }

            var usuario = await ObterUsuarioAtualAsync();
            if (usuario == null)
                return false;

            if (User.IsInRole("Motorista") && usuario.Perfil == "Motorista"
                && entrega.MotoristaId.HasValue && entrega.MotoristaId == usuario.Id)
                return true;

            if ((User.IsInRole("Cliente") && usuario.Perfil == "Cliente")
                || (User.IsInRole("Intermediador") && usuario.Perfil == "Intermediador"))
            {
                if (usuario.Cliente is { Ativo: true } cliente
                    && cliente.TenantId == usuario.TenantId
                    && entrega.Pedido?.ClienteId == cliente.Id)
                    return true;
            }

            return false;
        }

        private async Task<bool> PodeModificarEntregaAsync(Entrega entrega)
        {
            if (User.IsInRole("Admin") || User.IsInRole("Administrador"))
            {
                var admin = await ObterUsuarioAtualAsync();
                return admin != null && (admin.Perfil == "Admin" || admin.Perfil == "Administrador");
            }

            if (!User.IsInRole("Motorista"))
                return false;

            var usuario = await ObterUsuarioAtualAsync();
            return usuario != null
                && entrega.MotoristaId == usuario.Id
                && await _shiftService.HasActiveShiftAsync(usuario.Id, entrega.Id);
        }

        // GET: /api/entregas/{id}
        [HttpGet("{id:int}")]
        public async Task<IActionResult> GetDetalhes(int id)
        {
            var entrega = await _context.Entregas
                .Include(e => e.Pedido)
                    .ThenInclude(p => p!.Cliente)
                .Include(e => e.Motorista)
                .Include(e => e.EventosRastreio.OrderByDescending(ev => ev.DataHora))
                .FirstOrDefaultAsync(e => e.Id == id);

            if (entrega == null)
            {
                return NotFound(new { mensagem = "Entrega não encontrada." });
            }

            if (!await PodeAcessarEntregaAsync(entrega))
            {
                return Forbid();
            }

            if (User.IsInRole("Motorista"))
            {
                if (!int.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var driverId)
                    || !await _shiftService.HasActiveShiftAsync(driverId, id))
                    return Forbid();

                return Ok(new
                {
                    id = entrega.Id,
                    statusEntrega = entrega.StatusEntrega,
                    cidadeDestino = entrega.CidadeDestino,
                    estadoDestino = entrega.EstadoDestino,
                    enderecoDestino = $"{entrega.EnderecoDestino}, {entrega.NumeroDestino} - {entrega.CidadeDestino}/{entrega.EstadoDestino}",
                    veiculo = entrega.Veiculo?.Modelo,
                    placa = entrega.Veiculo?.Placa
                });
            }

            return Ok(new
            {
                id = entrega.Id,
                codigoRastreio = entrega.CodigoRastreio,
                statusEntrega = entrega.StatusEntrega,
                destinatarioNome = entrega.DestinatarioNome,
                destinatarioDocumento = entrega.DestinatarioDocumento,
                enderecoCompleto = $"{entrega.EnderecoDestino}, {entrega.NumeroDestino} {entrega.ComplementoDestino} - {entrega.BairroDestino}, {entrega.CidadeDestino}/{entrega.EstadoDestino} - CEP: {entrega.CepDestino}",
                motorista = entrega.Motorista != null ? entrega.Motorista.Nome : null,
                dataCriacao = entrega.DataCriacao,
                dataPrevisaoEntrega = entrega.DataPrevisaoEntrega,
                dataEntregaRealizada = entrega.DataEntregaRealizada,
                eventos = entrega.EventosRastreio.Select(ev => new
                {
                    id = ev.Id,
                    dataHora = ev.DataHora,
                    titulo = ev.Titulo,
                    descricao = ev.Descricao,
                    localidade = ev.Localidade,
                    status = ev.StatusAssociado,
                    icone = ev.Icone
                })
            });
        }

        public record AtualizarStatusRequest(string Status, string? Observacao, string? Localidade);

        // PUT: /api/entregas/{id}/status
        [HttpPut("{id:int}/status")]
        public async Task<IActionResult> AtualizarStatus(int id, [FromBody] AtualizarStatusRequest request)
        {
            var entrega = await _context.Entregas
                .Include(e => e.Pedido)
                .FirstOrDefaultAsync(e => e.Id == id);
            if (entrega == null)
            {
                return NotFound(new { mensagem = "Entrega não encontrada." });
            }

            if (!await PodeModificarEntregaAsync(entrega))
            {
                return Forbid();
            }

            var statusValidos = new[] { "Pendente", "Em transporte", "Entregue", "Cancelada" };
            if (string.IsNullOrWhiteSpace(request.Status) || !statusValidos.Contains(request.Status))
            {
                return BadRequest(new { mensagem = "Status inválido." });
            }

            if (request.Status == "Entregue")
            {
                return BadRequest(new { mensagem = "Finalize a entrega pelo endpoint de POD para registrar recebedor e assinatura." });
            }

            var isAdmin = User.IsInRole("Admin") || User.IsInRole("Administrador");
            if (!isAdmin && (request.Status != "Em transporte"
                || entrega.StatusEntrega != "Em transporte"))
            {
                return Forbid();
            }
            if (entrega.StatusEntrega == request.Status)
            {
                return Ok(new { sucesso = true, mensagem = "A entrega já está nesse estado.", novoStatus = entrega.StatusEntrega });
            }
            if (!isAdmin || entrega.StatusEntrega is not ("Pendente" or "Em transporte")
                || request.Status != "Cancelada")
            {
                return Conflict(new { mensagem = "Transição inválida. A saída de 'Pendente' exige início de turno validado." });
            }

            if (!int.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var actorId))
                return Forbid();
            await _shiftService.CancelCodeAsync(id, actorId, "Entrega cancelada pela operação.");
            entrega.StatusEntrega = request.Status;
            entrega.AtribuicaoAtiva = false;
            entrega.Versao++;
            if (entrega.Pedido != null)
            {
                entrega.Pedido.Status = "Cancelado";
                entrega.Pedido.Versao++;
            }
            var now = DateTime.UtcNow;
            var activeShifts = await _context.TurnosMotorista
                .Where(shift => shift.EntregaId == id && shift.Ativo)
                .ToListAsync();
            foreach (var shift in activeShifts)
            {
                shift.Ativo = false;
                shift.EncerradoEmUtc = now;
                _context.AuditoriaOperacional.Add(new AuditoriaOperacional
                {
                    AtorUsuarioId = actorId,
                    Evento = "TurnoEncerradoPorCancelamento",
                    TipoEntidade = "Entrega",
                    IdentificadorEntidade = id.ToString(),
                    Detalhes = $"Turno {shift.Id:N}.",
                    OcorridoEmUtc = now
                });
            }

            var local = string.IsNullOrWhiteSpace(request.Localidade) ? $"{entrega.CidadeDestino} - {entrega.EstadoDestino}" : request.Localidade;
            _context.EventosRastreio.Add(new EventoRastreio
            {
                EntregaId = entrega.Id,
                DataHora = now,
                Titulo = $"Entrega cancelada pela operação",
                Descricao = request.Observacao ?? "Cancelamento administrativo.",
                Localidade = local,
                StatusAssociado = request.Status,
                Icone = "bi-x-circle"
            });
            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                return Conflict(new { mensagem = "A entrega foi alterada por outra operação." });
            }

            return Ok(new { sucesso = true, mensagem = "Entrega cancelada e registrada na auditoria.", novoStatus = entrega.StatusEntrega });
        }

        public record NovoCheckpointApiRequest(string Titulo, string? Descricao, string Localidade, string Status, string? Icone);

        // POST: /api/entregas/{id}/checkpoint
        [HttpPost("{id:int}/checkpoint")]
        public async Task<IActionResult> AdicionarCheckpoint(int id, [FromBody] NovoCheckpointApiRequest request)
        {
            try
            {
                var entrega = await _context.Entregas
                    .Include(e => e.Pedido)
                    .FirstOrDefaultAsync(e => e.Id == id);
                if (entrega == null)
                    return NotFound(new { mensagem = "Entrega não encontrada." });

                if (!await PodeAcessarEntregaAsync(entrega))
                    return Forbid();
                if (!await PodeModificarEntregaAsync(entrega))
                    return Forbid();

                if (request.Status == "Entregue")
                    return BadRequest(new { mensagem = "Use o endpoint de POD para concluir a entrega." });

                if (request.Status != entrega.StatusEntrega)
                    return Conflict(new { mensagem = "Checkpoint não pode alterar o estado operacional." });

                if (!User.IsInRole("Admin") && request.Status != "Em transporte")
                    return Forbid();

                var evento = await _trackingService.AddCheckpointAsync(
                    id,
                    request.Titulo,
                    request.Descricao,
                    request.Localidade,
                    request.Status,
                    request.Icone ?? "bi-truck"
                );

                return Ok(new { sucesso = true, checkpointId = evento.Id, dataHora = evento.DataHora });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { mensagem = ex.Message });
            }
        }

        public record FinalizarPodApiRequest(string NomeRecebedor, string? DocumentoRecebedor, string? AssinaturaBase64, string? Localidade);

        // POST: /api/entregas/{id}/pod
        [HttpPost("{id:int}/pod")]
        [RequestSizeLimit(2 * 1024 * 1024)]
        public async Task<IActionResult> FinalizarPod(int id, [FromBody] FinalizarPodApiRequest request)
        {
            if (!User.IsInRole("Admin") && !User.IsInRole("Administrador") && !User.IsInRole("Motorista"))
                return Forbid();
            if (string.IsNullOrWhiteSpace(request.NomeRecebedor))
            {
                return BadRequest(new { mensagem = "O nome do recebedor é obrigatório." });
            }
            if (request.NomeRecebedor.Trim().Length > 100 || request.DocumentoRecebedor?.Trim().Length > 30)
                return BadRequest(new { mensagem = "Nome ou documento excede o tamanho permitido." });

            var entrega = await _context.Entregas
                .Include(e => e.Pedido)
                .FirstOrDefaultAsync(e => e.Id == id);
            if (entrega == null)
                return NotFound(new { mensagem = "Entrega não encontrada." });

            if (!await PodeAcessarEntregaAsync(entrega))
                return Forbid();
            if (entrega.StatusEntrega == "Entregue")
            {
                return PodContent.Matches(entrega, request.NomeRecebedor, request.DocumentoRecebedor, request.AssinaturaBase64)
                    ? Ok(new { sucesso = true, entregaId = id, status = "Entregue", mensagem = "O POD já havia sido registrado." })
                    : Conflict(new { mensagem = "A entrega já foi concluída com outro conteúdo de comprovante." });
            }
            if (!await PodeModificarEntregaAsync(entrega))
                return Forbid();

            var sucesso = await _trackingService.FinalizarEntregaPODAsync(
                id,
                request.NomeRecebedor.Trim(),
                request.DocumentoRecebedor?.Trim(),
                request.AssinaturaBase64,
                null,
                string.IsNullOrWhiteSpace(request.Localidade) ? "Destino Final" : request.Localidade.Trim()
            );

            if (!sucesso)
            {
                return Conflict(new { mensagem = "A entrega já foi concluída ou não está mais disponível para confirmação de POD." });
            }

            return Ok(new { sucesso = true, entregaId = id, status = "Entregue", mensagem = "Comprovante Digital de Entrega (POD) gravado com sucesso." });
        }

        // GET: /api/entregas/{id}/trafego-eta
        [HttpGet("{id:int}/trafego-eta")]
        public async Task<IActionResult> ObterTrafegoEta(
            int id, 
            [FromQuery] double? latAtual, 
            [FromQuery] double? lngAtual, 
            [FromQuery] int? tempoBaseMinutos)
        {
            var entrega = await _context.Entregas
                .Include(e => e.Pedido)
                .Include(e => e.Veiculo)
                .FirstOrDefaultAsync(e => e.Id == id);

            if (entrega == null)
            {
                return NotFound(new { mensagem = "Entrega não encontrada." });
            }

            if (!await PodeAcessarEntregaAsync(entrega))
                return Forbid();
            if (User.IsInRole("Motorista") && !await PodeModificarEntregaAsync(entrega))
                return Forbid();

            if (latAtual.HasValue != lngAtual.HasValue
                || (latAtual.HasValue && (!double.IsFinite(latAtual.Value) || latAtual.Value is < -90 or > 90))
                || (lngAtual.HasValue && (!double.IsFinite(lngAtual.Value) || lngAtual.Value is < -180 or > 180)))
            {
                return BadRequest(new { mensagem = "Coordenadas GPS inválidas." });
            }

            var pesoVeiculo = entrega.Veiculo?.PesoToneladas ?? entrega.PesoToneladas;
            var eixosVeiculo = entrega.Veiculo?.QtdEixos ?? entrega.QtdEixos;
            string perfil = pesoVeiculo > 3.5m || eixosVeiculo > 2 ? "Pesado" : "Leve";
            var plano = await _routingService.CalcularRotaParaEntregaAsync(id, perfil);
            if (!plano.RotaDisponivel || plano.TrajetoCoordenadas.Count < 2)
            {
                return Ok(new
                {
                    entregaId = id,
                    dadosAoVivoDisponiveis = false,
                    condicao = "Indisponível",
                    severidade = "Rota indisponível",
                    corHex = "#64748B",
                    tempoFinalMinutos = 0,
                    atrasoMinutos = 0,
                    descricao = plano.AvisoRota,
                    fonte = "Sem rota calculada"
                });
            }

            var origem = plano.TrajetoCoordenadas[0];
            var destino = plano.TrajetoCoordenadas[^1];
            double lat = latAtual ?? origem.Latitude;
            double lng = lngAtual ?? origem.Longitude;
            int tempoBase = tempoBaseMinutos is > 0 ? tempoBaseMinutos.Value : plano.TempoEstimadoMinutos;

            var trafego = await _trafficService.ObterCondicaoTrafegoAsync(
                lat, lng, destino.Latitude, destino.Longitude, tempoBase, perfil);

            return Ok(new
            {
                entregaId = id,
                codigoRastreio = entrega.CodigoRastreio,
                perfilVeiculo = perfil,
                tempoBaseMinutos = tempoBase,
                dadosAoVivoDisponiveis = trafego.DadosAoVivoDisponiveis,
                tempoFinalMinutos = trafego.TempoEstimadoMinutosComTrafego,
                atrasoMinutos = trafego.AtrasoMinutos,
                condicao = trafego.Condicao,
                severidade = trafego.Severidade,
                corHex = trafego.CorHex,
                descricao = trafego.DescricaoGargalo,
                fonte = trafego.Fonte
            });
        }

        // GET: /api/entregas/{id}/pod-pdf — Download do Canhoto Digital em PDF
        [HttpGet("{id:int}/pod-pdf")]
        [Authorize(Roles = "Admin,Motorista,Cliente,Intermediador")]
        public async Task<IActionResult> DownloadPodPdf(int id,
            [FromServices] IPodPdfService podPdfService)
        {
            var entrega = await _context.Entregas
                .Include(e => e.Pedido).ThenInclude(p => p!.Cliente)
                .Include(e => e.Motorista)
                .Include(e => e.Veiculo)
                .Include(e => e.EventosRastreio)
                .FirstOrDefaultAsync(e => e.Id == id);

            if (entrega == null)
                return NotFound(new { mensagem = "Entrega não encontrada." });

            if (!await PodeAcessarEntregaAsync(entrega))
                return Forbid();

            if (entrega.StatusEntrega != "Entregue" || string.IsNullOrWhiteSpace(entrega.NomeRecebedor))
                return BadRequest(new { mensagem = "POD disponível somente após entrega confirmada com assinatura." });

            var pdfBytes = await podPdfService.GerarPodPdfAsync(entrega);
            var nomeArquivo = $"POD_{entrega.CodigoRastreio}_{DateTime.Now:yyyyMMddHHmm}.pdf";

            return File(pdfBytes, "application/pdf", nomeArquivo);
        }

        // GET: /api/entregas/{id}/esg — Relatório de Emissões CO₂ da Entrega
        [HttpGet("{id:int}/esg")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> ObterEsgEntrega(int id,
            [FromServices] ICo2EsgService co2Service)
        {
            var entrega = await _context.Entregas
                .Include(e => e.Veiculo)
                .FirstOrDefaultAsync(e => e.Id == id);

            if (entrega == null)
                return NotFound(new { mensagem = "Entrega não encontrada." });

            bool isPesado = entrega.Veiculo == null || entrega.Veiculo.PesoToneladas > 3.5m || entrega.QtdEixos > 2;
            string combustivel = isPesado ? "Diesel S10" : "Gasolina Comum / Flex";
            decimal consumo = isPesado ? 2.8m : 8.5m;
            decimal distKm = isPesado ? 147.0m : 120.5m;

            var resultado = co2Service.CalcularEmissoesCo2(distKm, combustivel, consumo);

            return Ok(new
            {
                entregaId = id,
                codigoRastreio = entrega.CodigoRastreio,
                distanciaKm = resultado.DistanciaKm,
                tipoCombustivel = resultado.TipoCombustivel,
                litrosConsumidos = resultado.LitrosConsumidos,
                emissaoCo2Kg = resultado.EmissaoCo2Kg,
                emissaoCo2Toneladas = resultado.EmissaoCo2Toneladas,
                classificacao = resultado.ClassificacaoEmissao,
                corIndicador = resultado.CorIndicador,
                equivalenteArvores = resultado.EquivalenteArvores,
                metodologia = "IPCC AR6 / CETESB 2024"
            });
        }
    }
}
