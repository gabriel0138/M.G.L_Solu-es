namespace MglConnect.Web.ViewModels;

public sealed class MotoristaPerfilViewModel
{
    public string Nome { get; init; } = string.Empty;
    public string? Telefone { get; init; }
    public DateTime? DataNascimento { get; init; }
    public string? CpfMascarado { get; init; }
}
