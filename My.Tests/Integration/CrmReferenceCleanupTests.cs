using Microsoft.EntityFrameworkCore;
using My.DAL.Models;
using My.Functions.Helpers;
using My.Shared.Rules;
using Xunit;

namespace My.Tests.Integration;

/// <summary>
/// Organization delete must still succeed when a CRM potential points at that organization
/// and one of its contacts. ContactId is not a foreign key; the cleanup clears it
/// before the contact rows cascade away.
/// </summary>
public class CrmReferenceCleanupTests
{
    [SqlServerFact]
    public async Task Unlink_lets_organization_delete_keep_the_potential()
    {
        await using var db = IntegrationTestConnection.NewContext();

        var stage = new CrmStage
        {
            CrmStageId = Guid.NewGuid().ToString(),
            Name = "Cleanup " + Guid.NewGuid().ToString("N")[..8],
            DefaultProbability = CrmStageRules.Seeds[0].DefaultProbability,
            Outcome = CrmStageRules.Open,
            SortOrder = 1000
        };
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
        var potential = new Potential
        {
            PotentialId = Guid.NewGuid().ToString(),
            Name = "Deal",
            CrmStageId = stage.CrmStageId,
            Probability = stage.DefaultProbability,
            OrganizationId = org.OrganizationId,
            ContactId = contact.ContactId,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        db.CrmStages.Add(stage);
        db.Organizations.Add(org);
        db.Contacts.Add(contact);
        db.Potentials.Add(potential);
        await db.SaveChangesAsync();

        try
        {
            await CrmReferenceCleanup.UnlinkOrganizationAsync(db, org.OrganizationId);
            db.Organizations.Remove(org);
            await db.SaveChangesAsync();

            var kept = await db.Potentials.AsNoTracking()
                .SingleAsync(row => row.PotentialId == potential.PotentialId);
            Assert.Null(kept.OrganizationId);
            Assert.Null(kept.ContactId);
            Assert.False(await db.Organizations.AnyAsync(row => row.OrganizationId == org.OrganizationId));
        }
        finally
        {
            var leftover = await db.Potentials.FirstOrDefaultAsync(row => row.PotentialId == potential.PotentialId);
            if (leftover != null)
                db.Potentials.Remove(leftover);
            var leftoverContact = await db.Contacts.FirstOrDefaultAsync(row => row.ContactId == contact.ContactId);
            if (leftoverContact != null)
                db.Contacts.Remove(leftoverContact);
            var leftoverOrg = await db.Organizations.FirstOrDefaultAsync(row => row.OrganizationId == org.OrganizationId);
            if (leftoverOrg != null)
                db.Organizations.Remove(leftoverOrg);
            var leftoverStage = await db.CrmStages.FirstOrDefaultAsync(row => row.CrmStageId == stage.CrmStageId);
            if (leftoverStage != null)
                db.CrmStages.Remove(leftoverStage);
            await db.SaveChangesAsync();
        }
    }
}
