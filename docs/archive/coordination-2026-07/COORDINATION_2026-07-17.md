# Coordination Note — 2026-07-17 (Epic E delivery)

This note records file-ownership boundaries so the in-flight refactor process and
this session do not collide.

## In-flight process (Process X) — DO NOT TOUCH from other sessions

Observed mtimes at 23:10 (still active):

- `src/ZhuaQianDesktop.cs`        23:10
- `src/ZhuaQianDesktop.csproj`    23:10
- `src/build.ps1`                 23:10
- `src/ui/MainForm.Export.cs`     22:43
- `outputs/` (staged deletions)   Epic A regen
- `README.md`, `.gitignore`, `docs/_line_budget.json`

Process X owns: the main form, `MainForm.Export`, `csproj`, `build.ps1`,
`README`/`_line_budget`, and `outputs/`.

## This session claims (net-new only; safe to leave until patch applied)

- `src/Plugins/PluginManifest.cs`
- `src/Agent/Hooks/HookKind.cs`
- `src/Agent/Hooks/HookContext.cs`
- `src/Agent/Hooks/IPluginHook.cs`
- `src/Agent/Hooks/HookRegistry.cs`
- `docs/PLUGIN_ECOSYSTEM.md`
- `docs/MCP_RESEARCH_SPIKE.md`
- `docs/patches/EPIC_E_INTEGRATION.md`
- `docs/EXECUTION_BACKLOG.md` (edited — NOT in Process X's owned set)
- `docs/COORDINATION_2026-07-17.md`

## Integration

Epic E modules are NOT yet in the build (no `csproj`/`build.ps1` edits). Apply
`docs/patches/EPIC_E_INTEGRATION.md` **only after Process X's refactor lands and
the build is green**, to avoid a `csproj` merge conflict.

## Hard constraints discovered this session

- Sandbox blocks `csc.exe` -> cannot compile/test here. Build + verify on the real
  machine via `build.ps1` / `run-tests.ps1`.
- `System.Text.Json` is NOT referenced by the project (ConfigStore.cs comments this
  explicitly). Use `System.Web.Script.Serialization.JavaScriptSerializer`.

## Boundary update — 2026-07-17 23:36 (Supervisor code session)

A code session has wired `AgentPlanRunner.RunPlanAsync` into
`src/ui/MainForm.PlanExecution.cs` (replaced the inline per-command loop in
`ExecutePlanDraft` with a runner call; approval/parse/review logic untouched).
This file sits inside Process X's claimed "main form" family even though it was
not explicitly listed. To avoid a merge conflict:

- Process X: please do NOT edit `src/ui/MainForm.PlanExecution.cs` in the
  `ExecutePlanDraft` region — it now depends on `AgentPlanRunner` /
  `AgentPlanExecutionState` (csproj lines 85-86, no duplicate Includes).
- The change is net-additive to behaviour (per-step persisted state) and
  compiles only after `build.ps1` on the real machine (sandbox blocks csc.exe).
