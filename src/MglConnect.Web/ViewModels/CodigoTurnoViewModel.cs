namespace MglConnect.Web.ViewModels;

public sealed class CodigoTurnoViewModel
{
    public int EntregaId { get; init; }
    public string Codigo { get; init; } = string.Empty;
    public DateTime ExpiraEmUtc { get; init; }
}
