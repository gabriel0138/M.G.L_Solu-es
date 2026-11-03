using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MglConnect.Web.Models;
using MglConnect.Web.ViewModels;

namespace MglConnect.Web.Controllers;

public sealed partial class FrotaController
{
    // Protected snapshots detect stale forms without adding columns to existing databases.
    private IDataProtector Protector => protection.CreateProtector("MglConnect.Fleet.Edit.v1");
    private static string Snapshot(Veiculo v) => JsonSerializer.Serialize(new { v.Id, v.TenantId, v.Placa,
        v.Modelo, v.Ativo, v.CapacidadeCargaKg, v.AlturaMetros, v.LarguraMetros, v.ComprimentoMetros,
        v.PesoToneladas, v.QtdEixos, v.TipoCarga, v.PerfilOperacionalVersao, v.PerfilOperacionalJson });
    private static string Snapshot(Usuario u) => JsonSerializer.Serialize(new { u.Id, u.TenantId, u.Nome,
        u.Login, u.Telefone, u.Perfil, u.Ativo, u.ClienteId, u.DataCadastro });
    private bool Matches(string? seal, string snapshot)
    {
        if (string.IsNullOrEmpty(seal) || seal.Length > 8192) return false;
        try { return Protector.Unprotect(seal) == snapshot; }
        catch (CryptographicException) { return false; }
    }
    private FleetVehicleForm VehicleForm(Veiculo v) => new()
    {
        Id = v.Id, Selo = Protector.Protect(Snapshot(v)), Modelo = v.Modelo, Placa = v.Placa,
        CapacidadeCargaKg = v.CapacidadeCargaKg, AlturaMetros = v.AlturaMetros, LarguraMetros = v.LarguraMetros,
        ComprimentoMetros = v.ComprimentoMetros, PesoToneladas = v.PesoToneladas, QtdEixos = v.QtdEixos, TipoCarga = v.TipoCarga
    };
    private FleetDriverForm DriverForm(Usuario u) => new()
    {
        Id = u.Id, Selo = Protector.Protect(Snapshot(u)), Nome = u.Nome, Login = u.Login, Telefone = u.Telefone
    };
    private Task<bool> VehicleBusy(int id) => database.Entregas.AnyAsync(e => e.VeiculoId == id
        && (e.StatusEntrega == "Pendente" || e.StatusEntrega == "Em transporte"));
    private Task<bool> DriverBusy(int id) => database.Entregas.AnyAsync(e => e.MotoristaId == id
        && (e.StatusEntrega == "Pendente" || e.StatusEntrega == "Em transporte"));
    private async Task<bool> Busy(int id, bool vehicle) => (vehicle ? await VehicleBusy(id) : await DriverBusy(id))
        || await database.TurnosMotorista.AnyAsync(t => t.Ativo && (vehicle ? t.VeiculoId == id : t.MotoristaId == id));

    [HttpGet]
    public IActionResult NovoVeiculo() => View("VeiculoForm", new FleetVehicleForm());

