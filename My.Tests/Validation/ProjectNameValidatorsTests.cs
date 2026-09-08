using My.Shared.Dtos.Project;
using My.Shared.Validation;
using Xunit;

namespace My.Tests.Validation;

public class ProjectNameValidatorsTests
{
    [Fact]
    public void Create_accepts_100_character_name()
    {
        var result = new CreateProjectDtoValidator().Validate(new CreateProjectDto
        {
            Name = new string('a', ProjectFieldRules.NameMaxLength)
        });
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Create_rejects_101_character_name()
    {
        var result = new CreateProjectDtoValidator().Validate(new CreateProjectDto
        {
            Name = new string('a', ProjectFieldRules.NameMaxLength + 1)
        });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage == ProjectFieldRules.NameLengthMessage);
    }

    [Fact]
    public void Update_rejects_name_shorter_than_three()
    {
        var result = new UpdateProjectDtoValidator().Validate(new UpdateProjectDto
        {
            ProjectId = "p1",
            Name = "ab"
        });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage == ProjectFieldRules.NameLengthMessage);
    }
}
