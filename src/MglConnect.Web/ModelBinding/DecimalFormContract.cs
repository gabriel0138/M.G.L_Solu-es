using System.Globalization;

namespace MglConnect.Web.ModelBinding;

/// <summary>Wire format for fleet/trip measures: ASCII digits, optional leading minus,
/// optional dot followed by one or two digits. Units/ranges belong to the field.</summary>
public static class DecimalFormContract
{
    public const int Scale = 2;
    public const string Error = "Use um número com ponto decimal e no máximo duas casas, sem separador de milhares.";

    public static bool HasSupportedScale(decimal value) => decimal.Round(value, Scale) == value;

    public static bool TryParse(string? text, out decimal value)
    {
        value = default;
        if (string.IsNullOrEmpty(text)) return false;
        var start = text[0] == '-' ? 1 : 0;
        var dot = -1;
        for (var i = start; i < text.Length; i++)
        {
            if (text[i] is >= '0' and <= '9') continue;
            if (text[i] != '.' || dot >= 0) return false;
            dot = i;
        }
        var integerEnd = dot < 0 ? text.Length : dot;
        var scale = dot < 0 ? 0 : text.Length - dot - 1;
        if (integerEnd == start || dot >= 0 && scale is < 1 or > Scale) return false;
        if (!decimal.TryParse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture, out var parsed)) return false;

        // Decimal.TryParse may round near its precision limit. Compare the exact
        // fixed-point representation before accepting; never persist that rounding.
        var integer = text.AsSpan(start, integerEnd - start).TrimStart('0');
        var canonical = (start == 1 && parsed != 0 ? "-" : "")
            + (integer.IsEmpty ? "0" : integer.ToString())
            + (dot < 0 ? "" : text[dot..]);
        if (parsed.ToString($"F{scale}", CultureInfo.InvariantCulture) != canonical) return false;
        value = parsed;
        return true;
    }
}
