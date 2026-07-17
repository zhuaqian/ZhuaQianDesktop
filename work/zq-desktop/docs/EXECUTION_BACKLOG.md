# Execution Backlog

Updated: 2026-07-17

## Execution Principles

1. Converge facts before adding features.
2. Keep side-effect actions behind Command -> PermissionGate -> Executor -> Audit/Output records.
3. Prefer natural-language task entry over adding more buttons.
4. Update existing core docs instead of adding another loose evaluation document.
5. Keep `src/` and `work/zq-desktop/` synchronized until the mirror is retired.

## Done Recently

- Production empty catches removed or diagnosed.
- File export path moved through `AgentPipeline -> ExportFileExecutor`.
- Natural-language file generation creates real local files.
- Natural-language local actions added for opening targets, organizing folders, running plugins, and ending PID-based processes.
- Folder organization now uses `OrganizeFolderExecutor` through the pipeline.
- Plugin and process execution paths are shared between UI entry points and chat entry points.
- Agent executor registration is centralized in `AgentPipelineFactory`; the main form no longer hand-registers each executor at every side-effect call site.
- `AgentPlanCommandMapper` and plan execution sources are synchronized into the `work/zq-desktop` mirror.
- Current verification: both test suites are `186` passed / `0` failed, architecture/package checks pass.

## Highest Priority

### Competitive Alignment Snapshot

| Reference | What they are strong at | Current ZhuaQian gap | Backlog target |
|---|---|---|---|
| Codex-style coding agent | repo-aware task loop, terminal execution, diffs, tests, review handoff | ZhuaQian has tasks and executors, but no structured plan/diff/test loop | Epic C + Epic D |
| Claude Code-style agent | explicit plan/approval modes, tool permissioning, hooks/MCP-style extensibility | permissions exist, but plan text is not yet a schema and hooks/MCP are absent | Epic C + Epic E |
| WorkBuddy-style desktop office assistant | natural-language office work, document generation, desktop-context workflows | ZhuaQian now exports office files and searches web, but workflow templates and review UX are thin | Epic F |

### Epic A: Source-Tree Convergence

Goal: make the public contribution path unambiguous.

Tasks:

- A1. Declare `src/` as the public contribution source in README and CONTRIBUTING.
- A2. Keep `work/zq-desktop/` synchronized only while needed.
- A3. Decide whether public release omits `work/` or marks it as a legacy mirror.
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

- D1. Add a workspace scan summary: files changed, build commands, test commands, and risk notes.
- D2. Add command-run records with stdout/stderr summary and exit code.
- D3. Add a diff/review panel for generated or edited files.
- D4. Add "run tests after change" workflow with visible pass/fail state.

Acceptance:

- A code task can show Plan -> Command -> Diff -> Test -> Review without leaving the app.

### Epic E: Tool Ecosystem

Goal: make plugins safer and more reusable.

Tasks:

- E1. Define a simple plugin manifest contract.
- E2. Add hook points before/after model call, before/after command execution, and before file write.
- E3. Add MCP compatibility research spike; do not claim MCP support until a real client exists.
- E4. Add signed/trusted plugin folder guidance.

Acceptance:

- A contributor can add one safe tool without editing the main form.

### Epic F: Office-Workflow Parity

Goal: close the WorkBuddy-style natural-language office gap.

Tasks:

- F1. Add office templates for PPT, PDF, Excel, Word, and PNG.
- F2. Add a review/edit step before writing high-value office files.
- F3. Add "use current web research before generating office file" as a first-class workflow.
- F4. Add examples for sales deck, meeting minutes, report, spreadsheet, and poster generation.

Acceptance:

- A non-technical user can request a complete office artifact by natural language and get a reviewable local file.

## Not Recommended Right Now

- More broad UI buttons.
- New evaluation documents.
- Claims that security is enterprise-grade.
- Claims that source-tree convergence is complete.
