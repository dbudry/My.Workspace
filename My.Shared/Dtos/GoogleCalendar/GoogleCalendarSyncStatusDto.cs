namespace My.Shared.Dtos.GoogleCalendar;

public class GoogleCalendarSyncStatusDto
{
    public bool StorageConfigured { get; set; }
    public bool WebhookUrlReady { get; set; }
    public string WebhookUrlSource { get; set; } = "";
    public List<GoogleCalendarConnectedUserDto> Users { get; set; } = new();
}

public class GoogleCalendarConnectedUserDto
{
    public string UserId { get; set; } = "";
    public string Email { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public bool ImportEnabled { get; set; }
    public bool PublishEnabled { get; set; }
    public DateTime? WatchExpiresAtUtc { get; set; }
    public string Status { get; set; } = "";
}

public class GoogleCalendarQueueProbeDto
{
    public bool Success { get; set; }
    public string Message { get; set; } = "";
}

public class GoogleCalendarWatchRenewalResultDto
{
    public bool Success { get; set; }
    public string Message { get; set; } = "";
    public int Attempted { get; set; }
    public int Renewed { get; set; }
    public int Failed { get; set; }
}
