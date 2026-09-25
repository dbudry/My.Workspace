namespace My.Shared.Rules;

/// <summary>Fixed activity kinds on a CRM opportunity.</summary>
public static class CrmActivityTypeRules
{
    public const string Note = "Note";
    public const string Call = "Call";
    public const string Meeting = "Meeting";
    public const string Task = "Task";

    public static readonly string[] All = [Note, Call, Meeting, Task];

    public static bool TryCanonical(string? value, out string canonical)
    {
        var match = All.FirstOrDefault(kind =>
            string.Equals(kind, value?.Trim(), StringComparison.OrdinalIgnoreCase));
        canonical = match ?? string.Empty;
        return match != null;
    }
}
