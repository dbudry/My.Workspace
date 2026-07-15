using My.Shared.Rules;
using Xunit;

namespace My.Tests.Rules;

public class ContactTypeRulesTests
{
    [Fact]
    public void ValidateSettingsUpdate_blocks_removing_Primary()
    {
        var oldTypes = ContactTypeRules.DefaultTypes;
        var newTypes = new[] { "Billing", "Maintenance" };

        var error = ContactTypeRules.ValidateSettingsUpdate(oldTypes, newTypes, new Dictionary<string, int>());

        Assert.Contains("Primary", error, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidateSettingsUpdate_blocks_removing_type_in_use()
    {
        var oldTypes = ContactTypeRules.DefaultTypes;
        var newTypes = new[] { ContactTypeRules.Primary, ContactTypeRules.Maintenance };
        var usage = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            [ContactTypeRules.Billing] = 2
        };

        var error = ContactTypeRules.ValidateSettingsUpdate(oldTypes, newTypes, usage);

        Assert.Contains(ContactTypeRules.Billing, error, StringComparison.Ordinal);
        Assert.Contains("2 contacts", error, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidateSettingsUpdate_allows_removing_unused_type()
    {
        var oldTypes = ContactTypeRules.DefaultTypes;
        var newTypes = new[] { ContactTypeRules.Primary, ContactTypeRules.Billing };

        var error = ContactTypeRules.ValidateSettingsUpdate(oldTypes, newTypes, new Dictionary<string, int>());

        Assert.Null(error);
    }

    [Fact]
    public void DefaultForManualEntry_prefers_Primary()
    {
        var allowed = new[] { "Billing", ContactTypeRules.Primary, "Maintenance" };

        Assert.Equal(ContactTypeRules.Primary, ContactTypeRules.DefaultForManualEntry(allowed));
    }
}