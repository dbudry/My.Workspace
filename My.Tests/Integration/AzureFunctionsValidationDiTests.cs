using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using My.Shared.Dtos;
using My.Shared.Dtos.Contact;
using My.Shared.Dtos.Intranet;
using My.Shared.Dtos.Project;
using My.Shared.Dtos.StopwatchItem;
using My.Shared.Dtos.TrackedTask;
using My.Shared.Validation;
using Xunit;

namespace My.Tests.Integration;

/// <summary>
/// Mirrors Program.cs DI registration to ensure validators resolve in the Functions host.
/// </summary>
public class AzureFunctionsValidationDiTests
{
    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddValidatorsFromAssemblyContaining<CreateStopwatchItemDtoValidator>(ServiceLifetime.Singleton);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void Program_cs_validator_registration_resolves_for_stopwatch_dtos()
    {
        using var provider = BuildProvider();

        var createValidator = provider.GetRequiredService<IValidator<CreateStopwatchItemDto>>();
        var updateValidator = provider.GetRequiredService<IValidator<UpdateStopwatchItemDto>>();

        Assert.IsType<CreateStopwatchItemDtoValidator>(createValidator);
        Assert.IsType<UpdateStopwatchItemDtoValidator>(updateValidator);
    }

    [Fact]
    public void Program_cs_validator_registration_resolves_for_core_domain_dtos()
    {
        using var provider = BuildProvider();

        Assert.IsType<CreateContactDtoValidator>(provider.GetRequiredService<IValidator<CreateContactDto>>());
        Assert.IsType<CreateProjectDtoValidator>(provider.GetRequiredService<IValidator<CreateProjectDto>>());
        Assert.IsType<CreateTrackedTaskDtoValidator>(provider.GetRequiredService<IValidator<CreateTrackedTaskDto>>());
        Assert.IsType<UpdateAppSettingsRequestValidator>(provider.GetRequiredService<IValidator<List<AppSettingDto>>>());
        Assert.IsType<CreateIntranetPageDtoValidator>(provider.GetRequiredService<IValidator<CreateIntranetPageDto>>());
        Assert.IsType<ReorderPagesRequestValidator>(provider.GetRequiredService<ReorderPagesRequestValidator>());
    }
}