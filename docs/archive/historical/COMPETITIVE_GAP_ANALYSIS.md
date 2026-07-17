# ZhuaQian Desktop 竞品差距评估

> 完成度对齐：截至 2026-07-11，已实现/部分实现/未实现的权威表见 `CODE_COMPLETION_ALIGNMENT.md`。本文中早于该表的“下一步/缺口”描述，以该表和本文后续更新说明为准。

更新时间：2026-07-10

本文档评估 ZhuaQian Desktop 当前版本与 Cursor、Trae / CodeBuddy、VS Code、OpenCode、Claude Code、OpenAI Codex、Tencent WorkBuddy 的关键差距，并给出下一步产品与工程路线。

## 执行摘要

ZhuaQian 已经具备“轻型 Windows AI 工作台”的雏形：多模型、文件读取、真实导出、权限细分、Command Palette、Outputs、知识库 chunk、rollback manifest、云端上传确认。但与成熟竞品相比，差距集中在 6 个方面：

1. **Agent 状态管理不足**：已有基础多任务状态分组，但还没有类似 Claude Code Agent View / Codex app 的 review queue、后台任务监督和完整 Agent 状态机。
2. **执行闭环不足**：已有 rollback manifest，但还没有统一的 approval card、timeline、结果验收、失败重试、自动回滚策略。
3. **产物中心不足**：Outputs 只是导出历史列表，还不是 WorkBuddy 式“可验收工作成果物中心”。
4. **知识库不是语义 RAG**：已有 chunk，但没有 embedding、向量检索、重排、引用卡片。
5. **工程架构落后**：仍是单文件 WinForms，缺少模块化、测试体系和扩展 SDK。
6. **生态能力不足**：没有 Skills / MCP / 插件市场 / 自动化任务 / 手机端联动。

## 竞品关键趋势

### Cursor

Cursor 2026 年的方向不再只是 IDE 内补全，而是“统一 agent workspace”。Cursor 3 官方介绍强调 multi-repo layout、本地与云端 agents 无缝交接、从更高抽象层查看 agent 产出，同时仍可回到 IDE。

ZhuaQian 差距：

- 没有真正的多 agent workspace。
- 没有本地/云端任务交接。
- 没有 diff/review 级别的产物审查。
- 任务只是聊天记录，不是可监督的工作单元。

可追赶方向：

- 左侧任务按状态分组。
- 每个任务拥有 timeline、outputs、permissions、audit。
- 支持“本地执行 / 云端模型 / 本地模型”清晰标识。

### Trae / CodeBuddy

Trae / CodeBuddy 强调 Ask / Craft / Plan、自定义 agents、skills、MCP、custom models。它的优势是模式与能力边界清晰，用户知道 AI 是在问答、局部生成，还是规划大任务。

ZhuaQian 差距：

- 已有 Ask / Draft / Plan / Execute，但只是 prompt 注入，不是严格状态机。
- 没有自定义 Agent 管理。
- 没有 Skills/MCP 标准化扩展。
- 没有按职业/场景做可安装技能。

可追赶方向：

- 把当前 Skill Library 变成可编辑 JSON/文件夹技能。
- 增加 `skills/` 目录和 manifest。
- Plan 模式输出结构化 plan schema。

### VS Code

VS Code 的优势是成熟工作台信息架构：Activity Bar、Side Bar、Editor、Panel、Status Bar、Command Palette。功能很多但不乱，因为区域职责明确。

ZhuaQian 差距：

- 目前仍依赖弹窗：Tools、Outputs、Settings、Permissions。
- 没有右侧 Context / Files / Outputs / Knowledge / Audit tabs。
- Command Palette 只是最小版，没有快捷键、分类、权限状态、最近命令。
- 聊天消息没有 Copy / Save / Regenerate / Add to KB。

可追赶方向：

- 把弹窗逐步收敛进固定工作台布局。
- 建立 Activity Bar 或右侧 tab 面板。
- 给 Command Palette 加快捷键和命令元数据。

### OpenCode / opencode

