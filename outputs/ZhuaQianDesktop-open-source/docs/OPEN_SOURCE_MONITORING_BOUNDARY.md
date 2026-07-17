# Open Source Monitoring Boundary

Updated: 2026-07-17

## Verdict

The current monitoring implementation is acceptable for an open-source preview
only if it is positioned as a visible, local, read-only diagnostic feature.

It should not be described as anti-cheat, spyware detection, a hidden supervisor,
or an automated enforcement system.

Recommended product label:

```text
Activity Monitor
Local Diagnostics
Agent Monitor
Run Observer
```

Recommended Chinese labels:

```text
运行观察台
本地诊断
活动监视
智能体观察
```

Avoid these labels in public docs and UI:

```text
外挂监测
反外挂
监工外挂
隐蔽监控
自动封禁
```

## Current Safe Scope

The current code is within a reasonable open-source boundary because it is:

- visible to the user,
- manually triggered,
- read-only,
- stored locally,
- review-oriented,
- not a background service,
- not a process blocker,
- not a kernel driver,
- not a remote telemetry uploader.

`ProcessSnapshotCollector` should remain an evidence and review helper. It
should not become an enforcement engine.

## Required Public Framing

Use language like:

- "records local process snapshots for user review"
- "shows heuristic review hints"
- "helps diagnose local activity"
- "stores events locally under the user's app data folder"
- "does not hide from the user"
- "does not terminate or suspend processes"
- "does not upload monitoring records unless the user explicitly exports or shares them"

Do not use language like:

- "detects cheats"
- "finds spyware"
- "guaranteed risk score"
- "background supervisor"
- "automatic intervention"
- "endpoint security product"
- "anti-cheat engine"

## Open Source Red Lines

Do not add these features to the open-source preview:

- hidden background monitoring,
- startup persistence for monitoring without explicit opt-in,
- keylogging,
- password, browser, or chat-content collection,
- process injection,
- Windows API hooking for surveillance,
- memory scanning,
- kernel drivers,
- automatic process killing,
- automatic account blocking,
- remote upload of process lists, window titles, or file paths without explicit user action,
- claims of reliable anti-cheat or malware detection.

Any future background loop must be:

- off by default,
- opt-in,
- visibly indicated in the UI,
- permission-gated,
- locally logged,
- easy to pause and disable,
- documented with the exact fields collected.

## Current Risk Points To Fix Before Public Launch

1. Rename UI and docs away from "Monitoring Supervisor", "monitoring supervisor",
   and Chinese wording that implies "supervisor" or "cheat detection".
2. Fix mojibake in `src/ui/MainForm.Monitoring.cs` before shipping screenshots or
   release binaries.
3. Add a clear privacy note near the feature entry point:
   process name, PID, window title, module path when available, memory usage,
   session id, start time, review hint, and local event/case records.
4. Add a "Clear Records" action for local monitoring logs.
5. Keep heuristic matches as "Review Hint", not "Detection".
6. Move or rewrite historical docs that discuss anti-cheat, hidden monitoring,
   kernel-level monitoring, or automated enforcement.

## Implementation Guidance

Keep the feature architecture simple:

```text
User opens panel
  -> user clicks Collect Snapshot
  -> app reads process metadata
  -> app writes local JSONL events/cases
  -> user reviews or clears records
```

Avoid this architecture in the open-source preview:

```text
Hidden service
  -> continuous collection
  -> risk scoring
  -> automatic intervention
  -> remote upload
```

If agent-based review is added later, the agent should summarize local records
and ask for user confirmation before any side effect. It must not independently
block, kill, upload, or hide activity.

## Release Checklist

Before a public release:

- [x] Rename the UI entry to `Activity Monitor` or `Local Diagnostics`.
- [x] Replace public Chinese wording with `运行观察台` or `本地诊断`.
- [x] Fix mojibake in the monitoring UI file.
- [x] Add a privacy note and a clear-records action.
- [ ] Remove or rewrite public docs that frame this as anti-cheat.
- [x] Re-run build and tests after the rename.
- [ ] Verify release package docs do not contain misleading anti-cheat claims.
