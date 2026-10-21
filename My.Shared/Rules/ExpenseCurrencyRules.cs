namespace My.Shared.Rules;

/// <summary>
/// Currency codes, the rate to the company default currency, and the person's expense view choices.
/// The company default is rate 1. Until that choice changes, the default is US dollars.
/// </summary>
public static class ExpenseCurrencyRules
{
    public const string UsdCode = "USD";
    public const string EurCode = "EUR";
    public const int CodeMaxLength = 8;
    public const int NameMaxLength = 40;
    public const decimal MinRate = 0.000001m;
    public const decimal MaxRate = 1000000m;
    public const string DefaultUsdColor = "#1565C0";
    public const string DefaultEurColor = "#F9A825";

    public const string EditorDetail = "Detail";
    public const string EditorForm = "Form";
    public const string LayoutMixed = "Mixed";
    public const string LayoutGrouped = "Grouped";

    public static string NormalizeCode(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
            return UsdCode;
        var trimmed = code.Trim().ToUpperInvariant();
        return trimmed.Length <= CodeMaxLength ? trimmed : trimmed[..CodeMaxLength];
    }

    /// <summary>
    /// A person's default currency. Blank stays blank. It does not become US dollars.
    /// </summary>
    public static string? OptionalCode(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
            return null;
        var trimmed = code.Trim().ToUpperInvariant();
        return IsCodeShape(trimmed) ? trimmed : null;
    }

    public static bool IsUsd(string? code) =>
        string.Equals(NormalizeCode(code), UsdCode, StringComparison.Ordinal);

    /// <summary>The company default currency. A blank home code means US dollars.</summary>
    public static bool IsHome(string? code, string? homeCode)
    {
        var home = string.IsNullOrWhiteSpace(homeCode) ? UsdCode : NormalizeCode(homeCode);
        return string.Equals(NormalizeCode(code), home, StringComparison.Ordinal);
    }

    public static bool IsCodeShape(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
            return false;
        var text = code.Trim();
        if (text.Length is < 3 or > CodeMaxLength)
            return false;
        foreach (var ch in text)
        {
            if (!char.IsLetter(ch))
                return false;
        }

        return true;
    }

    public static string NormalizeView(string? value) =>
        string.Equals(value, EditorForm, StringComparison.OrdinalIgnoreCase) ? EditorForm : EditorDetail;

    public static string NormalizeLayout(string? value) =>
        string.Equals(value, LayoutGrouped, StringComparison.OrdinalIgnoreCase) ? LayoutGrouped : LayoutMixed;

    public static string NormalizeColor(string? color, string fallback)
    {
        if (string.IsNullOrWhiteSpace(color))
            return fallback;
        var text = color.Trim();
        if (!text.StartsWith('#'))
            text = "#" + text;
        if (text.Length != 7)
            return fallback;
        for (var i = 1; i < text.Length; i++)
        {
            if (!Uri.IsHexDigit(text[i]))
                return fallback;
        }

        return text.ToUpperInvariant();
    }

    public static decimal NormalizeRate(string? code, decimal rate) =>
        NormalizeRate(code, rate, null);

    public static decimal NormalizeRate(string? code, decimal rate, string? homeCode)
    {
        if (IsHome(code, homeCode))
            return 1m;
        if (rate < MinRate)
            rate = MinRate;
        if (rate > MaxRate)
            rate = MaxRate;
        return decimal.Round(rate, 6, MidpointRounding.AwayFromZero);
    }

    public static decimal ToUsd(decimal spent, decimal rate)
    {
        if (spent < 0m)
            spent = 0m;
        if (rate <= 0m)
            rate = 1m;
        return decimal.Round(spent * rate, 2, MidpointRounding.AwayFromZero);
    }

    /// <summary>Dark ink on a light swatch, white ink on a dark one.</summary>
    public static string Ink(string? backgroundHex)
    {
        var hex = NormalizeColor(backgroundHex, DefaultUsdColor);
        var r = Convert.ToInt32(hex.Substring(1, 2), 16);
        var g = Convert.ToInt32(hex.Substring(3, 2), 16);
        var b = Convert.ToInt32(hex.Substring(5, 2), 16);
        var luminance = ((0.2126 * r) + (0.7152 * g) + (0.0722 * b)) / 255d;
        return luminance > 0.6 ? "#1A1A1A" : "#FFFFFF";
    }
}
