using My.Shared.Dtos.TrackedTask;

namespace My.Shared.Rules
{
    /// <summary>
    /// Store contract for the manuals side of the unified Tasks list.
    /// Any backend (SQL today, Azure Table/Cosmos later) that can count matches and return an
    /// ordered prefix can feed <see cref="TaskListRules.BuildPageFromManualPrefix"/> — the merge
    /// and page math stay storage-agnostic and never require loading a user's full history.
    /// </summary>
    public interface ITaskListManualStore
    {
        /// <summary>
        /// How many manual tracked tasks (not stopwatch sessions) match the user + search.
        /// </summary>
        Task<int> CountAsync(string userId, string? search, CancellationToken cancellationToken = default);

        /// <summary>
        /// First <paramref name="take"/> manuals in the same sort order the task list uses,
        /// with search already applied. Must be enough for
        /// <see cref="TaskListRules.RequiredManualPrefixLength"/> so paging stays correct.
        /// </summary>
        Task<IReadOnlyList<TrackedTaskDto>> GetOrderedPrefixAsync(
            string userId,
            string? search,
            string? sortBy,
            bool sortDescending,
            int take,
            CancellationToken cancellationToken = default);
    }
}
