-- Optional cleanup: drop Tyme rows imported from Google that start more than 90 days out.
-- Does not run automatically. Review, then execute if far-future recurring [slug] imports
-- filled Submit. Skips stopwatch sessions and submitted months.
-- TrackedTasks.Details is the free-text field (not Name).

DECLARE @cutoff datetime2 = DATEADD(day, 90, SYSUTCDATETIME());

SELECT t.TaskId, t.UserId, t.StartDate, t.Details, t.GoogleEventId, p.Slug
FROM dbo.TrackedTasks t
LEFT JOIN dbo.Projects p ON p.ProjectId = t.ProjectId
WHERE t.GoogleEventId IS NOT NULL
  AND t.StopwatchItemId IS NULL
  AND t.StartDate > @cutoff
  AND NOT EXISTS (
      SELECT 1 FROM dbo.TimeSubmissions s
      WHERE s.UserId = t.UserId
        AND s.Year = YEAR(t.StartDate)
        AND s.Month = MONTH(t.StartDate)
  );

-- DELETE t
-- FROM dbo.TrackedTasks t
-- WHERE t.GoogleEventId IS NOT NULL
--   AND t.StopwatchItemId IS NULL
--   AND t.StartDate > @cutoff
--   AND NOT EXISTS (
--       SELECT 1 FROM dbo.TimeSubmissions s
--       WHERE s.UserId = t.UserId
--         AND s.Year = YEAR(t.StartDate)
--         AND s.Month = MONTH(t.StartDate)
--   );
