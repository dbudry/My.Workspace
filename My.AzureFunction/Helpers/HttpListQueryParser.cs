using Microsoft.Azure.Functions.Worker.Http;
using My.DAL.Models.Paging;
using My.Shared.Dtos.Paging;
using My.Shared.Helpers;

namespace My.Functions.Helpers
{
    internal static class HttpListQueryParser
    {
        internal static ListQueryParameters ParseListQuery(HttpRequestData req) =>
            ListQueryUrlBuilder.FromNameValueCollection(req.Query);

        internal static PagingParameters ToPagingParameters(ListQueryParameters query) => new()
        {
            PageNumber = query.PageNumber,
            PageSize = query.EffectivePageSize,
            Search = query.Search,
            SortBy = query.SortBy,
            SortDescending = query.SortDescending
        };
    }
}