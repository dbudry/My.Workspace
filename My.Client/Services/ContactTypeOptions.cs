using My.Shared.Constants;
using My.Shared.Rules;

namespace My.Client.Services
{
    public static class ContactTypeOptions
    {
        public static async Task<IReadOnlyList<string>> GetAsync(AppSettingsCache cache)
        {
            var settings = await cache.GetAsync();
            var row = settings.FirstOrDefault(s => s.Key == Constants.SettingKeys.ContactTypes);
            return ContactTypeRules.Parse(row?.Value);
        }

        public static string DefaultForNew(IReadOnlyList<string> allowedTypes) =>
            ContactTypeRules.DefaultForManualEntry(allowedTypes);
    }
}