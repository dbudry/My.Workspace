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

    [Fact]
    public void CreateProjectDtoValidator_rejects_hours_off_on_a_client_project()
    {
        var validator = new CreateProjectDtoValidator();
        var dto = new CreateProjectDto
        {
            Name = "Client work",
            IsSharedAvailability = false,
            CountsAsTime = false
        };

        var result = validator.Validate(dto);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateProjectDto.CountsAsTime));
    }

    [Fact]
    public void CreateProjectDtoValidator_allows_hours_off_on_availability_project()
    {
        var validator = new CreateProjectDtoValidator();
        var dto = new CreateProjectDto
        {
            Name = "Unavailable",
            IsSharedAvailability = true,
            CountsAsTime = false
        };

        var result = validator.Validate(dto);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void UpdateProjectDtoValidator_rejects_hours_off_on_a_client_project()
    {
        var validator = new UpdateProjectDtoValidator();
        var dto = new UpdateProjectDto
        {
            ProjectId = "p1",
            Name = "Client work",
            IsSharedAvailability = false,
            CountsAsTime = false
        };

        var result = validator.Validate(dto);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateProjectDto.CountsAsTime));
    }
}