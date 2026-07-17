# Bug Review And Optimization Notes

Updated: 2026-07-16

Scope: static review of `src/` plus current test run.

Verification run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\src\scripts\run-tests.ps1
```

Result:

```text
Passed: 186  Failed: 0
```

Passing tests do not cover all UI, file-system, provider, timeout, and security edge cases below.

## Fix Status

Implemented in the 2026-07-16/2026-07-17 code pass:

- fixed allowed-directory boundary matching in `PermissionGate`,
- fixed Outputs record mutation so delete/rename no longer rewrites a 100-row dialog page as the whole store,
- added stable legacy output IDs and record-source metadata,
- made folder-organizer rollback manifest names collision-resistant,
- fixed plugin timeout handling to wait after kill and flush async output,
- aligned `AgentPlanParser` open/launch steps with the registered `ComputerControl` executor,
- moved the monitoring panel into `ui/MainForm.Monitoring.cs` to keep `ZhuaQianDesktop.cs` under the architecture budget,
- changed test runner output to a unique temp exe to avoid locked `TestRunner.exe` failures,
- synchronized the same module/test fixes into `work/zq-desktop/`,
- persisted plugin stdout/stderr as `plugin-log` output artifacts,
- added quoted CSV field parsing for Excel export,
- added structured WebSearch diagnostics and wired `WebSearchExecutor` to it,
- added provider fallback notices to user-visible replies,
- made long computer-control wait actions return without blocking the UI thread,
- added `AgentPlanCommandMapper` plus a Plan Review execution path that maps recognized plan steps into guarded `AgentPipeline` commands,
- added regression tests for the above.

Still open from this review:

- no P0/P1 items remain open from this review.
- remaining product hardening is tracked as future work: richer provider health UI, standard test framework migration, and full async command pipeline.

## P0 Bugs / High-Risk Logic

### 1. Allowed-directory permission check can match sibling folders

Status: fixed.

Evidence:

- `src/Core/PermissionGate.cs:184`

Current logic:

```csharp
if (full.StartsWith(fd, StringComparison.OrdinalIgnoreCase)) return true;
```

Problem:

If `AllowedDirectories` contains `C:\Work\Safe`, then a path such as `C:\Work\SafeBackup\file.txt` also starts with `C:\Work\Safe`. That can incorrectly treat a sibling directory as allowed.

Impact:

This weakens the external-directory protection for file reads/writes/moves/plugins.

Fix suggestion:

Normalize allowed directories with a trailing directory separator and compare against either exact match or `allowedRoot + separator`.

Suggested shape:

```csharp
string full = Path.GetFullPath(path).TrimEnd('\\', '/');
string allowed = Path.GetFullPath(d).TrimEnd('\\', '/');
if (string.Equals(full, allowed, StringComparison.OrdinalIgnoreCase) ||
    full.StartsWith(allowed + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
{
    return true;
}
```

Add tests:

- allowed: `C:\tmp\safe\file.txt`
- denied: `C:\tmp\safe-backup\file.txt`
- exact directory path
- mixed slash and case variants

### 2. Outputs dialog delete/rename can rewrite only the first 100 loaded rows and reintroduce legacy rows

Status: fixed.

Evidence:

- `src/ZhuaQianDesktop.cs:2795`
- `src/ZhuaQianDesktop.cs:2841`
- `src/ZhuaQianDesktop.cs:2860`

Current flow:

```csharp
foreach (var row in LoadOutputRows(100))
...
SaveOutputRows(outputRecords);
```

Problem:

The dialog loads at most 100 merged output records, including converted legacy export-history rows, then writes that list back to `outputs.jsonl`.

Risks:

- Primary output rows beyond the first 100 can be dropped after delete/rename.
- Deleting a legacy export-history row does not remove it from `export-history.jsonl`, so it can reappear on refresh.
- Converted legacy rows get transient `outputId` values, so record identity is unstable.

Impact:

User-visible output history can lose records, duplicate records, or resurrect deleted records.

Fix suggestion:

Move all mutation through `OutputsHub` methods:

- `Delete(outputId)` for primary rows.
- `RemoveLegacyExportEntry(path)` for legacy rows.
- `Rename(outputId, displayName)` should not mutate file path unless the file is actually renamed.

Also distinguish primary vs legacy rows in `LoadOutputRows()`:

```json
{
  "recordSource": "outputs|legacy-export-history"
}
```

Add tests:

- deleting a legacy row removes it permanently,
- deleting one row does not truncate 150 existing rows,
- renaming display metadata does not rewrite path unless file rename is intended.

## P1 Bugs / Functional Reliability

### 3. Rollback manifest filenames can collide within the same second

Status: fixed.

Evidence:

- `src/Tools/FolderOrganizer.cs:100`

Current logic:

```csharp
organize-yyyyMMdd-HHmmss.json
```

Problem:

Two organize operations in the same second can use the same manifest path. The later operation can overwrite the earlier rollback manifest.

Impact:

Rollback data can be lost, making file moves harder or impossible to undo.

Fix suggestion:

Use milliseconds plus a short GUID:

```csharp
"organize-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + "-" +
Guid.NewGuid().ToString("N").Substring(0, 8) + ".json"
```

Add test:

- run two `FolderOrganizer.Execute()` calls in rapid succession and assert distinct manifest paths.

### 4. Plugin timeout handling can report the wrong failure and lose buffered output

Status: fixed.

Evidence:

- `src/Tools/PluginRunner.cs:113`
- `src/Tools/PluginRunner.cs:118`

Current flow:

```csharp
if (!proc.WaitForExit(timeoutMs))
{
    proc.Kill();
    result.TimedOut = true;
}
result.ExitCode = proc.ExitCode;
```

Problems:

- After `Kill()`, the process may not have fully exited when `ExitCode` is read.
- There is no second `WaitForExit()` to flush async stdout/stderr handlers.
- Timeout can become a generic exception or return incomplete output.

Impact:

Plugin failures may be misleading, and debugging plugin output becomes unreliable.

Fix suggestion:

After `Kill()`, call `WaitForExit()` in a guarded block and return immediately with `TimedOut = true`. For normal completion, call parameterless `WaitForExit()` after `WaitForExit(timeoutMs)` succeeds to flush async events.

### 5. Agent plan parser emits `OpenTarget`, but registered executor command is `ComputerControl`

Status: fixed.

Evidence:

- `src/Agent/AgentPlan.cs:197`
- `src/Agent/ComputerControlExecutor.cs:23`
- `src/ZhuaQianDesktop.cs:2718`

Problem:

The plan parser maps open/launch actions to:

```csharp
step.CommandType = "OpenTarget";
```

But the actual executor registered in the pipeline exposes:

```csharp
CommandType = "ComputerControl";
```

Impact:

When structured plans begin mapping steps directly into executor commands, open/launch steps will fail with "no executor registered" unless a translation layer exists.

Fix suggestion:

Either:

- change the parser command type to `ComputerControl` and set `parameters["action"] = "open"`, or
- add an `OpenTargetExecutor` that wraps the open behavior.

Add parser-to-executor contract tests for every emitted `CommandType`.

### 6. Plugin execution succeeds without an output record

Status: fixed.

Evidence:

- `src/Agent/PluginRunExecutor.cs:54`
- `src/Agent/AgentPipeline.cs:57-74`

Current behavior:

`PluginRunExecutor` returns text in `CommandResult.Message`, but `ResultPath` is null. `AgentPipeline` only records an output when `result.ResultPath` is non-empty.

Impact:

Successful plugin runs can be audited as actions but not appear in the Outputs workbench. This weakens the task/action/output chain.

Fix suggestion:

For plugin stdout, write a small `.log` or `.txt` artifact under the app output folder and return its path, or extend `OutputsHub` to support inline text outputs.

## P2 Logic Issues / Product Quality

### 7. CSV export parsing does not handle quoted CSV

Status: fixed.

Evidence:

- `src/Documents/OfficeExporter.cs:409`

Current logic:

```csharp
trimmed.Split(',')
```

Problem:

Quoted CSV fields such as `"ACME, Inc.",100` split incorrectly.

Impact:

Generated Excel output can silently corrupt table columns for common business data.

Fix suggestion:

Implement a small RFC4180-style CSV parser or use `TextFieldParser` from `Microsoft.VisualBasic.FileIO` if acceptable for the current .NET Framework target.

### 8. Web search hides provider failures and returns an empty result set

Status: fixed.

Evidence:

- `src/Tools/WebSearchClient.cs:36`
- `src/Tools/WebSearchClient.cs:44`

Problem:

Both search fallbacks swallow exceptions. The caller receives an empty list with no reason: network blocked, scraper changed, DNS failed, or no results all look identical.

Impact:

The UI/model may treat search as "no results" rather than "search failed".

Fix suggestion:

Return a result object with:

- `Success`
- `Results`
- `Provider`
- `ErrorMessage`
- `FallbackUsed`

At minimum, log the exception to audit/debug and show a user-visible warning when search was requested explicitly.

### 9. Provider fallback treats `not found` as retryable

Status: fixed.

Evidence:

- `src/providers/ProviderManager.cs:216`

Current logic:

```csharp
lower.Contains("not found")
```

Problem:

Model-not-found can mean the selected model id is invalid, deprecated, or unavailable to the account. Treating it as retryable helps fallback continue, but it also hides model registry/configuration drift.

Impact:

Users may not realize their selected model is invalid; the app silently switches to a fallback model.

Fix suggestion:

Keep fallback, but surface a warning:

```text
Selected model failed: not found. Fallback used: ...
```

Also mark the model as suspect in settings/test results.

### 10. Computer-control wait blocks the executing thread

Status: fixed for long waits.

Evidence:

- `src/Agent/ComputerControlExecutor.cs:148`

Current logic:

```csharp
Thread.Sleep(ms);
```

Problem:

If invoked on the UI thread through the current synchronous pipeline, a wait action freezes the app for up to 60 seconds.

Impact:

Poor UX and possible "app not responding" during automation flows.

Fix suggestion:

Move command execution to a background task, or make the pipeline async before expanding automation workflows.

## Cross-Cutting Optimization Suggestions

### A. Add contract tests for command/executor registration

Every command emitted by parsers or plan generation should have a registered executor or an explicit adapter.

Test shape:

```text
AgentPlanParser emitted command types
  -> all exist in executor registry
```

### B. Separate output identity from file path

Currently some UI rename behavior rewrites `path`, which is risky. Treat display name/title as metadata and only rename physical files through an explicit file operation with permission, audit, and rollback.

### C. Make OutputsHub the only writer for output history

Avoid direct `SaveOutputRows(outputRecords)` calls from UI. The UI should request operations; `OutputsHub` should handle persistence, legacy cleanup, and record source.

### D. Add filesystem collision tests

Cover:

- rapid repeated exports,
- rapid repeated folder organization,
- screenshot capture within the same second,
- concurrent output record writes.

### E. Add provider diagnostics state

Provider failures should have structured categories:

- missing key,
- quota/rate limit,
- model not found,
- network failure,
- malformed response,
- fallback used.

### F. Continue reducing `src/ZhuaQianDesktop.cs`

The file is over 5,000 lines. The most valuable extraction targets are:

1. Outputs dialog operations,
2. natural-language local action routing,
3. export path and file-generation routing,
4. computer-control orchestration,
5. provider send/fallback UI glue.

## Suggested Fix Order

1. Fix `PermissionGate.IsWithinAllowedDirectories()` path boundary logic.
2. Refactor Outputs dialog mutations through `OutputsHub`.
3. Make rollback manifest names collision-proof.
4. Fix `PluginRunner` timeout/flush behavior.
5. Align `AgentPlanParser` command names with registered executors.
6. Add tests for the five fixes above.
7. Improve CSV parser and web/provider diagnostics.
