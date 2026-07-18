# Gap Analysis: ZhuaQian Desktop vs OpenCode

Updated: 2026-07-18

This document compares **ZhuaQian Desktop** (our local-first Windows WinForms AI desktop
agent, C# / .NET Framework 4.8) against **OpenCode** — the open-source terminal/desktop/IDE
AI coding agent (TypeScript, MIT, 180k+ GitHub stars, 75+ LLM providers). OpenCode is the
most mature open reference for *agentic coding* in 2026, so it is the right yardstick for the
coding-agent half of our product.

The verdict up front:

- As a **coding agent**, OpenCode is ahead on four structural capabilities we do not yet have:
  **LSP code intelligence, subagent orchestration, a real MCP client, and a Plan/Build mode
  separation**. These are the P0 gaps.
- As a **desktop AI agent**, ZhuaQian already leads OpenCode on: office document generation,
  browser-render web research, a trusted plugin ecosystem, LAN/local sharing, and a
  self-healing coding loop. Do not regress these.

---

## 1. OpenCode capability baseline (verified)

Sourced from opencode.ai, its docs, and multiple 2026 reviews.

| Capability | Notes |
|---|---|
| Model agnostic | 75+ providers via Models.dev; BYOK; local models (Ollama etc.); GitHub Copilot login; ChatGPT Plus/Pro login; curated free "Zen" models |
| Multi-surface | Terminal TUI, desktop app (mac/win/linux, beta), IDE extension |
| LSP enabled | Auto-loads 30+ language servers; exposes diagnostics, goToDefinition, findReferences, hover, documentSymbol, workspaceSymbol to the LLM |
| Built-in tools | glob, grep, ls, view, write, edit, patch, diagnostics, bash, fetch (web), websearch (Exa), sourcegraph, agent (sub-task delegation) |
| Plan vs Build mode | Plan agent is read-only (denies edits, asks before shell); Build agent is full-access |
| Subagents | Built-in `General` (full) and `Explore` (read-only); child sessions with fresh scoped context + structured result; `@general`, `@council` |
| MCP client | Local (stdio) + remote (SSE) servers, OAuth for remote; auto tool-discovery; permission-gated |
| Permissions | Two-stage: filter tools before the model sees them, then re-check at execution (`allow`/`deny`/`ask`) |
| Project memory | `/init` generates `AGENTS.md` teaching project structure, conventions, commands |
| Sessions | Local persistence, manual compaction (summarization), share links, multi-session parallel |
| Custom commands | Markdown files with named args, per-user and per-project |
| Edit undo/redo | Undo/redo for AI-generated edits |
| Privacy | Privacy-first: no code/context storage; API keys stay local |

---

## 2. ZhuaQian Desktop current state (verified)

Sourced from `docs/EXECUTION_BACKLOG.md`, project memory, and the Epic A–G module set.

- **Architecture**: single side-effect pipeline `Command -> PermissionGate ->(Approval)-> Executor -> AuditLog + OutputsHub`. Central executor registration in `AgentPipelineFactory`. Async path via `IAsyncCommandExecutor`.
- **Module families**:
  - **A** Documents/Knowledge: `OfficeExporter`, `Redactor`, `Chunker`, `VectorIndex`, `OfficeTemplateLibrary`.
  - **B** UI: `MainForm` split into partials + core `ZhuaQianDesktop.cs`.
  - **C** Agent Loop: `AgentPlan`, `AgentPlanState/Runner`, `AgentPipeline.RunAsync`.
  - **D** Coding Agent: `CodingAgentSession` (Plan -> Command -> Diff -> Test -> Review), `WorkspaceScanSummary`, `CommandRunRecorder`, `PatchExecutor`, `FixLoopRunner` (self-healing), `GitWorkflowExecutor`.
  - **E** Tool Ecosystem: `Plugins/PluginManifest` + `Agent/Hooks/{HookRegistry, IPluginHook, HookKind, HookContext}`; `BeforeCommand`/`AfterCommand` wired into `AgentPipeline`.
  - **F** Office Workflow: `OfficeTemplateLibrary` + templates (SalesPitch/MeetingMinutes/Report/DataTable/Poster), review dialog.
  - **G** Browser Render: `BrowserRenderClient` (Playwright headless), `BrowserFetchExecutor`, `WebResearchFetcher` (static-first + browser fallback).
- **Providers**: `ModelRegistry` + clients for Gemini, OpenRouter, Local, OpenAI, Tencent WorkBuddy, Alibaba Qianwen, Zhipu GLM, plus `ProviderManager`/`ShareClient`/`StreamingBridge`. Multi-provider exists, but **no Copilot/Plus login, no Models.dev 75+ catalog, no documented local-Ollama path**.
- **Web research**: `WebSearchClient` (static) + `WebResearchFetcher` (browser-render fallback) — beyond what OpenCode ships natively.
- **Permissions**: `PermissionGate`, explicit approval surface, redaction, cloud-upload confirmation.
- **Installer + CI**: PowerShell installer with SHA-256, GitHub Actions `tests.yml`.
- **Local sharing**: `LanShareServer` + `ShareCrypto`.

**Structural absences** (the gaps this doc is about): no LSP client, no subagent
orchestration, no real MCP client (only a research spike), no Plan/Build mode separation,
no `AGENTS.md` project memory, no session compaction/share, no multi-session, no custom
commands, no undo/redo wired to agent edits, no terminal/TUI surface.

---

## 3. Gap matrix

Legend: **Lead** = we are ahead · **Parity** = comparable · **Partial** = exists but thinner · **Gap** = missing.

| # | Capability | OpenCode | ZhuaQian | Status | Backlog target |
|---|---|---|---|---|---|
| 1 | Model breadth (75+ / local / Copilot / Plus) | Yes | Many clients, no Copilot/Plus login, no Models.dev catalog | Partial | Provider expansion (OAuth logins, catalog) |
| 2 | Local model / Ollama path | Yes | `LocalClient` exists, path undocumented | Partial | Document + test local endpoint |
| 3 | **LSP code intelligence** | Yes (30+ langs, diagnostics) | None | **Gap (HIGH)** | New Epic: LSP client |
| 4 | Built-in dev tools (edit/patch/bash/grep/glob) | Yes | Partial: `ProcessManage`, file actions; build/test run inside `CodingAgentSession` via `GuardedCommandRunRecorder`; no general shell tool exposed to chat agent | Partial | Expose shell/edit/patch executors to chat |
| 5 | **Plan vs Build mode** | Yes (read-only plan agent) | Partial: `AgentPlan` schema + approval, but no separate read-only plan *agent* | Partial→Gap | C: read-only plan agent |
| 6 | **Subagent orchestration** | Yes (child sessions, scoped ctx) | None (only sequential plan steps) | **Gap (HIGH)** | New Epic: subagents |
| 7 | **MCP client** | Yes (stdio+SSE+OAuth) | None (research spike only) | **Gap (HIGH)** | E3 -> real client |
| 8 | Permissions model | Yes (filter + execute-time) | Yes (`PermissionGate` + approval + redaction) | Parity | — |
| 9 | Hooks / extensibility | Yes | Yes (`HookRegistry`, isolated) | Parity / Lead | — |
| 10 | AGENTS.md / project memory | Yes (`/init`) | None | Gap (MED) | New: project memory file |
| 11 | Session persistence + compaction | Yes | Partial: chat/state persistence (Epic B3), no compaction/summarization or share link | Partial | New: session compaction |
| 12 | Multi-session parallel | Yes | None (single desktop session) | Gap (LOW) | Future |
| 13 | Custom commands | Yes (markdown, args) | None | Gap (LOW) | Future |
| 14 | Undo/redo AI edits | Yes | `UndoRedoManager` tool exists, not wired to agent edits | Partial | Wire to agent edits |
| 15 | Privacy-first / no storage | Yes | Partial: local-first, but `AuditLog` stores context | Parity | — |
| 16 | Diff / diagnostics review | Yes (LSP + diff viewer) | Partial: `PlanReviewDialog` + `CodingAgentReportDialog` | Partial | D UI polish |
| 17 | Web research / browser render | Partial (fetch + websearch MCP) | **Yes** (static + Playwright render) | **Lead** | — |
| 18 | Office document generation | No | **Yes** (`OfficeTemplateLibrary`, redaction, review dialog) | **Lead** | — |
| 19 | Trusted plugin ecosystem | Partial (MCP) | **Yes** (`PluginManifest` + trusted folder + path-traversal guard) | **Lead** | — |
| 20 | Desktop GUI surface | Beta | **Yes** (native WinForms, mature) | **Lead** | — |
| 21 | Local / LAN sharing | No | **Yes** (`LanShareServer` + `ShareCrypto`) | **Lead** | — |
| 22 | Self-healing coding loop | Partial | **Yes** (`FixLoopRunner` + `PatchExecutor` + `GitWorkflowExecutor`) | **Lead** | — |

---

## 4. Prioritized backlog — what to close first

### P0 — closest to core coding parity, highest leverage
1. **MCP client (E3 -> real)**. Unlocks external tools (GitHub, Sentry, Context7, Playwright
   MCP) through a standard protocol; our `HookRegistry` is the natural seam. This is the
   single highest-ROI gap because it buys tool breadth without us writing each tool.
2. **LSP client**. Code intelligence (diagnostics, definitions, references) is the biggest
   differentiator for a *coding* agent and the capability OpenCode leans on hardest. Start
   with diagnostics-only exposure (mirror OpenCode's current scope), expand later.
3. **Subagent orchestration**. Child sessions with fresh scoped context + structured results
   (our `AgentPlan` steps can become delegatable child tasks). Enables parallel exploration
   and keeps the main context small.
4. **Plan / Build mode separation**. Promote `AgentPlan` into a distinct read-only planning
   agent that denies edits by default, matching OpenCode's two-mode model.

### P1 — breadth and robustness
5. **AGENTS.md / project memory** (`/init`-equivalent): scan repo, emit a conventions file the
   agent reads before tasks.
6. **Session compaction + share**: summarization to bound context; optional export/share of a
   session for debugging.
7. **Expose dev-tool executors to the chat agent**: shell, file edit/patch, diff — currently
   only `CodingAgentSession` runs commands; the chat agent should too, behind `PermissionGate`.
8. **Model breadth**: GitHub Copilot + ChatGPT Plus/Pro login, a provider catalog (Models.dev
   style), and a documented local-Ollama path.

### P2 — nice-to-have parity
9. Multi-session parallel. 10. Custom commands (markdown + args). 11. Undo/redo wired to agent
   edits. 12. Confirmed local-model smoke test.

---

## 5. Where ZhuaQian already leads — do not regress

Items 17–22 are genuine differentiation. They exist because ZhuaQian is a *broad desktop
agent*, not a terminal coding tool. Keep investing here; the strategy is **not** to out-OpenCode
OpenCode on pure coding, but to close the four P0 coding gaps while defending the desktop /
office / web-research / plugin / LAN lead.

---

## 6. Recommendation

- Close **P0 (MCP, LSP, subagents, Plan/Build)** before adding more UI breadth.
- Use the existing `HookRegistry` and `AgentPipeline` as the integration seams for MCP and
  subagents — no new architecture needed.
- Treat OpenCode as the coding-agent benchmark; treat WorkBuddy (office) and Codex/Claude Code
  (repo-aware loop) as the other two references already in `EXECUTION_BACKLOG.md`.

See also: `docs/EXECUTION_BACKLOG.md` (Competitive Alignment Snapshot),
`docs/MCP_RESEARCH_SPIKE.md`, `docs/patches/BROWSER_RENDER_INTEGRATION.md`.
