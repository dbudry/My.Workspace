using My.Shared.Dtos.Project;
using My.Shared.Dtos.StopwatchItem;
using My.Shared.Dtos.TrackedTask;
using My.Shared.Rules;
using Xunit;

namespace My.Tests.Rules
{
    /// <summary>
    /// TaskListRules is the server-side merge/sort/filter/page for the unified Tasks list — it
    /// replaced the old client "load every page then merge" behavior. These pin the ordering,
    /// filtering, and paging so the page can't silently regress to loading everything.
    /// </summary>
    public class TaskListRulesTests
    {
        private static readonly DateTime Now = new(2026, 7, 1, 12, 0, 0, DateTimeKind.Utc);

        private static StopwatchItemDto Stopwatch(string name, DateTime lastWorked, TimeSpan total, string? project = null) =>
            new()
            {
                StopwatchItemId = "sw-" + name,
                Name = name,
                LastWorkedAt = lastWorked,
                TotalDuration = total,
                Project = project == null ? null : new ProjectDto { Name = project, DisplayName = project }
            };

        private static TrackedTaskDto Manual(string name, DateTime start, TimeSpan duration, string? project = null) =>
            new()
            {
                TaskId = "mt-" + name,
                Name = name,
                StartDate = start,
                Duration = duration,
                Project = project == null ? null : new ProjectDto { Name = project, DisplayName = project }
            };

        [Fact]
        public void Default_sort_is_date_descending_across_both_kinds()
        {
            var stopwatch = new[] { Stopwatch("SW", new DateTime(2026, 6, 15), TimeSpan.FromHours(1)) };
            var manual = new[]
            {
                Manual("Old", new DateTime(2026, 1, 1), TimeSpan.FromHours(2)),
                Manual("Newest", new DateTime(2026, 6, 30), TimeSpan.FromHours(1)),
            };

            var page = TaskListRules.BuildPage(stopwatch, manual, null, null, sortDescending: true, 1, 50, Now);

            Assert.Equal(3, page.TotalCount);
            Assert.Equal("Newest", NameOf(page.Items.ElementAt(0)));
            Assert.Equal("SW", NameOf(page.Items.ElementAt(1)));
            Assert.Equal("Old", NameOf(page.Items.ElementAt(2)));
        }

        [Fact]
        public void Sorts_by_name_ascending()
        {
            var manual = new[]
            {
                Manual("Banana", new DateTime(2026, 6, 1), TimeSpan.FromHours(1)),
                Manual("apple", new DateTime(2026, 6, 2), TimeSpan.FromHours(1)),
                Manual("Cherry", new DateTime(2026, 6, 3), TimeSpan.FromHours(1)),
            };

            var page = TaskListRules.BuildPage(Array.Empty<StopwatchItemDto>(), manual,
                null, TaskListRules.SortName, sortDescending: false, 1, 50, Now);

            Assert.Equal(new[] { "apple", "Banana", "Cherry" }, page.Items.Select(NameOf));
        }

        [Fact]
        public void Sorts_by_duration_descending_including_stopwatch_totals()
        {
            var stopwatch = new[] { Stopwatch("Big", new DateTime(2026, 6, 1), TimeSpan.FromHours(10)) };
            var manual = new[]
            {
                Manual("Small", new DateTime(2026, 6, 2), TimeSpan.FromMinutes(30)),
                Manual("Medium", new DateTime(2026, 6, 3), TimeSpan.FromHours(3)),
            };

            var page = TaskListRules.BuildPage(stopwatch, manual,
                null, TaskListRules.SortDuration, sortDescending: true, 1, 50, Now);

            Assert.Equal(new[] { "Big", "Medium", "Small" }, page.Items.Select(NameOf));
        }

        [Fact]
        public void Search_filters_on_name_or_project_across_both_kinds()
        {
            var stopwatch = new[] { Stopwatch("Standup", new DateTime(2026, 6, 1), TimeSpan.FromHours(1), project: "Acme") };
            var manual = new[]
            {
                Manual("Invoicing", new DateTime(2026, 6, 2), TimeSpan.FromHours(1), project: "Acme"),
                Manual("Research", new DateTime(2026, 6, 3), TimeSpan.FromHours(1), project: "Beta"),
            };

            var byProject = TaskListRules.BuildPage(stopwatch, manual, "acme", null, true, 1, 50, Now);
            Assert.Equal(2, byProject.TotalCount);
            Assert.All(byProject.Items, r => Assert.Contains("Acme", ProjectOf(r)));

            var byName = TaskListRules.BuildPage(stopwatch, manual, "research", null, true, 1, 50, Now);
            Assert.Equal(1, byName.TotalCount);
            Assert.Equal("Research", NameOf(byName.Items.Single()));
        }

        [Fact]
        public void Pages_the_results_with_correct_totals_and_flags()
        {
            var manual = Enumerable.Range(1, 120)
                .Select(i => Manual($"T{i:000}", new DateTime(2026, 1, 1).AddDays(i), TimeSpan.FromHours(1)))
                .ToArray();

            var page2 = TaskListRules.BuildPage(Array.Empty<StopwatchItemDto>(), manual,
                null, TaskListRules.SortName, sortDescending: false, pageNumber: 2, pageSize: 50, Now);

            Assert.Equal(120, page2.TotalCount);
            Assert.Equal(3, page2.TotalPages);
            Assert.Equal(50, page2.Items.Count());
            Assert.True(page2.HasNext);
            Assert.True(page2.HasPrevious);
            Assert.Equal("T051", NameOf(page2.Items.First()));
            Assert.Equal("T100", NameOf(page2.Items.Last()));
        }

        [Fact]
        public void Empty_inputs_return_an_empty_page()
        {
            var page = TaskListRules.BuildPage(
                Array.Empty<StopwatchItemDto>(), Array.Empty<TrackedTaskDto>(), null, null, true, 1, 50, Now);

            Assert.Equal(0, page.TotalCount);
            Assert.Equal(0, page.TotalPages);
            Assert.False(page.HasNext);
            Assert.Empty(page.Items);
        }

        private static string NameOf(Shared.Dtos.TaskList.TaskListRowDto row) =>
            row.IsStopwatch ? row.StopwatchItem!.Name : row.ManualTask!.Name;

        private static string ProjectOf(Shared.Dtos.TaskList.TaskListRowDto row) =>
            row.IsStopwatch ? row.StopwatchItem!.Project?.DisplayName ?? "" : row.ManualTask!.Project?.DisplayName ?? "";
    }
}
