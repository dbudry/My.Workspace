using Xunit;
using My.Shared.Helpers;

namespace My.Tests.Helpers
{
    public class ProjectDeleteGuardTests
    {
        [Fact]
        public void Evaluate_allows_delete_when_no_history()
        {
            var impact = ProjectDeleteGuard.Evaluate(0, 0, 0, 0);

            Assert.True(impact.CanDelete);
            Assert.Null(impact.BlockReason);
        }

        [Fact]
        public void Evaluate_blocks_when_tasks_exist()
        {
            var impact = ProjectDeleteGuard.Evaluate(3, 1, 1, 0);

            Assert.False(impact.CanDelete);
            Assert.Contains("3 logged time entries", impact.BlockReason);
            Assert.Contains("personal Google Calendar", impact.BlockReason);
            Assert.Contains("Team Availability calendar", impact.BlockReason);
            Assert.Contains("Archive or set inactive", impact.BlockReason);
        }

        [Fact]
        public void Evaluate_blocks_when_manager_aliases_exist()
        {
            var impact = ProjectDeleteGuard.Evaluate(0, 0, 0, 2);

            Assert.False(impact.CanDelete);
            Assert.Contains("2 manager overrides", impact.BlockReason);
        }
    }
}