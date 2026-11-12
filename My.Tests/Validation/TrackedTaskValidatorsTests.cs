using My.Shared.Dtos.TrackedTask;
using My.Shared.Rules;
using My.Shared.Validation;
using Xunit;

namespace My.Tests.Validation;

public class TrackedTaskValidatorsTests
{
    private readonly CreateTrackedTaskDtoValidator _create = new();
    private readonly UpdateTrackedTaskDtoValidator _update = new();

    [Fact]
    public async Task Create_allows_a_guid_task_id_for_safe_retries()
    {
        var dto = new CreateTrackedTaskDto
        {
            StartDate = new DateTime(2026, 8, 14, 9, 0, 0),
            Duration = TimeSpan.FromHours(1),
            TaskId = Guid.NewGuid().ToString()
        };

        Assert.True((await _create.ValidateAsync(dto)).IsValid);
    }

    [Theory]
    [InlineData("not-a-guid")]
    [InlineData("1; DROP TABLE TrackedTasks")]
    public async Task Create_rejects_a_task_id_that_is_not_a_guid(string taskId)
    {
        var dto = new CreateTrackedTaskDto
        {
            StartDate = new DateTime(2026, 8, 14, 9, 0, 0),
            Duration = TimeSpan.FromHours(1),
            TaskId = taskId
        };

        var result = await _create.ValidateAsync(dto);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateTrackedTaskDto.TaskId));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("x")]
    [InlineData("Standup")]
    public async Task Create_allows_optional_or_short_details(string? name)
    {
        var dto = new CreateTrackedTaskDto
        {
                    Details = name,
            StartDate = new DateTime(2026, 8, 14, 9, 0, 0),
            Duration = TimeSpan.FromHours(1)
        };

        var result = await _create.ValidateAsync(dto);

        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task Create_rejects_details_over_max()
    {
        var dto = new CreateTrackedTaskDto
        {
                    Details = new string('x', TaskDetailsRules.MaxLength + 1),
            StartDate = new DateTime(2026, 8, 14, 9, 0, 0),
            Duration = TimeSpan.FromHours(1)
        };

        var result = await _create.ValidateAsync(dto);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage == TaskDetailsRules.MaxLengthMessage);
    }

    [Fact]
    public async Task Update_allows_empty_details()
    {
        var dto = new UpdateTrackedTaskDto
        {
            TaskId = "task-1",
                    Details = "",
            StartDate = new DateTime(2026, 8, 14, 9, 0, 0),
            Duration = TimeSpan.FromHours(1)
        };

        var result = await _update.ValidateAsync(dto);

        Assert.True(result.IsValid);
    }
}
