# ZhuaQian 并行协作看板

更新：2026-07-17 23:44（代码会话续：UI 审查打通 + 纠正 build/tests 登记过时项）
当前执行者：并行构建进程（Epic E 集成，未提交在制品）+ 本会话（net-new + 清理）。

## 监督基线（23:30 实测）

### git 状态
- HEAD: `b10b4ce` refactor(ui): shrink main form under budget (Epic B)
- 共 6 提交：init → .gitattributes → Export 抽取 → 源码树收敛 → CI → 主文件压预算
- 未提交在制品（构建进程做 Epic E 集成）：csproj/build.ps1/run-tests.ps1/AgentPipeline.cs/TestRunner.cs/AgentPlanCommandMapper.cs/_line_budget.json/.gitignore

### 行数预算
- 主文件 `ZhuaQianDesktop.cs` = 3624 非空行 = maxMainLines=3624 → **持平，零余量**

## 已落地
- [x] 异步执行契约（IAsyncCommandExecutor + RunAsync + Wait 真 await）
- [x] AgentPlan 引擎（Plan / Step / Parser + ToReviewMarkdown）
- [x] Hooks 框架（HookKind / HookContext / IPluginHook / HookRegistry）
- [x] PluginManifest + Parser（校验 / 路径穿越防护 / JSON 往返）
- [x] WebSearch Executor + Client
- [x] Export 模块化（MainForm.Export.cs partial）
- [x] git init + 6 提交 + CI（tests.yml：push/PR 编译测试，tag 出 Release）
- [x] work/ 退役 + outputs/ 重复树清理（commit a624059）
- [x] 主文件压预算（5240 → 3624 非空行）
- [x] README 记录 186 测试 passed / 0 failed

## Partly Implemented
- [~] Epic E 集成：在制品，csproj/build/TestRunner 已改但**未提交、未编译验证**（沙箱禁 csc）
- [x] **Agent per-step 状态引擎已接入 UI 闭环**：`AgentPlanState.cs` + `AgentPlanRunner.cs`（csproj 行 85-86 已登记）已被 `src/ui/MainForm.PlanExecution.cs` 的 `ExecutePlanDraft` 调用——逐 step `MarkDoing/Done/Failed/Skipped` 并持久化 JSON 到 `%APPDATA%/ZhuaQianDesktop/plan-runs/<goal>.json`。**待本地 build.ps1 + run-tests.ps1 验证**。
- [~] Epic D1 `WorkspaceScanSummary.cs` / `CommandRunRecorder.cs` / `CodingAgentSession.cs`：已被 Process X 在 23:10 登记进 build.ps1(52-54) + run-tests.ps1(93-98)，**编译可见性已解决**（待本地 build 验证）。
- [x] ~~`WorkspaceScanner.cs` 重复死代码~~ → 本回合已删（与 `WorkspaceScanSummary.cs` 撞车，属我上轮误建的重复实现，非 Process X 产物）。
- [ ] Activity Monitor 仍为手动快照
- [ ] Provider mock 覆盖薄

## 用户指令（2026-07-18 00:00）：全部做完，再开源
- 老李明确：先把所有剩余工作收口，**最后一步才开源**（push GitHub / 公开仓库）。
- 因此「push 到 GitHub 仓库」从"尽早接通 CI"降级为**收尾闸门**——不在开发中途做，避免半成品外流。
-  consequence：本会话继续在沙箱内做可编译的代码端收口（net-new + 清理 + 文档），开源动作留到最后。

### 开源前收尾路线图（按依赖排序，未完成项即下方 Not Done）
1. **本地 full build + run-tests 全绿**（用户真机，沙箱禁 csc）→ 验证所有在制品 + F1 模板库实际可编。
2. 构建进程收口 Epic E 集成并**提交**（Hooks / PluginManifest / CodingAgentSession / AgentPlanState / AgentPlanRunner）。
3. 接 UI 入口消费 F1 模板库（谁有空谁做，不冲突）。
4. **Installer**（需用户定格式：InnoSetup / NSIS / MSIX？）。
5. **真实 MCP client 集成**（需用户定接哪些 MCP server）。
6. **企业级安全 / 内核监控 / 反作弊驱动**（需用户定范围，外部依赖重，风险最高）。
7. 完整 UI 自动化测试 + 标准测试框架（可替换自定义 TestRunner）。
8. 开源前安全审查：`.gitignore` 收紧 + 密钥/敏感信息扫描 + README/LICENSE。
9. **【最终步】push GitHub + 公开仓库**（用户给地址 + 凭证）。

> 标记 ⚠️ 的项需用户拍板外部资源/方向，本会话无法独自收口；其余代码端项本会话可在沙箱内继续推进。

