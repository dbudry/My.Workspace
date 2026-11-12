using System.Text.Json.Serialization;

namespace My.Shared.Dtos.TimeSubmission;

/// <summary>
/// State of one (employee × month) cell on the submission grid. The converter attribute pins
/// string form on every transport: the Functions host writes enums as strings, the Blazor
/// client's default options read ints and would throw <c>DeserializeUnableToConvertValue</c>.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SubmissionCellStatus
{
    /// <summary>No tracked time in the month, nothing to submit.</summary>
    NoData = 0,

    /// <summary>Month is over, has tracked time, not submitted. Needs follow-up.</summary>
    Unsubmitted = 1,

    /// <summary>Month is submitted.</summary>
    Submitted = 2,

    /// <summary>Current or future month, has tracked time, not submitted yet. Not overdue.</summary>
    Open = 3
}

public class SubmissionGridMonthDto
{
    public int Year { get; set; }
    public int Month { get; set; }
}

public class SubmissionGridCellDto
{
    public SubmissionCellStatus Status { get; set; }
    public DateTime? SubmittedAt { get; set; }

    /// <summary>Id of the submission when <see cref="Status"/> is Submitted. Needed to unsubmit.</summary>
    public string? TimeSubmissionId { get; set; }
}

public class SubmissionGridRowDto
{
    public string UserId { get; set; } = null!;
    public string UserName { get; set; } = null!;

    /// <summary>One cell per entry in <see cref="SubmissionGridDto.Months"/>, same order.</summary>
    public List<SubmissionGridCellDto> Cells { get; set; } = [];
}

/// <summary>
/// Employees × months submission matrix. Returned by GET /api/timesubmissions/grid and also
/// rendered into the submit notification email.
/// </summary>
public class SubmissionGridDto
{
    public List<SubmissionGridMonthDto> Months { get; set; } = [];
    public List<SubmissionGridRowDto> Rows { get; set; } = [];
}

public class TymeSubmitNotifyUserDto
{
    public string UserId { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string Email { get; set; } = null!;
}

/// <summary>
/// Tyme Manager settings for the submit notification email. <see cref="MonthsBefore"/> and
/// <see cref="MonthsAfter"/> are offsets from the month being submitted (default 1 and 1).
/// The grid always lists every active Tyme user.
/// </summary>
public class TymeSubmitNotifySettingsDto
{
    public List<TymeSubmitNotifyUserDto> Recipients { get; set; } = [];
    public int MonthsBefore { get; set; } = 1;
    public int MonthsAfter { get; set; } = 1;
}

public class UpdateTymeSubmitNotifySettingsDto
{
    public List<string> RecipientIds { get; set; } = [];
    public int MonthsBefore { get; set; } = 1;
    public int MonthsAfter { get; set; } = 1;
}

/// <summary>Body of POST /api/timesubmissions/reminder.</summary>
public class SendUnsubmittedReminderDto
{
    public string UserId { get; set; } = null!;

    /// <summary>The month the manager clicked. Coverage is counted from it.</summary>
    public int Year { get; set; }
    public int Month { get; set; }
}

/// <summary>
/// Tyme Manager settings for the reminder email grid. Coverage is "month" (the clicked month),
/// "range" (months before and after the clicked month) or "year" (the clicked month's calendar year).
/// <see cref="Statuses"/> are status keys; unsubmitted is always included.
/// </summary>
public class TymeReminderSettingsDto
{
    public string Coverage { get; set; } = "month";
    public int MonthsBefore { get; set; } = 1;
    public int MonthsAfter { get; set; } = 1;
    public List<string> Statuses { get; set; } = ["unsubmitted"];
}

/// <summary>What the reminder email will say, shown to the manager before sending.</summary>
public class UnsubmittedReminderPreviewDto
{
    public string ToName { get; set; } = "";
    public string ToEmail { get; set; } = "";
    public string Subject { get; set; } = "";

    /// <summary>Encoded HTML, safe to render. Same markup that is emailed.</summary>
    public string Html { get; set; } = "";

    /// <summary>The employee's unsubmitted past months, oldest first.</summary>
    public List<SubmissionGridMonthDto> Months { get; set; } = [];
}
