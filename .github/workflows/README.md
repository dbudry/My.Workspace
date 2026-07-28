# CI/CD workflows (My.Workspace)

Three separate workflows so each Actions graph only shows jobs that actually run.

**My.Workspace is a generic product** for others to host. There is **no Azure publish**
from this repository. Master creates a **GitHub Release** (`v{Version}`) after a green build.

## How work becomes a release

```
feature branch
    │  open PR → development
    ▼
[PR] Build & Test → (optional auto-merge into development)
    │
    ▼
[Development] Build & Test
    → Check version vs master
    → Create development → master PR   (only if <Version> is ahead of master)
    │
    ▼
[PR → master] Version bump check → Build & Test
    → green checks = MERGE THE PR
    │
    ▼
[Master] Build & Test → Create GitHub Release `vX.Y.Z`
```

## Version gate

Promotion PRs open only when `My.Client/My.Client.csproj` `<Version>` is **strictly greater** than master.

| Situation | What you see |
|-----------|----------------|
| Version not bumped | Development run: **Create PR fails red** with a clear message |
| Version bumped + green build | Opens/keeps `development → master (vX.Y.Z)` |
| Master PR checks green | **You merge** → Master workflow creates release |

## Files

| File | When it runs | Jobs you see |
|------|----------------|--------------|
| `pr.yml` | Any pull request | Build (+ version check on master PRs; auto-merge on development PRs) |
| `development.yml` | Push to `development` | Build → version check → create promotion PR |
| `master.yml` | Push to `master` | Detect → build → **Create release** (no Azure) |
| `_build-and-test.yml` | Called by the others | Shared build/test (not triggered alone) |

## Daily tips

1. Land features via PR → `development`.
2. When ready for a public release: bump `<Version>`, merge to `development`, wait for the promotion PR.
3. On the master PR, green checks mean **merge it** — the Master workflow creates `v{Version}` on GitHub.
4. Consumers of this product deploy with **their own** Azure (or other) hosting; this repo does not deploy for them.
