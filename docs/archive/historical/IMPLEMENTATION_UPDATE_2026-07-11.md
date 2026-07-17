# ZhuaQian Desktop Implementation Update - 2026-07-11

> 完成度对齐：截至 2026-07-11，已实现/部分实现/未实现的权威表见 `CODE_COMPLETION_ALIGNMENT.md`。本文中早于该表的“下一步/缺口”描述，以该表和本文后续更新说明为准。

This update records the next execution slice after reading:

- `outputs/DEEP_EVALUATION.md`
- `outputs/OPENCODE_ACTION_PLAN.md`

## What Was Implemented

The first practical slice of the recommended Agent/workflow foundation is now in code.

### Task Status

Implemented in `work/zq-desktop/ZhuaQianDesktop.cs`:

- `TaskInfo.Status`
- `TaskInfo.LastAction`
- Current task fields:
  - `currentTaskStatus`
  - `currentTaskLastAction`
- Task JSON persistence for:
  - `status`
  - `lastAction`
- New tasks default to `draft`.
- Left sidebar now groups tasks by status:
  - `Needs input`
  - `Running`
  - `Ready`
  - `Failed`
  - `Draft`
  - `Done`
- Task header shows current status and last action.

### Status Transitions

Implemented status updates for real workflows:

- Sending a provider request: `running`
- Successful model reply: `ready_for_review`
- Provider error: `failed`
- Permission denial: `needs_input`
- Cancelled cloud upload confirmation: `needs_input`
- TXT / MD / Word / PPT / Excel export success: `ready_for_review`
- Folder organize success: `ready_for_review`
- Rollback success: `ready_for_review`
- Rollback failure: `failed`
- Knowledge folder indexing success: `ready_for_review`
- Knowledge folder indexing failure: `failed`
- Plugin success: `ready_for_review`
- Plugin failure: `failed`

### Action Records

Added structured action logging:

```text
%APPDATA%\ZhuaQianDesktop\actions.jsonl
```

Each line includes:

- `actionId`
- `at`
- `taskId`
- `taskTitle`
- `taskStatus`
- `type`
- `status`
- `detail`
- `outputPath`

Action records are now written for:

- permission blocks
- cloud upload confirmation
- chat completion success/failure
- TXT export
- generic file export
- folder organize
- rollback
- folder index
- plugin execution

### Source Package Build

The open-source package build was also fixed:

- `src/providers/*.cs` and `src/ui/*.cs` are now included in the package.
- Provider/UI helper modules were made compatible with the bundled .NET Framework compiler.
- `build.ps1` now fails immediately when `csc.exe` returns a non-zero exit code, instead of continuing with an old executable.

## Why This Matters

This directly addresses the highest-impact workflow gap identified in the evaluation docs:

- tasks are no longer plain chat titles only;
- the app can distinguish work that is running, blocked, failed, or ready for review;
- local side effects now have a machine-readable audit trail;
- future undo/redo, approval cards, outputs v2, and agent review queues have a concrete data foundation.

## Still Missing

These gaps remain after this execution slice:

- Full OpenCode-style permission values: `allow` / `ask` / `deny`.
- Permission pattern matching, such as allowing safe commands but denying destructive ones.
- Reusable Approval Card UI to replace complex `MessageBox` confirmations.
- General undo/redo beyond folder organize rollback.
- Rich Outputs v2 with links between outputs and action records.
- File-system Skills loaded from `SKILL.md`.
- File-system command definitions.
- Streaming provider responses.
- Unit tests for task storage, action records, export, permission gates, and document parsing.

## Recommended Next PR

Build the reusable Approval Card first.

Minimum scope:

- One modal form that displays title, permission, affected paths, risk, and details.
- Buttons: `Approve`, `Cancel`.
- Copyable detail text.
- Writes an action record with `approved` or `cancelled`.
- Replace cloud upload confirmation first.
- Then replace folder organize and plugin run confirmations.

This keeps the product aligned with OpenCode and WorkBuddy without adding more unrelated buttons.
