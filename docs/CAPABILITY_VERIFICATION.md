# 能力验证报告（CAPABILITY VERIFICATION）

> 验证时间：2026-07-20
> 验证方式：**代码/架构级**（静态阅读 + Grep 交叉验证）。
> 重要声明：本沙箱**禁止 `csc.exe`**，无法编译运行 EXE，因此以下均为"代码确实存在且已接线到 UI / 管道"级别的验证，**非运行时验证**。真机运行需用户本地 `.\src\build.ps1` + `.\src\scripts\run-tests.ps1`。

## 结论速览

| # | 声称能力 | 代码现状 | 结论 |
|---|---------|---------|------|
| 1 | 改本地文件 / 存储 | `AgentPipeline` 单管道 + `ExportFileExecutor`/`OrganizeFolderExecutor`/`PatchExecutor`/`RollbackExecutor`；持久化 `VectorIndex`(知识库)、`AuditLog`、`ConfigStore` | ✅ 真实可用 |
| 2 | 修改 / 优化 PPT·Word·Excel | 读+写真实：`OfficeExporter`(ZipArchive 生成 OOXML) + `Extract*`(读 OOXML)；脱敏 `Redactor`；摘要提示 `office_summary` | ⚠️ 读/写真实；"优化"仅=摘要+PII 打码，**无结构化压缩/重写** |
| 3 | Coding / Vibe Coding | `CodingAgentSession`+`DiagnoseFixExecutor`+`FixLoopRunner`+`PatchExecutor`+`GitWorkflowExecutor`；UI：「Diagnose & Fix Project」按钮 + 聊天路由 | ✅ 真实但保守（见边界） |
| 4 | 做网站（生成网站/前端） | `SiteGenerator`(调 LLM 生成 index.html/style.css/app.js) 经 `WriteFileExecutor` 写盘；UI："build website" 意图 (`MainForm.Capabilities.cs`) | ✅ 已实现（代码级） |
| 5 | 修复 Bug（自愈闭环） | `DiagnoseFixExecutor`→`FixLoopRunner`→`BuildFixLoop`(`build→parse→patch→rebuild`)；支持外部仓库 `root` | ✅ 真实但仅覆盖琐碎 C# 编译错误 |

### 2026-07-20 新增能力（代码级，未运行时验证）

| # | 声称能力 | 代码现状 | 结论 |
|---|---------|---------|------|
| 6 | 开源上传仓库（GitHub/Gitee/GitLab） | `GitHostPublisher`(`IGitHost` 抽象 + 三平台 REST 建仓 + git CLI 推本地项目，PAT 存 `ConfigStore` 不进日志)；`PublishRepo` 执行器过单管道；入口 `/publish`、`/settoken`、`/browser` | ✅ 已实现（代码级） |
| 7 | web 登录验证（浏览器保持登录态） | 复用内置 Playwright/Chromium（MIT）；`BrowserAgentClient.SaveSessionAsync` + `BrowserSessionHub` 共享会话；`BrowserControlExecutor` 的 `login`/`savesession`/`loadsession` | ✅ 已实现（代码级） |
| 8 | 双皮肤（浅色/深色） | `ThemeManager`(Light/Dark 调色板 + 递归 `Apply` + 工具条 `ThemeColorTable`)；`MainForm.Theme` 接管 `OnLoad` 重放按钮角色；`zq*` 改计算属性；设置→主题下拉持久化 `theme.json` | ✅ 已实现（代码级） |

---

## 逐项详述

### 1. 修改本地文件 & 持久化存储 ✅
- **单管道**：`Agent/AgentPipeline.cs`(`Run`/`RunAsync`) → `AgentPipelineFactory` 注册执行器。
- **写文件**：`ExportFileExecutor.cs`。**移动/删除/整理**：`OrganizeFolderExecutor.cs` + `RollbackExecutor.cs`。**改代码**：`PatchExecutor.cs` + `Agent/Coding/CodePatcher.cs`。
- **持久化**：`Knowledge/VectorIndex.cs`（向量知识库 `knowledge-vectors.jsonl`）、`Core/AuditLog.cs`、`Agent/CommandRunRecorder.cs`。
- **UI 入口**：导出按钮 `ui/MainForm.Export.cs`、整理文件夹按钮 `ZhuaQianDesktop.cs:2124`、分享 `ui/MainForm.Share.cs`。
- 证据：`AgentPipelineFactory` 注册 `ExportFileExecutor`/`OrganizeFolderExecutor`/`PatchExecutor`；`CodePatcher` 用 `File.Copy`/`File.Delete` 做备份式改码。

