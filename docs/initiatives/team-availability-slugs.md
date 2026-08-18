# Team Availability

**Branch:** `feature/team-availability-slugs` (name retained — the implementation pivoted away from slugs)
**Status:** Phase 1 shipped 2026-05-19. Phase 2a shipped 2026-05-19.

## Goal

Replace the manual "Team Availability" Google calendar — where employees had to type
their OOO / Vacation entries by hand into a shared calendar — with an automatic
dual-publish out of Tyme.

## What shipped (Phase 1)

- New flag on Project: **Share availability with team** (manager-set on the project
  edit form). When on, every time entry logged against that project is dual-published
  as a sanitized sister event to a workspace-shared Google calendar.
- Team-calendar event format:
  - Title: `[<FullName>] <ProjectName>` — matches the legacy script's format the
    team already recognized. Project name flows through as the manager typed it:
    "Vacation", "Wedding", "Sick", "OOO" — Tyme doesn't dictate the vocabulary.
  - Color: fixed Sage (Google color id `2`).
  - No task name, no description, no slug. Tyme entries stay private "except to the
    manager or admin"; the team calendar gets only an availability echo.
- New AppSetting **TeamAvailabilityCalendarId** (admin-set under
  `/admin/appsettings → Tyme → Team Availability Calendar`). Empty disables the
  feature.
- Writes use each user's existing per-user OAuth token (no service account, no
  admin-owned credentials).
- Linkage stored on `TrackedTask.TeamAvailabilityEventId` so updates and deletes
  flow through cleanly. Flipping the project flag from on→off on an existing entry
  deletes the sister event; off→on creates it on next save.

## All-day entries (built alongside)

A vacation that runs Mon→Fri shouldn't require typing "40 hours". Phase 1 added:

- New `TrackedTask.IsAllDay` flag and "End date (inclusive)" picker in the task
  dialog. Hours/minutes fields collapse when All Day is on.
- New AppSetting **WorkdayHours** (default 8, set under
  `/admin/appsettings → Tyme → All-Day Entries`).
- Derived `Duration = WorkdayHours × inclusive day span`, persisted at save time so
  existing reports and dashboards keep working without learning about the flag.
- Google publishes all-day entries via `EventDateTime.Date` (end-exclusive) on both
  the user's primary calendar and the team-availability calendar.

## Phase 2a — Manager-side: display-name override + dedicated Availability page

Phase 1 worked but had two rough edges: (a) availability projects were buried in
the regular Project Manager next to client work, and (b) the team calendar leaked
the internal project name — a manager who named the project "Q2 Vacation 2026"
for their own bookkeeping saw that string on the public calendar.

Phase 2a adds:

- **`Project.DisplayName string?`** (migration `20260519151721_AddProjectDisplayName`).
  When set, the team-calendar event uses this name instead of `Project.Name`.
  Falls back to `Name` when null. Internal Tyme surfaces (autocomplete, tables,
  reports) still use `Name` everywhere.
- **`/tyme/availability` (`Manager:Tyme+`)** — a dedicated page listing every
  project flagged `IsSharedAvailability=true`. Columns: Internal name, Team
  calendar name, Slug, Group, Status. Add/Edit actions open a focused
  `AvailabilityDialog` that hides the Org/Dept fields and pre-fills the
  Project Group to "Time Off" (auto-creating that group on first use, Sage color
  to match the team-calendar events).
- **Regular ProjectDialog** also got the optional Display Name field — visible
  only when the "Share availability with team" switch is on. Lets a regular
  client project override its team-calendar identity too.
- Nav menu entry under Tyme, gated by `Manager:Tyme+`.

## Forward-only

Flipping `IsSharedAvailability` on a project does **not** retro-publish historical
TrackedTasks. Only entries created or edited after the flag is set hit the team
calendar. A manual "Publish past entries" button is a possible future addition.

## Out of scope (intentionally)

- **Slug grammar.** The original 2026-05-13 design proposed `[ooo]` / `[vac]` slug
  parsing. The 2026-05-15 redesign moved to a project-flag model — a Tyme manager
  flips `IsSharedAvailability` on each availability project, no shared vocabulary
  required. Slug-based detection wasn't built.
- **PTO request workflow, balance tracking, approval flows.** Tyme records what
  *did* happen; an approval system is a separate concern.
- **Per-organization team calendars.** Workspace-wide single calendar in v1. Per-org
  scope can come later.

## Decisions log

**2026-05-21 — Title format: `[<Name>] <ProjectName>` (brackets, no em-dash)**
Matches the format the existing legacy team-availability calendar already used
(brackets around the name, project name after), so the team's eye is already
tuned to scanning bracketed names. Project name is whatever the manager named
it. Tyme doesn't enforce a vocabulary,
so a single em-dash separator is cleaner than parentheses; the team can scan a
column of names with consistent visual structure.

**2026-05-19 — Timed entries respect actual times; All Day spans multiple days**
A half-day vacation should look like a half-day, not a full day. Tyme exposes both
modes: timed with hours/minutes, or All Day with end-date picker. Multi-day spans
are first-class so a week-long vacation is one entry, not five.

**2026-05-19 — Fixed Sage color, not configurable**
Color isn't a per-event signal here — it's the team-calendar's house style.
Configurability adds a setting screen for no real benefit.

**2026-05-19 — Workspace-wide single team calendar**
Per-organization team calendars can come later if needed. For now one calendar id
in AppSettings.

**2026-05-19 — Forward-only**
A manual "publish past entries" button can come later; for now toggling the project
flag affects future entries only. This avoids accidentally fan-publishing a year of
historical vacations when an admin first sets up the calendar id.

**2026-05-19 — WorkdayHours stored workspace-wide**
Per-user workday hours could come later if needed. For now one number drives every
all-day entry's derived duration.

**2026-05-19 (Phase 2a) — DisplayName overrides only the team calendar, not internal Tyme**
A manager naming a project "Q2 Vacation 2026" for their own bookkeeping shouldn't
have to expose that string to the team. DisplayName is a public-facing alias that
applies on shared surfaces only; everywhere employees pick / report on / search the
project, the internal `Name` is what shows.

**2026-05-19 (Phase 2a) — Dedicated page rather than a ProjectManager filter**
The conceptual gap between client projects and PTO categories is wide enough that
a separate page wins over a "show availability only" toggle on Project Manager.
Both dialogs reuse the same `/api/projects` endpoints, so there's no API duplication.

**2026-05-19 (Phase 2a) — Auto-create "Time Off" project group**
Asking a manager to manually set up a group before creating their first vacation
category is unnecessary friction. The Availability Manager creates "Time Off"
(Sage color) on first use so PTO totals cluster on Reports and the dashboard
project-mix automatically.
