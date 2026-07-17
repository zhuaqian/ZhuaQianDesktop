# ZhuaQian Desktop Current Reality

Updated: 2026-07-17

## One-Line Status

ZhuaQian Desktop is a dense Windows local-first AI workbench prototype. It is useful enough to preview as open source, but it is still v0.1 engineering: the main form is too large, the source trees are still being converged, and the repo needs a clean Git history before public launch.

## Verified State

Commands run in this workspace:

| Check | Result |
|---|---|
| `build.ps1` | passed; generated `dist/ZhuaQianDesktop.exe` |
| `src/scripts/run-tests.ps1` | `168` passed / `0` failed |
| `work/zq-desktop/scripts/run-tests.ps1` | `168` passed / `0` failed |
| `work/zq-desktop/scripts/smoke-test.ps1` | passed; no empty `catch` blocks in production source |

Empty `catch {}` blocks remain only in test cleanup code.

## Source Trees

- `src/` is the preferred contribution tree. It builds, tests, and is the tree copied into the open-source package.
- `work/zq-desktop/` is a transitional runtime mirror. Keep it synchronized while it still exists, but do not make it the public contribution target.
- `outputs/ZhuaQianDesktop-open-source/` is generated release output. Do not edit it as source.

The `.codex/` and `.agents/` directories are currently empty placeholders; they are not active project configuration.

## Current Code Size

| File | Size | Judgment |
|---|---:|---|
| `src/ZhuaQianDesktop.cs` | 4841 lines / 267812 bytes | still too large; current public source |
| `work/zq-desktop/ZhuaQianDesktop.cs` | 4841 lines / 267812 bytes | transitional mirror; still too large |

## What Improved Recently

- Production empty catches were removed or diagnosed.
- Exported file generation now goes through `AgentPipeline -> PermissionGate -> ExportFileExecutor`.
- Natural-language file requests now create real local files after the model reply.
- Natural-language local actions now route through controlled execution paths for opening targets, organizing folders, running plugins, and ending PID-based processes.
- Folder organization, plugin execution, process management, rollback, and file export now have clearer executor/pipeline paths.
- `AgentPipelineFactory` centralizes standard executor registration so the main form no longer hand-registers executor sets at each pipeline call site.
- Basic Windows monitoring data structures, process snapshot tests, and a read-only manual Activity Monitor now exist, but no continuous background or automated enforcement loop is wired.

## Hard Limits

- The repo `.git` metadata is unusable in this workspace. Public release should happen from a clean `git init` or repaired clone.
- The main WinForms files are still oversized and should be reduced before v0.2.
- Security should be described as prototype-grade: visible permissions, approval cards, DPAPI, audit logs, and trusted-plugin assumptions, not enterprise hardening.
- The test runner is custom PowerShell/C# infrastructure, not a standard xUnit/NUnit project.

## Current Priority

Stop adding broad new UI features. Focus on:

1. Make `src/` the only public contribution source.
2. Retire or clearly archive `work/zq-desktop/`.
3. Continue reducing `ZhuaQianDesktop.cs` by extracting orchestration code into modules.
4. Keep every side-effect action behind command, permission, executor, audit, and output records.