### 2. PPT / Word / Excel 修改 & 优化 ⚠️
- **读 + 写真实**：
  - 写：`Documents/OfficeExporter.cs` —— `SaveDocx`(L56)/`SavePptx`(L81)/`SaveXlsx`(L109)，均用 `System.IO.Compression.ZipArchive` 生成真实 OOXML 包（已 Grep 确认，非桩）。
  - 读：`ui/MainForm.LlmDocHelpers.cs` 的 `ExtractTextDocument`/`ExtractPptx`/`ExtractXlsx`/`ExtractZipXml`（用 `ZipFile.OpenRead` 解析 OOXML）。
  - 模板：`Documents/OfficeTemplateLibrary.cs`；脱敏：`Documents/Redactor.cs`；切块：`Knowledge/Chunker.cs`。
  - UI：`ui/OfficeGenerateDialog.cs`、`OfficeTemplate` 命令、`MainForm.Export.cs`。
- **"优化"边界（关键）**：Grep `Optimize|Compress|Restructure|RewriteSlide|ReduceSlide` 在 `src/`（排除 `packages/`）**无结构化压缩/重写代码**。存在的只有：
  - `Redactor`：按正则对 PII 打码（脱敏，不是"精简/优化")。
  - `Core/PromptLibrary.cs` 的 `office_summary` 提示：让 LLM **生成文档摘要**（文本输出），不回写压缩后的原文件。
  - 即：能"基于文档做摘要/问答/脱敏"，但**不能把一份 50 页 PPT 自动压成 10 页或重写版式**。
- 结论：读写与模板/脱敏是真实能力；"优化"一词若指"结构化精简/版式重写"则**未实现**。

### 3. Coding / Vibe Coding ✅（保守）
- **实现**：`Agent/CodingAgentSession.cs`、`DiagnoseFixExecutor.cs`、`Agent/Coding/BuildFixLoop.cs`、`CodingLoopSession.cs`、`PatchExecutor.cs`、`GitWorkflowExecutor.cs`。
- **UI 入口**：「Diagnose & Fix Project」按钮(`ZhuaQianDesktop.cs:1255`)、「Programmer Debug」按钮(L1983)、聊天关键词 `diagnose and fix`(`ui/MainForm.DiagnoseFix.cs:14`)、Plan Review(`ui/MainForm.CodingAgentReview.cs:21`)。
- **边界（务必如实告知）**：
  - 自主自愈的**规则策略** `RuleBasedFixStrategy`(`BuildFixLoop.cs:26`) 只修 **CS1002(缺 `;`)/CS0246/CS0103(缺 `using`)** 这类琐碎 C# 编译错误。
  - 模型驱动的写码是 **plan-step 编排**（AgentPlan/AgentPipelineFactory），`ModelFixStrategy` 在注释中标注为「future」——即"让模型直接写补丁"尚未落地为独立策略。
  - 复杂 bug 会**诚实报 `CannotFix`**，不会乱改。
- 结论：具备"vibe coding"雏形（自主发现编译错→安全补丁→重建→提交），但覆盖面窄，不是"任意需求直接生成整个项目"。

### 4. 做网站 ✅ 已实现（代码级）
- `src/Agent/SiteGenerator.cs`：`SiteGenerator` 调 LLM 生成 `index.html` / `style.css` / `app.js`。
- 落盘走 `WriteFileExecutor`（`permFileWrite`，过单管道）；UI 入口：`MainForm.Capabilities.cs` 的 "build website" 自然语义 + `/save` 体系。
- 边界：生成的是**静态站点/演示文稿**（HTML/CSS/JS），不是带后端服务的站点；需 LLM 可用。

