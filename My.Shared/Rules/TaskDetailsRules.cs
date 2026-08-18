namespace My.Shared.Rules;

/// <summary>
/// UI/validation for the free-text field stored as <c>Details</c>.
/// Tracked-task Details are optional. Stopwatch work-item Details still use
/// <see cref="MinLength"/> / <see cref="RequiredMessage"/>.
/// </summary>
public static class TaskDetailsRules
{
    public const int MinLength = 2;
    public const int MaxLength = 500;
    /// <summary>Details can grow with Shift+Enter; cap so a form cannot become a page of text.</summary>
    public const int MaxInputLines = 8;

    public const string ListHeader = "Details";
    public const string InputLabel = "What are you working on";
    public const string RequiredMessage = "Details are required.";
    public const string MinLengthMessage = "Details must be at least 2 characters.";
    public const string MaxLengthMessage = "Details cannot exceed 500 characters.";

    /// <summary>Enter (no Shift) accepts the field — submit the form or save the dialog.</summary>
    public static bool IsAcceptKey(string? key, bool shift) =>
        !shift && (key == "Enter" || key == "NumpadEnter");

    /// <summary>Shift+Enter inserts a line break. Plain Enter never does.</summary>
    public static bool IsLineBreakKey(string? key, bool shift) =>
        shift && (key == "Enter" || key == "NumpadEnter");

    public static string TruncateForList(string? text, int maxChars = 80)
    {
        var value = (text ?? string.Empty)
            .Replace("\r\n", " ")
            .Replace('\n', ' ')
            .Replace('\r', ' ')
            .Trim();
        if (value.Length <= maxChars)
            return value;

        return value[..Math.Max(0, maxChars - 1)].TrimEnd() + "…";
    }
}
