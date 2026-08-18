using My.DAL.Models;

namespace My.Functions.Helpers;

/// <summary>
/// Legacy/imported AspNetUsers rows may have null SecurityStamp or ConcurrencyStamp.
/// Identity UserManager.UpdateAsync throws InvalidOperationException when SecurityStamp is null.
/// </summary>
internal static class IdentityUserHealing
{
    public static void EnsureStamps(ApplicationUser user)
    {
        if (string.IsNullOrWhiteSpace(user.SecurityStamp))
            user.SecurityStamp = Guid.NewGuid().ToString();

        if (string.IsNullOrWhiteSpace(user.ConcurrencyStamp))
            user.ConcurrencyStamp = Guid.NewGuid().ToString();
    }
}