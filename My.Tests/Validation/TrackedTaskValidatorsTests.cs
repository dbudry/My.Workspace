using My.Shared.Dtos.TrackedTask;
using My.Shared.Rules;
using My.Shared.Validation;
using Xunit;

namespace My.Tests.Validation;

public class TrackedTaskValidatorsTests
{
    private readonly CreateTrackedTaskDtoValidator _create = new();
    private readonly UpdateTrackedTaskDtoValidator _update = new();

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
