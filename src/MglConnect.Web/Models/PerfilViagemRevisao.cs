namespace MglConnect.Web.Models;

// Append-only application history; evaluations retain their own independent snapshots.
public sealed class PerfilViagemRevisao : ITenantOwned
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public int EntregaId { get; set; }
    public int VeiculoId { get; set; }
    public int AtorUsuarioId { get; set; }
    public int Revisao { get; set; }
    public DateTime CriadoEmUtc { get; set; }
    public string PerfilJson { get; set; } = "";
    public string CadastroVeiculoJson { get; set; } = "";
}
