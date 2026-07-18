# Project Handoff

Updated: 2026-07-17

This is the short current-state document for a developer or AI agent taking over the project. If older notes disagree with this file, trust this file together with `README.md`, `docs/INDEX.md`, `docs/EXECUTION_BACKLOG.md`, and `docs/ARCHITECTURE_CHARTER.md`.

## Project Identity

ZhuaQian Desktop is a local-first Windows AI workbench prototype. It combines chat, task history, document parsing, office file export, local knowledge indexing, multi-provider model routing, permission-gated local actions, audit records, and output records.

It is not just a chat UI. The product direction is a permission-aware desktop agent where real side effects are visible, approved when risky, auditable, and tied back to tasks and generated outputs.

## Current Status

- Version stage: v0.1 prototype.
- Public source target: `src/` (the single source tree).
- Transitional mirror: retired (was `work/zq-desktop/`).
- Generated package output: `outputs/ZhuaQianDesktop-open-source/` (regenerate from `src/`).
- Latest verified result in this workspace: root build passed; the `src` test suite passed with `219` passed / `0` failed; architecture/package checks passed.
- Git metadata in this workspace is usable; publish from this repository or a clean clone.

## Directory Guide

| Path | Meaning | Edit? |
|---|---|---|
| `src/` | **The single public contribution source tree.** Build and tests pass here. | Yes |
| `outputs/` | Generated release-package snapshot. Regenerate from `src/`; never edit by hand. | No, regenerate instead |
| `dist/` | Build output from root build. | No |
| `docs/` | Current docs plus archived historical notes. | Yes, but update core docs rather than adding loose reports |
| `assets/` | Screenshots used by docs/release material. | Yes, when updating visual evidence |
| `.agents/`, `.codex/` | Empty placeholders in this workspace. | Not active config |

## Main Code Shape

The app is a .NET Framework 4.8 WinForms application.

Current important modules under `src/`:

- `ZhuaQianDesktop.cs`: current main WinForms implementation; still oversized and should be reduced.
- `Program.cs`: application entry point.
- `Agent/`: command contracts, plan-step command mapping, pipeline, centralized executor factory, and executors for export, folder organization, plugin run, process management, rollback, web search, and basic computer control.
- `Core/`: configuration, DPAPI-backed storage, permission gate, audit log, outputs hub, sharing, and package building.
- `Documents/`: Office export and redaction helpers.
- `Knowledge/`: chunking and vector/index primitives.
- `providers/`: provider clients and `ProviderManager` for Gemini, OpenRouter, local/Ollama-compatible, OpenAI-compatible, Tencent WorkBuddy, Alibaba Qianwen, and Zhipu AI.
- `Tools/`: folder organization, plugin runner, command parsing, timeline, approval UI, system diagnostics, process snapshot monitoring scaffold, and related local tools.
- `ui/`: settings dialog, plan review/execution partials, right panel, monitoring panel, share/live-session partials.
- `tests/`: custom C# test runner and module tests.

## Architecture Rule To Preserve

All real side-effect actions should move toward this pipeline:

```text
IAgentCommand
  -> PermissionGate
  -> Approval surface when needed
  -> ICommandExecutor
  -> AuditLog + OutputsHub
```

Do not add new local actions by directly wiring business logic into `MainForm` or by writing a separate permission/logging path.

## Build And Test

Run from repository root:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\src\scripts\run-tests.ps1
```

When behavior also exists in the transitional mirror:

> The `work/zq-desktop/` transitional mirror has been **retired**. The `src/` tree is the only source. If you still have a local `work/zq-desktop/` checkout, delete it — it is no longer maintained and will drift from `src/`.

## Current Strengths

- The app builds into a real Windows executable.
- Office outputs can be written as real files, not just described by the model.
- Multi-provider routing and fallback exist.
- API key storage uses Windows DPAPI for the current user.
- Permission, approval, audit, and output primitives exist.
- Natural-language local actions have begun moving through executor paths.
- Basic computer-control actions and a read-only Activity Monitor exist, but they are preview-grade.
- Tests cover important Core and Agent pieces through the custom runner.

## Current Risks

1. `src/ZhuaQianDesktop.cs` is still too large and mixes UI, task orchestration, provider flow, and local action routing.
2. Tests are custom scripts, not standard xUnit/NUnit.
3. Some historical docs contain old source-tree assumptions and old completion numbers.
4. The security model is prototype-grade, not enterprise endpoint protection.

## Near-Term Priorities

1. `work/zq-desktop/` has been retired; `src/` is the single public contribution source. Generated `outputs/` and `dist/` are git-ignored and regenerated from `src/`.
2. Continue shrinking `ZhuaQianDesktop.cs` by extracting orchestration into tested modules.
3. Keep every new side-effect action behind command, permission, executor, audit, and output records.
4. Convert the task flow toward `Plan -> Approval -> Execute -> Output -> Review`.
5. Move the test system toward a standard .NET test project after source-tree convergence.

## Documentation Reading Order

1. `README.md`
2. `docs/INDEX.md`
3. `docs/PROJECT_HANDOFF_2026-07-16.md`
4. `docs/ARCHITECTURE_CHARTER.md`
5. `docs/EXECUTION_BACKLOG.md`
6. `docs/PRODUCT_IMPLEMENTATION_DIRECTION_2026-07-16.md`

Historical files under `docs/archive/` are useful context, but they are not the current source of truth.
