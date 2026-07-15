using System.Text.Json.Serialization;

namespace My.Shared.Rules;

/// <summary>
/// How a user wants project colors resolved across the app — picker on Settings,
/// applied everywhere project rows or chart segments carry a color indicator
/// (ProjectManager, TaskCalendar, Stopwatch, Tasks, Reports, dashboard chart).
/// Stored as an int on UserSettings so the DB value survives reordering.
///
/// The <see cref="JsonStringEnumConverter"/> attribute pins string-form serialization
/// on every transport: the AzureFunction host registers <c>JsonStringEnumConverter</c>
/// globally for its controllers, but the Blazor client uses default System.Text.Json
/// options which serialize/deserialize enums as ints by default. Without this attribute
/// the two sides disagree and deserializing the settings response throws
/// <c>DeserializeUnableToConvertValue</c>.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ProjectColorSource
{
    /// <summary>Use the project group's color; fall back to the organization's color if no group.
    /// Default — what ProjectManager already did before the picker existed.</summary>
    GroupThenOrganization = 0,

    /// <summary>Always use the organization's color, ignoring the project group.</summary>
    Organization = 1,

    /// <summary>Always use the project group's color; no fallback.</summary>
    ProjectGroup = 2,

    /// <summary>No project color anywhere — for users who find the indicators distracting.</summary>
    None = 3,
}
