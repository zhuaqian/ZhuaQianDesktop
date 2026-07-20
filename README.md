# ZhuaQian Desktop

ZhuaQian Desktop is a free, open-source Windows AI workbench that pairs a permission-controlled desktop automation assistant with an autonomous coding agent. On your own machine it handles local work and documents; it can also diagnose build/test failures in a code project, generate patches, run tests, and commit fixes — every side effect routed through the same guarded, auditable pipeline.

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
- Act as an autonomous coding agent: scan a project, locate its build/test script, run tests, read failures, generate and apply patches through the guarded pipeline, and commit fixes (DiagnoseFix / BuildFixLoop / CodePatcher / GitWorkflow)
- Share/import task packages, share over LAN, use relay sharing, and start/join live sessions when network permission is enabled
- Store API keys with Windows DPAPI for the current user
- Publish a local project as a public open-source repo on GitHub, Gitee, or GitLab (`/publish host=github path=...`) — the PAT is kept in local config only and never written to the audit log
- Keep a logged-in web session in the built-in open-source Chromium browser and reuse it later (`/browser login` → `savesession` → `loadsession`)
- Switch between a clean Light skin and a Dark skin from Settings → Theme (persisted to `%AppData%/ZhuaQianDesktop/theme.json`)
- Generate a static HTML presentation / website from a prompt via the LLM (`/build website` or natural language)
- Gate risky actions behind permissions, Power mode, approval cards, and audit/output records

## Implemented Vs Planned

Implemented in the current preview:

- Desktop workbench UI, task history, provider routing, file parsing, real exports, outputs history, local knowledge index, command palette, permission settings, approval cards, audit log, plugin runner, rollback, basic computer control, plan review with first-step execution, process snapshot monitoring scaffold, and a read-only local activity monitor — and an autonomous coding-agent loop (diagnose build failure → patch → test → commit through the guarded pipeline).
- Open-source publishing to GitHub / Gitee / GitLab, browser login-state persistence (built-in Chromium), Light/Dark themes, and LLM-driven static website / HTML presentation generation.

Partly implemented:

- Agent workflow. A persistent per-step state machine now backs the `Plan -> Approval -> Execute -> Output/Review` loop (`AgentPlanState` + `AgentPlanRunner`, surfaced via `PlanReviewDialog` and `AgentPlanCommandMapper`); `AgentPipeline` runs steps asynchronously and awaits each step's completion.
- Local activity monitoring. `ProcessSnapshotCollector`, `monitoring-events.jsonl`, `monitoring-cases.jsonl`, and a manual review panel exist, but there is no continuous background monitor, endpoint-security engine, or automated enforcement loop.
- UI modularity. Helper modules exist, but `src/ZhuaQianDesktop.cs` is still the main integration file.

Not implemented yet:

- MCP client integration (research only — see `docs/MCP_RESEARCH_SPIKE.md`)
- Runtime-enforced signed plugin trust (manifest + trust-folder guidance exists in `docs/PLUGIN_ECOSYSTEM.md`, not yet enforced at load time)
- Full UI automation test suite and a standard test framework (currently a custom `TestRunner`)
- Enterprise endpoint-security features, kernel-level monitoring, and anti-cheat drivers

> Open source: by project decision, the public GitHub repository is published only after all remaining work is finished and a clean build/test pass plus a security review are done. See `docs/FREE_OPEN_SOURCE_RELEASE_PLAN.md`.

> Note: a PowerShell-based installer (`installer/Install.ps1`) now ships with the repo and installs the CI-built `dist/ZhuaQianDesktop.exe` to Program Files with SHA-256 verification, shortcuts, and an uninstall entry. The local workspace already has a clean linear 9-commit history; publish from a clean clone if you want a pristine public history.

## Current Status

This is a v0.1 prototype, not a polished commercial office-automation product.

Verified on 2026-07-18 in this workspace:

- `build.ps1`: passed, generated `dist/ZhuaQianDesktop.exe`
- `src/scripts/run-tests.ps1`: passed, `219` passed / `0` failed
- Architecture and package checks: passed

Code added on 2026-07-20 (code-level, pending a fresh local build/test in this sandbox-free environment):
open-source publishing (GitHub/Gitee/GitLab), browser login-state persistence,
dual Light/Dark themes, and LLM website generation. The build scripts were updated
to reference `System.Net.Http`/`System.Xml` (required by the publish feature) and to
resolve `csc.exe` on the CI `windows-latest` runner; run `src/build.ps1` then
`src/scripts/run-tests.ps1` to confirm the tree still compiles and tests green.

The transitional `work/zq-desktop/` mirror has been retired and is no longer a build/test source.

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
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\src\build.ps1
```

The default output is `dist/ZhuaQianDesktop.exe` at the repository root (ignored by
`.gitignore`). The script auto-resolves `csc.exe` and references `System.Net.Http` /
`System.Xml` (needed by the open-source publishing feature).

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
- **External review snapshots come from git only.** Use `scripts/export-review-snapshot.ps1`, which wraps `git archive HEAD` and writes a SHA-256 sidecar. Do not manually zip the full workspace for review.

## Read First

- [Docs Index](docs/INDEX.md) — 所有文档的导航中枢
- [Architecture Charter](docs/ARCHITECTURE_CHARTER.md)
- [Project Handoff](docs/PROJECT_HANDOFF_2026-07-16.md)
- [Product Implementation Direction](docs/PRODUCT_IMPLEMENTATION_DIRECTION_2026-07-16.md)
- [Desktop Prompt Collaboration](docs/DESKTOP_PROMPT_COLLABORATION_2026-07-17.md)
- [Product Requirements](docs/PRODUCT_REQUIREMENTS.md)
- [Product Architecture](docs/PRODUCT_ARCHITECTURE.md)
- [Open Source Monitoring Boundary](docs/OPEN_SOURCE_MONITORING_BOUNDARY.md)
- [Free Open Source Release Plan](docs/FREE_OPEN_SOURCE_RELEASE_PLAN.md)
- [Release Trust Pipeline](docs/RELEASE_TRUST_PIPELINE.md)

## License

MIT License. See [LICENSE](LICENSE).
