# ZhuaQian Desktop 下一步执行文档

> 完成度对齐：截至 2026-07-11，已实现/部分实现/未实现的权威表见 `CODE_COMPLETION_ALIGNMENT.md`。本文中早于该表的“下一步/缺口”描述，以该表和本文后续更新说明为准。

更新时间：2026-07-11

本文档基于以下已读文档整理：

- `outputs/NEXT_SUPER_AGENT_BRIEF.md`
- `outputs/CURRENT_GAPS_ASSESSMENT.md`
- `outputs/COMPETITIVE_GAP_ANALYSIS.md`
- `outputs/UI_COMPETITOR_RESEARCH.md`

目标：把当前原型从“功能密集的单文件 WinForms 工具”推进为“可维护、可测试、可扩展、可开源协作的轻型 Windows AI 工作台”。

## 当前判断

ZhuaQian Desktop 现在已经不是空壳：

- 能聊天
- 能接 Gemini / OpenRouter / Local
- 能上传和读取多种文件
- 能截图 OCR
- 能剪贴板监控
- 能真实生成 TXT / MD / Word / PPT / Excel
- 能索引本地知识库 chunk
- 有 Ask / Draft / Plan / Execute 模式
- 有 Cmd 命令面板
- 有 Outputs 面板
- 有 Power 总开关与细分权限
- API Key 已用 Windows DPAPI 保护
- 文件整理已有 rollback manifest 与回滚入口
- 云端上传已有权限和摘要确认

但它仍然有明显瓶颈：

- 单文件源码过大
- 任务已有第一版状态字段与分组，但还没有完整工作流状态机
- Plan / Execute 还是 prompt 约束，不是工作流状态机
- Outputs 还不是真正成果中心
- 知识库没有 embedding
- 插件不是生态
- 测试很少

下一步不要继续随意加按钮。要先做“状态、产物、架构、测试”。

## 阶段 0.1.4：任务状态化与执行闭环

目标：对齐 Claude Code Agent View、Codex review queue、WorkBuddy plan/execute/result 流程。

### 1. 给 Task 增加状态字段

新增字段：

```text
status:
- draft
- needs_input
- planning
- running
- ready_for_review
- completed
- failed
```

任务 JSON 中保存：

```json
{
  "id": "...",
  "title": "...",
  "status": "ready_for_review",
  "lastAction": "Exported PPT",
  "lastOutputPath": "...",
  "updatedAt": "..."
}
```

UI 要求：

- 增强左侧任务状态分组。
- 状态可用颜色区分。
- 有待确认的任务排在最上面。

验收标准：

- 新任务默认 `draft`。
- 触发云端上传确认、权限请求、计划审批时进入 `needs_input`。
- 文件导出成功进入 `ready_for_review`。
- Provider 报错进入 `failed`。

### 2. 建立统一 ActionRecord

当前副作用散落在各方法里。下一步需要统一动作记录。

建议结构：

```json
{
  "actionId": "...",
  "taskId": "...",
  "type": "export_file|organize_folder|rollback|run_plugin|end_process|cloud_upload",
  "status": "planned|approved|running|succeeded|failed|cancelled",
  "requestedAt": "...",
  "approvedAt": "...",
  "completedAt": "...",
  "permissions": ["writeFiles"],
  "affectedPaths": [],
  "outputIds": [],
  "rollbackManifest": "",
  "error": ""
}
```

文件位置：

```text
%APPDATA%\ZhuaQianDesktop\actions.jsonl
```

验收标准：

- Save File 写 action。
- Organize Folder 写 action。
- Rollback 写 action。
- Run Plugin 写 action。
- Cloud Upload confirmation 写 action。

### 3. 第一版 Approval Card

当前确认用 MessageBox，体验粗糙。先做一个通用审批窗体。

审批卡字段：

- 标题
- 模式
- 需要权限
- 影响文件
- 风险
- 输出
- 审计说明

按钮：

- Approve
- Edit
- Cancel

先替换这些场景：

- 云端上传摘要
- 文件整理确认
- 插件运行确认

验收标准：

- MessageBox 不再承载复杂确认内容。
- 用户可复制审批内容。
- 批准结果写入 action log。

## 阶段 0.1.5：Outputs 成果中心升级

目标：对齐 WorkBuddy 的“交付成果”与 Codex 的 review output。

### 1. 从 export-history 改为 outputs.jsonl

当前：

```text
export-history.jsonl
```

新建：

```text
outputs.jsonl
```

结构：

```json
{
  "outputId": "...",
  "taskId": "...",
  "taskTitle": "...",
  "type": "txt|md|docx|pptx|xlsx|eml|rollback|plugin|folder|log",
  "path": "...",
  "createdAt": "...",
  "sourceActionId": "...",
  "exists": true,
  "sizeBytes": 12345,
  "metadata": {}
}
```

兼容：

- 继续读取旧 `export-history.jsonl`。
- 新产物写入 `outputs.jsonl`。

### 2. Outputs 面板增强

新增字段显示：

- 类型
- 文件名
- 所属任务
- 大小
- 是否存在
- 创建时间
- 来源动作

按钮：

- Open
- Reveal
- Add to KB
- Rename record
- Delete record
- Rollback（仅 rollback 类型）

验收标准：

- 文件不存在时显示灰色。
- 可清理失效记录。
- rollback 类型可直接触发回滚。

## 阶段 0.1.6：源码拆分第一刀

目标：降低维护风险，不做大重构幻觉。

### 拆分原则

先拆“无 UI、低耦合、容易测试”的类。

第一批：

```text
src/Core/ConfigStore.cs
src/Core/AuditLog.cs
src/Core/PermissionGate.cs
src/Documents/OfficeExporter.cs
src/Documents/Redactor.cs
src/Knowledge/Chunker.cs
src/Tools/FolderOrganizer.cs
```

