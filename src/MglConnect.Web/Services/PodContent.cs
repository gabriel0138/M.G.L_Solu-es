using MglConnect.Web.Models;

namespace MglConnect.Web.Services;

public static class PodContent
{
    public static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public static bool Matches(Entrega delivery, string name, string? document, string? signature, string? photo = null) =>
        string.Equals(Normalize(delivery.NomeRecebedor), Normalize(name), StringComparison.Ordinal)
        && string.Equals(Normalize(delivery.DocumentoRecebedor), Normalize(document), StringComparison.Ordinal)
        && string.Equals(Normalize(delivery.AssinaturaDigitalBase64), Normalize(signature), StringComparison.Ordinal)
        && string.Equals(Normalize(delivery.FotoComprovantePath), Normalize(photo), StringComparison.Ordinal);
}
