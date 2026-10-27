namespace MglConnect.Web.ViewModels;

public sealed class MotoristaViagemViewModel
{
    public int EntregaId { get; init; }
    public string Status { get; init; } = string.Empty;
    public string CidadeDestino { get; init; } = string.Empty;
    public string EstadoDestino { get; init; } = string.Empty;
    public string? ModeloVeiculo { get; init; }
    public string? PlacaVeiculo { get; init; }
    public bool VeiculoAtribuido { get; init; }
    public bool TurnoAtivo { get; init; }
}
