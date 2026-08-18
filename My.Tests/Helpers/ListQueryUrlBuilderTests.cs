using Xunit;
using My.Shared.Constants;
using My.Shared.Dtos.Paging;
using My.Shared.Helpers;

namespace My.Tests.Helpers
{
    public class ListQueryUrlBuilderTests
    {
        [Fact]
        public void Build_includes_groupBy_when_set()
        {
            var query = new ListQueryParameters
            {
                PageNumber = 2,
                PageSize = 25,
                GroupBy = ProjectListGroupBy.Organization,
                Search = "alpha"
            };

            var url = ListQueryUrlBuilder.Build("projects", query);

            Assert.Contains("groupBy=Organization", url);
            Assert.Contains("Search=alpha", url);
            Assert.Contains("PageNumber=2", url);
        }
    }
}