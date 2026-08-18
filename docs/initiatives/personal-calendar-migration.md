# Personal Calendar Migration (away from a dedicated Tyme calendar)

**Branch:** `feature/personal-calendar-migration`
**Captured:** 2026-05-13
**Status:** Phase 1 complete 2026-05-14

## Goal

Use **each user's primary Google calendar** as the source/sink for time entries. There is no dedicated `Tyme` sub-calendar — it was a pre-launch design choice that we abandoned before going live.

To avoid pulling in every event from someone's personal calendar, time entries are identified by a **slug** convention in the event title: `[<slug>]`.

## Scope notes

- **In scope:**
  - Read from and write to the user's primary calendar.
  - Slug-based filtering on intake (`[slug]` required; untagged or unknown-slug events are dropped).
- **Out of scope:** Replacing slugs with a different identifier scheme (e.g. metadata-only categories). Slug is the agreed mechanism.

## Rules (as stated)

1. **Only accept** calendar entries that contain a slug in the form `[<slug>]`.
2. **Ignore** calendar events whose slug is unknown — `[<unknown slug>]`.
3. Recurring entries and invites need explicit handling (see open questions).

## Open questions

> These are Derek's questions verbatim — do not answer unilaterally.

1. ~~**Calendar invites with a slug** — what happens?~~ *Answered 2026-05-15: import only when the invitee accepts (or marks tentative). See Decisions log.*
2. **Recurring meeting cancelled by someone else** — what happens to the invitee's Tyme entries?
   - Are all past + future entries removed?
   - Only entries going forward from cancellation date?
   - Are past entries preserved (since the work already happened)?

## Concerns / risks

- **Privacy** — pulling from someone's primary calendar means we see a *lot* of events. We must only persist events that explicitly match a slug, and never persist event details that don't.
- **Slug collisions / typos** — `[oo]` vs `[ooo]` vs `[OoO]`. Decide casing rules and whether to log near-misses for the user to fix.
- **Time-zone handling** — primary calendars are more likely to contain events in multiple timezones; verify the existing TZ work covers this.
- **Bidirectional sync** — current sync is two-way. If users edit a time entry in Tyme, we write back to the calendar event — but only events we created. Never modify externally-invited events.
- **Cancellation semantics** — Google Calendar fires different events for "this occurrence cancelled" vs "this and following" vs "entire series deleted". Each needs explicit handling per Derek's question 2.

## Sub-tasks

- [x] Bind sync to the user's primary calendar.
- [x] Drop inbound import of untagged / unresolved-slug events entirely.
- [x] Update all user-facing strings (settings, dialogs, help, app-settings descriptions).
- [ ] Confirm answers to the two open questions above with Derek.
- [ ] Define slug grammar (case sensitivity, allowed characters, multi-slug events) — *carried over from current impl: `[a-z0-9]+` case-insensitive, first slug wins. Length tightening deferred.*
- [ ] Decide write-back policy (we only modify events we own).
- [ ] Cancellation semantics (Phase 2 — deferred pending Google Calendar event-status investigation).

## Decisions log

**2026-05-21** — **Nightly self-heal timer (`PullMissedEventsNightly`)** added as the
non-manual half of webhook-miss recovery. Schedule: `0 0 7 * * *` (07:00 UTC) — one hour
after `GoogleCalendarWatchRenewalFunction` (06:00 UTC) so a freshly-renewed channel
doesn't get re-scanned pointlessly. Window: rolling `[-7d, +7d]`. Past covers webhook
drops and channel gaps; future catches vacations entered last week but starting next week
so the team calendar pre-populates. Reuses `TryImportEventAsync` directly — same decision
tree as the webhook. Per-user try/catch isolates failures. Why: the manual pull endpoint
puts detection on the human, which means users on vacation (the canonical Team Availability
use case) never click the recovery button. Nightly self-heal makes "no daily admin calls"
actually achievable.

**2026-05-21** — **Hard cap on `ListEventsInRangeAsync` results (5000)**. The Function App's
HTTP timeout is 230s on Consumption plan; with `singleEvents=true` expanding recurring
events, a year-range pull on someone with a weekly recurring `[ooo]` standup blows past
the timeout. New `CalendarRangeTooLargeException` thrown by the service, caught in the
endpoint and surfaced as a friendly DTO error suggesting the user narrow the range. Nightly
timer catches it at WARN and skips that user — they'd need the manual surgical pull anyway.
Cap is conservative: typical ±7-day windows max out around 100-500 events.

**2026-05-21** — **Pull missed events from Google** added as the recovery path for webhook
misses. Endpoint: `POST /api/googlecalendar/pullfromgoogle?from=&to=[&userId=]`. Self-service
by default; global Admin can target another user via `userId`. Mechanism: lists all events
in the date range via `EventsResource.List` (no sync token) and runs each through the same
`TryImportEventAsync` helper the webhook uses — so the behavior is identical, just triggered
on demand instead of by push. Surfaces: Settings → Google Calendar (self) and Admin → Users
(admin-targeted). Why: push channels expire, transient errors swallow webhook deliveries, and
users sometimes type `[vac]` while disconnected — without a manual recovery path the only
fix was disconnect-and-reconnect (which loses linkage). Idempotent: existing TrackedTasks
get updated; nothing duplicates.

**2026-05-21** — `TryImportEventAsync` extracted from `ImportChangesAsync` so the webhook
and the pull endpoint share one decision tree. Returns an `EventImportOutcome` enum so the
pull endpoint can roll up per-outcome counters for the UI; the webhook ignores the return.
Why: parallel reimplementations of the cancellation + slug-resolve + invite-policy + dual-publish
flow would drift over time. One method, two callers.

