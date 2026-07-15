/*
  Ensures IntranetNavigationMaxDepth exists in AppSettings (idempotent).
  Normally applied by EF migration 20260707145432_AddIntranetNavigationMaxDepth.
*/
IF NOT EXISTS (SELECT 1 FROM dbo.AppSettings WHERE [Key] = N'IntranetNavigationMaxDepth')
BEGIN
    INSERT INTO dbo.AppSettings ([Key], [Value], [Description])
    VALUES (
        N'IntranetNavigationMaxDepth',
        N'10',
        N'Maximum nesting depth for curated intranet sidebar navigation. Top-level menu entries count as depth 1.'
    );
END
SELECT [Key], [Value] FROM dbo.AppSettings WHERE [Key] = N'IntranetNavigationMaxDepth';