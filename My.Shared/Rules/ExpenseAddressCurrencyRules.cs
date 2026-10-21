namespace My.Shared.Rules;

/// <summary>
/// Maps a confirmed home country to its currency. A blank default currency uses this.
/// An unknown country stays blank. It does not fall back to US dollars.
/// </summary>
public static class ExpenseAddressCurrencyRules
{
    private static readonly Dictionary<string, string> CurrencyByCountry = new(StringComparer.OrdinalIgnoreCase)
    {
        ["United States"] = "USD",
        ["USA"] = "USD",
        ["United States of America"] = "USD",
        ["Canada"] = "CAD",
        ["Mexico"] = "MXN",
        ["United Kingdom"] = "GBP",
        ["UK"] = "GBP",
        ["Great Britain"] = "GBP",
        ["England"] = "GBP",
        ["Scotland"] = "GBP",
        ["Wales"] = "GBP",
        ["Ireland"] = "EUR",
        ["France"] = "EUR",
        ["Germany"] = "EUR",
        ["Spain"] = "EUR",
        ["Italy"] = "EUR",
        ["Netherlands"] = "EUR",
        ["Holland"] = "EUR",
        ["Belgium"] = "EUR",
        ["Austria"] = "EUR",
        ["Portugal"] = "EUR",
        ["Finland"] = "EUR",
        ["Greece"] = "EUR",
        ["Luxembourg"] = "EUR",
        ["Switzerland"] = "CHF",
        ["Sweden"] = "SEK",
        ["Norway"] = "NOK",
        ["Denmark"] = "DKK",
        ["Poland"] = "PLN",
        ["Czech Republic"] = "CZK",
        ["Czechia"] = "CZK",
        ["Hungary"] = "HUF",
        ["Romania"] = "RON",
        ["Australia"] = "AUD",
        ["New Zealand"] = "NZD",
        ["Japan"] = "JPY",
        ["China"] = "CNY",
        ["Hong Kong"] = "HKD",
        ["Singapore"] = "SGD",
        ["India"] = "INR",
        ["South Korea"] = "KRW",
        ["Korea"] = "KRW",
        ["Taiwan"] = "TWD",
        ["Thailand"] = "THB",
        ["Philippines"] = "PHP",
        ["Malaysia"] = "MYR",
        ["Indonesia"] = "IDR",
        ["Vietnam"] = "VND",
        ["United Arab Emirates"] = "AED",
        ["UAE"] = "AED",
        ["Saudi Arabia"] = "SAR",
        ["Israel"] = "ILS",
        ["South Africa"] = "ZAR",
        ["Brazil"] = "BRL",
        ["Argentina"] = "ARS",
        ["Chile"] = "CLP",
        ["Colombia"] = "COP",
        ["Turkey"] = "TRY",
        ["Egypt"] = "EGP"
    };

    public static IReadOnlyList<string> CountryNames { get; } = CurrencyByCountry.Keys
        .Where(name => name.Length > 3)
        .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    public static string? CurrencyForCountry(string? country)
    {
        if (string.IsNullOrWhiteSpace(country))
            return null;
        return CurrencyByCountry.TryGetValue(country.Trim(), out var code) ? code : null;
    }

    public static string? CurrencyForAddress(string? address)
    {
        var (_, _, country) = ExpenseReportRules.SplitHomeAddress(address);
        return CurrencyForCountry(country);
    }
}