OpenCode 的优势在于极简、TUI、session、attention、undo/share 等开发者工作流。OpenCode 文档提到 TUI 可对 questions、permissions、session errors、completed sessions 请求用户注意；社区 cheatsheet 也强调 `/undo`、`/redo`、`/share`、`/unshare`。

ZhuaQian 差距：

- 没有 attention/notification 机制。
- 没有 session share。
- 没有完整 undo/redo，只对文件整理有 rollback。
- 没有脚本化 CLI/TUI。

可追赶方向：

- 给任务加 `Needs input / Running / Done / Failed` 状态。
- 每个本地文件操作都生成 rollback action。
- 提供开发者 CLI 或插件 API。

### Claude Code

Claude Code 的 Agent View 官方文档强调把 `Ready for review`、`Needs input` 放在 `Working` 和 `Completed` 上方，方便监督多 session。CLI 文档则强调 start、pipe、resume 等终端工作流。

ZhuaQian 差距：

- 左侧任务列表已有基础状态分组；仍缺少成熟 Agent View。
- 没有 Ready for review / Needs input。
- 没有后台 session。
- 没有 resume/continue 的任务级语义。

可追赶方向：

- 当前任务对象增加 `status` 字段。
- 任务列表按状态分组。
- 计划审批、云端上传确认、权限请求都进入 `Needs input`。
- 导出完成、插件完成、批处理完成进入 `Ready for review`。

### OpenAI Codex

Codex app 官方介绍强调 desktop command center、background agents、Automations、Skills、review queue。Codex developer docs 也强调 Skills 可封装 instructions/resources/scripts，Automations 可按计划运行并把结果送进 inbox/review。

ZhuaQian 差距：

- 没有 Automations。
- 没有 Inbox / Review queue。
- 没有技能标准。
- 没有 cloud background task。
- 没有 GitHub/代码工作区级集成。

可追赶方向：

- 先做本地 scheduled task 基础结构。
- 输出结果进入 `Review` 面板。
- Skill Library 改成开放技能格式。
- 后续再接 GitHub 或本地 repo 工具。

### Tencent WorkBuddy

WorkBuddy 的公开流程非常明确：Brief it -> Confirm the plan -> Watch it work -> Get the result。腾讯云资料称执行在 sandbox 中显示 live progress，最终交付 files、charts、reports。App Store 页面强调覆盖行业、AI 顾问、技能插件、云上/本机双模式，通过对话完成工作产物。

ZhuaQian 差距：

- 没有沙盒执行环境。
- 没有实时执行进度面板。
- 没有真正的专家/技能生态。
- 没有行业模板。
- 没有手机扫码/跨端工作流。
- 产物中心不够成熟。

可追赶方向：

- Workflow 统一为：Brief -> Plan -> Approve -> Execute -> Output -> Review。
- Outputs 面板升级为工作成果中心。
- 插件必须 manifest + permissions。
- 做局域网手机上传作为跨端第一步。

## 功能差距矩阵

| 能力 | ZhuaQian 当前 | 竞品成熟度 | 差距等级 | 下一步 |
|---|---|---:|---:|---|
| 多任务状态视图 | 任务列表，无状态分组 | Claude/Codex/Cursor 强 | 高 | 增加 Needs input / Running / Review / Done / Failed |
| 模式切换 | Ask/Draft/Plan/Execute | Trae/CodeBuddy 强 | 中 | 从 prompt 变成状态机 |
| 命令面板 | 初版 Cmd | VS Code 强 | 中 | 快捷键、分类、权限状态、最近命令 |
| 产物中心 | Outputs 历史列表 | WorkBuddy 强 | 高 | 产物数据库、预览、重新生成、加入知识库 |
| 审批流程 | 权限弹窗 + 确认框 | WorkBuddy/Codex 强 | 高 | Approval Card + Timeline |
| Rollback | 文件整理支持 | OpenCode undo 更完整 | 中 | 所有文件副作用统一 undo/rollback |
| 云端上传安全 | 权限 + 摘要确认 | 部分竞品更细 | 中 | 临时允许、始终允许、内容摘要 diff |
| API Key 安全 | DPAPI 已补 | 合格 | 低 | 增加迁移/恢复提示 |
| 知识库 | chunk + keyword | RAG 产品更强 | 高 | embedding、hybrid search、引用卡片 |
| 插件系统 | 安全收窄但简陋 | Codex Skills / Trae MCP 强 | 高 | manifest、permissions、SDK |
| 自动化任务 | 无 | Codex Automations 强 | 高 | 本地 scheduler + review inbox |
| 跨端 | 无 | WorkBuddy 手机端强 | 高 | LAN upload + QR |
| 工程架构 | 单文件 | 竞品工程化强 | 极高 | 模块化、测试、CI 扩展 |

