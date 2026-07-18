# End-to-End Regression Scenarios

Updated: 2026-07-17

Three priority e2e regression scenarios for ZhuaQian Desktop. Each is phrased as a
natural-language user request that must flow through the single side-effect pipeline
(`IAgentCommand -> PermissionGate -> (Approval) -> ICommandExecutor -> AuditLog + OutputsHub`)
and leave an observable, assertable result. They are the regression net for the Epic C/D/E
work (plan execution, coding-agent review, plugin/hook ecosystem).

Run order: these are independent. Each should be executed from a clean app state (or a
recorded seed) and verified against `docs/_last_verification.txt` after
`src/scripts/run-tests.ps1`.

---

## Scenario 1 — Folder Organization + Excel Report

**Natural-language trigger (chat or local action):**

> "整理桌面上的 '2026-Q2 报销' 文件夹，把图片放进 images、表格放进 sheets、文档放进 docs，
> 然后生成一份 Excel 汇总（每个子文件夹的文件数 + 总大小）。"

**Expected pipeline path:**

1. `AgentPlanCommandMapper` / chat router maps the request to an `OrganizeFolderExecutor`
   command (`permFileMoveDelete` permission).
2. `PermissionGate.Check` returns `Ask` -> UI shows approval card with target path
   `桌面/2026-Q2 报销` and the planned sub-folder layout.
3. On approve, `AgentPipeline.Run` executes; `BeforeCommand`/`AfterCommand` hooks fire
   (HookRegistry, must not throw).
4. `OrganizeFolderExecutor` moves files by extension into `images/`, `sheets/`, `docs/`.
5. A second command (`ExportFileExecutor`, `permFileWrite`) writes an `.xlsx` summary via
   `Documents/OfficeExporter` (or `Documents/OfficeTemplateLibrary` `DataTable` template).
6. `OutputsHub.RecordOutput` registers the produced `.xlsx`; `AuditLog` records both commands.

**Assertions / regression signals:**

- The source folder no longer contains loose images/sheets/docs at its root.
- `images/`, `sheets/`, `docs/` exist with the expected split.
- An `.xlsx` appears in Outputs and opens (valid Office OpenXML zip).
- Audit log contains exactly two entries (`organize` + `export`) with status `ok`.
- A throwing `IPluginHook` (registered for `AfterCommand`) does NOT abort the export.

**What breaks if regressed:**

- Organizer writes under a rooted/traversal path -> blocked by `PermissionGate` or executor
  guard; the move silently no-ops.
- Approval card missing target path -> user cannot confirm; request hangs at `Ask`.
- Hooks throw -> pipeline aborts (would violate the "hooks never break the pipeline" rule).

---

## Scenario 2 — Web Search With Cited Sources

**Natural-language trigger:**

> "搜一下 2026 年 C# Source Generators 的最佳实践，给我一份带来源链接的摘要。"

**Expected pipeline path:**

1. Router maps to `WebSearchExecutor` (`permNetwork` permission).
2. `PermissionGate` -> `Ask` (network egress) -> approve.
3. `WebSearchExecutor` calls `Tools/WebSearchClient`, returns result items each carrying a
   `SourceUrl`.
4. The response rendered to chat is a summary where every claim line links to its
   `SourceUrl`; no claim is presented without a source reference.
5. `OutputsHub` records the search as an output; `AuditLog` records `websearch` `ok`.

**Assertions / regression signals:**

- Chat output contains at least one `http(s)://` link and each summary bullet maps to a source.
- No "orphan" claim (a factual sentence with zero associated source).
- `Network` permission surfaced on the approval card before any request left the machine.
- A `BeforeModelCall`/`AfterModelCall` hook (when adopted) observes the request/response
  without altering it.

**What breaks if regressed:**

- Summary drops source links -> regresses the "带来源摘要" guarantee (Epic F3 precursor).
- Network egress without `permNetwork` approval -> bypasses the gate.
- `WebSearchClient` returns empty -> executor must surface "no results", not a hallucinated
  summary.

---

## Scenario 3 — Wait + Multi-Step Plan + Approval

**Natural-language trigger:**

> "执行这个计划：1) 打开记事本并输入 'hello'，等 2 秒；2) 再输入 ', world'；3) 保存为
> C:\temp\greet.txt。每步开始前让我确认。"

**Expected pipeline path:**

1. `AgentPlan` parsed into 3 steps; `AgentPlanCommandMapper.BuildCommandForStep` produces one
   `IAgentCommand` per step.
2. `MainForm.PlanExecution` delegates the loop to `AgentPlanRunner.RunPlanAsync(plan, options)`:
   - step 1: `ComputerControlExecutor` opens notepad + types 'hello', then the 2s wait goes
     through the **real async** `IAsyncCommandExecutor.ExecuteAsync` (true `await Task.Delay`,
     NOT the old thread-pool fake-complete).
   - `BeforeCommand`/`AfterCommand` hooks fire per step.
   - UI shows an approval prompt before each step (or per the plan's approval policy).
   - step result persisted to `AgentPlanExecutionState` (`%APPDATA%/ZhuaQianDesktop/plan-runs/<id>.json`).
3. After all steps, `CodingAgentSession` produces a lightweight Plan->Command->Diff->Test->Review
   report (read-only diff scan; does NOT trigger a heavy build/test run).

**Assertions / regression signals:**

- The 2s wait is genuinely waited (>= ~1900ms, not instant) -> proves the async fix holds.
- `plan-runs/<id>.json` shows each step `Done` (or `Failed`/`Skipped`) with duration.
- `greet.txt` ends with `hello, world`.
- Cancelling at step 2's approval leaves step 3 unexecuted and `FailedCount`/`SkippedCount`
  reflected in state.
- UI is not frozen during the wait (async on background thread).

**What breaks if regressed:**

- Wait completes instantly -> the `ComputerControlExecutor.Wait` thread-pool fake-complete bug
  returned (guarded by `TestAgentPipelineAsync` in `run-tests.ps1`).
- Multi-step plan runs all steps without per-step approval -> approval surface regressed.
- UI thread blocked during wait -> async path not actually backgrounded.
- State JSON missing a step -> `AgentPlanRunner` persistence regressed.

---

## Why these three

They lock the three highest-risk, hardest-to-notice regressions introduced across the recent
epics:

| Scenario | Locks | Related epic |
|---|---|---|
| 1 Folder + Excel | side-effect pipeline + approval + hook isolation + office export | B / E / F1 |
| 2 Web search + sources | network permission gating + source citation guarantee | F3 precursor |
| 3 Wait + multi-step + approval | async wait fix + plan execution + per-step state | C / D |

If any scenario fails after a change, the offending diff is almost always in
`AgentPipeline`, `AgentPlanRunner`, `ComputerControlExecutor`, `WebSearchExecutor`,
`OrganizeFolderExecutor`, or `HookRegistry` — check those first.
