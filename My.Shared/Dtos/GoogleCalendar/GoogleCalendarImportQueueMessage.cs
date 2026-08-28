using System.Text.Json.Serialization;

namespace My.Shared.Dtos.GoogleCalendar;

/// <summary>
/// Payload the Google Calendar webhook enqueues. No SQL lookup on the HTTP
/// path — the queue trigger maps <see cref="ChannelId"/> to the user.
/// </summary>
public sealed class GoogleCalendarImportQueueMessage
{
    [JsonPropertyName("channelId")]
    public string ChannelId { get; set; } = "";

    [JsonPropertyName("channelToken")]
    public string? ChannelToken { get; set; }

    [JsonPropertyName("resourceState")]
    public string? ResourceState { get; set; }
}
