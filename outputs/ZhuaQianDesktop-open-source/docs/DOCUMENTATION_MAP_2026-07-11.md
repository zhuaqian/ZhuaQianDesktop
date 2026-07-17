# Documentation Map

Updated: 2026-07-16

Use this map to avoid getting misled by older evaluation snapshots.

## Read First

| File | Purpose |
|---|---|
| `README.md` | public entry point, build/test commands, source-tree warning |
| `docs/PROJECT_HANDOFF_2026-07-16.md` | concise current-state handoff for developers and AI agents |
| `docs/PRODUCT_IMPLEMENTATION_DIRECTION_2026-07-16.md` | highest-value product direction and next implementation spine |
| `docs/BUG_REVIEW_2026-07-16.md` | current code bug review, fix suggestions, logic issues, and optimization notes |
| `docs/CURRENT_REALITY_2026-07-11.md` | current factual state of code, tests, source trees, and release readiness |
| `docs/CODE_COMPLETION_ALIGNMENT.md` | feature completion and remaining gaps |
| `docs/ARCHITECTURE_CHARTER.md` | non-negotiable architecture rules for side-effect actions |
| `docs/OPEN_SOURCE_MONITORING_BOUNDARY.md` | open-source boundary for local process/activity monitoring |
| `docs/EXECUTION_BACKLOG.md` | current execution priorities |
| `CONTRIBUTING.md` | contributor workflow |
| `SECURITY.md` | security scope and reporting guidance |

## Reference Docs

These remain useful but may contain older numbers or dates:

- `docs/PRODUCT_REQUIREMENTS.md`
- `docs/PRODUCT_ARCHITECTURE.md`
- `docs/PRE_OPENSOURCE_CHECKLIST.md`
- `docs/OPEN_SOURCE_READINESS_REVIEW_2026-07-12.md`
- `docs/FREE_OPEN_SOURCE_RELEASE_PLAN.md`
- `docs/PROMOTION_PROSPECTS_2026-07-12.md`

## Historical Archive

Files under `docs/archive/` are historical notes. They can explain why a decision was made, but they are not the current truth for:

- test counts,
- source-tree authority,
- whether refactoring is complete,
- whether a feature is production-grade.

## Current Source-Tree Rule

- Edit `src/` first.
- Sync `work/zq-desktop/` only while it remains a transitional mirror.
- Never edit `outputs/` as source; regenerate it for releases.

If an older document calls `work/zq-desktop/` the primary runtime or development baseline, treat that as historical unless a current task explicitly targets the mirror.

## Current Verification Commands

Run these from the repository root:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\src\scripts\run-tests.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\work\zq-desktop\scripts\run-tests.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\work\zq-desktop\scripts\smoke-test.ps1
```

The last verified result on 2026-07-17 was `160` passed / `0` failed for both source trees, and smoke test passed with no production empty catches.
