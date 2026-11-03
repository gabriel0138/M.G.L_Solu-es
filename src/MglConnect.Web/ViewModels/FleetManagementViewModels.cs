using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using MglConnect.Web.ModelBinding;

namespace MglConnect.Web.ViewModels;

public sealed class FleetVehicleForm : IValidatableObject
{
    public int Id { get; set; }
    public string? Selo { get; set; }
    [Required, StringLength(80)] public string Modelo { get; set; } = "";
    [Required, StringLength(10)] public string Placa { get; set; } = "";
    [ModelBinder(BinderType = typeof(HtmlDecimalModelBinder))]
    [Range(typeof(decimal), "0", "99999999.99", ParseLimitsInInvariantCulture = true)] public decimal CapacidadeCargaKg { get; set; }
    [ModelBinder(BinderType = typeof(HtmlDecimalModelBinder))]
    [Range(typeof(decimal), "0", "999.99", ParseLimitsInInvariantCulture = true)] public decimal AlturaMetros { get; set; }
    [ModelBinder(BinderType = typeof(HtmlDecimalModelBinder))]
    [Range(typeof(decimal), "0", "999.99", ParseLimitsInInvariantCulture = true)] public decimal LarguraMetros { get; set; }
    [ModelBinder(BinderType = typeof(HtmlDecimalModelBinder))]
    [Range(typeof(decimal), "0", "999.99", ParseLimitsInInvariantCulture = true)] public decimal ComprimentoMetros { get; set; }
    [ModelBinder(BinderType = typeof(HtmlDecimalModelBinder))]
    [Range(typeof(decimal), "0", "9999.99", ParseLimitsInInvariantCulture = true)] public decimal PesoToneladas { get; set; }
    [Range(1, 20)] public int QtdEixos { get; set; }
    [Required, StringLength(50)] public string TipoCarga { get; set; } = "";

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        foreach (var (field, value) in new[] { (nameof(CapacidadeCargaKg), CapacidadeCargaKg),
            (nameof(AlturaMetros), AlturaMetros), (nameof(LarguraMetros), LarguraMetros),
            (nameof(ComprimentoMetros), ComprimentoMetros), (nameof(PesoToneladas), PesoToneladas) })
        {
            if (value == 0 && Id == 0)
                yield return new ValidationResult("Informe um valor maior que zero.", [field]);
            if (decimal.Round(value, 2) != value)
                yield return new ValidationResult("Informe no máximo duas casas decimais.", [field]);
        }
        if (PesoToneladas > 0 && CapacidadeCargaKg > PesoToneladas * 1000)
            yield return new ValidationResult("A capacidade de carga não pode superar o valor de massa informado no cadastro legado.", [nameof(CapacidadeCargaKg)]);
    }
}

public sealed class FleetDriverForm
{
    public int Id { get; set; }
    public string? Selo { get; set; }
    [Required, StringLength(100)] public string Nome { get; set; } = "";
    [Required, StringLength(50, MinimumLength = 3), RegularExpression(@"[a-zA-Z0-9._-]+", ErrorMessage = "Use letras, números, ponto, hífen ou sublinhado no login.")]
    public string Login { get; set; } = "";
    [StringLength(20), RegularExpression(@"[+0-9()\s-]{8,20}", ErrorMessage = "Informe um telefone válido ou deixe o campo vazio.")]
    public string? Telefone { get; set; }
    [DataType(DataType.Password), StringLength(128, MinimumLength = 10, ErrorMessage = "A senha inicial deve ter de 10 a 128 caracteres.")]
    public string? SenhaInicial { get; set; }
}

public sealed record FleetHistoryTrip(int Id, string Codigo, string Status, bool AtribuicaoAtiva);
public sealed record FleetHistoryShift(int EntregaId, DateTime Inicio, DateTime? Fim, bool Ativo);
public sealed class FleetResourceDetailsViewModel
{
    public FleetVehicleForm? Veiculo { get; init; }
    public FleetDriverForm? Motorista { get; init; }
    public int Id => Veiculo?.Id ?? Motorista!.Id;
    public string Nome => Veiculo?.Modelo ?? Motorista!.Nome;
    public string Selo => Veiculo?.Selo ?? Motorista!.Selo!;
    public bool Ativo { get; init; }
    public bool Comprometido { get; init; }
    public int Pagina { get; init; }
    public int Paginas { get; init; }
    public int TotalViagens { get; init; }
    public int TotalTurnos { get; init; }
    public IReadOnlyList<FleetHistoryTrip> Viagens { get; init; } = [];
    public IReadOnlyList<FleetHistoryShift> Turnos { get; init; } = [];
}
