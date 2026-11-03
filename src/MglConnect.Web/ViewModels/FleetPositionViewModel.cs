namespace MglConnect.Web.ViewModels;

public sealed class FleetPositionViewModel
{
    public int Id { get; init; }
    public string CodigoRastreio { get; init; } = string.Empty;
    public string StatusEntrega { get; init; } = string.Empty;
    public string CidadeDestino { get; init; } = string.Empty;
    public string EstadoDestino { get; init; } = string.Empty;
    public string? Motorista { get; init; }
    public string? Veiculo { get; init; }
    public string? Placa { get; init; }
    public double? Latitude { get; init; }
    public double? Longitude { get; init; }
    public double? PrecisaoMetros { get; init; }
    public double? VelocidadeKmH { get; init; }
    public double? Direcao { get; init; }
    public DateTime? CapturadoEmUtc { get; set; }
    public DateTime? RecebidoEmUtc { get; set; }
}
