# CI/CD workflows (My.Workspace)

Three separate workflows so each Actions graph only shows jobs that actually run.
No more mega-pipeline with a wall of grey/skipped jobs.

## How work gets to production

```
feature branch
    │  open PR → development
    ▼
[PR] Build & Test → auto-merge into development
    │
    ▼
[Development] Build & Test
    → Check version vs master
    → Create development >> master PR   (only if <Version> is ahead of master)
    │
    ▼
[PR → master] Version bump check → Build & Test
    → graph ends green = MERGE THE PR (that is the next step)
    │
    ▼
[Master] Build & Test → Publish Azure → Create Release
```

## Version gate

Promotion PRs open only when `My.Client/My.Client.csproj` `<Version>` is **strictly greater** than master.

| Situation | What you see |
|-----------|----------------|
| Version not bumped | Development run: **Create PR fails red** with a clear message |
| Version bumped + green build | Opens/keeps `development >> master (vX.Y.Z)` |
| Master PR checks green | **You merge** → production deploy starts |

## Files

| File | When it runs | Jobs you see |
|------|----------------|--------------|
| `pr.yml` | Any pull request | Build (+ version check on master PRs; auto-merge on development PRs) |
| `development.yml` | Push to `development` | Build → version check → create promotion PR |
| `master.yml` | Push to `master` | Detect → build → publish Azure → release |
| `_build-and-test.yml` | Called by the others | Shared build/test (not triggered alone) |

## Daily tips

1. Land features via PR → `development` (auto-merges when green).
2. When ready for production: bump `<Version>`, push to `development`, wait for the promotion PR.
3. On the master PR, green checks mean **merge it** — deploy is not a job on that graph.
4. Watch **Master** workflow after merge for Azure publish + release.
