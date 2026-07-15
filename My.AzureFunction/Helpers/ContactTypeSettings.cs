using My.DAL.Models;
using My.DAL.Repository;
using My.Shared.Constants;
using My.Shared.Rules;

namespace My.Functions.Helpers
{
    internal static class ContactTypeSettings
    {
        internal static async Task<IReadOnlyList<string>> GetAllowedTypesAsync(IRepository<AppSetting> appSettingRepository)
        {
            var row = await appSettingRepository.GetById(Constants.SettingKeys.ContactTypes);
            return ContactTypeRules.Parse(row?.Value);
        }

        internal static async Task<(bool Ok, string? Error, string? NormalizedType)> ValidateAsync(
            IRepository<AppSetting> appSettingRepository,
            string? contactType,
            bool required)
        {
            var allowed = await GetAllowedTypesAsync(appSettingRepository);

            if (string.IsNullOrWhiteSpace(contactType))
            {
                if (required)
                    return (false, "Contact type is required.", null);

                return (true, null, null);
            }

            if (!ContactTypeRules.IsAllowed(contactType, allowed))
                return (false, $"Contact type must be one of: {string.Join(", ", allowed)}.", null);

            return (true, null, ContactTypeRules.Normalize(contactType, allowed));
        }
    }
}