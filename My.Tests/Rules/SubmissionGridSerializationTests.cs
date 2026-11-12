using System.Text.Json;
using My.Shared.Dtos.TimeSubmission;
using Xunit;

namespace My.Tests.Rules;

/// <summary>
/// The Functions host serializes enums as strings and the Blazor client uses default
/// System.Text.Json options. The grid DTO must survive that round trip.
/// </summary>
public class SubmissionGridSerializationTests
{
    [Fact]
    public void Grid_round_trips_with_default_client_options_from_string_enum_payload()
    {
        const string hostJson = """
            {"months":[],"rows":[{"userId":"u1","userName":"Ann","cells":[{"status":"Submitted"},{"status":"NoData"}]}]}
            """;

        var dto = JsonSerializer.Deserialize<SubmissionGridDto>(
            hostJson, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.NotNull(dto);
        Assert.Equal(SubmissionCellStatus.Submitted, dto!.Rows[0].Cells[0].Status);
        Assert.Equal(SubmissionCellStatus.NoData, dto.Rows[0].Cells[1].Status);
    }

    [Fact]
    public void Cell_status_serializes_as_string()
    {
        var json = JsonSerializer.Serialize(new SubmissionGridCellDto { Status = SubmissionCellStatus.Unsubmitted });
        Assert.Contains("\"Unsubmitted\"", json);
    }
}
