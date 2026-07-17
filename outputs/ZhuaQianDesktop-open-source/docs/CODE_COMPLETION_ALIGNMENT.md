# Code Completion Alignment

Updated: 2026-07-17

This document is the current handoff baseline. If older docs disagree with this file, trust this file first.

## Verification Matrix

| Area | Current result |
|---|---|
| Root build | passed |
| `src` tests | `168` passed / `0` failed |
| `work/zq-desktop` tests | `168` passed / `0` failed |
| `work/zq-desktop` smoke test | passed |
| Production empty catches | none found by smoke test |
| Open-source package snapshot | regenerated under `outputs/ZhuaQianDesktop-open-source/` |

## Implemented

### User-Facing Product Surface

| Area | Implemented behavior | Main code paths |
|---|---|---|
| Desktop shell | WinForms workbench with left task list, central chat, top mode/model/power controls, bottom composer, command palette, tools panel, Activity Monitor, outputs panel, and settings | `src/ZhuaQianDesktop.cs`, `src/ui/MainForm.Monitoring.cs` |
| Task history | Multi-task create/switch/rename/clear/save/load with status labels | `src/ZhuaQianDesktop.cs`, `%APPDATA%\ZhuaQianDesktop\tasks` |
| Modes | `Ask`, `Draft`, `Plan`, and `Execute` modes influence prompts and UI state | `src/ZhuaQianDesktop.cs` |
| Providers | Gemini, OpenRouter, local/Ollama-compatible, OpenAI-compatible, Tencent WorkBuddy, Alibaba Qianwen, and Zhipu AI clients are present behind `ProviderManager` | `src/providers/*`, `src/ui/SettingsDialog.cs` |
| File input | Images, PDF, Word, PowerPoint, Excel, Markdown, TXT, CSV, JSON, and common text/code files can be attached or extracted as context | `src/ZhuaQianDesktop.cs`, `src/Documents/*` |
| File output | TXT, Markdown, DOCX, PPTX, and XLSX exports create real local files and output records | `src/Agent/ExportFileExecutor.cs`, `src/Documents/OfficeExporter.cs`, `src/Core/OutputsHub.cs` |
| Natural-language file generation | User requests for saving/generating files are detected, then routed to real local file generation after model output | `DetectExportFormat`, `SaveLastReplyAsFile`, `RunSaveCommand` in `src/ZhuaQianDesktop.cs` |
| Slash commands | `/save`, `/open`, `/type`, `/hotkey`, `/key`, `/click`, `/wait`, and local action helpers exist for selected controlled actions | `src/Tools/CommandParser.cs`, `src/ZhuaQianDesktop.cs`, `src/Agent/ComputerControlExecutor.cs` |
| Knowledge | Folder indexing, chunk metadata, vector persistence primitives, keyword/hybrid search entry points | `src/Knowledge/*`, `IndexFolder`, `SearchKnowledge` |
| Outputs | Output records can be listed, opened, located, renamed, deleted, and added to the knowledge base | `src/Core/OutputsHub.cs`, `ShowOutputsPanel` |
| Sharing | Task package export/import, LAN share, relay share, import from URL, and live-session partials exist behind file/network permissions | `src/ui/MainForm.Share.cs`, `src/ui/MainForm.LiveSession.cs`, `src/Core/LanShareServer.cs` |

### Local Actions, Permissions, And Audit

| Area | Implemented behavior | Main code paths |
|---|---|---|
| Permission model | Boolean UI permissions plus `PermissionGate` allow/ask/deny model, allowed directories, and auto mode | `src/Core/PermissionGate.cs`, `ShowPermissionSettings` |
| Master switch | `Power` gate for high-risk local side effects | `ToggleComputerControlPower`, `EnsureComputerControlPower` |
| Approval UI | Risky commands can show `ApprovalCard` with permission, affected target, risk, output, and audit note | `src/Tools/ApprovalCard.cs`, `ShowApprovalCard` |
| Audit/action records | Action log and audit log exist for key local side effects | `src/Core/AuditLog.cs`, `RecordAction`, `LogAction` |
| Agent pipeline | Export, folder organization, plugin run, process management, rollback, web search, and basic computer control can run through `AgentPipeline`; standard executor registration is centralized | `src/Agent/*`, `src/Agent/AgentPipelineFactory.cs`, `src/ZhuaQianDesktop.cs` |
| Folder organization | Selected folder organization with rollback manifest and output/action records | `src/Agent/OrganizeFolderExecutor.cs`, `src/Tools/FolderOrganizer.cs` |
| Rollback | Rollback manifests can be previewed and executed through the pipeline | `src/Agent/RollbackExecutor.cs`, `ShowRollbackPanel` |
| Plugin runner | Trusted local plugin execution with permission/approval path | `src/Agent/PluginRunExecutor.cs`, `src/Tools/PluginRunner.cs` |
| Process management | Resource monitor lists processes and can end an approved PID through the pipeline | `ShowResourceMonitor`, `src/Agent/ProcessManageExecutor.cs` |
| Basic computer control | Open target, type text, send hotkeys/keys, click coordinates, and wait are implemented behind `permAutomationInput` and approval | `src/Agent/ComputerControlExecutor.cs`, `RunComputerControl` |
| Activity Monitor | Read-only process snapshots, manual snapshot recording, local events, local cases, case closing, clear-records action, privacy note, and evidence-folder access are implemented and tested at the collector level | `src/ui/MainForm.Monitoring.cs`, `src/Tools/ProcessSnapshotCollector.cs`, `src/tests/TestRunner.cs` |

### Verification Coverage

Tests currently cover redaction, chunking, Office export, permission gate, config store, outputs hub, folder organizer, plugin runner, agent plan parser, agent pipeline, executor factory registration, streaming bridge, process snapshot collector, system diagnostics, and smoke checks through the custom runner.

## Partly Implemented

- Agent state machine: task statuses exist, but a full plan/execute/review loop is not yet a dedicated engine.
- Source convergence: `src/` is the preferred public tree, but `work/zq-desktop/` still exists as a synchronized mirror.
- Security model: permissions and audit exist, but this is not enterprise endpoint security.
- Provider reliability: fallback behavior exists, but provider mock coverage is still thin.
- UI modularity: modules exist, but `ZhuaQianDesktop.cs` remains oversized.
- Activity Monitor: manual snapshot collection and review UI exist, but it is not a continuous background monitor or automated enforcement loop.

## Not Done

- Clean Git history.
- Single authoritative source tree with `work/` retired.
- Standard test framework project.
- Installer.
- MCP integration.
- Mature plugin ecosystem and signed plugin trust model.
- Continuous Windows activity-monitor background loop.
- Full UI automation tests.
- Kernel-level monitoring or anti-cheat driver.
- Enterprise endpoint security hardening.

## Current Risk Register

1. Two source trees can drift if changes are not synchronized.
2. The main form still mixes UI orchestration, task state, local action routing, and provider flow.
3. Some historical docs describe old test counts and old source-tree assumptions.
4. Open-source contributors will need clear instructions to edit `src/`, not generated `outputs/`.

## Definition Of Done For New Work

- Code lands in `src/`.
- `work/zq-desktop/` is synchronized when the same behavior still exists there.
- Root build passes.
- `src/scripts/run-tests.ps1` passes.
- `work/zq-desktop/scripts/run-tests.ps1` and smoke test pass when runtime behavior is affected.
- Docs are updated in existing core docs, not by adding another loose evaluation note.
