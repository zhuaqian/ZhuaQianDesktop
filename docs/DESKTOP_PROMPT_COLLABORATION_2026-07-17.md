# Desktop Prompt Collaboration

Updated: 2026-07-17

## Purpose

ZhuaQian Desktop should keep prompt orchestration inside the Windows desktop app, not in a separate web UI.

The desktop Prompt Workbench connects:

- Prompt Registry
- Prompt Assembly
- Permission checks
- Local Memory
- Audit Log
- Current task attachments

## Entry Point

Open from the desktop app:

```text
Tools -> Prompt Workbench
```

The dialog can:

- Select a workflow assembly.
- Write and select local memory.
- Assemble a prompt from system policy, domain rules, workflow, memory, attachments, and the current user task.
- Check a proposed action against `PermissionGate`.
- Show recent `AuditLog` entries.
- Insert the assembled prompt back into the main chat input.

## Current Source Files

```text
src/Core/PromptLibrary.cs
src/ui/PromptWorkbenchDialog.cs
src/ZhuaQianDesktop.cs
```

Build list updates:

```text
src/build.ps1
src/ZhuaQianDesktop.csproj
```

## Desktop-Only Rule

Do not add a Node or browser server for this capability.

The desktop app already has:

- File attachment parsing
- Image/PDF inline parts
- `PermissionGate`
- `AuditLog`
- `OutputsHub`
- `AgentPipeline`
- Approval cards

Prompt collaboration should reuse these existing primitives.

## Collaboration Model

```text
User task
  -> Prompt Workbench
  -> PromptLibrary.Assemble
  -> PermissionGate.Check
  -> AuditLog.Log
  -> Main chat input
  -> Provider / Agent pipeline
```

## Permission Rules

Attaching files to the current desktop task is local and allowed when file-read permission is enabled.

The following still require approval:

- Uploading attachments
- Publishing content
- Sending emails/messages
- Writing files
- Moving/deleting files
- Running plugins
- Managing processes
- Sharing task packages over network

## Memory Rules

Prompt Workbench memory is stored under the app config directory:

```text
%APPDATA%\ZhuaQianDesktop\prompt-memory\
```

Do not store:

- API keys
- Passwords
- Browser cookies
- Wallets
- Private keys
- Sensitive personal identifiers unless the user explicitly chooses to

## Agent Collaboration

Recommended prompt roles:

| Role | Use |
| --- | --- |
| Supervisor | Route task and enforce completion criteria |
| Planner | Produce safe step-by-step plan |
| Builder | Implement or draft output |
| Reviewer | Review risks, tests, privacy, correctness |
| Researcher | Use public, official sources only |

## Workflow Assemblies

Initial built-in assemblies:

- `programmer_bugfix`
- `programmer_review`
- `office_summary`
- `office_meeting`
- `media_script`
- `media_calendar`

Future work can move these definitions from C# constants into a user-editable prompt registry file, but the first desktop version keeps them compiled for reliability.

## Verification

Run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\src\scripts\run-tests.ps1
```

If the transitional mirror is touched, also run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\work\zq-desktop\scripts\run-tests.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\work\zq-desktop\scripts\smoke-test.ps1
```
