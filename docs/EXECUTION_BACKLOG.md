# Execution Backlog

Updated: 2026-07-18

## Execution Principles

1. Converge facts before adding features.
2. Keep side-effect actions behind Command -> PermissionGate -> Executor -> Audit/Output records.
3. Prefer natural-language task entry over adding more buttons.
4. Update existing core docs instead of adding another loose evaluation document.
5. Treat `src/` as the only development source; regenerate `outputs/` and `dist/` from it.

## Done Recently

- Production empty catches removed or diagnosed.
- File export path moved through `AgentPipeline -> ExportFileExecutor`.
- Natural-language file generation creates real local files.
- Natural-language local actions added for opening targets, organizing folders, running plugins, and ending PID-based processes.
- Folder organization now uses `OrganizeFolderExecutor` through the pipeline.
- Plugin and process execution paths are shared between UI entry points and chat entry points.
- Agent executor registration is centralized in `AgentPipelineFactory`; the main form no longer hand-registers each executor at every side-effect call site.
- `work/zq-desktop` has been retired; `src/` is the single build/test source.
- Current verification: `src/scripts/run-tests.ps1` is `219` passed / `0` failed, architecture/package checks pass; root build compiles successfully.
- URL analysis workflow added: pasted URLs can be fetched locally, turned into a source-grounded Markdown analysis report, and saved as a real file when the prompt asks for report/export output.
- Release trust pipeline added: external review snapshots use `scripts/export-review-snapshot.ps1` (`git archive HEAD` + SHA-256), and `CodingAgentSession` build/test execution now goes through `GuardedCommandRunRecorder`.
- Installer delivered: `installer/Install.ps1` + `Uninstall.ps1` + `Build-Bundle.ps1` stage the CI-built `dist/ZhuaQianDesktop.exe` to Program Files with SHA-256 verification, shortcuts, and an uninstall registry entry. README "Install" section added; Installer moved out of "Not implemented yet".
- Epic A5 effectively done: local Git repo is a clean linear 9-commit history, `.github/workflows/tests.yml` provides CI build+test, README git claim corrected.
- Coding loop delivered: 6 modules in `src/Agent/Coding/` (`ProjectAnalyzer`, `CodePatcher`, `ErrorParser`, `BuildFixLoop`, `GitWorkflow`, `CodingLoopSession`) close the "read project -> fix code -> re-run -> diff/commit" gap vs Codex/Claude Code. Analyze -> Plan -> Execute -> Review -> Done state machine, all side-effects through PermissionGate. 6 test files with `RunAll()` registered in TestRunner. Design doc: `docs/CODING_LOOP_DESIGN.md`.

## Highest Priority

### Competitive Alignment Snapshot

| Reference | What they are strong at | Current ZhuaQian gap | Backlog target |
|---|---|---|---|
| Codex-style coding agent | repo-aware task loop, terminal execution, diffs, tests, review handoff | ZhuaQian has tasks and executors, but no structured plan/diff/test loop | Epic C + Epic D |
| Claude Code-style agent | explicit plan/approval modes, tool permissioning, hooks/MCP-style extensibility | permissions exist, but plan text is not yet a schema and hooks/MCP are absent | Epic C + Epic E |
| WorkBuddy-style desktop office assistant | natural-language office work, document generation, desktop-context workflows | ZhuaQian now exports office files and searches web, but workflow templates and review UX are thin | Epic F |
| OpenCode-style coding agent | LSP code intelligence, subagent orchestration, real MCP client, Plan/Build mode separation, 75+ providers, AGENTS.md, session compaction, multi-session, undo/redo for AI edits | ZhuaQian leads on office generation, browser-render web research, plugin ecosystem, LAN share, self-healing loop; but missing LSP, subagents, MCP client, Plan/Build split, AGENTS.md, session compaction | P0: MCP + LSP + subagents + Plan/Build (see `docs/GAP_VS_OPENCODE.md`) |

### Epic A: Source-Tree Convergence

Goal: make the public contribution path unambiguous.

Tasks:

- A1. Declare `src/` as the public contribution source in README and CONTRIBUTING.
- A2. Retire `work/zq-desktop/` and stop mirror synchronization. **DONE**
- A3. Public release omits `work/`; `src/` is the only contribution source. **DONE**
- A4. Regenerate `outputs/` from source only.
- A5. Create a clean Git repo and verify CI.

Acceptance:

- New contributor knows where to edit.
- `outputs/` is not treated as source.
- Clean repo verification passes.

