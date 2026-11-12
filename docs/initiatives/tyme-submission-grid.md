# Tyme submission grid and submit email

**Branch:** `feature/#1121_tyme-submit-grid`
**Status:** Built, awaiting review.

## Goal

Jim wants to see who has submitted time without opening each person. Two parts:

1. A **Grid** view next to My and Team on Tyme > Submit. Employees down, months across.
   Default is last month, this month and next month, for all employees. The user can change
   the months and pick employees. Same access as Team (Manager:Tyme and Admin:Tyme).
2. When someone submits, email an HTML copy of the grid to people a Tyme Manager lists.
   The subject names the submitter. The submitter's row is highlighted.

Also fixed: the Employee dropdown on Team showed the employee id after selecting. It is now a
checkbox list that shows names.

## Decisions

- Cell states: **Submitted** (green), **Not submitted** (orange, month is over), **In progress**
  (blue, current or future month with time, not due yet) and **No time** (grey).
- **Status** is a checkbox list on both Grid and Team (null = all, empty = none). It filters per cell:
  Grid blanks cells whose status is not ticked and hides employees with no visible cell. Team sends the
  ticked keys as `?status=a,b`.
- Team also has **In progress** and **No time** rows: In progress is the current or a future month with time, No time is a
  finished month with nothing tracked. Both come from the Grid service, only exist when a range is sent
  (so the dashboard widget never gets them), cover at most the latest 12 months of the range, and have no
  Submit or Unsubmit action. "All" includes them.
- Settings live on a new **Tyme > Settings** page (Manager:Tyme). The App Settings Tyme tab is
  global-Admin only, so a Tyme Manager could not reach it.
- Settings use existing `AppSettings` rows (no migration):
  `TymeSubmitNotifyUserIds`, `TymeSubmitNotifyMonthsBefore`, `TymeSubmitNotifyMonthsAfter`.
  The email grid always lists every active Tyme user (no employee picker). Recipients are
  picked with a checkbox list of Tyme Editors and Managers. Both live in one "Submission email" card.
- No recipients means no email. There is no fallback to the submitter.
- The email also goes out when a manager submits on behalf of an employee. The named submitter
  is the employee whose time was submitted.
- An email failure never fails the submission. The API returns `EmailWarning` and the Submit page
  shows it as a snackbar.
- Out of scope: right-click unsubmit, any change to how time is stored or rounded.

## What was built

- `My.Shared/Rules/SubmissionGridRules.cs` — month window, range check (max 12 months), cell status.
- `My.Shared/Rules/SubmissionGridEmailBuilder.cs` — subject, inline-styled HTML, plain text.
- `My.Shared/Rules/MultiSelectRules.cs` — checkbox list selection logic (null means all, empty means none; clearing All clears every box).
- `GET /api/timesubmissions/grid?from=yyyy-MM&to=yyyy-MM&userIds=a,b` (Manager:Tyme).
- `GET|PUT /api/timesubmissions/notify-settings`, `GET /api/timesubmissions/notify-directory` (Manager:Tyme).
- `GET /api/timesubmissions/team?userId=` now accepts a comma-separated list.
- `My.AzureFunction/Services/SubmissionGridService.cs` — one query path for the Grid view and the email.
- `My.AzureFunction/Services/TymeSubmitNotifier.cs` and `TymeSubmitMailer.cs` — settings and ACS HTML send.
- `My.Client/Components/Common/MultiSelectCheckList.razor` — reusable checkbox dropdown.
- `My.Client/Components/TimeSubmissions/SubmissionGridView.razor` — the Grid view. Cells are click-to-act:
  Unsubmit on Submitted, Submit for employee on Not submitted (when the app setting allows). No visible controls.
- `My.Client/Services/SubmissionActionsService.cs` — unsubmit and submit-on-behalf flow shared by Team and Grid.
- `My.Client/Pages/Tyme/TymeSettings.razor` — recipients and month window.

## Unsubmitted-time reminder email

A Manager can email an employee about unsubmitted past months. Switch: `TymeUnsubmittedReminderEnabled`
(Admin → App Settings → Tyme, default off).

- Grid: **Send reminder** in the menu of a Not submitted cell only. Menu items are alphabetical
  (Send reminder, Submit for employee, Unsubmit).
- Tyme → Settings → Reminder Email Grid: **Date coverage** (By month clicked, By month range with months
  before/after, By year) and **Time status to send**. By month clicked allows only Not submitted, so the
  other boxes clear. Not submitted can never be unticked. The clicked month is where coverage starts.
- Team: **Remind** button on Not submitted rows.
- A dialog shows the email as it will be sent, with Send and Cancel.
- Email: "<Name>, You may have forgotten to submit your time using Tyme (link to
  `<domain>/tyme/submit`) for the month(s) ...", a one-row grid of the unsubmitted months in the same
  colors, "Thanks, <manager>", and the app title. No logo image is included.
- Months: the text lists only the Not submitted months in the coverage, oldest first. The grid shows every
  month in the coverage whose status is ticked.
- Code: `UnsubmittedReminderRules`, `UnsubmittedReminderEmailBuilder`, `UnsubmittedReminderService`,
  `GET|POST /api/timesubmissions/reminder`, `UnsubmittedReminderDialog`, `SubmissionActionsService.SendReminderAsync`.

## Open questions

- Should the Expenses submission email picker be moved onto a shared person picker later?
  It is still inline in `ExpenseSettings.razor`.
