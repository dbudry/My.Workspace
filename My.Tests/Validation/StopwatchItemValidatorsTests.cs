using My.Shared.Dtos.StopwatchItem;
using My.Shared.Validation;
using Xunit;

namespace My.Tests.Validation;

public class StopwatchItemValidatorsTests
{
    private readonly CreateStopwatchItemDtoValidator _createValidator = new();
    private readonly UpdateStopwatchItemDtoValidator _updateValidator = new();

    [Fact]
    public async Task Create_rejects_missing_project()
    {
        var dto = new CreateStopwatchItemDto { Details = "Valid name", ProjectId = "" };

        var result = await _createValidator.ValidateAsync(dto);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateStopwatchItemDto.ProjectId));
    }

    [Fact]
    public async Task Create_rejects_short_name()
    {
        var dto = new CreateStopwatchItemDto { Details = "A", ProjectId = "proj-1" };

        var result = await _createValidator.ValidateAsync(dto);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateStopwatchItemDto.Details));
    }

    [Fact]
    public async Task Create_accepts_valid_payload()
    {
        var dto = new CreateStopwatchItemDto { Details = "Write docs", ProjectId = "proj-1" };

        var result = await _createValidator.ValidateAsync(dto);

        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task Update_rejects_missing_item_id()
    {
        var dto = new UpdateStopwatchItemDto
        {
            StopwatchItemId = "",
            Details = "Write docs",
            ProjectId = "proj-1"
        };

        var result = await _updateValidator.ValidateAsync(dto);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateStopwatchItemDto.StopwatchItemId));
    }
}