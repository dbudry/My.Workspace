using My.Shared.Rules;
using Xunit;

namespace My.Tests.Rules;

/// <summary>
/// Guards the connect/resume HTTP path. ImportChangesAsync must not be called
/// from the method those requests wait on.
/// </summary>
public class GoogleCalendarHttpBudgetTests
{
    [Fact]
    public void Initial_import_is_defined_to_run_on_the_queue() =>
        Assert.True(GoogleCalendarWebhookRules.InitialImportRunsOnQueue);

    [Fact]
    public void Watch_start_does_not_import_inside_the_http_call()
    {
        var source = File.ReadAllText(FindFunctionSource());
        var method = SliceMethod(source, "private async Task<WatchStartOutcome> TryStartWatchAsync");

        Assert.DoesNotContain("ImportChangesAsync", method, StringComparison.Ordinal);
        Assert.Contains("EnqueueInitialImportAsync", method, StringComparison.Ordinal);
    }

    private static string FindFunctionSource()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(
                dir.FullName,
                "My.AzureFunction",
                "Functions",
                "GoogleCalendarFunction.cs");
            if (File.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }

        throw new InvalidOperationException("GoogleCalendarFunction.cs was not found above the test output directory.");
    }

    private static string SliceMethod(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Missing {signature}");
        var next = source.IndexOf("\n        private ", start + signature.Length, StringComparison.Ordinal);
        Assert.True(next > start, "Could not find the end of TryStartWatchAsync.");
        return source[start..next];
    }
}
