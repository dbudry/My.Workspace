using Microsoft.EntityFrameworkCore;
using My.DAL.Models;
using My.Functions.Helpers;
using Xunit;

namespace My.Tests.Integration;

/// <summary>
/// Organization delete must still succeed when a CRM deal points at that organization
/// and one of its contacts. ContactId is not a foreign key; the cleanup clears it
/// before the contact rows cascade away.
/// </summary>
public class CrmReferenceCleanupTests
{
    [SqlServerFact]
    public async Task Unlink_lets_organization_delete_keep_the_opportunity()
    {
        await using var db = IntegrationTestConnection.NewContext();

        var org = new Organization
        {
            OrganizationId = Guid.NewGuid().ToString(),
            Name = "CRM unlink " + Guid.NewGuid().ToString("N")[..8]
        };
        var contact = new Contact
        {
            ContactId = Guid.NewGuid().ToString(),
            Name = "Buyer",
            OrganizationId = org.OrganizationId
        };
        var opportunity = new Opportunity
        {
            OpportunityId = Guid.NewGuid().ToString(),
            Name = "Deal",
            Stage = "Lead",
            OrganizationId = org.OrganizationId,
            ContactId = contact.ContactId,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        db.Organizations.Add(org);
        db.Contacts.Add(contact);
        db.Opportunities.Add(opportunity);
        await db.SaveChangesAsync();

        try
        {
            await CrmReferenceCleanup.UnlinkOrganizationAsync(db, org.OrganizationId);
            db.Organizations.Remove(org);
            await db.SaveChangesAsync();

            var kept = await db.Opportunities.AsNoTracking()
                .SingleAsync(o => o.OpportunityId == opportunity.OpportunityId);
            Assert.Null(kept.OrganizationId);
            Assert.Null(kept.ContactId);
            Assert.False(await db.Organizations.AnyAsync(o => o.OrganizationId == org.OrganizationId));
        }
        finally
        {
            var leftover = await db.Opportunities.FirstOrDefaultAsync(o => o.OpportunityId == opportunity.OpportunityId);
            if (leftover != null)
                db.Opportunities.Remove(leftover);
            var leftoverContact = await db.Contacts.FirstOrDefaultAsync(c => c.ContactId == contact.ContactId);
            if (leftoverContact != null)
                db.Contacts.Remove(leftoverContact);
            var leftoverOrg = await db.Organizations.FirstOrDefaultAsync(o => o.OrganizationId == org.OrganizationId);
            if (leftoverOrg != null)
                db.Organizations.Remove(leftoverOrg);
            await db.SaveChangesAsync();
        }
    }
}
