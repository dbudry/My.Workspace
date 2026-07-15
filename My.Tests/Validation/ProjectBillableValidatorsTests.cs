using My.Shared.Dtos.Project;
using My.Shared.Validation;
using Xunit;

namespace My.Tests.Validation;

public class ProjectBillableValidatorsTests
{
    [Fact]
    public void CreateProjectDtoValidator_rejects_billable_availability_project()
    {
        var validator = new CreateProjectDtoValidator();
        var dto = new CreateProjectDto
        {
            Name = "Vacation",
            IsSharedAvailability = true,
            IsBillable = true
        };

        var result = validator.Validate(dto);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateProjectDto.IsBillable));
    }

    [Fact]
    public void UpdateProjectDtoValidator_rejects_billable_availability_project()
    {
        var validator = new UpdateProjectDtoValidator();
        var dto = new UpdateProjectDto
        {
            ProjectId = "p1",
            Name = "Vacation",
            IsSharedAvailability = true,
            IsBillable = true
        };

        var result = validator.Validate(dto);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateProjectDto.IsBillable));
    }
}