## Not Done（需用户拍板 / 外部资源，按完成度排序）
- [ ] **push 到 GitHub 仓库 / 开源**（**最终步：全部做完才做**）→ 仓库地址 + 凭证由用户给，公开前需 full build/tests 绿 + 安全审查
- [ ] Installer
- [ ] MCP 集成（仅调研文档，无真实 client）
- [ ] 企业级安全 / 内核监控 / 反作弊驱动
- [ ] 完整 UI 自动化测试
- [ ] 标准测试框架工程（当前自定义 TestRunner）

## 本会话监督期间已做（23:30–23:35，net-new + 清理，未碰构建进程独占文件）
1. **删死代码** `src/Agent/ICommandHook.cs`：与 `IPluginHook` 重复且无人引用，确认死代码后删除（风险#1 已解）。
2. **清 work/ 垃圾**：物理删除 `work/zq-desktop/`（全是 .exe/.backup 旧编译产物，已从 git 退役）（风险#3 已解）。
3. **新增 Epic D1 `src/Agent/WorkspaceScanner.cs`**：只读工作区扫描，汇总 git 变更文件、构建/测试命令、
   行数预算、超大文件等风险提示，输出 Summary/ToJson。接 coding-agent 闭环（Plan→Command→Diff→Test→Review）。
   用 System.Web.Script.Serialization（与 AgentPlanState/PluginManifest 一致），Process 跑 git 带超时+异常隔离。
4. **写构建登记补丁** `docs/patches/NEW_MODULES_BUILD_REGISTRATION.md`：记录 AgentPlanState.cs（csproj 有、build/tests 缺）
   + WorkspaceScanner.cs（三者皆缺）需加入的精确条目；待构建进程停手、本地 build/run-tests 全绿后应用。

## 风险预警（23:35 更新）
- [x] ~~ICommandHook 重复接口~~ → 已删
- [x] ~~work/ 垃圾目录~~ → 已清
- [~] **构建文件同步**：AgentPlanState.cs 在 csproj 但不在 build.ps1/run-tests.ps1；WorkspaceScanner.cs 三者皆缺。
      → 待构建进程停手后按补丁登记 + 本地 build/run-tests 验证。
- [~] **所有在制品未提交 + 沙箱禁 csc**：Epic E 集成改动未经编译验证，需用户本地确认。
- [~] **预算持平**：主文件 3624 非空行 = maxMainLines 3624，零余量。再加任何代码到主文件即超预算。

## 监督策略
- 本会话做 net-new + 清理时**避开构建进程独占文件**（csproj/build.ps1/run-tests.ps1/TestRunner.cs/
  AgentPipeline.cs/AgentPlanCommandMapper.cs/_line_budget.json/.gitignore）
- 通过 git status / git log 监控提交节奏和文件漂移
- 发现风险即更新本看板 + 通知用户
- 每日自动化哨兵 `automation-1784298718845`（ACTIVE，DAILY）跑架构/打包/git 检查

## 代码会话边界更新（23:36，Supervisor 接管代码）

本回合（代码会话）在"下一步"中把 `AgentPlanRunner.RunPlanAsync` 接入了 UI 执行入口：
- 改动文件：`src/ui/MainForm.PlanExecution.cs`（替换 `ExecutePlanDraft` 内联 for 循环为 `runner.RunPlanAsync`，
  审批 / 解析 / review 逻辑全部保留，最小侵入）。
- 依赖：`AgentPlanState.cs` + `AgentPlanRunner.cs`（二者 csproj 行 85-86 已登记，与 Process X 的
  Hooks / PluginManifest / CodingAgentSession 等条目无重复）。
- **边界提醒**：该文件位于 Process X 声明拥有的 "main form" 家族内（其清单未单列 PlanExecution.cs）。
  本回合已认领此文件，提醒 Process X 勿再改 `MainForm.PlanExecution.cs` 同一区域，以免合并冲突。
- 验证状态：沙箱禁 csc，未编译验证；需用户本地 build.ps1 + run-tests.ps1 全绿确认。

## 代码会话续：UI 审查打通（23:44，用户"继续开始下一步"）

核查戳穿一批过时项（基于 23:30 监督快照，但 Process X 23:10 已改 build/run-tests）：
- `build.ps1`(50-54) + `run-tests.ps1`(92-98) **已含** AgentPlanState / WorkspaceScanSummary / CommandRunRecorder / CodingAgentSession（连同其 3 个测试）。→ 旧"3 模块需登记进 build/tests"基本是虚惊，已被 Process X 做掉。
- `WorkspaceScanner.cs`：`grep` 全 src **零引用**（孤立死代码），不用编译、不需登记。旧"三者皆缺需补"是伪问题。
- `AgentPlanRunner.cs`：build 有(51) / run-tests 缺 —— 但无测试引用它（仅 MainForm 引用，MainForm 不在 tests 编译集）→ 无妨。

