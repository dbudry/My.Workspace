using Xunit;

namespace My.Tests.Helpers
{
    /// <summary>
    /// Mirrors OrganizationListFilters / ProjectListFilters visibility rules.
    /// Archived lifecycle rows keep IsActive=false, so archived visibility must not
    /// depend on the inactive toggle.
    /// </summary>
    public class ListEntityVisibilityTests
    {
        private static bool IsVisible(bool isActive, bool isArchived, bool includeInactive, bool includeArchived) =>
            (!isArchived && (isActive || includeInactive))
            || (isArchived && includeArchived);

        [Fact]
        public void Default_view_shows_active_only()
        {
            Assert.True(IsVisible(isActive: true, isArchived: false, includeInactive: false, includeArchived: false));
            Assert.False(IsVisible(isActive: false, isArchived: false, includeInactive: false, includeArchived: false));
            Assert.False(IsVisible(isActive: false, isArchived: true, includeInactive: false, includeArchived: false));
        }

        [Fact]
        public void View_archived_shows_archived_even_when_inactive()
        {
            Assert.True(IsVisible(isActive: false, isArchived: true, includeInactive: false, includeArchived: true));
            Assert.False(IsVisible(isActive: false, isArchived: false, includeInactive: false, includeArchived: true));
        }

        [Fact]
        public void Show_inactive_shows_inactive_only_not_archived()
        {
            Assert.True(IsVisible(isActive: false, isArchived: false, includeInactive: true, includeArchived: false));
            Assert.False(IsVisible(isActive: false, isArchived: true, includeInactive: true, includeArchived: false));
        }
    }
}