**2026-05-15** — Calendar invites with a slug → **import only when the invitee accepts (or
marks tentative)**. Implementation: the inbound sync inspects `event.attendees[]` and finds
the attendee row marked `Self=true` (Google's own marker for the calendar owner — more
reliable than email matching when aliases are involved). If that row's `responseStatus` is
not `accepted` or `tentative`, the event is skipped; if a previously-imported entry exists
and the response transitions to declined / needsAction, the entry is removed (unless the
month is submitted). Self-organized events without attendees fall through and import normally.
Why: the slug is the *organizer's* intent, but the invitee is the one whose time would be
tracked. Acceptance is the invitee's clean signal of "yes, I'm committing." Tentative counts
as accepted so people aren't penalized for being honest about uncertainty.
*(Supersedes the 2026-05-14 "auto-add on receive" decision, which was Claude's draft, not a
ratified call.)*

**2026-05-14** — Recurring meeting cancelled by someone else → **preserve past, remove future**.
Why: past entries represent work that actually happened — removing them rewrites history and
distorts billing. Future entries are speculative and should be reclaimed when the meeting is
cancelled. Mirrors the audit-trail sentiment of the submission system.

**2026-05-14** — Untagged events on the primary calendar → **completely ignored**, not even
imported as "unresolved/red". Why: the primary calendar contains private/personal events; we
must never persist them.

**2026-05-14** — Slug grammar carried over from current implementation: `[a-z0-9]+`
(case-insensitive, leading/trailing whitespace inside brackets tolerated). The first slug
in a title wins for multi-slug events. Project slugs are workspace-unique so they route to
exactly one project regardless of which user owns the calendar. Tightening the length to
2–5 chars (to match the project create UI) is deferred — not a blocker for this work.

**2026-05-14** — No migration path for any "old Tyme calendar." Pre-launch, no users have one;
the dedicated-calendar code path is deleted, not deprecated. (Corrected after initial Phase 1
left stale "preserve the old calendar" framing in place.)

**2026-05-15** — Initial sync window bounded to **30 days back / 90 days forward**. After
that, sync token drives incremental updates with no time bound. Why: an unbounded first sync
on a primary calendar can return thousands of recurring-event instances spanning decades
(weekly "Home" reminders out to 2037), and at ~50ms per event for the downstream DB lookup,
the OAuth callback can take minutes — long enough for the SPA to time out, retry the POST,
and burn the single-use OAuth code, surfacing a misleading "invalid_grant" to the user even
though the connection was actually saved. Newly-created events outside the bounded window
still come in via the webhook because Google fires changes with a sync token diff, not a
time range. The retry handler was also tightened the same day to never auto-retry POST/PATCH
on 500 (see `My.Shared/Rules/RetryPolicy.cs`) so a slow callback can't double-fire side
effects regardless of cause.

**2026-05-15** — Outbound event time is sent as **wall-clock + IANA zone**, not UTC offset.
See `My.Shared/Rules/GoogleEventTimeRules.cs`. The previous `BuildEvent` did
`DateTime.SpecifyKind(task.StartDate, DateTimeKind.Utc)` on a wall-clock DateTime that came
out of EF with `Kind=Unspecified`, then handed Google a DateTimeOffset that claimed to be
UTC. Result: every event landed at the user's local hour *minus* the local UTC offset
(a 9 AM Eastern task showed up at 5 AM Eastern). Fix sends `"2026-05-15T09:00:00"` with
`timeZone = "America/New_York"` from `UserSettings.TimeZone`; Google handles DST. Fallback
when TimeZone is null keeps the legacy UTC-stamped offset path so the API call still succeeds.

**2026-05-15** — Disconnect **no longer clears `GoogleEventId`** on TrackedTasks. The earlier
behavior duplicated every event when a user reconnected with the same account: the originals
were still on Google, the DB linkage was gone, and Backfill saw every task as never-synced.
Now disconnect just stops the watch and forgets tokens; same-account reconnect picks back up
with no dupes. Different-account reconnect produces stale event ids, which `TryPushUpdateAsync`
recognizes via 404/410 from Google's Update API and recovers from by clearing the id and
creating a fresh event.

**2026-05-15** — Inbound cancellation of a Tyme-pushed event **propagates to Tyme**. The
import loop in `ImportChangesAsync` used to early-skip any event tagged with our
`source=tyme` extended property to avoid re-importing our own pushes. That swallowed
cancellations too: if the user deleted a Tyme-published event from Google Calendar, we'd
see the webhook, recognize it as ours, and ignore it — leaving the Tyme task orphaned.
Now cancellation handling runs *before* the source-skip; only non-cancellation echoes of
our own writes are skipped. Self-triggered deletes (from `TryPushDeleteAsync`) remain
idempotent because the Tyme task is already gone by the time the webhook arrives.

**2026-05-15** — Outbound event color is **user-configurable**. Two new settings on
`UserSettings`: `TymeEventColorId` (matched/slug-tagged events; null = calendar default)
and `TymeUnmatchedEventColorId` (no-project events; default "11" / Tomato). Valid Google
event color ids are "1".."11" — empty string is rejected by Google's Insert endpoint and
was specifically the value that previously broke backfill, so `GoogleEventColors.NormalizeOrNull`
silently coerces anything outside the valid set to null. Applied at create/update time;
existing events keep their stored color until the user next touches the task. Migration
`AddTymeEventColors` adds the columns; existing rows are backfilled to Tomato for the
unmatched setting so the "needs a project" flag works without a Settings visit.