### Epic B: Main Form Reduction

Goal: shrink `ZhuaQianDesktop.cs` by moving orchestration into modules.

Tasks:

- B1. Extract natural-language local action parsing into a `Tools` class.
- B2. Extract file-generation routing into an action/service class.
- B3. Extract task state persistence and chat message persistence.
- B4. Extract provider call orchestration from UI code.
- B1-WebSearchClientExecutor. Migrate URL/page search usage from direct `new Tools.WebSearchClient(...)` wiring into `Agent/WebSearchExecutor` as the primary command path.
- B1-SystemDiagnosticsExecutor. Wrap diagnostics collection in an executor with explicit permission, redaction, and cloud-upload confirmation.
- B1-UndoRedoPipeline. Move undo/rollback orchestration behind command records instead of main-form owned tool state.
- B1-CommandParserExtraction. Keep command parsing outside the UI layer and remove direct construction from `ZhuaQianDesktop.cs`.

Acceptance:

- Main form is mostly UI wiring and display updates.
- New tests cover extracted classes.
- No feature behavior changes without tests.

### Epic C: Agent Loop

Goal: turn chat-plus-tools into a clear task loop.

Tasks:

- C1. Define a small plan schema. `Agent/AgentPlan.cs` added as the first foundation.
- C2. Map plan steps to existing executor commands.
- C3. Link ActionRecord and OutputRecord to task review state.
- C4. Add review status after execution.
- C5. Add an approval surface that can show parsed steps, permissions, target paths, and rollback hints.

Acceptance:

- At least one workflow runs as Plan -> Approval -> Execute -> Output -> Review.

### Epic D: Coding-Agent Parity

Goal: close the Codex/Claude Code gap for repo tasks.

Tasks:

- D1. Add a workspace scan summary: files changed, build commands, test commands, and risk notes. **IMPLEMENTED + INTEGRATED** — `src/Agent/WorkspaceScanSummary.cs` (`Capture` via read-only `git status --porcelain` + pure `CollectRiskNotes` for line-budget/`new Tools.` anti-pattern risk). Registered in csproj; `build.ps1`/`run-tests.ps1` are dynamic (auto-include).
- D2. Add command-run records with stdout/stderr summary and exit code. **IMPLEMENTED + INTEGRATED** — `src/Agent/CommandRunRecorder.cs` (`ICommandRecorder` + deadlock-free async stream capture) populating `AgentPlanStepResult` (from `AgentPlanState.cs`). Registered in csproj; build scripts dynamic.
- D-orchestrator. **IMPLEMENTED + INTEGRATED** — `src/Agent/CodingAgentSession.cs` composes plan + scan + command-run into one `Plan -> Command -> Diff -> Test -> Review` markdown report (Epic D acceptance narrative). Registered in csproj; build scripts dynamic.
- D3. Add a diff/review panel for generated or edited files. **DONE (UI)** — `PlanReviewDialog` (plan parse + approval grid + review text, wired to "Agent Planner" button) plus `CodingAgentReportDialog` + `MainForm.CodingAgentReview` ("Full Review" button runs the full build+test review). A read-only post-execution review is also appended to chat by `ExecutePlanDraft` (`Recorder=null`).
- D4. Add "run tests after change" workflow with visible pass/fail state. **DONE** — read-only review after execution (`Recorder=null`) surfaces build/test pass/fail in chat; the "Full Review" action runs the real build+test and shows pass/fail in `CodingAgentReportDialog`.

Acceptance:

- A code task can show Plan -> Command -> Diff -> Test -> Review without leaving the app. **DONE**: `CodingAgentSession` produces exactly this narrative; it is surfaced via `PlanReviewDialog` (plan review), the post-execution chat review (`ExecutePlanDraft`), and `CodingAgentReportDialog` ("Full Review" with real build+test).

Coordination note: D modules are net-new files (zero conflict with the in-flight Epic B refactor). `AgentPlanState.cs` (per-step state engine) was added by the P-C supervisor session and is also an orphaned file pending csproj registration. Apply `docs/patches/EPIC_D_INTEGRATION.md` (csproj + run-tests.ps1 + TestRunner) only after the concurrent builder merges.

### Epic E: Tool Ecosystem

Goal: make plugins safer and more reusable.

Tasks:

