using Microsoft.EntityFrameworkCore;
using My.DAL.Data;

namespace My.Functions.Helpers;

/// <summary>
/// Clears CRM links before an organization or contact row is removed.
/// Opportunity.OrganizationId is ON DELETE SET NULL. ContactId is not a foreign key,
/// so it has to be cleared here or the deal keeps an id that no longer exists.
/// </summary>
public static class CrmReferenceCleanup
{
    public static async Task UnlinkOrganizationAsync(
        ApplicationDbContext db,
        string organizationId,
        CancellationToken cancellationToken = default)
    {
        var contactIds = await db.Contacts.AsNoTracking()
            .Where(c => c.OrganizationId == organizationId)
            .Select(c => c.ContactId)
            .ToListAsync(cancellationToken);

        if (contactIds.Count > 0)
        {
            await db.Opportunities
                .Where(o => o.ContactId != null && contactIds.Contains(o.ContactId))
                .ExecuteUpdateAsync(
                    s => s.SetProperty(o => o.ContactId, (string?)null),
                    cancellationToken);
        }
    }

    public static async Task UnlinkContactAsync(
        ApplicationDbContext db,
        string contactId,
        CancellationToken cancellationToken = default)
    {
        await db.Opportunities
            .Where(o => o.ContactId == contactId)
            .ExecuteUpdateAsync(
                s => s.SetProperty(o => o.ContactId, (string?)null),
                cancellationToken);
    }
}
