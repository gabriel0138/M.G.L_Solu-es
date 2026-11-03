using System;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using MglConnect.Web.ViewModels;
using MglConnect.Web.Data;
using MglConnect.Web.Models;
using MglConnect.Web.Services;

namespace MglConnect.Web.Controllers
{
    [Authorize(Roles = "Admin,Motorista")]
    public class EntregasController : Controller
    {
        private readonly MglDbContext _context;
        private readonly ITrackingService _trackingService;
        private readonly ITruckRoutingService _routingService;
        private readonly IFuelCostService _fuelService;
        private readonly IDriverShiftService _shiftService;
        private readonly IEmergencyAlertService _emergencyAlertService;
        private readonly ILogger<EntregasController>? _logger;

        public EntregasController(
            MglDbContext context, 
            ITrackingService trackingService,
            ITruckRoutingService routingService,
            IFuelCostService fuelService,
            IDriverShiftService shiftService,
            IEmergencyAlertService emergencyAlertService,
            ILogger<EntregasController>? logger = null)
        {
            _context = context;
            _trackingService = trackingService;
            _routingService = routingService;
            _fuelService = fuelService;
            _shiftService = shiftService;
            _emergencyAlertService = emergencyAlertService;
            _logger = logger;
        }

        public async Task<IActionResult> Index(string? status, string? termo)
        {
            if (User.IsInRole("Motorista") && !User.IsInRole("Admin"))
            {
                var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (!int.TryParse(userIdClaim, out var motoristaId))
                    return Forbid();

                var viagens = await _context.Entregas
                    .AsNoTracking()
                    .Where(e => e.MotoristaId == motoristaId
                        && e.AtribuicaoAtiva
                        && e.StatusEntrega != "Entregue"
                        && e.StatusEntrega != "Cancelada")
                    .OrderBy(e => e.DataPrevisaoEntrega)
                    .Select(e => new MotoristaViagemViewModel
                    {
                        EntregaId = e.Id,
                        Status = e.StatusEntrega,
                        CidadeDestino = e.CidadeDestino,
                        EstadoDestino = e.EstadoDestino,
                        ModeloVeiculo = e.Veiculo == null ? null : e.Veiculo.Modelo,
                        PlacaVeiculo = e.Veiculo == null ? null : e.Veiculo.Placa,
                        VeiculoAtribuido = e.VeiculoId.HasValue,
                        TurnoAtivo = _context.TurnosMotorista.Any(t =>
                            t.MotoristaId == motoristaId && t.EntregaId == e.Id && t.Ativo)
                    })
                    .ToListAsync();

                return View("IndexMotorista", viagens);
            }

            var query = _context.Entregas
                .Include(e => e.Pedido)
                    .ThenInclude(p => p!.Cliente)
                .Include(e => e.Motorista)
                .Include(e => e.EventosRastreio)
                .AsQueryable();

            // Se o usuário logado for Motorista (e não Admin), filtra apenas as entregas atribuídas a ele
            if (User.IsInRole("Motorista") && !User.IsInRole("Admin"))
            {
                var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (!int.TryParse(userIdClaim, out var motoristaId))
                    return Forbid();

                query = query.Where(e => e.MotoristaId == motoristaId);
            }

            if (!string.IsNullOrWhiteSpace(status))
            {
                query = query.Where(e => e.StatusEntrega == status);
            }

            if (!string.IsNullOrWhiteSpace(termo))
            {
                var t = termo.Trim().ToLower();
                query = query.Where(e => e.CodigoRastreio.ToLower().Contains(t)
                                      || e.DestinatarioNome.ToLower().Contains(t)
                                      || e.CidadeDestino.ToLower().Contains(t)
                                      || (e.Pedido != null && e.Pedido.NumeroPedido.ToLower().Contains(t)));
            }

            ViewBag.StatusSelecionado = status;
            ViewBag.TermoBusca = termo;

            var entregas = await query.OrderByDescending(e => e.DataCriacao).ToListAsync();
            return View(entregas);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> IniciarTurno(int entregaId, string codigo)
        {
            if (!TryGetCurrentUserId(out var driverId)) return Forbid();

            var result = await _shiftService.StartAsync(driverId, entregaId, codigo);
            if (result.Status is DriverShiftStartStatus.Started or DriverShiftStartStatus.AlreadyActive)
            {
                TempData["MensagemSucesso"] = "Turno iniciado e validado pelo servidor.";
                return RedirectToAction(nameof(Navegar), new { id = entregaId });
            }

            TempData["MensagemErro"] = result.Status switch
            {
                DriverShiftStartStatus.Expired => "O código de turno expirou. Solicite um novo à operação.",
                DriverShiftStartStatus.AttemptsExceeded => "Limite de tentativas atingido. Solicite um novo código à operação.",
                DriverShiftStartStatus.AnotherShiftActive => "Encerre seu turno ativo antes de iniciar outra viagem.",
                DriverShiftStartStatus.AssignmentUnavailable => "Esta viagem não está mais atribuída a você com veículo ativo.",
                DriverShiftStartStatus.Conflict => "O início do turno concorreu com outra alteração. Atualize as viagens e tente novamente.",
                _ => "Código inválido ou cancelado."
            };
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EncerrarTurno()
        {
            if (!TryGetCurrentUserId(out var driverId)) return Forbid();
            if (!await _shiftService.EndAsync(driverId))
            {
                TempData["MensagemErro"] = "Não há turno ativo para encerrar.";
            }
            else
            {
                TempData["MensagemSucesso"] = "Turno encerrado. A viagem continua atribuída e pode ser retomada com novo código.";
            }
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [Authorize(Roles = "Admin")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EmitirCodigoTurno(int entregaId)
        {
            if (!TryGetCurrentUserId(out var adminId)) return Forbid();
            var result = await _shiftService.IssueCodeAsync(entregaId, adminId);
            if (!result.Success || result.Code == null || result.ExpiresAtUtc == null)
            {
                TempData["MensagemErro"] = result.Error ?? "Não foi possível emitir o código.";
                return RedirectToAction(nameof(Details), new { id = entregaId });
            }

            Response.Headers.CacheControl = "no-store, no-cache";
            Response.Headers.Pragma = "no-cache";
            return View("CodigoTurno", new CodigoTurnoViewModel
            {
                EntregaId = entregaId,
                Codigo = result.Code,
                ExpiraEmUtc = result.ExpiresAtUtc.Value
            });
        }

        [HttpPost]
        [Authorize(Roles = "Admin")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CancelarCodigoTurno(int entregaId)
        {
            if (!TryGetCurrentUserId(out var adminId)) return Forbid();
            var cancelled = await _shiftService.CancelCodeAsync(entregaId, adminId, "Cancelamento administrativo.");
            TempData[cancelled ? "MensagemSucesso" : "MensagemErro"] = cancelled
                ? "Código não utilizado cancelado."
                : "Não há código pendente para cancelar.";
            return RedirectToAction(nameof(Details), new { id = entregaId });
        }

        [HttpGet]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> AlertasEmergencia()
        {
            var alerts = await _context.AlertasEmergencia.AsNoTracking()
                .OrderByDescending(alert => alert.CriadoEmUtc)
                .Take(100)
                .ToListAsync();
            var alertIds = alerts.Select(alert => alert.Id.ToString("N")).ToArray();
            ViewBag.Auditoria = await _context.AuditoriaOperacional.AsNoTracking()
                .Where(entry => entry.TipoEntidade == "AlertaEmergencia"
                    && alertIds.Contains(entry.IdentificadorEntidade))
                .OrderByDescending(entry => entry.OcorridoEmUtc)
                .Take(250)
                .ToListAsync();
            return View(alerts);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CriarAlertaEmergencia(
            Guid chaveIdempotencia,
            int entregaId,
            double? latitude,
            double? longitude,
            double? precisaoMetros,
            DateTime? localizacaoCapturadaEmUtc)
        {
            if (!TryGetCurrentUserId(out var driverId)) return Forbid();
            EmergencyAlertResult result;
            try
            {
                result = await _emergencyAlertService.CreateAsync(driverId, new EmergencyAlertRequest(
                    chaveIdempotencia,
                    entregaId,
                    latitude,
                    longitude,
                    precisaoMetros,
                    localizacaoCapturadaEmUtc));
            }
            catch (DbUpdateException exception)
            {
                _logger?.LogError(exception, "Falha ao persistir alerta de emergência para a entrega {EntregaId}.", entregaId);
                return StatusCode(StatusCodes.Status503ServiceUnavailable, new
                {
                    registrado = false,
                    mensagem = "O banco não confirmou o registro do alerta. Verifique a conexão e reenvie a mesma solicitação."
                });
            }

            if (result.Success)
            {
                return Ok(new
                {
                    registrado = true,
                    duplicado = result.Duplicate,
                    alertaId = result.Alert!.Id,
                    status = result.Alert.Status,
                    localizacao = result.Alert.SituacaoLocalizacao,
                    mensagem = result.Duplicate
                        ? "Este acionamento já havia sido registrado."
                        : "Alerta registrado no sistema. A equipe pode consultá-lo na central operacional."
                });
            }

            return Conflict(new { registrado = false, mensagem = result.Error ?? "O alerta não foi registrado." });
        }

        [HttpPost]
        [Authorize(Roles = "Admin")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ReconhecerAlertaEmergencia(Guid id)
        {
            if (!TryGetCurrentUserId(out var operatorId)) return Forbid();
            if (!await _emergencyAlertService.AcknowledgeAsync(id, operatorId))
            {
                TempData["MensagemErro"] = "O alerta não existe, já foi reconhecido ou foi alterado por outro operador.";
            }
            else
            {
                TempData["MensagemSucesso"] = "Alerta reconhecido e ação registrada na auditoria.";
            }
            return RedirectToAction(nameof(AlertasEmergencia));
        }

        [HttpPost]
        [Authorize(Roles = "Admin")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AtualizarStatusAlertaEmergencia(Guid id, string status)
        {
            if (!TryGetCurrentUserId(out var operatorId)) return Forbid();
            if (!await _emergencyAlertService.UpdateStatusAsync(id, operatorId, status))
            {
                TempData["MensagemErro"] = "Transição de status inválida ou alerta já atualizado.";
            }
            else
            {
                TempData["MensagemSucesso"] = $"Status do alerta atualizado para {status}.";
            }
            return RedirectToAction(nameof(AlertasEmergencia));
        }

        [HttpGet]
        public async Task<IActionResult> PerfilMotorista()
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!int.TryParse(userIdClaim, out var motoristaId))
                return Forbid();

            var motorista = await _context.Usuarios
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.Id == motoristaId && u.Perfil == "Motorista" && u.Ativo);
            if (motorista == null) return NotFound();

            var cpfDigits = new string((motorista.Cpf ?? string.Empty).Where(char.IsDigit).ToArray());
            var model = new MotoristaPerfilViewModel
            {
                Nome = motorista.Nome,
                Telefone = motorista.Telefone,
                DataNascimento = motorista.DataNascimento,
                CpfMascarado = cpfDigits.Length >= 2 ? $"***.***.***-{cpfDigits[^2..]}" : null
            };

            return View("PerfilMotorista", model);
        }

        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Create(int? pedidoId)
        {
            // Pedidos que ainda não têm entrega
            var pedidosDisponiveis = await _context.Pedidos
                .Include(p => p.Cliente)
                .Where(p => p.Entrega == null
                    && (p.Status == "Registrado" || p.Status == "EmProcessamento"))
                .OrderByDescending(p => p.DataPedido)
                .ToListAsync();

            await PrepararOpcoesDespachoAsync(pedidosDisponiveis);

            var model = new NovaEntregaViewModel();
            if (pedidoId.HasValue)
            {
                var pedido = pedidosDisponiveis.FirstOrDefault(p => p.Id == pedidoId.Value);
                if (pedido != null)
                {
                    model.PedidoId = pedido.Id;
                    PreencherDestinatario(model, pedido);
                }
            }

            return View(model);
        }

        [HttpPost]
        [Authorize(Roles = "Admin")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(NovaEntregaViewModel model)
        {
            try
            {
                if (ModelState.IsValid)
                {
                    await using var transaction = _context.Database.IsRelational()
                        ? await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable)
                        : null;
                    var pedido = await _context.Pedidos
                        .Include(p => p.Cliente)
                        .FirstOrDefaultAsync(p => p.Id == model.PedidoId);
                    var pedidoJaPossuiEntrega = await _context.Entregas.AnyAsync(e => e.PedidoId == model.PedidoId);
                    if (pedido == null || (pedido.Status != "Registrado" && pedido.Status != "EmProcessamento"))
                    {
                        ModelState.AddModelError(nameof(model.PedidoId), "O pedido não está disponível para despacho.");
                    }
                    else if (pedidoJaPossuiEntrega)
                    {
                        ModelState.AddModelError(nameof(model.PedidoId), "Este pedido já possui uma entrega cadastrada.");
                    }

                    Usuario? motorista = null;
                    Veiculo? veiculo = null;
                    if (ModelState.IsValid && model.MotoristaId.HasValue)
                    {
                        motorista = await _context.Usuarios.FirstOrDefaultAsync(u =>
                            u.Id == model.MotoristaId.Value
                            && u.Perfil == "Motorista"
                            && u.Ativo);
                        if (motorista == null)
                        {
                            ModelState.AddModelError(nameof(model.MotoristaId), "Selecione um motorista ativo da operação.");
                        }
                        else if (await _context.Entregas.AnyAsync(e =>
                            e.MotoristaId == motorista.Id
                            && (e.AtribuicaoAtiva || e.StatusEntrega == "Pendente" || e.StatusEntrega == "Em transporte"))
                            || await _context.TurnosMotorista.AnyAsync(t => t.MotoristaId == motorista.Id && t.Ativo))
                        {
                            ModelState.AddModelError(nameof(model.MotoristaId), "Este motorista já possui uma entrega ativa.");
                        }
                    }

                    if (ModelState.IsValid && model.VeiculoId.HasValue)
                    {
                        veiculo = await _context.Veiculos.FirstOrDefaultAsync(v =>
                            v.Id == model.VeiculoId.Value && v.Ativo);
                        if (veiculo == null)
                        {
                            ModelState.AddModelError(nameof(model.VeiculoId), "Selecione um veículo ativo da operação.");
                        }
                        else if (veiculo.CapacidadeCargaKg < pedido!.PesoKg)
                        {
                            ModelState.AddModelError(nameof(model.VeiculoId), "A capacidade nominal do veículo é menor que o peso informado do pedido.");
                        }
                        else if (await _context.Entregas.AnyAsync(e =>
                            e.VeiculoId == veiculo.Id
                            && (e.AtribuicaoAtiva || e.StatusEntrega == "Pendente" || e.StatusEntrega == "Em transporte"))
                            || await _context.TurnosMotorista.AnyAsync(t => t.VeiculoId == veiculo.Id && t.Ativo))
                        {
                            ModelState.AddModelError(nameof(model.VeiculoId), "Este veículo já está reservado para uma entrega ativa.");
                        }
                    }

                    if (ModelState.IsValid)
                    {
                        var codigoRastreio = _trackingService.GenerateTrackingCode();
                        while (await _context.Entregas.AnyAsync(e => e.CodigoRastreio == codigoRastreio))
                        {
                            codigoRastreio = _trackingService.GenerateTrackingCode();
                        }

                        var entrega = new Entrega
                        {
                            PedidoId = model.PedidoId,
                            MotoristaId = model.MotoristaId,
                            VeiculoId = model.VeiculoId,
                            CodigoRastreio = codigoRastreio,
                            DestinatarioNome = model.DestinatarioNome,
                            DestinatarioDocumento = model.DestinatarioDocumento,
                            CepDestino = model.CepDestino,
                            EnderecoDestino = model.EnderecoDestino,
                            NumeroDestino = model.NumeroDestino,
                            ComplementoDestino = model.ComplementoDestino,
                            BairroDestino = model.BairroDestino,
                            CidadeDestino = model.CidadeDestino,
                            EstadoDestino = model.EstadoDestino.ToUpper(),
                            DataCriacao = DateTime.UtcNow,
                            DataPrevisaoEntrega = DateTime.UtcNow.AddDays(model.PrevisaoDias),
                            StatusEntrega = "Pendente",
                            AtribuicaoAtiva = model.MotoristaId.HasValue && model.VeiculoId.HasValue,
                            Observacoes = model.Observacoes,
                            AlturaMetros = model.AlturaMetros,
                            LarguraMetros = model.LarguraMetros,
                            PesoToneladas = model.PesoToneladas,
                            QtdEixos = model.QtdEixos,
                            TipoCarga = model.TipoCarga ?? "Carga Geral Seca",
                            RotaRestritaCaminhao = model.RotaRestritaCaminhao
                        };

                        if (veiculo != null)
                        {
                            entrega.AlturaMetros = veiculo.AlturaMetros;
                            entrega.LarguraMetros = veiculo.LarguraMetros;
                            entrega.PesoToneladas = veiculo.PesoToneladas;
                            entrega.QtdEixos = veiculo.QtdEixos;
                            entrega.TipoCarga = veiculo.TipoCarga;
                        }

                        _context.Entregas.Add(entrega);

                        pedido!.Status = "ComEntregaAtribuida";
                        pedido.Versao++;

                        _context.EventosRastreio.Add(new EventoRastreio
                        {
                            Entrega = entrega,
                            DataHora = DateTime.UtcNow,
                            Titulo = "Pedido validado e remessa preparada",
                            Descricao = "Aguardando início de turno autenticado pelo motorista.",
                            Localidade = string.IsNullOrWhiteSpace(pedido.OrigemCidade)
                                ? "Local de coleta não informado"
                                : $"Coleta prevista: {pedido.OrigemCidade} / {pedido.OrigemEstado}",
                            StatusAssociado = "Pendente",
                            Icone = "bi-box-seam"
                        });

                        try
                        {
                            await _context.SaveChangesAsync();
                            if (transaction != null) await transaction.CommitAsync();
                        }
                        catch (DbUpdateConcurrencyException)
                        {
                            if (transaction != null) await transaction.RollbackAsync();
                            TempData["MensagemErro"] = "O pedido foi alterado por outra operação. Atualize e tente novamente.";
                            return RedirectToAction("Details", "Pedidos", new { id = model.PedidoId });
                        }
                        catch (DbUpdateException exception)
                        {
                            if (transaction != null) await transaction.RollbackAsync();
                            _logger?.LogError(exception, "Falha concorrente ao despachar pedido {PedidoId}.", model.PedidoId);
                            TempData["MensagemErro"] = "A atribuição conflitou com outra operação e não foi gravada. Atualize o despacho e tente novamente.";
                            return RedirectToAction("Details", "Pedidos", new { id = model.PedidoId });
                        }

                        TempData["MensagemSucesso"] = $"Entrega gerada com sucesso! Código de Rastreio: {codigoRastreio}";
                        return RedirectToAction(nameof(Details), new { id = entrega.Id });
                    }
                }

                var pedidosDisponiveis = await _context.Pedidos
                    .Include(p => p.Cliente)
                    .Where(p => (p.Entrega == null
                        && (p.Status == "Registrado" || p.Status == "EmProcessamento"))
                        || p.Id == model.PedidoId)
                    .OrderByDescending(p => p.DataPedido)
                    .ToListAsync();
                await PrepararOpcoesDespachoAsync(pedidosDisponiveis);

                return View(model);
            }
            catch (DbException exception)
            {
                _logger?.LogWarning(exception, "O despacho do pedido {PedidoId} conflitou com outra operação.", model.PedidoId);
                TempData["MensagemErro"] = "O despacho não foi confirmado. Atualize e confira os recursos antes de reenviar.";
                return RedirectToAction(nameof(Create), new { pedidoId = model.PedidoId });
            }
        }

        private async Task PrepararOpcoesDespachoAsync(List<Pedido> pedidosDisponiveis)
        {
            ViewBag.PedidosDisponiveis = pedidosDisponiveis;
            ViewBag.Motoristas = await _context.Usuarios
                .Where(u => u.Perfil == "Motorista" && u.Ativo
                    && !_context.Entregas.Any(e => e.MotoristaId == u.Id
                        && (e.StatusEntrega == "Pendente" || e.StatusEntrega == "Em transporte"))
                    && !_context.TurnosMotorista.Any(t => t.MotoristaId == u.Id && t.Ativo))
                .OrderBy(u => u.Nome)
                .ToListAsync();
            ViewBag.Veiculos = await _context.Veiculos
                .Where(v => v.Ativo
                    && !_context.Entregas.Any(e => e.VeiculoId == v.Id
                        && (e.StatusEntrega == "Pendente" || e.StatusEntrega == "Em transporte"))
                    && !_context.TurnosMotorista.Any(t => t.VeiculoId == v.Id && t.Ativo))
                .OrderBy(v => v.Modelo)
                .ToListAsync();
        }

        private static void PreencherDestinatario(NovaEntregaViewModel model, Pedido pedido)
        {
            model.DestinatarioNome = pedido.Cliente?.RazaoSocial ?? string.Empty;
            model.DestinatarioDocumento = pedido.Cliente?.Cnpj;
            model.CepDestino = pedido.DestinoCep ?? pedido.Cliente?.Cep ?? string.Empty;
            model.EnderecoDestino = pedido.DestinoEndereco ?? pedido.Cliente?.Logradouro ?? string.Empty;
            model.NumeroDestino = pedido.DestinoNumero ?? pedido.Cliente?.Numero ?? string.Empty;
            model.ComplementoDestino = pedido.Cliente?.Complemento ?? string.Empty;
            model.BairroDestino = pedido.DestinoBairro ?? pedido.Cliente?.Bairro ?? string.Empty;
            model.CidadeDestino = pedido.DestinoCidade ?? pedido.Cliente?.Cidade ?? string.Empty;
            model.EstadoDestino = pedido.DestinoEstado ?? pedido.Cliente?.Estado ?? "SP";
        }

        [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null) return NotFound();

            if (User.IsInRole("Motorista") && !User.IsInRole("Admin"))
            {
                var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (!int.TryParse(userIdClaim, out var motoristaId))
                    return Forbid();

                var assigned = await _context.Entregas
                    .AnyAsync(e => e.Id == id && e.MotoristaId == motoristaId);
                if (!assigned) return Forbid();

                return RedirectToAction(nameof(Navegar), new { id });
            }

            var entrega = await _context.Entregas
                .Include(e => e.Pedido)
                    .ThenInclude(p => p!.Cliente)
                .Include(e => e.Motorista)
                .Include(e => e.Veiculo)
                .Include(e => e.EventosRastreio.OrderByDescending(ev => ev.DataHora))
                .FirstOrDefaultAsync(e => e.Id == id);

            if (entrega == null) return NotFound();
            if (!PodeAcessarEntrega(entrega)) return Forbid();

            ViewBag.HasActiveShift = await _context.TurnosMotorista.AnyAsync(t => t.EntregaId == entrega.Id && t.Ativo);
            ViewBag.Motoristas = await _context.Usuarios
                .Where(u => u.Perfil == "Motorista" && u.Ativo
                    && !_context.Entregas.Any(e => e.Id != entrega.Id && e.MotoristaId == u.Id
                        && (e.StatusEntrega == "Pendente" || e.StatusEntrega == "Em transporte"))
                    && !_context.TurnosMotorista.Any(t => t.EntregaId != entrega.Id && t.MotoristaId == u.Id && t.Ativo))
                .OrderBy(u => u.Nome)
                .ToListAsync();

            ViewBag.Veiculos = await _context.Veiculos
                .Where(v => v.Ativo
                    && !_context.Entregas.Any(e => e.Id != entrega.Id && e.VeiculoId == v.Id
                        && (e.StatusEntrega == "Pendente" || e.StatusEntrega == "Em transporte"))
                    && !_context.TurnosMotorista.Any(t => t.EntregaId != entrega.Id && t.VeiculoId == v.Id && t.Ativo))
                .OrderBy(v => v.Modelo)
                .ToListAsync();

            return View(entrega);
        }

        [HttpGet]
        [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
        public async Task<IActionResult> Navegar(int id)
        {
            var isAdmin = User.IsInRole("Admin");
            IQueryable<Entrega> entregaQuery = _context.Entregas
                .Include(e => e.Veiculo);

            if (isAdmin)
            {
                entregaQuery = entregaQuery
                    .Include(e => e.Pedido)
                        .ThenInclude(p => p!.Cliente);
            }

            var entrega = await entregaQuery.FirstOrDefaultAsync(e => e.Id == id);

            if (entrega == null) return NotFound();

            if (!PodeAcessarEntrega(entrega)) return Forbid();
            if (!isAdmin && (!TryGetCurrentUserId(out var driverId)
                || !await _shiftService.HasActiveShiftAsync(driverId, id)))
            {
                return Forbid();
            }

            var pesoVeiculo = entrega.Veiculo?.PesoToneladas ?? entrega.PesoToneladas;
            var eixosVeiculo = entrega.Veiculo?.QtdEixos ?? entrega.QtdEixos;
            bool isPesado = pesoVeiculo > 3.5m || eixosVeiculo > 2;
            string perfil = isPesado ? "Pesado" : "Leve";

            var plano = await _routingService.CalcularRotaParaEntregaAsync(id, perfil);
            var custo = isAdmin
                ? _fuelService.CalcularCustoViagem(
                    plano.DistanciaKm,
                    perfil,
                    plano.EixosVeiculo,
                    valorPedido: entrega.Pedido?.ValorTotal ?? 0m)
                : new TripCostResult();

            ViewBag.GpsTurnoId = isAdmin ? (Guid?)null : await _context.TurnosMotorista
                .Where(t => t.EntregaId == id && t.Ativo && t.MotoristaId == entrega.MotoristaId)
                .Select(t => (Guid?)t.Id).SingleOrDefaultAsync();
            ViewBag.PlanoRota = plano;
            ViewBag.CustoViagem = custo;
            ViewBag.IsDriver = !isAdmin;

            return View(entrega);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> FinalizarEntregaPOD(
            int entregaId, 
            string nomeRecebedor, 
            string? documentoRecebedor, 
            string? assinaturaBase64, 
            string? localidade)
        {
            var driverRequest = User.IsInRole("Motorista") && !User.IsInRole("Admin");
            if (string.IsNullOrWhiteSpace(nomeRecebedor))
            {
                TempData["MensagemErro"] = "O nome da pessoa que recebeu o pacote é obrigatório para emissão do POD.";
                return driverRequest
                    ? RedirectToAction(nameof(Navegar), new { id = entregaId })
                    : RedirectToAction(nameof(Details), new { id = entregaId });
            }

            var entrega = await _context.Entregas.FindAsync(entregaId);
            if (entrega == null) return NotFound();
            if (!PodeAcessarEntrega(entrega)) return Forbid();
            if (entrega.StatusEntrega == "Entregue")
            {
                if (PodContent.Matches(entrega, nomeRecebedor, documentoRecebedor, assinaturaBase64))
                {
                    TempData["MensagemSucesso"] = "A confirmação de entrega já havia sido registrada.";
                }
                else
                {
                    TempData["MensagemErro"] = "Esta entrega já foi concluída com outro conteúdo de comprovante.";
                }
                return driverRequest
                    ? RedirectToAction(nameof(Index))
                    : RedirectToAction(nameof(Details), new { id = entregaId });
            }
            if (!User.IsInRole("Admin")
                && (!TryGetCurrentUserId(out var driverId)
                    || !await _shiftService.HasActiveShiftAsync(driverId, entregaId)))
                return Forbid();
            if (entrega.StatusEntrega != "Em transporte")
            {
                TempData["MensagemErro"] = "Só é possível concluir uma entrega em transporte.";
                return RedirectToAction(nameof(Index));
            }

            bool sucesso;
            try
            {
                sucesso = await _trackingService.FinalizarEntregaPODAsync(
                    entregaId,
                    nomeRecebedor.Trim(),
                    documentoRecebedor?.Trim(),
                    assinaturaBase64,
                    null,
                    string.IsNullOrWhiteSpace(localidade) ? "Destino Final" : localidade.Trim());
            }
            catch (DbUpdateException exception)
            {
                _logger?.LogError(exception, "Falha ao persistir POD da entrega {EntregaId}.", entregaId);
                TempData["MensagemErro"] = "O banco não confirmou a conclusão. Confirme a conexão antes de reenviar o POD.";
                return driverRequest
                    ? RedirectToAction(nameof(Navegar), new { id = entregaId })
                    : RedirectToAction(nameof(Details), new { id = entregaId });
            }

            if (sucesso)
            {
                TempData["MensagemSucesso"] = "Comprovante Digital de Entrega (POD) registrado com sucesso! Pacote finalizado.";
            }
            else
            {
                TempData["MensagemErro"] = "Falha ao registrar comprovante digital.";
            }

            return driverRequest
                ? RedirectToAction(nameof(Index))
                : RedirectToAction(nameof(Details), new { id = entregaId });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> AtribuirVeiculo(int entregaId, int? veiculoId)
        {
            try
            {
                await using var transaction = _context.Database.IsRelational()
                    ? await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable) : null;
                var entrega = await _context.Entregas
                    .Include(e => e.Pedido)
                    .FirstOrDefaultAsync(e => e.Id == entregaId);
                if (entrega == null) return NotFound();
                if (await _context.TurnosMotorista.AnyAsync(t => t.EntregaId == entregaId && t.Ativo))
                    return Conflict("Encerre o turno antes de alterar o veículo.");

                if (entrega.StatusEntrega is "Entregue" or "Cancelada")
                {
                    TempData["MensagemErro"] = "Não é possível alterar o veículo de uma entrega concluída ou cancelada.";
                    return RedirectToAction(nameof(Details), new { id = entregaId });
                }

                Veiculo? veiculo = null;
                if (veiculoId.HasValue)
                {
                    veiculo = await _context.Veiculos.FirstOrDefaultAsync(v =>
                        v.Id == veiculoId.Value && v.Ativo);
                    if (veiculo == null)
                    {
                        TempData["MensagemErro"] = "O veículo selecionado não está ativo ou não pertence à operação.";
                        return RedirectToAction(nameof(Details), new { id = entregaId });
                    }

                    if (entrega.Pedido != null && veiculo.CapacidadeCargaKg < entrega.Pedido.PesoKg)
                    {
                        TempData["MensagemErro"] = "A capacidade nominal do veículo é menor que o peso informado do pedido.";
                        return RedirectToAction(nameof(Details), new { id = entregaId });
                    }

                    if (await _context.Entregas.AnyAsync(e =>
                        e.Id != entrega.Id
                        && e.VeiculoId == veiculo.Id
                        && (e.AtribuicaoAtiva || e.StatusEntrega == "Pendente" || e.StatusEntrega == "Em transporte"))
                        || await _context.TurnosMotorista.AnyAsync(t => t.VeiculoId == veiculo.Id && t.Ativo))
                    {
                        TempData["MensagemErro"] = "Este veículo já está reservado para outra entrega ativa.";
                        return RedirectToAction(nameof(Details), new { id = entregaId });
                    }
                }
                else if (entrega.StatusEntrega == "Em transporte")
                {
                    TempData["MensagemErro"] = "Não é possível remover o veículo de uma entrega em transporte.";
                    return RedirectToAction(nameof(Details), new { id = entregaId });
                }

                if (veiculoId.HasValue && entrega.MotoristaId.HasValue
                    && (!await _context.Usuarios.AnyAsync(u => u.Id == entrega.MotoristaId && u.Ativo && u.Perfil == "Motorista")
                        || await _context.Entregas.AnyAsync(e => e.Id != entrega.Id && e.MotoristaId == entrega.MotoristaId
                            && (e.StatusEntrega == "Pendente" || e.StatusEntrega == "Em transporte"))
                        || await _context.TurnosMotorista.AnyAsync(t => t.MotoristaId == entrega.MotoristaId && t.Ativo)))
                {
                    TempData["MensagemErro"] = "O motorista vinculado não está elegível para esta atribuição.";
                    return RedirectToAction(nameof(Details), new { id = entregaId });
                }
                if (!TryGetCurrentUserId(out var adminId)) return Forbid();
                var vehicleChanged = entrega.VeiculoId != veiculoId;
                if (!vehicleChanged)
                {
                    TempData["MensagemSucesso"] = "Nenhuma alteração de veículo foi necessária.";
                    return RedirectToAction(nameof(Details), new { id = entregaId });
                }
                await _shiftService.CancelCodeAsync(entregaId, adminId, "Atribuição de veículo alterada.");
                entrega.VeiculoId = veiculoId;
                LimparUltimaPosicaoGps(entrega);
                if (vehicleChanged)
                {
                    entrega.StatusEntrega = "Pendente";
                }
                entrega.AtribuicaoAtiva = entrega.MotoristaId.HasValue && entrega.VeiculoId.HasValue
                    && entrega.StatusEntrega is "Pendente" or "Em transporte";
                entrega.Versao++;
                if (veiculo != null)
                {
                    entrega.AlturaMetros = veiculo.AlturaMetros;
                    entrega.LarguraMetros = veiculo.LarguraMetros;
                    entrega.PesoToneladas = veiculo.PesoToneladas;
                    entrega.QtdEixos = veiculo.QtdEixos;
                    entrega.TipoCarga = veiculo.TipoCarga;
                }

                try
                {
                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateException exception)
                {
                    _logger?.LogError(exception, "Conflito de atribuição de veículo para entrega {EntregaId}.", entregaId);
                    TempData["MensagemErro"] = "O veículo foi reservado em outra operação. Atualize e tente novamente.";
                    return RedirectToAction(nameof(Details), new { id = entregaId });
                }
                if (transaction != null) await transaction.CommitAsync();
                TempData["MensagemSucesso"] = veiculo == null
                    ? "Veículo removido da entrega pendente."
                    : "Veículo e parâmetros de carga atualizados.";
                return RedirectToAction(nameof(Details), new { id = entregaId });
            }
            catch (Exception exception) when (exception is DbUpdateException or DbException)
            {
                _logger?.LogWarning(exception, "A atribuição da entrega {EntregaId} conflitou com outra operação.", entregaId);
                TempData["MensagemErro"] = "A atribuição não foi confirmada. Atualize e tente novamente.";
                return RedirectToAction(nameof(Details), new { id = entregaId });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AdicionarCheckpoint(NovoCheckpointViewModel model)
        {
            if (!ModelState.IsValid)
            {
                TempData["MensagemErro"] = "Preencha todos os campos obrigatórios para o novo checkpoint.";
                return RedirectToAction(nameof(Details), new { id = model.EntregaId });
            }

            var entrega = await _context.Entregas.FindAsync(model.EntregaId);
            if (entrega == null) return NotFound();
            if (!PodeAcessarEntrega(entrega)) return Forbid();
            if (!User.IsInRole("Admin")
                && (!TryGetCurrentUserId(out var driverId)
                    || !await _shiftService.HasActiveShiftAsync(driverId, entrega.Id)))
                return Forbid();

            if (model.StatusAssociado == "Entregue")
            {
                TempData["MensagemErro"] = "Use a confirmação de POD para concluir a entrega.";
                return RedirectToAction(nameof(Details), new { id = model.EntregaId });
            }

            if (model.StatusAssociado != entrega.StatusEntrega)
            {
                TempData["MensagemErro"] = "Checkpoint não altera o status operacional. Use o fluxo autorizado de turno ou despacho.";
                return RedirectToAction(nameof(Details), new { id = model.EntregaId });
            }

            if (!User.IsInRole("Admin") && (model.StatusAssociado != "Em transporte"
                || entrega.StatusEntrega != "Em transporte"))
                return Forbid();

            // Validação de regra de negócio do PIM: não permitir voltar estado se já entregue
            if (entrega.StatusEntrega == "Entregue" && model.StatusAssociado != "Entregue")
            {
                TempData["MensagemErro"] = "Uma entrega já finalizada (Entregue) não pode retornar a status anteriores.";
                return RedirectToAction(nameof(Details), new { id = model.EntregaId });
            }

            await _trackingService.AddCheckpointAsync(
                model.EntregaId,
                model.Titulo,
                model.Descricao,
                model.Localidade,
                model.StatusAssociado,
                model.Icone
            );

            TempData["MensagemSucesso"] = "Novo checkpoint de rastreamento adicionado com sucesso!";
            return RedirectToAction(nameof(Details), new { id = model.EntregaId });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> AtribuirMotorista(int entregaId, int? motoristaId)
        {
            try
            {
                await using var transaction = _context.Database.IsRelational()
                    ? await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable) : null;
                var entrega = await _context.Entregas
                    .Include(e => e.Pedido)
                    .FirstOrDefaultAsync(e => e.Id == entregaId);

                if (entrega == null) return NotFound();

                if (entrega.StatusEntrega is "Entregue" or "Cancelada")
                {
                    TempData["MensagemErro"] = "Não é possível alterar o motorista de uma entrega concluída ou cancelada.";
                    return RedirectToAction(nameof(Details), new { id = entregaId });
                }

                if (await _context.TurnosMotorista.AnyAsync(t => t.EntregaId == entregaId && t.Ativo))
                {
                    TempData["MensagemErro"] = "Não é possível alterar a atribuição durante um turno ativo.";
                    return RedirectToAction(nameof(Details), new { id = entregaId });
                }

                Usuario? motorista = null;
                if (motoristaId.HasValue)
                {
                    motorista = await _context.Usuarios.FirstOrDefaultAsync(u =>
                        u.Id == motoristaId.Value
                        && u.Perfil == "Motorista"
                        && u.Ativo);
                    if (motorista == null)
                    {
                        TempData["MensagemErro"] = "O motorista selecionado não está ativo ou não pertence à operação.";
                        return RedirectToAction(nameof(Details), new { id = entregaId });
                    }

                    if (await _context.Entregas.AnyAsync(e =>
                        e.Id != entrega.Id
                        && e.MotoristaId == motorista.Id
                        && (e.AtribuicaoAtiva || e.StatusEntrega == "Pendente" || e.StatusEntrega == "Em transporte"))
                        || await _context.TurnosMotorista.AnyAsync(t => t.MotoristaId == motorista.Id && t.Ativo))
                    {
                        TempData["MensagemErro"] = "Este motorista já possui uma entrega ativa.";
                        return RedirectToAction(nameof(Details), new { id = entregaId });
                    }
                }

                if (motoristaId.HasValue && entrega.VeiculoId.HasValue
                    && await _context.Entregas.AnyAsync(e =>
                        e.Id != entrega.Id
                        && e.VeiculoId == entrega.VeiculoId
                        && (e.AtribuicaoAtiva || e.StatusEntrega == "Pendente" || e.StatusEntrega == "Em transporte")))
                {
                    TempData["MensagemErro"] = "O veículo desta entrega já está reservado para outra entrega ativa.";
                    return RedirectToAction(nameof(Details), new { id = entregaId });
                }

                if (motoristaId.HasValue && entrega.VeiculoId.HasValue
                    && (!await _context.Veiculos.AnyAsync(v => v.Id == entrega.VeiculoId && v.Ativo
                            && (entrega.Pedido == null || v.CapacidadeCargaKg >= entrega.Pedido.PesoKg))
                        || await _context.TurnosMotorista.AnyAsync(t => t.VeiculoId == entrega.VeiculoId && t.Ativo)))
                {
                    TempData["MensagemErro"] = "O veículo vinculado está inativo, comprometido ou com capacidade insuficiente.";
                    return RedirectToAction(nameof(Details), new { id = entregaId });
                }
                if (!TryGetCurrentUserId(out var adminId)) return Forbid();
                if (entrega.MotoristaId == motoristaId)
                {
                    TempData["MensagemSucesso"] = "Nenhuma alteração de motorista foi necessária.";
                    return RedirectToAction(nameof(Details), new { id = entregaId });
                }
                await _shiftService.CancelCodeAsync(entregaId, adminId, "Atribuição de motorista alterada.");
                entrega.MotoristaId = motoristaId;
                LimparUltimaPosicaoGps(entrega);
                entrega.StatusEntrega = "Pendente";
                entrega.AtribuicaoAtiva = entrega.MotoristaId.HasValue && entrega.VeiculoId.HasValue;
                entrega.Versao++;
                _context.EventosRastreio.Add(new EventoRastreio
                {
                    EntregaId = entrega.Id,
                    DataHora = DateTime.UtcNow,
                    Titulo = "Atribuição de motorista atualizada",
                    Descricao = "A operação alterou a atribuição. Aguarda novo início de turno.",
                    Localidade = "Localização não informada na alteração de atribuição",
                    StatusAssociado = "Pendente",
                    Icone = "bi-person-badge"
                });
                try
                {
                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateException exception)
                {
                    _logger?.LogError(exception, "Conflito de atribuição de motorista para entrega {EntregaId}.", entregaId);
                    TempData["MensagemErro"] = "O motorista já foi atribuído a outra viagem. Atualize e tente novamente.";
                    return RedirectToAction(nameof(Details), new { id = entregaId });
                }

                if (transaction != null) await transaction.CommitAsync();
                TempData["MensagemSucesso"] = "Motorista atualizado com sucesso!";
                return RedirectToAction(nameof(Details), new { id = entregaId });
            }
            catch (Exception exception) when (exception is DbUpdateException or DbException)
            {
                _logger?.LogWarning(exception, "A atribuição da entrega {EntregaId} conflitou com outra operação.", entregaId);
                TempData["MensagemErro"] = "A atribuição não foi confirmada. Atualize e tente novamente.";
                return RedirectToAction(nameof(Details), new { id = entregaId });
            }
        }

        private static void LimparUltimaPosicaoGps(Entrega entrega)
        {
            // Historical samples retain their original resource IDs. A new
            // assignment waits for a new observation instead of inheriting a marker.
            entrega.UltimaLatitudeGps = null; entrega.UltimaLongitudeGps = null;
            entrega.UltimaPrecisaoGps = null; entrega.UltimaVelocidadeGps = null; entrega.UltimaDirecaoGps = null;
            entrega.UltimoGpsCapturadoEmUtc = null; entrega.UltimoGpsRecebidoEmUtc = null;
        }

        private bool PodeAcessarEntrega(Entrega entrega)
        {
            if (User.IsInRole("Admin"))
                return true;

            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            return User.IsInRole("Motorista")
                && int.TryParse(userIdClaim, out var motoristaId)
                && entrega.MotoristaId == motoristaId;
        }

        private bool TryGetCurrentUserId(out int userId) =>
            int.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out userId);

    }
}
