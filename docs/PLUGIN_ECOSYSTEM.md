# Plugin Ecosystem (Epic E)

Updated: 2026-07-17

This document is the canonical design note for Epic E (Tool Ecosystem). It covers
the plugin manifest contract (E1), the pipeline hook framework (E2), and trusted
plugin-folder guidance (E4). It is intentionally a design + integration note, not
a separate evaluation report (per the execution backlog's "update core docs"
principle).

> Status: the new source modules are written as **net-new files** and are
> delivered with a deferred integration patch (`docs/patches/EPIC_E_INTEGRATION.md`).
> They are NOT yet registered in `src/ZhuaQianDesktop.csproj` / `src/build.ps1`,
> and `AgentPipeline` does not yet invoke hooks. Apply the patch after the other
> in-flight refactor (main-form export extraction + `outputs/` regen) has landed,
> to avoid a `csproj` merge conflict.

## Design Goals

- A contributor can add one safe tool **without editing the main form**.
- Plugins are described by a small, human-readable file (`plugin.json`) instead of
  hardcoded C#.
- Every plugin side effect still flows through the existing
  `Command -> PermissionGate -> Executor -> Audit/Output` pipeline.
- A bad plugin can never break the host or another plugin.

## 1. Plugin Manifest Contract (E1)

File: `src/Plugins/PluginManifest.cs` (new)

A manifest is `plugin.json` sitting next to the entry script inside a trusted
folder. Fields:

| Field | Required | Meaning |
|---|---|---|
| `id` | yes | Stable plugin id (lowercase, no spaces) |
| `name` | yes | Display name |
| `version` | yes | Semver-ish (`1`, `1.2`, `1.2.3`, `1.2.3-beta.4`) |
| `author` | no | Author |
| `description` | no | Short description |
| `category` | no | Category grouping |
| `entry` | yes | Entry script/command, **relative to the manifest directory** |
| `entryType` | yes | One of `ps1`, `py`, `exe`, `external` |
| `requiredPermissions` | no | Permissions the plugin needs (validated against a known allow-list) |
| `hooks` | no | Pipeline hook kinds the plugin subscribes to |
| `minAppVersion` | no | Minimum app version |
| `trusted` | no | Signed / trusted flag |
| `homepage`, `license` | no | Metadata |

Parsing and validation live in `PluginManifestParser`:

- Missing required fields -> failure with a specific error.
- `version` must match a loose semver pattern.
- `requiredPermissions` must be in the known allow-list
  (`permFileWrite`, `permFileMoveDelete`, `permFileRead`, `permPluginRun`,
  `permProcessManage`, `permAutomationInput`, `permNetworkUpload`, `permComputerControl`).
- `hooks` must be in the known hook-kind list.
- **Path-traversal guard**: `entry` must resolve to the manifest's own directory
  or a descendant. `..\evil.ps1` is rejected even before any file is touched.
- `ToJson()` round-trips the model for safe re-serialization.

JSON is parsed with `System.Web.Script.Serialization.JavaScriptSerializer`
(`System.Web.Extensions`), matching the rest of the codebase. **Do not switch to
`System.Text.Json`** — it is not referenced by this project and will not compile.

## 2. Hook Framework (E2)

Files: `src/Agent/Hooks/{HookKind,HookContext,IPluginHook,HookRegistry}.cs` (new)

Hook kinds:

- `BeforeModelCall` / `AfterModelCall` — provider/model request boundaries.
- `BeforeCommand` / `AfterCommand` — command execution boundaries in `AgentPipeline`.
- `BeforeFileWrite` — before a real local file is written by an executor.

`IPluginHook` contract:

- `Invoke(HookContext)` is **synchronous and must be fast**.
- If a hook needs async work it should start its own `Task` and return.
- A throwing hook is isolated by `HookRegistry.Run` and recorded in `LastErrors`;
  it never aborts the command it observes.

`HookRegistry` is keyed by `HookKind`, supports `Register`, `Get`, `Count`, and a
synchronous, isolated `Run(kind, context)`.

### Planned integration points

| Hook kind | Where it fires | Status |
|---|---|---|
| `BeforeCommand` / `AfterCommand` | `AgentPipeline.Run` / `RunAsync` | Patch provided |
| `BeforeFileWrite` | `ExportFileExecutor` / `FolderOrganizer` / `PluginRunner` write paths | Define now, wire later |
| `BeforeModelCall` / `AfterModelCall` | `ProviderManager` request path | Define now, wire later |

Only `BeforeCommand` / `AfterCommand` are wired in the first patch. The other
kinds are defined so executors and the provider layer can adopt them without a
schema change.

## 3. Trusted Plugin Folders (E4)

Guidance for safe plugin operation:

1. Plugins run only from **trusted folders** (configured paths, not arbitrary
   locations). `Tools.PluginRunner.Validate` already rejects plugins outside the
   trusted folder and blocks `exe`/`bat` by default.
2. A plugin's `entry` is constrained to its own directory by the manifest
   path-traversal guard above.
3. A plugin's `requiredPermissions` are surfaced to the user via the existing
   `PermissionGate` + approval flow before execution. A plugin asking for
   `permProcessManage` or `permFileMoveDelete` should always require approval.
4. Signed / trusted plugins set `trusted: true` in the manifest; the UI may show
   a trust badge and skip a warning, but permission gating still applies.
5. Keep a trusted-folder allow-list in config (extend `Core.ConfigStore`), and
   reject any plugin whose manifest or entry escapes it.

## 4. MCP Compatibility (E3)

See `docs/MCP_RESEARCH_SPIKE.md`. **No MCP support is claimed.** The hook
framework is the integration seam a future MCP client would plug into; until a
real MCP client exists, the project must not advertise MCP compatibility.

## Acceptance (Epic E)

- A contributor can add one safe tool without editing the main form.
- A plugin is described by a manifest and validated before any code runs.
- A throwing hook never breaks the pipeline.