    [HttpGet]
    public async Task<IActionResult> EditarVeiculo(int id)
    {
        var v = await database.Veiculos.AsNoTracking().SingleOrDefaultAsync(v => v.Id == id);
        return v == null ? NotFound() : View("VeiculoForm", VehicleForm(v));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public Task<IActionResult> NovoVeiculo(FleetVehicleForm model) => SaveVehicle(model, false);

    [HttpPost, ValidateAntiForgeryToken]
    public Task<IActionResult> EditarVeiculo(int id, FleetVehicleForm model) =>
        id != model.Id ? Task.FromResult<IActionResult>(BadRequest()) : SaveVehicle(model, true);

    private IActionResult InvalidVehicle(FleetVehicleForm model, string? message = null, int status = 400)
    {
        if (message != null) ModelState.AddModelError("", message);
        Response.StatusCode = status;
        return View("VeiculoForm", model);
    }
    private async Task<IActionResult> SaveVehicle(FleetVehicleForm model, bool edit)
    {
        model.Modelo = model.Modelo?.Trim() ?? "";
        model.TipoCarga = model.TipoCarga?.Trim() ?? "";
        var normalized = Regex.Replace(model.Placa ?? "", @"[\s-]", "").ToUpperInvariant();
        if (!ModelState.IsValid) return InvalidVehicle(model);
        try
        {
            await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            var v = edit ? await database.Veiculos.SingleOrDefaultAsync(v => v.Id == model.Id) : new Veiculo();
            if (v == null) return NotFound();
            if (edit && !Matches(model.Selo, Snapshot(v)))
                return InvalidVehicle(model, "Cadastro alterado por outra operação. Reabra a edição antes de salvar.", 409);
            // Preserve legacy plates when unchanged; every new plate must use a supported Brazilian format.
            if ((!edit || model.Placa != v.Placa) && !Regex.IsMatch(normalized, "^[A-Z]{3}[0-9][A-Z0-9][0-9]{2}$"))
                return InvalidVehicle(model, "Informe uma placa brasileira válida, no padrão ABC1234 ou ABC1D23.");
            if (await database.Veiculos.IgnoreQueryFilters().AnyAsync(x => x.Id != v.Id
                && x.Placa.Replace("-", "").Replace(" ", "").ToUpper() == normalized))
                return InvalidVehicle(model, "Placa já cadastrada ou indisponível.", 409);
            if (new[] { (model.CapacidadeCargaKg, v.CapacidadeCargaKg), (model.AlturaMetros, v.AlturaMetros),
                (model.LarguraMetros, v.LarguraMetros), (model.ComprimentoMetros, v.ComprimentoMetros),
                (model.PesoToneladas, v.PesoToneladas) }.Any(pair => pair.Item1 == 0 && (!edit || pair.Item2 != 0)))
                return InvalidVehicle(model, "Informe valores maiores que zero. Valores não informados de cadastros antigos só podem ser preservados.");
            var operationalChange = normalized != Regex.Replace(v.Placa, @"[\s-]", "").ToUpperInvariant()
                || model.CapacidadeCargaKg != v.CapacidadeCargaKg || model.AlturaMetros != v.AlturaMetros
                || model.LarguraMetros != v.LarguraMetros || model.ComprimentoMetros != v.ComprimentoMetros
                || model.PesoToneladas != v.PesoToneladas || model.QtdEixos != v.QtdEixos || model.TipoCarga != v.TipoCarga;
            if (edit && operationalChange && await Busy(v.Id, true))
                return InvalidVehicle(model, "Encerre ou remova as atribuições e turnos antes de alterar placa, capacidade ou características operacionais.", 409);
            v.Modelo = model.Modelo;
            v.Placa = edit && model.Placa == v.Placa ? v.Placa : normalized;
            v.CapacidadeCargaKg = model.CapacidadeCargaKg;
            v.AlturaMetros = model.AlturaMetros; v.LarguraMetros = model.LarguraMetros;
            v.ComprimentoMetros = model.ComprimentoMetros; v.PesoToneladas = model.PesoToneladas;
            v.QtdEixos = model.QtdEixos; v.TipoCarga = model.TipoCarga;
            if (!edit) database.Veiculos.Add(v);
            await database.SaveChangesAsync();
            await Audit(v.Id, "Veiculo", edit ? "VeiculoAtualizado" : "VeiculoCadastrado");
            await transaction.CommitAsync();
            TempData["MensagemSucesso"] = "Cadastro de veículo salvo.";
            return RedirectToAction(nameof(Veiculo), new { id = v.Id });
        }
        catch (Exception ex) when (ex is DbUpdateException or DbException)
        { return InvalidVehicle(model, "O cadastro não foi confirmado. Atualize a página e confira os dados antes de reenviar.", 409); }
    }

    [HttpGet]
    public IActionResult NovoMotorista() => View("MotoristaForm", new FleetDriverForm());

    [HttpGet]
    public async Task<IActionResult> EditarMotorista(int id)
    {
        var d = await database.Usuarios.AsNoTracking().SingleOrDefaultAsync(d => d.Id == id && d.Perfil == "Motorista");
        return d == null ? NotFound() : View("MotoristaForm", DriverForm(d));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public Task<IActionResult> NovoMotorista(FleetDriverForm model) => SaveDriver(model, false);

    [HttpPost, ValidateAntiForgeryToken]
    public Task<IActionResult> EditarMotorista(int id, FleetDriverForm model) =>
        id != model.Id ? Task.FromResult<IActionResult>(BadRequest()) : SaveDriver(model, true);

    private IActionResult InvalidDriver(FleetDriverForm model, string? message = null, int status = 400)
    {
        // Even invalid POSTs never put an initial password back into the response.
        model.SenhaInicial = null;
        ModelState.SetModelValue(nameof(model.SenhaInicial), null, null);
        if (message != null) ModelState.AddModelError("", message);
        Response.StatusCode = status;
        return View("MotoristaForm", model);
    }
    private async Task<IActionResult> SaveDriver(FleetDriverForm model, bool edit)
    {
        model.Nome = model.Nome?.Trim() ?? "";
        model.Login = model.Login?.Trim().ToLowerInvariant() ?? "";
        model.Telefone = string.IsNullOrWhiteSpace(model.Telefone) ? null : model.Telefone.Trim();
        if (!edit && string.IsNullOrEmpty(model.SenhaInicial)) ModelState.AddModelError(nameof(model.SenhaInicial), "Informe a senha inicial do motorista.");
        if (edit && !string.IsNullOrEmpty(model.SenhaInicial)) ModelState.AddModelError("", "A edição cadastral não altera a senha de acesso.");
        if (!ModelState.IsValid) return InvalidDriver(model);
        try
        {
            await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            var d = edit ? await database.Usuarios.SingleOrDefaultAsync(d => d.Id == model.Id && d.Perfil == "Motorista") : new Usuario();
            if (d == null) return NotFound();
            if (edit && !Matches(model.Selo, Snapshot(d)))
                return InvalidDriver(model, "Cadastro alterado por outra operação. Reabra a edição antes de salvar.", 409);
            if (edit && model.Login != d.Login.ToLowerInvariant())
                return InvalidDriver(model, "O login é preservado na edição cadastral.");
            if (!edit && await database.Usuarios.IgnoreQueryFilters().AnyAsync(u => u.Login.ToLower() == model.Login))
                return InvalidDriver(model, "Login já cadastrado ou indisponível.", 409);
            d.Nome = model.Nome; d.Telefone = model.Telefone;
            if (!edit)
            {
                d.Login = model.Login; d.Perfil = "Motorista"; d.Ativo = true;
                d.SenhaHash = passwords.HashPassword(model.SenhaInicial!);
                database.Usuarios.Add(d);
            }
            await database.SaveChangesAsync();
            await Audit(d.Id, "Motorista", edit ? "MotoristaAtualizado" : "MotoristaCadastrado");
            await transaction.CommitAsync();
            TempData["MensagemSucesso"] = "Cadastro de motorista salvo.";
            return RedirectToAction(nameof(Motorista), new { id = d.Id });
        }
        catch (Exception ex) when (ex is DbUpdateException or DbException)
        { return InvalidDriver(model, "O cadastro não foi confirmado. Atualize a página e confira os dados antes de reenviar.", 409); }
    }

    [HttpGet]
    public Task<IActionResult> Veiculo(int id, int pagina = 1) => Details(id, true, pagina);
    [HttpGet]
    public Task<IActionResult> Motorista(int id, int pagina = 1) => Details(id, false, pagina);

    private async Task<IActionResult> Details(int id, bool vehicle, int page = 1)
    {
        var v = vehicle ? await database.Veiculos.AsNoTracking().SingleOrDefaultAsync(v => v.Id == id) : null;
        var d = vehicle ? null : await database.Usuarios.AsNoTracking().SingleOrDefaultAsync(d => d.Id == id && d.Perfil == "Motorista");
        if (v == null && d == null) return NotFound();
        var trips = database.Entregas.AsNoTracking().Where(e => vehicle ? e.VeiculoId == id : e.MotoristaId == id);
        var shifts = database.TurnosMotorista.AsNoTracking().Where(t => vehicle ? t.VeiculoId == id : t.MotoristaId == id);
        var tripCount = await trips.CountAsync(); var shiftCount = await shifts.CountAsync();
        var pages = Pages(Math.Max(tripCount, shiftCount)); page = Math.Clamp(page, 1, pages);
        return View("ResourceDetails", new FleetResourceDetailsViewModel
        {
            Veiculo = v == null ? null : VehicleForm(v), Motorista = d == null ? null : DriverForm(d),
            Ativo = v?.Ativo ?? d!.Ativo, Comprometido = await Busy(id, vehicle),
            Pagina = page, Paginas = pages, TotalViagens = tripCount, TotalTurnos = shiftCount,
            Viagens = await trips.OrderByDescending(e => e.Id).Skip((page - 1) * PageSize).Take(PageSize)
                .Select(e => new FleetHistoryTrip(e.Id, e.CodigoRastreio, e.StatusEntrega, e.AtribuicaoAtiva)).ToListAsync(),
            Turnos = await shifts.OrderByDescending(t => t.IniciadoEmUtc).ThenBy(t => t.Id).Skip((page - 1) * PageSize).Take(PageSize)
                .Select(t => new FleetHistoryShift(t.EntregaId, t.IniciadoEmUtc, t.EncerradoEmUtc, t.Ativo)).ToListAsync()
        });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public Task<IActionResult> SituacaoVeiculo(int id, bool? ativo, string? selo) =>
        !ModelState.IsValid || !ativo.HasValue ? Task.FromResult<IActionResult>(BadRequest()) : SetActive(id, ativo.Value, selo, true);
    [HttpPost, ValidateAntiForgeryToken]
    public Task<IActionResult> SituacaoMotorista(int id, bool? ativo, string? selo) =>
        !ModelState.IsValid || !ativo.HasValue ? Task.FromResult<IActionResult>(BadRequest()) : SetActive(id, ativo.Value, selo, false);

    private async Task<IActionResult> SetActive(int id, bool active, string? seal, bool vehicle)
    {
        try
        {
            await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            var v = vehicle ? await database.Veiculos.SingleOrDefaultAsync(v => v.Id == id) : null;
            var d = vehicle ? null : await database.Usuarios.SingleOrDefaultAsync(d => d.Id == id && d.Perfil == "Motorista");
            if (v == null && d == null) return NotFound();
            string? error = !Matches(seal, v != null ? Snapshot(v) : Snapshot(d!))
                ? "Cadastro alterado. Confira o estado atual antes de tentar novamente." : null;
            if (error == null && !active && await Busy(id, vehicle))
                error = "Inativação bloqueada: encerre ou remova as atribuições e turnos ativos primeiro.";
            if (error != null)
            {
                TempData["MensagemErro"] = error;
                return RedirectToAction(vehicle ? nameof(Veiculo) : nameof(Motorista), new { id });
            }
            var changed = (v?.Ativo ?? d!.Ativo) != active;
            if (changed)
            {
                if (v != null) v.Ativo = active; else d!.Ativo = active;
                await database.SaveChangesAsync();
                await Audit(id, vehicle ? "Veiculo" : "Motorista", active ? "CadastroReativado" : "CadastroInativado");
            }
            await transaction.CommitAsync();
            TempData["MensagemSucesso"] = changed ? "Situação cadastral atualizada." : "A situação cadastral já estava atualizada.";
        }
        catch (Exception ex) when (ex is DbUpdateException or DbException)
        { TempData["MensagemErro"] = "A alteração não foi confirmada. Atualize o cadastro antes de tentar novamente."; }
        return RedirectToAction(vehicle ? nameof(Veiculo) : nameof(Motorista), new { id });
    }

    private async Task Audit(int id, string kind, string operation)
    {
        database.AuditoriaOperacional.Add(new AuditoriaOperacional
        {
            AtorUsuarioId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!, CultureInfo.InvariantCulture),
            Evento = operation, TipoEntidade = kind, IdentificadorEntidade = id.ToString(CultureInfo.InvariantCulture),
            OcorridoEmUtc = DateTime.UtcNow
        });
        await database.SaveChangesAsync();
    }
}
