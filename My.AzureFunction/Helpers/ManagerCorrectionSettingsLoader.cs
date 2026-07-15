using Microsoft.EntityFrameworkCore;
using My.DAL.Data;
using My.Shared.Rules;
using ConstantsClass = My.Shared.Constants.Constants;

namespace My.Functions.Helpers;

internal static class ManagerCorrectionSettingsLoader
{
    public static async Task<ManagerCorrectionSettings> LoadAsync(ApplicationDbContext dbContext)
    {
        var keys = new[]
        {
            ConstantsClass.SettingKeys.TymeAllowManagerTimeCorrection,
            ConstantsClass.SettingKeys.TymeManagerCorrectionMode,
        };

        var rows = await dbContext.AppSettings.AsNoTracking()
            .Where(s => keys.Contains(s.Key))
            .ToDictionaryAsync(s => s.Key, s => s.Value);

        return ManagerCorrectionRules.Parse(rows);
    }
}