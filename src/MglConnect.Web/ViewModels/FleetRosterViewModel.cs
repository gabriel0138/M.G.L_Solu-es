namespace MglConnect.Web.ViewModels;

public sealed record FleetAssignment(int EntregaId, string Codigo, string Status, int? MotoristaId, int? VeiculoId);
public sealed record FleetVehicle(int Id, string Modelo, string Placa, decimal CapacidadeKg, bool Ativo,
    IReadOnlyList<FleetAssignment> Atribuicoes);
public sealed record FleetDriver(int Id, string Nome, bool Ativo, int TurnosAtivos,
    IReadOnlyList<FleetAssignment> Atribuicoes);
public sealed class FleetRosterViewModel
{
    public string Termo { get; init; } = "";
    public string Status { get; init; } = "todos";
    public int TotalVeiculos { get; init; }
    public int TotalMotoristas { get; init; }
    public int PaginaVeiculos { get; init; }
    public int PaginaMotoristas { get; init; }
    public int PaginasVeiculos { get; init; }
    public int PaginasMotoristas { get; init; }
    public int VeiculosAtivos { get; init; }
    public int VeiculosSemAtribuicao { get; init; }
    public int MotoristasAtivos { get; init; }
    public int TotalTurnosAtivos { get; init; }
    public IReadOnlyList<FleetVehicle> Veiculos { get; init; } = [];
    public IReadOnlyList<FleetDriver> Motoristas { get; init; } = [];
}
