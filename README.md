# ZhuaQian Desktop

ZhuaQian Desktop is a free, open-source Windows AI workbench prototype for local work, document handling, and permission-controlled desktop actions.

It is built around a simple idea: an AI assistant should be useful on your own Windows machine without hiding local side effects. File reads, exports, cloud uploads, plugins, process actions, and folder organization should be visible, permission-aware, and auditable.

## What It Can Do

- Chat with Gemini, OpenRouter, local Ollama-compatible models, and OpenAI-compatible providers
- Work with modes: `Ask`, `Draft`, `Plan`, and `Execute`
- Upload and lightly parse images, PDF, Word, PowerPoint, Excel, Markdown, TXT, CSV, and JSON
- Export real local files: TXT, Markdown, Word, PowerPoint, and Excel
- Turn natural-language file requests and `/save` slash commands into real local files through the desktop app
- Keep multi-task state and output records
- Index local folders into chunks, metadata, and an early hybrid knowledge search
- Use screenshot OCR, clipboard monitoring, batch reports, folder organization, rollback, resource monitoring, a read-only local activity monitor, and trusted local plugins
- Use the desktop Prompt Workbench to assemble programmer, office, and media workflows with local memory, permission checks, audit records, and current task attachments
- Review structured agent plans and execute recognized plan steps through the guarded `AgentPipeline`
- Run permission-gated local actions for selected tasks: open targets, type text, send hotkeys/keys, click coordinates, wait, organize folders, run plugins, rollback organized files, and end approved PID-based processes
- Share/import task packages, share over LAN, use relay sharing, and start/join live sessions when network permission is enabled
- Store API keys with Windows DPAPI for the current user
- Gate risky actions behind permissions, Power mode, approval cards, and audit/output records

## Implemented Vs Planned

Implemented in the current preview:

- Desktop workbench UI, task history, provider routing, file parsing, real exports, outputs history, local knowledge index, command palette, permission settings, approval cards, audit log, plugin runner, rollback, basic computer control, plan review with first-step execution, process snapshot monitoring scaffold, and a read-only local activity monitor.

Partly implemented:

- Agent workflow. The first `Plan -> Approval -> Execute -> Output/Review` vertical slice exists through `AgentPlanCommandMapper`, `PlanReviewDialog`, and `AgentPipeline`; it is not yet a full persistent state machine.
- Local activity monitoring. `ProcessSnapshotCollector`, `monitoring-events.jsonl`, `monitoring-cases.jsonl`, and a manual review panel exist, but there is no continuous background monitor, endpoint-security engine, or automated enforcement loop.
- UI modularity. Helper modules exist, but `src/ZhuaQianDesktop.cs` is still the main integration file.

Not implemented yet:

- MCP integration, signed plugin ecosystem, full UI automation tests, enterprise endpoint security, and kernel-level monitoring.

> Note: a PowerShell-based installer (`installer/Install.ps1`) now ships with the repo and installs the CI-built `dist/ZhuaQianDesktop.exe` to Program Files with SHA-256 verification, shortcuts, and an uninstall entry. The local workspace already has a clean linear 9-commit history; publish from a clean clone if you want a pristine public history.

## Current Status

This is a v0.1 prototype, not a polished commercial office-automation product.

Verified on 2026-07-17 in this workspace:

- `build.ps1`: passed, generated `dist/ZhuaQianDesktop.exe`
- `src/scripts/run-tests.ps1`: passed, `186` passed / `0` failed
- `work/zq-desktop/scripts/run-tests.ps1`: passed, `186` passed / `0` failed
- `work/zq-desktop/scripts/smoke-test.ps1`: passed
- Architecture and package checks: passed

The local workspace Git repository is usable (linear history, see `git log`). For a pristine public history, publish from a clean clone.

## Source Trees

This repository has a single contribution source tree:

- `src/`: the only public contribution source. It builds and tests successfully, and is the source of truth for the product.

The `work/zq-desktop/` transitional mirror has been **retired** (see `docs/PROJECT_HANDOFF_2026-07-16.md`). `outputs/` is a generated release-package snapshot and must **not** be treated as a development source tree — regenerate it from `src/` only. Build output under `dist/`, `bin/`, `obj/`, and `generated/` is produced locally and never committed (see `.gitignore`).

The `.codex/` and `.agents/` folders are currently empty placeholders in this workspace; they are not active configuration sources.

## Build

Requirements:

- Windows
- .NET Framework 4.x
- `csc.exe`, usually available under `C:\Windows\Microsoft.NET\Framework*\v4.0.30319\`

Build the modular tree:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
```

The stable local executable is always `dist/ZhuaQianDesktop.exe`. If older tagged builds accumulate in `dist/`, archive them without deleting:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\clean-dist.ps1
```

Run tests (this also compiles the production sources, so a green run means the tree builds):

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\src\scripts\run-tests.ps1
```

## Install (from a release artifact)

`installer/Install.ps1` stages a built `dist/ZhuaQianDesktop.exe` on the local machine. It requires administrator privileges, verifies the `.sha256` sidecar, creates Start Menu / Desktop shortcuts, and writes an uninstall entry under `Programs and Features`.

```powershell
# from an elevated PowerShell prompt, in the repo root
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\installer\Install.ps1
```

To produce a distributable zip (executable + installer + README + LICENSE):

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\installer\Build-Bundle.ps1
```

Uninstall with `installer/Uninstall.ps1` (also elevated) or via Programs and Features.

## Packaging & Release

- **Local builds are dev-only.** `build.ps1` produces `dist/ZhuaQianDesktop.exe`; the accompanying SHA256 sidecar is written next to it. Treat any local zip you make as `*local-dev*` and never publish it as an official release.
- **Official release artifacts are produced by CI only.** On a pushed tag, `.github/workflows/tests.yml` builds, runs the test suite, and uploads `ZhuaQianDesktop-<tag>.zip` as a Release artifact. The root `.git` metadata in this workspace is usable; publish from a clean clone or this repository.

## Read First

- [Architecture Charter](docs/ARCHITECTURE_CHARTER.md)
- [Project Handoff](docs/PROJECT_HANDOFF_2026-07-16.md)
- [Product Implementation Direction](docs/PRODUCT_IMPLEMENTATION_DIRECTION_2026-07-16.md)
- [Desktop Prompt Collaboration](docs/DESKTOP_PROMPT_COLLABORATION_2026-07-17.md)
- [Product Requirements](docs/PRODUCT_REQUIREMENTS.md)
- [Product Architecture](docs/PRODUCT_ARCHITECTURE.md)
- [Current Reality](docs/CURRENT_REALITY_2026-07-11.md)
- [Code Completion Alignment](docs/CODE_COMPLETION_ALIGNMENT.md)
- [Open Source Monitoring Boundary](docs/OPEN_SOURCE_MONITORING_BOUNDARY.md)
- [Open Source Readiness Review](docs/OPEN_SOURCE_READINESS_REVIEW_2026-07-12.md)
- [Free Open Source Release Plan](docs/FREE_OPEN_SOURCE_RELEASE_PLAN.md)

## License

MIT License. See [LICENSE](LICENSE).
