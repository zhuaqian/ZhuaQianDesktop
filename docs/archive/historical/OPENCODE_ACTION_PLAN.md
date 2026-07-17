# ZhuaQian Desktop × OpenCode：吸收与待执行文档

> 完成度对齐：截至 2026-07-11，已实现/部分实现/未实现的权威表见 `CODE_COMPLETION_ALIGNMENT.md`。本文中早于该表的“下一步/缺口”描述，以该表和本文后续更新说明为准。

> **目标**：系统分析 OpenCode（https://github.com/anomalyco/opencode）的架构、权限、Agent、Skill、命令等机制，提炼出 ZhuaQian Desktop 可直接借鉴的设计与实现任务清单。
>
> OpenCode 版本：v1.17.18（2026-07-09），160K+ GitHub Stars，900+ Contributors

---

## 目录

1. [OpenCode 核心架构概览](#1-opencode-核心架构概览)
2. [可直接吸收的设计模式](#2-可直接吸收的设计模式)
   - 2.1 权限系统
   - 2.2 Agent 体系
   - 2.3 Skills 技能系统
   - 2.4 自定义命令
   - 2.5 Session 管理
   - 2.6 Undo/Redo
   - 2.7 Attention 通知
   - 2.8 CLI 优先
3. [ZhuaQian 现状映射](#3-zhuaqian-现状映射)
4. [待执行任务清单](#4-待执行任务清单)
   - P0：权限系统改造
   - P1：Agent 体系构建
   - P1：命令系统升级
   - P2：Session 与 Undo
   - P2：Skills 技能系统
   - P2：CLI 接口
   - P3：Attention 与生态
5. [优先级与工作量估算](#5-优先级与工作量估算)
6. [总结：一句话行动建议](#6-总结)

---

## 1. OpenCode 核心架构概览

```
┌─────────────────────────────────────────────────────────────┐
│                    OpenCode Architecture                      │
├─────────────────────────────────────────────────────────────┤
│  CLI Layer (opencode run / TUI / web / serve / attach)       │
├─────────────────────────────────────────────────────────────┤
│  Agent Layer (build / plan / general / explore / scout)      │
│  ├── Primary Agents: Tab 切换                                │
│  └── Subagents: @name 调用 (general, explore, scout)         │
├─────────────────────────────────────────────────────────────┤
│  Tool Layer (12 built-in tools)                              │
│  ├── bash, read, write, edit, apply_patch                    │
│  ├── glob, grep, lsp                                         │
│  ├── webfetch, websearch, question                           │
│  ├── skill, todowrite, task                                  │
│  └── MCP Servers (任意外部工具)                              │
├─────────────────────────────────────────────────────────────┤
│  Permission Layer (allow / ask / deny + pattern match)       │
│  ├── 全局权限 + 每 Agent 覆盖                                │
│  ├── 粒度: * → ask, "git *" → allow                          │
│  └── external_directory 限制外部路径                         │
├─────────────────────────────────────────────────────────────┤
│  Skills Layer (SKILL.md 声明式技能)                          │
│  ├── 位置: .opencode/skills/<name>/SKILL.md                 │
│  ├── frontmatter: name, description, license, metadata       │
│  └── 按需加载，权限控制                                      │
├─────────────────────────────────────────────────────────────┤
│  Config Layer (opencode.jsonc + tui.jsonc)                   │
│  ├── Provider, Model, Agent, Permission, Command             │
│  ├── Theme, Keybind, Formatter, LSP, Reference               │
│  └── Multi-surface: TUI / Desktop / IDE / Web                │
├─────────────────────────────────────────────────────────────┤
│  Storage Layer (sessions, snapshots, auth)                   │
│  ├── 本地存储，不传云端                                       │
│  ├── Session 可分享 (share link)                             │
│  └── Auth: 75+ providers via Models.dev                      │
└─────────────────────────────────────────────────────────────┘
```

---

## 2. 可直接吸收的设计模式

### 2.1 权限系统 ← **最高优先级**

OpenCode 的权限模型是 ZhuaQian 权限改造的最佳参考。

**OpenCode 的做法**：
- 每个 tool 独立权限：`"edit": "deny"`, `"bash": "ask"`, `"read": "allow"`
- **pattern matching** 机制：`"bash": { "*": "ask", "git status *": "allow", "rm *": "deny" }`
- 三层值：`allow`（自动执行） / `ask`（询问用户） / `deny`（拒绝）
- **`--auto` 模式**：自动批准未显式 deny 的请求
- `external_directory`：限制对外部路径的访问
- Agent 级别覆盖：每个 agent 可覆盖全局权限
- `.env` 文件默认 deny read

**ZhuaQian 当前状态**：
- ✅ Power 总开关（On/Off）
- ✅ 8 种布尔权限（permFileRead/Write/MoveDelete/ProcessManage/PluginRun/Screenshot/Clipboard/NetworkUpload）
- ❌ 没有 allow/ask/deny 三层模型
- ❌ 没有 pattern matching（如"允许 git 命令，拒绝 rm"）
- ❌ 没有 auto 模式
- ❌ 没有 external_directory 限制
- ❌ 权限拒绝时没有"临时允许/始终允许/拒绝"的选择体验

**吸收方案**：把 `PermissionGate` 从布尔值升级为三层 + pattern matching。

### 2.2 Agent 体系

OpenCode 的 Agent 设计清晰且可扩展：

**Agent 类型**：
| Agent | Type | 权限 | 用途 |
|-------|------|------|------|
| build | primary | 全开 | 默认开发 agent |
| plan | primary | edit=deny, bash=ask | 只读分析 |
| general | subagent | 全开（无 todo） | 复杂搜索和多步骤任务 |
| explore | subagent | 只读 | 快速代码探索 |
| scout | subagent | 只读 | 外部文档和依赖研究 |

**ZhuaQian 当前**：
- ✅ 已有 Ask / Draft / Plan / Execute 模式（但只是 prompt 注入）
- ❌ 不是真正的 Agent 状态机
- ❌ 没有权限级别的 Agent 定义
- ❌ 没有 subagent / @mention 机制

**吸收方案**：将当前模式从 prompt 注入升级为 Agent 状态机。

### 2.3 Skills 技能系统

**OpenCode SKILL.md 格式**：
```yaml
---
name: git-release
description: Create consistent releases and changelogs
license: MIT
compatibility: opencode
metadata:
  audience: maintainers
  workflow: github
---
## What I do
- Draft release notes from merged PRs
- Propose a version bump
```

**发现机制**：`.opencode/skills/<name>/SKILL.md` + 全局 `~/.config/opencode/skills/`
**权限控制**：`"permission": { "skill": { "internal-*": "deny" } }`

**ZhuaQian 当前**：
- ✅ 已有 Skill Library（6 种技能按钮）
- ❌ 技能是硬编码的 C# 字符串，不是文件系统技能
- ❌ 没有 manifest/description/name
- ❌ 没有权限控制

**吸收方案**：把技能从硬编码按钮改为 SKILL.md 文件系统 + 按需加载。

### 2.4 自定义命令

**OpenCode 命令格式**：
```yaml
---
description: Run tests with coverage
agent: build
model: anthropic/claude-3-5-sonnet-20241022
---
Run the full test suite with coverage report.
```

支持 `$ARGUMENTS` 占位符、`!command` shell 注入、`@file` 文件引用。

**ZhuaQian 当前**：
- ✅ 已有 Command Palette + BuildCommands()
- ❌ 命令是硬编码 C# 列表，不是声明式文件
- ❌ 没有 `/` 快捷键前缀
- ❌ 没有模板/参数/描述

**吸收方案**：把 Command Palette 改为文件系统命令 + 模板引擎。

### 2.5 Session 管理

**OpenCode**：
- `/sessions` 列出所有 session
- `/continue` / `--session` 恢复历史对话
- `/share` 生成分享链接
- `/export` 导出为 Markdown
- 会话压缩 `/compact` 自动处理长上下文

**ZhuaQian 当前**：
- ✅ 任务持久化（保存到 `%APPDATA%\ZhuaQianDesktop\tasks\`）
- ❌ 没有 session 恢复/继续语义
- ❌ 没有分享机制
- ❌ 没有自动压缩

**吸收方案**：给任务增加 resume/continue 元数据。

### 2.6 Undo/Redo

**OpenCode**：通过 Git 实现 `/undo` 和 `/redo`，撤销最近消息和文件修改。

**ZhuaQian 当前**：
- ✅ 文件整理有 rollback manifest
- ❌ 没有通用 undo
- ❌ 没有消息级撤销

**吸收方案**：扩展 rollback 机制覆盖所有本地副作用。

### 2.7 Attention 通知

**OpenCode TUI** 支持桌面通知和声音提示：
- `question` — 提问时通知
- `permission` — 请求权限时通知
- `error` — 错误时通知
- `done` — 任务完成时通知
- `subagent_done` — 子 agent 完成时通知

**ZhuaQian 当前**：无通知机制。

**吸收方案**：利用 Windows 原生通知（`NotifyIcon` + `BalloonTip`）。

### 2.8 CLI 优先

**OpenCode CLI 命令**：
```
opencode                          # 启动 TUI
opencode run "query"              # 非交互运行
opencode run --file file.txt "query"  # 带文件
opencode serve                    # HTTP API 服务
opencode attach http://...        # 远程 TUI
opencode auth login               # Provider 配置
opencode models                   # 列出模型
opencode stats                    # Token 使用统计
```

**ZhuaQian 当前**：纯 GUI，无 CLI。

**吸收方案**：增加最小 CLI 接口（如 `ZhuaQianDesktop.exe /run "query"`）。

---

## 3. ZhuaQian 现状映射

| OpenCode 能力 | ZhuaQian 当前 | 差距 | 优先级 |
|:-------------|:-------------|:----:|:------:|
| **权限三层模型** (allow/ask/deny) | 布尔值 Power + 8 perms | **设计级差距** | **P0** |
| **权限 pattern matching** | 无 | 完全缺失 | P0 |
| **Agent 体系** (build/plan/explore) | 4 个模式（Ask/Draft/Plan/Execute） | 模式是 prompt 注入，不是状态机 | P1 |
| **Subagent** (@general/@explore) | 无 | 完全缺失 | P2 |
| **Skills 文件系统** | 6 个硬编码技能按钮 | 不是声明式文件 | P1 |
| **自定义命令** | 硬编码 Command Palette | 不是文件系统命令 | P1 |
| **Session 管理** | 任务列表 + 文件持久化 | 无 resume/share | P2 |
| **Undo/Redo** | 仅有文件整理的 rollback | 不完全 | P1 |
| **Attention 通知** | 无 | 完全缺失 | P2 |
| **CLI 接口** | 无 | 完全缺失 | P2 |
| **MCP 协议** | 无 | 完全缺失 | P3 |
| **Share link** | 无 | 完全缺失 | P3 |
| **Streaming** | 无（同步阻塞） | 完全缺失 | P1 |
| **多模型列表** | 3 个硬编码 | 不是动态获取 | P2 |
| **文件 @ 引用** | 无 | 完全缺失 | P2 |
| **Multi-surface** | 仅 WinForms exe | 无 Web/CLI | P3 |

---

## 4. 待执行任务清单

### P0：权限系统改造（预估：3-4 天）

#### P0.1 权限模型从布尔值 → 三层值（1 天）

**目标**：把 `permFileRead` / `permFileWrite` 等 8 个布尔字段改为带 `allow/ask/deny` 三层的权限系统。

**参考代码**（`ZhuaQianDesktop.cs:L649-L662`）：
```csharp
// 当前
bool permFileRead = true;
bool permFileWrite = true;

// 目标
enum PermissionLevel { Allow, Ask, Deny }
PermissionLevel permFileRead = PermissionLevel.Allow;
PermissionLevel permFileWrite = PermissionLevel.Allow;
```

**实现步骤**：
1. 定义 `PermissionLevel` 枚举
2. 把 8 个 `bool perm*` 改为 `PermissionLevel`
3. 修改 `EnsurePermission()`：`Allow` 直接过，`Ask` 弹确认框，`Deny` 拒绝
4. 修改 `ShowPermissionSettings()` 和 `SaveConfig()` / `LoadConfig()`
5. **单元测试**：验证三层所有组合

#### P0.2 实现 pattern matching 权限（1.5 天）

**目标**：借鉴 OpenCode 的 `"bash": { "git *": "allow", "rm *": "deny" }` 语法。

**具体方案**：
- 新增 `PermissionPattern` 类：`string Pattern` + `PermissionLevel Level`
- 为 `bash` / `plugin` 等工具增加 pattern 列表
- 实现模式匹配引擎（支持 `*` 通配符）
- 在插件执行、文件整理、进程管理等入口应用模式匹配

**配置存储**（`config.json` 扩展）：
```json
{
  "permissionPatterns": {
    "plugin": [
      { "pattern": "backup-*", "level": "allow" },
      { "pattern": "*", "level": "ask" }
    ],
    "process": [
      { "pattern": "notepad*", "level": "allow" },
      { "pattern": "*", "level": "deny" }
    ]
  }
}
```

**单元测试**：pattern 匹配逻辑、覆盖顺序、fallthrough。

#### P0.3 实现 "temp allow / always allow / deny" 选择体验（0.5 天）

**目标**：把当前简单的 MessageBox OK/Cancel 升级为三选一体验。

**参考**：OpenCode `once / always / reject`

**UI**：
```
┌──────────────────────────────────┐
│  Allow this action?               │
│                                   │
│  Plugin: backup-reports.py        │
│  Preview: ...                     │
│                                   │
│  [Once]  [Always for this]  [Deny]│
└──────────────────────────────────┘
```

#### P0.4 增加 auto 模式（0.5 天）

**目标**：类似 OpenCode `--auto`，自动批准未 deny 的请求。

**实现**：`EnableAutoMode` 开关，打开后所有 `Ask` 级别自动转为 `Allow`（`Deny` 仍拒绝）。

#### P0.5 增加 external_directory 概念（0.5 天）

**目标**：当工具访问项目目录之外的文件时，默认 ask。

**实现**：在 `EnsurePermission` 中检查路径是否在许可范围内。

---

### P1：Agent 体系构建（预估：4-5 天）

#### P1.1 Agent 定义接口与基类（1 天）

**目标**：把当前 mode（Ask/Draft/Plan/Execute）从字符串 + prompt 注入改为 Agent 对象。

```csharp
class AgentDef {
    string Id;              // "ask" / "draft" / "plan" / "execute"
    string DisplayName;     // "Ask" / "Draft" / "Plan" / "Execute"
    string SystemPrompt;    // mode instruction
    bool DenyEdits;
    bool DenyLocalActions;
    bool RequireApprovalForBash;
    // ...
}
```

#### P1.2 Agent 状态机（1.5 天）

**目标**：给扩展任务状态机与 Agent 状态联动，Agent 切换时联动。

**状态迁移**：
```
Created → NeedsInput → Running → ReadyForReview → Completed / Failed / Cancelled
```

**实现**：
- `TaskInfo.Status` 字段（string enum）
- 增强左侧任务状态分组（Needs input / Running / Ready for review / Done / Failed）
- 状态切换时更新 UI

#### P1.3 Approval Card（1.5 天）

**目标**：Plan 模式和 Execute 模式下，Agent 输出结构化 plan → 审批卡。

**UI**：
```
┌──────────────────────────────────┐
│  Plan                             │
│                                   │
│  Step 1: Read folder ...          │
│  Step 2: Analyze Excel ...        │
│  Step 3: Generate report.docx     │
│                                   │
│  Required: permFileRead           │
│  Affected: C:\reports\...         │
│                                   │
│  [Approve]  [Edit Plan]  [Cancel] │
└──────────────────────────────────┘
```

#### P1.4 @mention 子 agent 入口（1 天）

**目标**：在输入框支持 `@general`、`@explore` 等 @mention 调用于 agent。

**实现**：
- 解析输入框中的 `@name` 模式
- 匹配到子 agent 后切换上下文
- 结果返回主对话

---

### P1：命令系统升级（预估：2 天）

#### P1.1 命令文件系统（1 天）

**目标**：把 `BuildCommands()` 硬编码命令改为文件系统声明式命令。

**参考**：OpenCode `.opencode/commands/*.md`

**ZhuaQian 实现**：
- 定义 `commands/` 目录（`%APPDATA%\ZhuaQianDesktop\commands\`）
- 每个命令一个 JSON 文件：

```json
{
  "id": "export-chat",
  "description": "Export current chat as Markdown",
  "template": "Export the current conversation as a Markdown file.",
  "permissions": ["permFileWrite"],
  "args": []
}
```

- `ShowCommandPalette()` 改为扫描 `commands/` 目录
- 支持搜索中文别名

#### P1.2 命令参数与模板引擎（0.5 天）

**目标**：支持 `$ARGUMENTS` 和 `$1`、`$2` 等占位符。

#### P1.3 快捷键绑定（0.5 天）

**目标**：`Ctrl+K` / `Ctrl+Shift+P` 打开命令面板，支持 `/` 前缀快速输入。

---

### P2：Session 与 Undo（预估：3 天）

#### P2.1 任务元数据扩展（0.5 天）

**目标**：给 `TaskInfo` 增加 `Status`、`ParentTaskId`、`ModelUsed`、`TokenCount`、`CompactSummary` 字段。

#### P2.2 通用 Undo/Redo（1.5 天）

**目标**：从仅文件整理 rollback 扩展为所有本地副作用的通用 undo。

**实现**：
- 统一的 `ActionRecord` 类（actionId, type, before, after, timestamp）
- 全局 `undoStack` / `redoStack`
- 支持撤销：文件写入、文件移动、文件删除、进程结束（记录 PID+name）、插件执行
- UI：`Cmd -> Undo` / `Cmd -> Redo`

#### P2.3 Session 导出/恢复（1 天）

**目标**：支持导出任务为 JSON/HTML 分享格式，支持导入恢复。

---

### P2：Skills 技能系统（预估：2 天）

#### P2.1 SKILL.md 文件系统（1 天）

**目标**：把当前 6 个硬编码技能按钮改为 SKILL.md 文件系统。

**位置**：`%APPDATA%\ZhuaQianDesktop\skills\<name>\SKILL.md`

**格式**：
```markdown
---
name: finance-reconcile
description: Finance reconciliation assistant
permissions: [permFileRead, permFileWrite]
---
You are a finance reconciliation assistant. Compare records, find mismatches, missing invoices, duplicate payments, and output an exception table plus next actions.
```

#### P2.2 技能加载与权限控制（0.5 天）

**目标**：`skill` tool 加载 SKILL.md，受 PermissionGate 控制。

#### P2.3 技能按钮自动发现（0.5 天）

**目标**：`ShowSkillLibrary()` 自动扫描 `skills/` 目录生成按钮，不再硬编码。

---

### P2：CLI 接口（预估：2 天）

#### P2.1 最小 CLI（1 天）

**目标**：支持命令行参数：

```
ZhuaQianDesktop.exe /run "分析这个文件"
ZhuaQianDesktop.exe /file report.xlsx "做销售分析"
ZhuaQianDesktop.exe /screenshot "识别这段文字"
ZhuaQianDesktop.exe /quiet      # 无 UI，返回结果到 stdout
```

#### P2.2 --auto 参数（0.5 天）

**目标**：`/auto` 自动批准权限。

#### P2.3 Provider 命令行配置（0.5 天）

**目标**：`/connect-gemini KEY`、`/connect-openrouter KEY` 快速配置。

---

### P3：Attention 与生态（预估：3 天）

#### P3.1 Windows 原生通知（1 天）

**目标**：使用 `NotifyIcon` + `BalloonTip` 实现：
- 任务完成通知
- 权限请求通知
- 错误通知
- 子任务完成通知

#### P3.2 MCP 协议接入（2 天）

**目标**：实现最小 MCP 客户端，支持 local MCP server。

**参考**：OpenCode MCP server 格式
```json
{
  "mcp": {
    "my-tool": {
      "type": "local",
      "command": ["npx", "-y", "@modelcontextprotocol/server-everything"]
    }
  }
}
```

---

## 5. 优先级与工作量估算

```
P0 ─────────────────────────────────────────────
 权限三层模型            ████████░░  1.0d  ████
 Pattern matching        ████████████ 1.5d  ██████
 Temp/always/deny UI     ████░░░░░░  0.5d  ██
 Auto mode               ████░░░░░░  0.5d  ██
 External directory       ████░░░░░░  0.5d  ██
                         ───────────
                         合计 4.0 天

P1 ─────────────────────────────────────────────
 Agent 定义/基类          ████████░░  1.0d  ████
 Agent 状态机             ████████████ 1.5d  ██████
 Approval Card            ████████████ 1.5d  ██████
 @mention 子 agent        ████████░░  1.0d  ████
 命令文件系统             ████████░░  1.0d  ████
 命令模板/参数            ████░░░░░░  0.5d  ██
 快捷键绑定               ████░░░░░░  0.5d  ██
                         ───────────
                         合计 7.0 天

P2 ─────────────────────────────────────────────
 通用 Undo/Redo           ████████████ 1.5d  ██████
 Session 元数据扩展        ████░░░░░░  0.5d  ██
 Skills 文件系统          ████████░░  1.0d  ████
 技能权限控制             ████░░░░░░  0.5d  ██
 CLI 接口                ████████░░  1.0d  ████
 CLI --auto/connect      ████░░░░░░  0.5d  ██
                         ───────────
                         合计 5.5 天

P3 ─────────────────────────────────────────────
 原生通知                 ████████░░  1.0d  ████
 MCP 协议接入             ████████████ 2.0d  ████████
                         ───────────
                         合计 3.0 天
────────────────────────────────────────────────
 总计                    ≈ 19.5 天
```

### 依赖顺序

```
P0 权限改造 ──→ P1 Agent 体系 ──→ P2 Undo/Session ──→ P3 Attention
                                       │
                                       ├── P2 Skills ──→ P3 MCP
                                       │
                                       └── P2 CLI ──→ P3 生态
```

**关键依赖**：
- **P0.1（权限三层）** 是 **所有后续任务的前提** → 第一个做
- **P0.2（pattern matching）** 可独立于 P0.3/P0.4 并行
- **P1.1（Agent 定义）** 依赖 P0.1（权限模型）
- **P2.1（Undo）** 独立，可与 P1 并行
- **P2.3（Skills）** 独立，可与 P1 并行

---

## 6. 总结

### OpenCode 最值得 ZhuaQian 借鉴的 5 个设计

| # | 设计 | 优先级 | 为什么重要 |
|---|------|:------:|-----------|
| 1 | **三层权限模型** (allow/ask/deny) | P0 | 当前布尔值权限太粗糙，无法表达"允许大多数、拒绝少数、询问不确定" |
| 2 | **Pattern matching** | P0 | 让权限不只是一刀切，可以精细控制（如"允许 git 命令但拒绝 rm"） |
| 3 | **Agent 状态机** | P1 | 把 Ask/Draft/Plan/Execute 从 prompt 注入变成真正的状态控制 |
| 4 | **文件系统 Skills** | P1 | 技能不再硬编码，用户可自行添加/分享，生态可扩展 |
| 5 | **命令文件系统** | P1 | Command Palette 不再是硬编码 C# 列表，而是可编辑的命令文件 |

### 一句话行动建议

> **先抄 OpenCode 的权限模型（allow/ask/deny + pattern matching），再抄 Agent 状态机，然后按文件系统把技能和命令开放给用户。这三件事做完，ZhuaQian 就从"功能堆叠原型"变成了"可扩展平台"。**

---

*本文档基于对 OpenCode v1.17.18 官方文档（opencode.ai/docs）的完整阅读，以及 ZhuaQian Desktop `DEEP_EVALUATION.md` 的评估结果生成。*
