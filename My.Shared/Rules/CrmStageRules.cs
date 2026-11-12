namespace My.Shared.Rules;

/// <summary>
/// Stage outcomes and the seven rows inserted by the CRM potentials migration.
/// Names, percents, and order after that live in CrmStages and are not fixed here.
/// </summary>
public static class CrmStageRules
{
    public const string Open = "Open";
    public const string ClosedWon = "Closed Won";
    public const string ClosedLost = "Closed Lost";

    public const int NameMaxLength = 50;
    public const int OutcomeMaxLength = 20;
    public const int ProbabilityMin = 0;
    public const int ProbabilityMax = 100;

    public const string LeadId = "11150000-0000-4000-8000-000000000001";
    public const string QualifiedId = "11150000-0000-4000-8000-000000000002";
    public const string ValuePropositionId = "11150000-0000-4000-8000-000000000003";
    public const string ProposalId = "11150000-0000-4000-8000-000000000004";
    public const string NegotiationId = "11150000-0000-4000-8000-000000000005";
    public const string ClosedWonId = "11150000-0000-4000-8000-000000000006";
    public const string ClosedLostId = "11150000-0000-4000-8000-000000000007";

    public static readonly string[] Outcomes = [Open, ClosedWon, ClosedLost];

    public static readonly CrmStageSeed[] Seeds =
    [
        new(LeadId, "Lead", 5, Open, 0),
        new(QualifiedId, "Qualified", 10, Open, 1),
        new(ValuePropositionId, "Value Proposition", 30, Open, 2),
        new(ProposalId, "Proposal", 50, Open, 3),
        new(NegotiationId, "Negotiation", 70, Open, 4),
        new(ClosedWonId, "Closed Won", 100, ClosedWon, 5),
        new(ClosedLostId, "Closed Lost", 0, ClosedLost, 6)
    ];

    public static bool IsClosed(string? outcome) =>
        string.Equals(outcome, ClosedWon, StringComparison.Ordinal)
        || string.Equals(outcome, ClosedLost, StringComparison.Ordinal);

    public static bool TryCanonicalOutcome(string? value, out string canonical)
    {
        var match = Outcomes.FirstOrDefault(outcome =>
            string.Equals(outcome, value?.Trim(), StringComparison.OrdinalIgnoreCase));
        canonical = match ?? string.Empty;
        return match != null;
    }

    public static decimal ExpectedRevenue(decimal? amount, int probability) =>
        Math.Round((amount ?? 0m) * probability / 100m, 2, MidpointRounding.AwayFromZero);

    public static DateTime? ClosingDate(DateTime? actualClose, DateTime? forecastClose) =>
        actualClose ?? forecastClose;

    /// <summary>
    /// Fills today only when the stage changes into a closed outcome and the date is empty.
    /// A later save that clears the date, or a move back to an open stage, keeps what was sent.
    /// </summary>
    public static DateTime? ResolveActualClose(
        bool stageChanged,
        string? newOutcome,
        DateTime? requested,
        DateTime today)
    {
        if (stageChanged && IsClosed(newOutcome) && requested == null)
            return today.Date;
        return requested?.Date;
    }
}

public sealed record CrmStageSeed(string Id, string Name, int DefaultProbability, string Outcome, int SortOrder);