## 最大产品差距

### 1. ZhuaQian 已有第一版“任务状态”

目前任务已保存状态并按状态分组，但还不是完整 Agent View。竞品的 agent 产品都在走“任务状态化”：

- Claude Code：Needs input / Working / Completed / Ready for review
- Codex app：background agents + review queue
- Cursor 3：workspace 中查看 agent 产出与进度
- WorkBuddy：确认计划、沙盒执行、交付结果

ZhuaQian 下一步必须让任务状态可见：

```text
Needs input
- 等待云端上传确认
- 等待权限授权
- 等待计划审批

Running
- 正在索引文件夹
- 正在生成报告

Ready for review
- 生成了 PPT
- 文件整理完成，有 rollback manifest

Done
- 已确认完成

Failed
- 插件失败
- provider 报错
```

### 2. ZhuaQian 还没有“审批卡片”

当前确认主要是 MessageBox。竞品更接近工作流：

```text
Plan
Affected files
Permissions
Risks
Outputs

[Approve] [Edit] [Cancel]
```

建议把 Plan 模式输出转换成结构化审批卡，而不是只显示一段聊天文本。

### 3. ZhuaQian 的 Outputs 还不是“工作成果”

WorkBuddy 的价值是交付文件、图表、报告。ZhuaQian 虽然能生成文件，但 Outputs 只是历史记录。

建议 Outputs 升级：

- 类型图标
- 路径
- 文件是否存在
- 文件大小
- 关联任务
- 产生动作
- Open
- Reveal
- Add to KB
- Regenerate
- Rename
- Delete record
- Rollback

### 4. ZhuaQian 的插件还不是生态

Codex Skills、Trae Skills/MCP、WorkBuddy 技能插件都在走“可安装能力包”路线。

ZhuaQian 当前插件只是运行脚本，缺少：

- manifest
- 权限声明
- 输入 schema
- 输出 schema
- 版本号
- 作者
- 安全级别

建议插件格式：

```json
{
  "id": "excel.sales.analysis",
  "name": "Excel Sales Analysis",
  "version": "0.1.0",
  "entry": "main.py",
  "permissions": ["readFiles", "writeFiles"],
  "inputs": ["xlsx", "csv"],
  "outputs": ["md", "xlsx"],
  "description": "Analyze sales trend and produce report."
}
```

## 最大工程差距

### 1. 单文件是当前最危险瓶颈

竞品可以快速迭代，是因为 UI、provider、tools、state、permissions、storage 都有清晰边界。ZhuaQian 目前一个 `ZhuaQianDesktop.cs` 承担所有责任。

下个阶段不应继续大规模堆功能，而应拆：

- `PermissionGate`
- `AuditLog`
- `ConfigStore`
- `OfficeExporter`
- `KnowledgeIndex`
- `PluginRunner`
- `WorkflowActions`
- `MainForm`

### 2. 测试太少

当前 smoke test 只验证 Office 导出基础结构。还缺：

- 权限矩阵测试
- rollback manifest 测试
- cloud upload confirmation 测试
- config DPAPI migration 测试
- chunk search 测试
- plugin path validation 测试

### 3. 没有统一状态机

当前很多功能是按钮事件直连方法。竞品 agent 产品都在走状态机：

