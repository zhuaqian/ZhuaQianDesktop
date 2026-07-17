# Product Implementation Direction

Updated: 2026-07-16

This document captures the highest-value product direction for ZhuaQian Desktop after the current project handoff review.

## Core Bet

The best next product move is not to add more buttons. It is to turn ZhuaQian Desktop from a usable AI toolbox into a local, permission-aware desktop agent workbench.

The core experience should be:

```text
User goal
  -> AI-generated structured plan
  -> visible permission and risk review
  -> user approval
  -> controlled execution
  -> real local outputs
  -> audit and rollback record
  -> review
```

This creates a clear difference from ordinary chat products. The app should not merely answer; it should help users complete local office tasks while making side effects visible and controllable.

## North-Star Workflow

Example user request:

```text
Read the contracts in this folder, create an Excel risk summary, and organize the original files by customer name.
```

Target app behavior:

```text
Plan
1. Scan selected folder.
2. Extract contract text.
3. Identify risk items.
4. Generate Excel summary.
5. Propose an archive structure by customer.
6. Ask for approval before moving files.
```

Each step should have:

- action type,
- target paths,
- risk level,
- required permission,
- expected output,
- execution status,
- audit record,
- rollback information when possible.

## Product Pillars

### 1. Agent Plan Panel

Plan mode should produce a structured plan, not only free-form prose.

Each plan step should include:

- `stepId`
- `title`
- `actionType`
- `target`
- `riskLevel`
- `requiredPermission`
- `expectedOutput`
- `rollbackPossible`
- `status`

The plan panel should let the user inspect what the agent intends to do before any risky local action happens.

### 2. Approval And Review Loop

High-risk actions must be reviewed before execution:

- write files,
- move or rename files,
- run plugins or scripts,
- manage processes,
- upload local content to cloud providers,
- perform future desktop automation.

The user should be able to:

- approve all,
- approve one step,
- reject one step,
- edit plan inputs,
- cancel execution,
- review completed steps.

### 3. Unified Outputs Workbench

Outputs should become a real workbench, not just an export list.

It should track:

- Word, PowerPoint, Excel, Markdown, TXT files,
- logs,
- plugin outputs,
- folder organization manifests,
- rollback manifests,
- package exports.

Each output should know:

- `outputId`
- `taskId`
- `sourceActionId`
- `type`
- `path`
- `createdAt`
- `exists`
- `sizeBytes`
- `metadata`

Expected actions:

- open,
- locate,
- rename record,
- remove stale record,
- add to knowledge base,
- regenerate when possible.

### 4. Folder-Level Office Workflows

The strongest user-facing product surface is natural-language office work over local folders.

Priority workflows:

- read a folder of contracts and create an Excel risk register,
- summarize meeting notes into Word or Markdown,
- generate a PowerPoint draft from source documents,
- organize invoices by vendor and month,
- create a batch report from CSV/Excel files,
- package selected outputs for sharing,
- produce rollback records for file movement.

These workflows should feel like task completion, not isolated tools.

### 5. Main Form Reduction

The current `src/ZhuaQianDesktop.cs` remains too large. The product direction depends on reducing this file and moving orchestration into testable modules.

Extraction priorities:

1. natural-language local action parsing,
2. file-generation routing,
3. task state persistence,
4. provider call orchestration,
5. plan execution state,
6. output/action linking.

New work should avoid adding more business logic directly to `MainForm`.

## Target Architecture Flow

All real side-effect actions should continue moving toward this pipeline:

```text
AgentPlan
  -> IAgentCommand
  -> PermissionGate
  -> ApprovalCard / PlanPanel
  -> ICommandExecutor
  -> AuditLog
  -> OutputsHub
  -> ReviewState
```

This pipeline is the product. It is what turns an AI chat surface into a trustworthy local agent.

## Recommended MVP

Build the first complete vertical slice:

```text
Natural-language office request
  -> structured plan
  -> approval card
  -> export local file
  -> output record
  -> review state
```

Suggested first scenario:

```text
Generate a Word or Excel file from the current chat/task content.
```

Acceptance:

- user sees a structured plan before execution,
- write-file permission is checked,
- approval is visible,
- executor writes a real file,
- OutputsHub records it,
- audit log records the action,
- task review shows success or failure.

## Next Vertical Slice

After file generation works through the full loop, extend the same loop to:

```text
Read selected folder
  -> summarize documents
  -> generate Excel report
  -> propose folder organization
  -> approve file moves
  -> execute moves
  -> record rollback manifest
```

This is the first workflow that can demonstrate ZhuaQian as a real local office agent.

## Non-Goals For This Phase

Do not prioritize:

- more broad toolbar buttons,
- another loose evaluation document,
- enterprise security claims,
- MCP marketing before a real client exists,
- large new UI surfaces before the plan/approval/output loop works.

## Success Criteria

This direction is working when:

- a non-technical user can describe an office task in natural language,
- the app turns it into a visible plan,
- risky local effects wait for approval,
- execution creates real files or records real failures,
- outputs and rollback records are easy to inspect,
- another developer can extend the workflow by adding commands/executors instead of editing the main form.
