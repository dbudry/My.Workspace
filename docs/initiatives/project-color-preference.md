# Project Color Preference

User-controlled choice of how project colors are picked across every Tyme surface, replacing the inconsistent per-page logic that existed before.

**Status:** Shipped 2026-05-18 on `fix/calendar-sync-bounds`.

## Goal / Scope

Before this change each page invented its own color-source rule:

| Surface | Before | After |
|---|---|---|
| ProjectManager | Group color, fall back to org | User preference |
| TaskCalendar | Group color only (no fallback) | User preference |
| TaskStopwatch | No color | User preference |
| Tasks | No color | User preference |
| Reports (donut + rows) | Default MudBlazor palette | User preference |
| Dashboard project mix | Default MudBlazor palette | Axis-native color (Org/Group), default if user opts out |

A single `UserSettings.ProjectColorSource` enum drives the entire app via a shared `My.Shared.Rules.ProjectColorRules.Resolve(...)` helper.

## Picker options

The Settings page exposes four options:

1. **Project group, then organization** *(default — matches ProjectManager's historical behavior)*
2. **Organization only**
3. **Project group only**
4. **None** — hide all project color indicators

## Where it applies

- `ProjectManager` row indicator
- `TaskCalendar` event chip background
- `TaskStopwatch` running-timer row indicator
- `Tasks` row indicator
- `Reports` donut chart + Task Details row indicator
- `Dashboard` "Project mix" doughnut (axis-native; falls back to default palette when user opts out)

## What's intentionally NOT scoped here

- **Per-project color override.** Decided against — global preference only. Reopens if users ask for it later.
- **Management.** Manager-only view; Derek explicitly listed Stopwatch/Tasks/Reports/Dashboard, not this one.
- **Department pivot.** Removed from the dashboard axis toggle — departments don't carry their own color in the schema, so the segment palette always fell back to gray and looked worse than the default rainbow.

## Decisions log

**2026-05-18 — Default = GroupThenOrganization**
Matches ProjectManager's existing fallback so a user who never visits Settings sees no behavioral change on that page. (Derek noted the fallback was a quiet choice originally — surfacing it as the explicit default makes it visible.)

**2026-05-18 — User preference doesn't apply to dashboard segments**
The dashboard pivots by Organization or Group, not by Project. The natural color is the axis-native color (org pivot → org color, group pivot → group color). The user's preference only acts as an on/off switch: `None` disables the custom palette and falls back to MudBlazor's default colors.

**2026-05-18 — `ProjectColorSource` stored as int**
Enum on the DTO, int on the model + DB column. Default value 0 maps to `GroupThenOrganization`. The `ProjectColorRulesTests.Default_enum_value_is_GroupThenOrganization` test guards against silent reorderings.