```text
Created -> Planning -> WaitingApproval -> Running -> ReadyForReview -> Completed / Failed / Cancelled
```

ZhuaQian 已引入任务状态字段与分组，下一步是把它升级为完整任务状态机。

## 与 WorkBuddy 的办公场景差距

WorkBuddy 的核心是“工作成果交付”。ZhuaQian 当前在办公场景缺：

- 行业模板
- 专家角色
- 沙盒执行
- 实时进度
- 手机端文件输入
- 多智能体协作
- 图表/报表模板
- 自动排版
- 文档预览

ZhuaQian 可以先做小而实用的路径：

1. `Outputs` 做成真正成果中心。
2. Word/PPT/Excel 导出增加模板。
3. Excel 分析结果能生成新 workbook。
4. 文件整理支持 rollback UI。
5. 手机扫码上传文件到桌面端。
6. 本地知识库引用可点击。

## 与 Codex / Cursor 的开发者场景差距

ZhuaQian 不是代码 IDE，但如果要吸引开发者开源参与，需要补：

- repo/workspace 概念
- diff preview
- file edit patch preview
- command run approval
- test result parser
- Git status
- plugin SDK
- skill format
- issue/PR templates

## 三阶段追赶路线

### 阶段 1：把原型变稳定

目标：减少安全风险，降低维护成本。

- 拆分单文件
- 提取 PermissionGate / ConfigStore / AuditLog / OfficeExporter / KnowledgeIndex
- 加单元测试
- 统一 ActionResult / AuditEvent / OutputRecord
- 完善 rollback 冲突处理

### 阶段 2：把聊天变工作流

目标：对齐 WorkBuddy / Codex 的 agent 工作台。

- 任务状态分组
- Approval Card
- Execution Timeline
- Ready for review
- Outputs 成果中心
- Context / Files / Knowledge / Audit 右侧 tabs

### 阶段 3：把工具变生态

目标：对齐 Codex Skills / Trae Skills / WorkBuddy 插件。

- Skill manifest
- Plugin manifest
- MCP 接入
- 本地 embedding RAG
- Automations
- LAN 手机上传
- 可发布插件市场雏形

## 当前最应该做的 10 件事

1. 把 `ZhuaQianDesktop.cs` 拆成 8-10 个类。
2. 给 PermissionGate 和 ConfigStore 写测试。
3. 给 rollback manifest 写测试和冲突处理。
4. 给扩展任务状态机与 Agent 状态联动。
5. 增强左侧任务状态分组。
6. 把 MessageBox 确认升级成 Approval Card。
7. Outputs 改为持久 `outputs.jsonl` 产物数据库。
8. Command Palette 增加快捷键和权限状态。
9. 本地知识库接 Ollama embedding。
10. 插件系统改成 manifest + permissions。

## 参考来源

- Cursor 官方产品页：https://cursor.com/
- Cursor 3 发布说明：https://cursor.com/blog/cursor-3
- Trae 官方文档：https://docs.trae.ai/
- VS Code UI 文档：https://code.visualstudio.com/docs/editing/userinterface
- OpenCode TUI 文档：https://opencode.ai/docs/tui/
- OpenCode CLI 文档：https://opencode.ai/docs/cli/
- Claude Code Agent View：https://code.claude.com/docs/en/agent-view
- Claude Code CLI：https://code.claude.com/docs/en/cli-reference
- OpenAI Codex app：https://openai.com/index/introducing-the-codex-app/
- Codex Skills：https://developers.openai.com/codex/skills
- Codex Automations：https://developers.openai.com/codex/app/automations
- Tencent WorkBuddy 海外指南：https://www.tencentcloud.com/techpedia/144100?lang=zh
- Tencent WorkBuddy App Store：https://apps.apple.com/cn/app/workbuddy-%E4%BD%A0%E7%9A%84-ai-%E5%B7%A5%E4%BD%9C%E5%8F%B0/id6761374913
- Tencent office AI Agent Suite：https://www.tencent.com/en-us/articles/2202350.html

