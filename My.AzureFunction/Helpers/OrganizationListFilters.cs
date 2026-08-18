using System.Linq.Expressions;
using My.DAL.Models;

namespace My.Functions.Helpers
{
    internal static class OrganizationListFilters
    {
        internal static Expression<Func<Organization, bool>> Build(
            bool includeArchived,
            bool includeInactive,
            string? search)
        {
            var term = search?.Trim();
            if (string.IsNullOrEmpty(term))
            {
                return o =>
                    (!o.IsArchived && (o.IsActive || includeInactive))
                    || (o.IsArchived && includeArchived);
            }

            // Search only org scalar fields here — nested department/contact EXISTS
            // subqueries on every row were timing out list endpoints at scale.
            return o =>
                ((!o.IsArchived && (o.IsActive || includeInactive))
                    || (o.IsArchived && includeArchived))
                && (o.Name.Contains(term)
                    || (o.Address != null && o.Address.Contains(term))
                    || (o.City != null && o.City.Contains(term))
                    || (o.State != null && o.State.Contains(term)));
        }

        internal static Func<IQueryable<Organization>, IOrderedQueryable<Organization>> OrderByName(
            bool sortDescending) =>
            sortDescending
                ? q => q.OrderByDescending(o => o.Name)
                : q => q.OrderBy(o => o.Name);
    }
}