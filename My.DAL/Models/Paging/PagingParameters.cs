namespace My.DAL.Models.Paging
{
    public class PagingParameters
    {
        public const int MaxPageSize = 50;
        public const int DefaultPageSize = 50;

        public int PageNumber { get; set; } = 1;

        private int pageSize = DefaultPageSize;

        public string? Search { get; set; }

        public string? SortBy { get; set; }

        public bool SortDescending { get; set; }

        public int PageSize
        {
            get => pageSize;
            set => pageSize = value < 1 ? DefaultPageSize : Math.Min(value, MaxPageSize);
        }
    }
}