已做（② UI 审查打通，零撞车）：
- `src/ui/MainForm.PlanExecution.cs`：在 `runner.RunPlanAsync` 跑完后，调 `new CodingAgentSession()` 生成
  Codex 式轻量审查报告（`Recorder=null` → 只读 diff 扫描 + plan 执行状态，**不触发重型 build/test**），结果 `AppendChat` 给用户。
  不新建类、不碰 build.ps1/csproj/Process X 文件，仅引用已在其三者中的 CodingAgentSession。
  → "执行闭环（AgentPlanRunner）" 与 "审查报告（CodingAgentSession）" 在 UI 端打通，形成 Plan→Command→Diff→Test→Review 闭环展示。
- 验证状态：沙箱禁 csc，未编译验证；CodingAgentSession 已在 build/run-tests，理论可编，需用户本地 build/run-tests 全绿确认。

剩余 Not Done（不变）：push GitHub（需仓库+凭证）/ Installer / 真实 MCP / 企业安全内核监控 / 完整 UI 自动化测试 / 标准测试框架。

## 代码会话续：Epic F1 办公模板库（23:50，用户"直接做代码端，和它们协同"）

本回合在白地 Epic F（办公工作流模板）落地 F1 模板库——这是之前所有进程都没碰的净空区。

已做（net-new，零撞车，未碰构建进程在制品）：
- `src/Documents/OfficeTemplateLibrary.cs`（新增，~330 行，远低于 maxOtherCsLines=900）：
  - `enum OfficeTemplateKind`：SalesPitch / MeetingMinutes / Report / DataTable / Poster
  - `TemplateContext`：标题/副标题/作者/日期/Closing + `Bullets` 字典 + `Columns`/`Rows`
  - `OfficeTemplateLibrary.Render(kind, ctx)` 返回 `TemplateResult{Text, SuggestedExtension}`
  - 五大渲染器生成**直接交给 OfficeExporter 的文本骨架**（pptx 用 `#` 分片、docx 用 `#`/`##` 标题、xlsx 用 pipe 表、png 用大标题）
  - 附带 `RenderByName` / `ListKinds` / `SuggestedExtension` 供命令映射
- `src/tests/TestOfficeTemplateLibrary.cs`（新增，自带 `static int RunAll()` 环回测试：生成文本 → OfficeExporter 验证合法 zip/png，9 个断言）

协同登记（沿用既定协议，不动构建进程独占文件）：
- 构建登记 4 处精确条目已写入 `docs/patches/NEW_MODULES_BUILD_REGISTRATION.md` 的"Epic F1 待登记"小节：
  csproj / build.ps1 / run-tests.ps1 各加 `Documents\OfficeTemplateLibrary.cs`，TestRunner.Main 加
  `failures += TestOfficeTemplateLibrary.RunAll();`
- 预期测试数 189 → 190（新增 1 组 F1 单测）。

边界说明：OfficeTemplateLibrary 是纯库，不引用任何 UI，也不被现有代码引用（由构建进程后续接 UI/命令入口即可）。
验证状态：沙箱禁 csc，未编译；需用户本地 build.ps1 + run-tests.ps1 全绿确认。

## 当前状态（本会话收口后，2026-07-17 深夜）

- **构建清单自洽**：build.ps1 + run-tests.ps1 已动态枚举（a123ec5）；csproj 80 个 Compile Include 完整覆盖所有新模块，无 tests/、无死文件泄漏。新增 .cs 无需手动改 build/run-tests（动态自动拾取），但 csproj 仍需手动加（本会话已加 D3 两文件）。
- **Epic D 全收口**：D1/D2/D-orchestrator 模块已写入并登记；D3 差量审查面板已落地（PlanReviewDialog 计划审查 + 执行后只读审查进聊天 + CodingAgentReportDialog "Full Review" 跑完整 build+test）；D4 "run tests after change" 由只读审查 + Full Review 覆盖。
- **Epic F1 已完成**：OfficeTemplateLibrary.cs（5 模板 + OfficeExporter round-trip）+ TestOfficeTemplateLibrary.cs（已接入 TestRunner，189→190）。
- **风险**：全部在制品未提交；沙箱禁 csc 未经真编译；主文件 3624 非空行 = 预算零余量（本会话未碰主文件）。
- 建议：用户本地 `build.ps1` + `run-tests.ps1` 全绿后，一次性提交本夜全部 Epic C/D/E/F 净新增 + 构建文件改动。
- 补丁文档 `docs/patches/EPIC_*_INTEGRATION.md` / `NEW_MODULES_BUILD_REGISTRATION.md` 现已过时（build 动态、csproj 已登记），可标记 obsolete 或删除。