- E1. Define a simple plugin manifest contract. **IMPLEMENTED (modules written)** — `src/Plugins/PluginManifest.cs` (`PluginManifest` + `PluginManifestParser` with validation, path-traversal guard, `ToJson` round-trip). JSON via `System.Web.Script.Serialization` (not `System.Text.Json`, which is unreferenced). Integration deferred — see `docs/patches/EPIC_E_INTEGRATION.md`.
- E2. Add hook points before/after model call, before/after command execution, and before file write. **IMPLEMENTED (modules written)** — `src/Agent/Hooks/{HookKind,HookContext,IPluginHook,HookRegistry}.cs`. Sync, isolated `Run(kind, ctx)`; a throwing hook never breaks the pipeline. `BeforeCommand`/`AfterCommand` wired into `AgentPipeline` via the deferred patch; `BeforeFileWrite` + `Before/AfterModelCall` defined for later adoption.
- E3. Add MCP compatibility research spike; do not claim MCP support until a real client exists. **DONE** — `docs/MCP_RESEARCH_SPIKE.md`. Explicitly states no MCP support is implemented; hook framework is the future seam.
- E4. Add signed/trusted plugin folder guidance. **DONE** — `docs/PLUGIN_ECOSYSTEM.md` (trusted-folder allow-list, manifest path-traversal guard, permission surfacing, trust badge guidance).

Acceptance:

- A contributor can add one safe tool without editing the main form. **On track**: manifest + hook modules are new files; once the deferred patch is applied, a tool is described by `plugin.json` and observed via hooks without touching `ZhuaQianDesktop.cs`. **2026-07-18**: a divergent second plugin system `src/Plugins/PluginRegistry.cs` (orphaned, referenced by 0 production files, broke compilation via an undefined `PluginSandboxSettings` type + `System.Text.Json`) was **retired**; `PluginManifest` + `HookRegistry` is now the single plugin approach.

Coordination note: Epic E modules were authored as net-new files only, because the in-flight refactor (main-form export extraction + `outputs/` regen) owns `ZhuaQianDesktop.csproj`, `src/build.ps1`, `src/ZhuaQianDesktop.cs`, `src/ui/MainForm.Export.cs`, `README.md`, `docs/_line_budget.json`, and `outputs/`. Apply `docs/patches/EPIC_E_INTEGRATION.md` only after that refactor lands.

Hook reconciliation (2026-07-17): a second command-hook contract `src/Agent/ICommandHook.cs` appeared from the concurrent builder (P-B) for Epic E2 — it overlapped with `HookRegistry`'s `BeforeCommand`/`AfterCommand`. The in-flight refactor later DELETED `ICommandHook.cs` as dead code, leaving `IPluginHook` (`src/Agent/Hooks/`) as the single hook contract for the project. `HookRegistry` is `IPluginHook`-only; command/model/file hooks all register by `HookKind`. The earlier "duplicate hook" risk on the 23:30 board is therefore resolved by deletion, not adaptation. No duplicate contract remains.

### Epic F: Office-Workflow Parity

Goal: close the WorkBuddy-style natural-language office gap.

Tasks:

- F1. Add office templates for PPT, PDF, Excel, Word, and PNG. **IMPLEMENTED** - `src/Documents/OfficeTemplateLibrary.cs` (SalesPitch/MeetingMinutes/Report/DataTable/Poster, round-trip via `OfficeExporter`) + `src/tests/TestOfficeTemplateLibrary.cs` / `src/tests/TestOfficeTemplateExecutor.cs` (current suite: 206 tests).
- F2. Add a review/edit step before writing high-value office files. **IMPLEMENTED** — `src/ui/OfficeGenerateDialog.cs` (template picker + editable content + live preview + explicit `SaveFileDialog` = write-before-review) and `src/Agent/OfficeTemplateExecutor.cs` (command path that honors the reviewed/edited text, or falls back to structured-field render). The dialog is the single review surface; the executor only writes.
- F3. Add "use current web research before generating office file" as a first-class workflow. **IMPLEMENTED** — `OfficeGenerateDialog` "Use Web Research" button calls `WebSearchClient.SearchDetailed(q, 5)` and appends snippets into the bullets box before render/preview.
- F4. Add examples for sales deck, meeting minutes, report, spreadsheet, and poster generation. **PARTIALLY DONE** — the five template kinds ship with working defaults and the dialog pre-fills a topic from the natural-language request; concrete "example" presets / templates gallery is still TODO.

Acceptance:

- A non-technical user can request a complete office artifact by natural language and get a reviewable local file.

## Not Recommended Right Now

- More broad UI buttons.
- New evaluation documents.
- Claims that security is enterprise-grade.
- Claims that source-tree convergence is complete.
