namespace My.Shared.Rules;

/// <summary>Fixed CRM pipeline stages for the first slice.</summary>
public static class CrmStageRules
{
    public const string Lead = "Lead";
    public const string Qualified = "Qualified";
    public const string Proposal = "Proposal";
    public const string Negotiation = "Negotiation";
    public const string Won = "Won";
    public const string Lost = "Lost";

    public static readonly string[] All = [Lead, Qualified, Proposal, Negotiation, Won, Lost];

    public static bool TryCanonical(string? value, out string canonical)
    {
        var match = All.FirstOrDefault(stage =>
            string.Equals(stage, value?.Trim(), StringComparison.OrdinalIgnoreCase));
        canonical = match ?? string.Empty;
        return match != null;
    }
}
