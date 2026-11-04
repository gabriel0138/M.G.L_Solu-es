namespace MglConnect.Web.Services;

// Explicit public contract: never pass EF entities or free-form checkpoint text
// to anonymous pages, APIs or broadcasts.
public sealed record PublicTrackingDetails(
    string CodigoRastreio, string StatusEntrega, string DestinatarioNome,
    string? CidadeOrigem, string? EstadoOrigem, string? CidadeDestino, string? EstadoDestino,
    DateTime? DataCriacao, DateTime? DataPrevisaoEntrega, DateTime? DataEntregaRealizada,
    bool InformacoesPublicasReduzidas,
    IReadOnlyList<PublicTrackingEvent> EventosRastreio);

public sealed record PublicTrackingEvent(
    DateTime DataHora, string Titulo, string Descricao, string Localidade,
    string StatusAssociado, string Icone);

public static class PublicTrackingProjection
{
    public static string? NormalizeCode(string? code)
    {
        var value = code?.Trim();
        if (string.IsNullOrEmpty(value) || value.Length > 64
            || value.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-')) return null;
        return value.ToUpperInvariant();
    }

    public static bool IsWeakLegacyCode(string? code)
    {
        var value = NormalizeCode(code);
        return (value is { Length: 8 } && value.All(char.IsAsciiDigit))
            || (value is { Length: 14 }
                && value.StartsWith("MGL-", StringComparison.Ordinal)
                && value.EndsWith("BR", StringComparison.Ordinal)
                && value.Substring(4, 8).All(char.IsAsciiDigit));
    }

    public static string FirstName(string name) =>
        name.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "Destinatário";

    public static string Status(string status) => status switch
    {
        "Pendente" or "Em transporte" or "Entregue" or "Cancelada" => status,
        _ => "Em atualização"
    };

    public static PublicTrackingEvent Event(DateTime date, string status)
    {
        var safeStatus = Status(status);
        var (title, description, icon) = safeStatus switch
        {
            "Entregue" => ("Entrega concluída", "Comprovante disponível na área autenticada.", "bi-patch-check-fill"),
            "Em transporte" => ("Em transporte", "A encomenda está em transporte.", "bi-truck"),
            "Cancelada" => ("Entrega cancelada", "Consulte a área autenticada para mais informações.", "bi-x-circle"),
            "Pendente" => ("Em preparação", "A encomenda está em preparação.", "bi-box-seam"),
            _ => ("Atualização operacional", "Consulte novamente para acompanhar a entrega.", "bi-info-circle")
        };
        return new(date, title, description, "Atualização operacional", safeStatus, icon);
    }
}