保留：

```text
MainForm.cs
```

不要一口气重写 UI。

### 验收标准

- `build.ps1` 能编译多个 `.cs` 文件。
- MainForm 只调用类，不再直接包含所有逻辑。
- smoke test 仍通过。

## 阶段 0.1.7：测试体系

目标：从“只靠人工点”进入基础工程化。

### 第一批测试对象

- `Redactor`
- `Chunker`
- `OfficeExporter`
- `PermissionGate`
- `ConfigStore`
- `FolderOrganizer`

### 最小测试方案

如果继续使用 .NET Framework + csc，可先写 Powershell smoke/unit hybrid：

```text
scripts/test-redactor.ps1
scripts/test-chunker.ps1
scripts/test-office-exporter.ps1
scripts/test-permissions.ps1
scripts/test-folder-organizer.ps1
```

后续再迁移到正式测试框架。

验收标准：

- GitHub Actions 运行 build + smoke + tests。
- 测试失败阻止 artifact 发布。

## 阶段 0.2：知识库 RAG

目标：把当前 keyword chunk search 升级到本地语义检索。

### 1. Embedding

优先 Ollama：

```text
http://localhost:11434/api/embeddings
model: nomic-embed-text
```

每个 chunk 保存：

```json
{
  "chunkId": "...",
  "embeddingModel": "nomic-embed-text",
  "embedding": [0.01, ...]
}
```

### 2. 存储

第一版可以 JSONL：

```text
knowledge-vectors.jsonl
```

后续迁 SQLite。

### 3. 检索策略

Hybrid：

- keyword score
- vector cosine score
- layer bonus
- modified time bonus

输出必须包含：

- chunkId
- path
- heading
- snippet
- score

## 阶段 0.2：技能与插件生态

目标：从“运行脚本”变成“可控能力包”。

### Skill manifest

```json
{
  "id": "finance.reconcile",
  "name": "Finance Reconcile",
  "version": "0.1.0",
  "description": "Compare two Excel/CSV files and generate reconciliation report.",
  "entry": "main.py",
  "permissions": ["readFiles", "writeFiles"],
  "inputs": ["xlsx", "csv"],
  "outputs": ["md", "xlsx"]
}
```

### UI

- Skill Library 读取 `skills/` 文件夹
- 显示技能列表
- 点击技能显示权限
- 执行前走 Approval Card

## 阶段 0.3：Windows 监工智能体与外挂监测

目标：把当前一次性的 `Resource Monitor` 升级为可审计、可暂停、可复核的 Windows 本地安全监控方向。详细方案见 `WINDOWS_AGENT_MONITORING_FEASIBILITY.md`。

### 当前边界

当前代码还没有真正的外挂监测：

- `Resource Monitor` 只是枚举进程并允许确认后结束 PID。
- `Agent Planner` 只是生成安全计划 prompt。
- `audit.log` 和 `actions.jsonl` 可以作为审计基础，但还不是监控事件库。

### 第一批任务

1. 新增 `monitoring-events.jsonl` 和 `monitoring-cases.jsonl` 数据格式。
2. 从 `ShowResourceMonitor()` 拆出 `ProcessSnapshotCollector`。
3. 增加只读进程快照记录，不做阻断。
4. 在权限模型中预留 `Security monitoring` / `Monitor processes`。
5. 新增 `Monitoring` 报告面板，显示 Agent 状态、最近事件、待复核 case。

### 后续任务

- 用户态 Windows Monitoring Agent。
- Agent 心跳与悬挂检测。
- 进程、模块、窗口、目标目录文件变更采集。
- 外挂监测智能体做风险解释和证据摘要。
- 监工智能体做告警归并、人工复核和策略回流。

短期不要做内核驱动、隐蔽监控、自动封禁，也不要承诺百分百识别外挂。

## 不要做的事

短期不要做：

- 不要继续堆新按钮。
- 不要重写成 Electron。
- 不要把 Llama 模型打包进 exe。
- 不要默认打开危险权限。
- 不要让模型直接声称完成本地动作。
- 不要一次性大重构 UI。

## 最优先的 5 个 PR

### PR 1：Task status

- 扩展 TaskInfo 状态元数据
- task JSON 继续保存并兼容状态元数据
- 增强左侧任务按 status 分组

### PR 2：ActionRecord + Approval Card

- 扩展现有 actions.jsonl
- 新增审批窗体
- 接入云端上传、文件整理、插件执行

### PR 3：Outputs v2

- 新增 outputs.jsonl
- 输出记录统一化
- Outputs 面板增强

### PR 4：Core extraction

- 抽 ConfigStore / AuditLog / Redactor / Chunker
- build.ps1 支持多文件

### PR 5：Tests

- 增加 scripts/test-*.ps1
- GitHub Actions 加测试步骤

## 下一位开发者开工顺序

1. 先不要改 UI。
2. 先读 `ZhuaQianDesktop.cs` 中 task 保存/加载逻辑。
3. 给 `TaskInfo` 加 status。
4. 让任务 JSON 兼容旧数据。
5. 左侧任务列表先用文本分组，不追求漂亮。
6. 编译。
7. 跑 smoke test。
8. 更新 README / CHANGELOG / CURRENT_GAPS_ASSESSMENT。

## 当前最新产物路径

```text
outputs/ZhuaQianDesktop.exe
outputs/ZhuaQianDesktop-open-source.zip
outputs/NEXT_SUPER_AGENT_BRIEF.md
outputs/CURRENT_GAPS_ASSESSMENT.md
outputs/COMPETITIVE_GAP_ANALYSIS.md
outputs/UI_COMPETITOR_RESEARCH.md
```
