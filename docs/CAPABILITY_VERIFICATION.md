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
| 4 | 做网站（生成网站/前端） | 仅网页**读取/分析**：`BrowserFetchExecutor`/`WebResearchFetcher`/`WebPageReportBuilder`；无 `BuildSite`/`GenerateSite`/`GenerateHtml` 类 | ❌ **代码中不存在** |
| 5 | 修复 Bug（自愈闭环） | `DiagnoseFixExecutor`→`FixLoopRunner`→`BuildFixLoop`(`build→parse→patch→rebuild`)；支持外部仓库 `root` | ✅ 真实但仅覆盖琐碎 C# 编译错误 |

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

### 4. 做网站 ❌ 不存在
- Grep（`website|webpage|BuildSite|GenerateSite|StaticSite|scaffold|GenerateHtml|BuildHtml|建站|前端生成`）在 `src/` 中**零命中**（仅 `packages/` 的 Playwright XML 文档含 "website" 字样，无关）。
- 唯一相关代码是**读取**网页：`BrowserFetchExecutor`/`WebResearchFetcher`/`WebPageReportBuilder` —— 用于"分析网页/抓取内容"，**不是生成站点**。
- 文档侧：`docs/*.md` 也从未声称"建站/生成网站/前端"。无虚假声明，但用户五点中**此项确为缺口**。

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
| 4. 做网站（生成静态站点/前端） | 若产品需要，建议补：可复用 `OfficeExporter` 的 OOXML 思路 + 新增 `SiteGenerator`(HTML/JS) + 接 LLM 生成 | 中（需新模块 + UI 入口 + 测试） |
| 2. Office "优化"（结构化压缩/版式重写） | 建议补：在 `OfficeExporter` 基础上加 `OptimizePptx/Xlsx`(删冗余、重排、摘要回写) | 中 |
| 3. 模型驱动写码策略 `ModelFixStrategy` | 已标记 future，建议落地以拓宽自愈覆盖面 | 中 |

> 以上补强可在用户拍板后动手；本轮仅做验证，未改动任何源文件。