### 6. 开源上传仓库 ✅ 已实现（代码级）
- `src/Agent/GitHostPublisher.cs`：`IGitHost`(`GitHubHost`/`GiteeHost`/`GitLabHost`) + `GitHostHttp`(`HttpClient`，已补 `System.Net.Http` 引用)。流程：REST 建公开仓 → `git` CLI 以 token-in-URL remote 推送；PAT 从 `ConfigStore` 的 `<host>_token` 读取，**不进命令参数/审计日志**。
- 执行器 `GitHostPublisherExecutor`(`CommandType=PublishRepo`) 注册进 `AgentPipelineFactory`，过单管道审批。
- 路由：`MainForm.Publish.cs` 的 `/publish` / `/settoken` / `/browser`（及自然语言）。
- 发布前若无 `.gitignore` 自动写入（排除 bin/obj/packages/node_modules 等）。

### 7. web 登录验证（浏览器保持登录态）✅ 已实现（代码级）
- 复用内置 **Playwright/Chromium**（开源 MIT，模块 G/H），无需新建浏览器。
- `BrowserAgentClient.SaveSessionAsync` 导出 cookie/localStorage；`BrowserSessionHub` 共享单例，保证 `login`→`savesession`→`loadsession` 同一会话。
- `BrowserControlExecutor` 新增 `login`/`savesession`/`loadsession` 动作，经 `BrowserControl` 命令过单管道。

### 8. 双皮肤（浅色/深色）✅ 已实现（代码级）
- `src/ui/ThemeManager.cs`：`ThemeName{Light,Dark}` + 完整调色板 + 递归 `Apply`(含工具条 `ThemeColorTable`) + `theme.json` 持久化。
- `src/ui/MainForm.Theme.cs` 接管 `OnLoad`：`ThemeManager.Apply(this)` + 订阅 `ThemeChanged` 重放按钮角色。
- `ZhuaQianDesktop.cs` 的 11 个 `zq*` 颜色字段改为读 `ThemeManager` 的计算属性，聊天区/语义色全部走主题令牌。
- 切换入口：设置对话框「主题」下拉，即时生效并持久化。

### 5. 修复 Bug ✅（同 #3 闭环）
- `DiagnoseFixExecutor` + `FixLoopRunner` + `BuildFixLoop` + `GitWorkflowExecutor` + `RuleBasedFixStrategy` + `Agent/Coding/ErrorParser.cs`。
- UI：「Diagnose & Fix Project」按钮 + 聊天路由 `LooksLikeDiagnoseFixRequest`。
- 闭环：`BuildFixLoop.Run`(L360-475)：build → `ErrorParser.Parse` → `RuleBasedFixStrategy.SuggestFixes` → `CodePatcher.ApplyAll` → 重建，最多 `MaxIterations`；支持 `root` 指向**外部仓库**。
- 边界同 #3：仅琐碎 C# 编译错误可自动修。

---

## 文档 vs 代码 一致性
- 未发现"文档声称但代码没有"的虚假声明（Feature 4 在代码与文档中都不存在）。
- 唯一**能力边界**需向用户澄清：文档把项目定位为"自主编码智能体"，但当前自主写码覆盖面仅琐碎 C# 编译错误（非任意需求生成）。属边界说明，非文档落差。

## 缺失项与建议
| 缺口 | 是否建议补 | 工作量估计 |
|------|-----------|-----------|
| 4. 做网站（生成静态站点/前端） | **已补**：`SiteGenerator`(HTML/JS) + `WriteFileExecutor` 落盘 + "build website" 意图（代码级，待本地编译验证） | 中（已完成代码级） |
| 2. Office "优化"（结构化压缩/版式重写） | 建议补：在 `OfficeExporter` 基础上加 `OptimizePptx/Xlsx`(删冗余、重排、摘要回写) | 中 |
| 3. 模型驱动写码策略 `ModelFixStrategy` | 已标记 future，建议落地以拓宽自愈覆盖面 | 中 |

> 以上补强可在用户拍板后动手；本轮仅做验证，未改动任何源文件。
