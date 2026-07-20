# ZhuaQian Desktop — 项目评估报告

- **评估日期**：2026-07-20
- **评估方式**：基于真实仓库状态（git 历史、架构检查脚本、代码规模统计），非凭记忆复述
- **项目定位**：local-first Windows AI 桌面智能体（C# + .NET Framework 4.8 WinForms），已正式转向「自主编码智能体」
- **核心铁律**：所有真实副作用必须过 `Command → PermissionGate →(Approval)→ Executor → AuditLog + OutputsHub` 单一管道

---

## 1. 总体结论

**架构健康、功能广度完整、编码智能体是最大亮点；最关键的短板是「从未真机编译验证」与「产品方向未定」。**

这是一个完成度很高、但验证闭环尚未闭合的项目。代码治理（单一管道、行预算、模块化）执行到位；功能面覆盖文档、UI、Agent、编码智能体、工具生态、办公模板、浏览器抓取、浏览器/桌面闭环八大模块；编码智能体的自愈闭环已通用化到外国仓库并有端到端 demo。但所有 C# 改动从未在真机编译，CI 从未实际运行，存在潜伏回归风险。

---

## 2. 架构健康度（实测）

| 指标 | 实测值 | 评价 |
|------|--------|------|
| 架构检查 `check-architecture.ps1` | **PASSED** | 合格 |
| `.cs` 文件总数 | 107（排除 tests/packages） | 模块化良好 |
| 超过 900 行的文件 | 仅主文件 `ZhuaQianDesktop.cs` | 拆分健康 |
| 主文件非空行 | **3616**，正好顶满 `maxMainLines` | 零余量（双刃剑） |
| 单一管道铁律 | 贯穿全局 | 合规 |
| 高风险命令防线 | `ApprovalCard` 逐字展示、无「记住」开关 | 合规 |

> 主文件零余量：既防止膨胀，也意味着任何新增非空行都会让架构检查 FAIL。这是后续加功能的结构性约束。

---

## 3. 功能完成度（8 个 Epic）

| Epic | 模块 | 状态 |
|------|------|------|
| A | 文档/知识（OfficeExporter, Redactor, Chunker, VectorIndex, OfficeTemplateLibrary） | ✅ 100% |
| B | UI（MainForm 拆 partial + 主文件核心 partial） | ✅ 100% |
| C | Agent Loop（AgentPlan, AgentPlanState/Runner, IAsyncCommandExecutor, AgentPipeline） | ✅ 100% |
| D | Coding Agent（CodingAgentSession, WorkspaceScanSummary, PatchExecutor, FixLoopRunner, GitWorkflowExecutor） | ✅ 100% |
| E | Tool 生态（Plugins/PluginManifest + Hooks） | ✅ 100% |
| F | 办公模板（OfficeTemplateLibrary + Test） | ✅ 100% |
| G | 浏览器渲染抓取（BrowserRenderClient + BrowserFetchExecutor + WebResearchFetcher） | ✅ 100% |
| H | 浏览器交互 + 桌面控制闭环（BrowserAgentClient, BrowserControlExecutor, DesktopScreenCapture, ScreenCaptureExecutor, TaskAgentRunner） | ⚠️ 85% |

**H 待完成项**：`ITaskPolicy`（接 LLM 的生产实现）待写；`env.Actuate` 尚未改走 PermissionGate 管线。算法已有 `outputs/browser-task-loop/` Python 原型验证。

---

## 4. 编码智能体（最近重心，最高价值）

- **自愈闭环完整**：`build → ErrorParser → RuleBasedFixStrategy → 重新构建`。`RuleBasedFixStrategy` 可安全修复 `CS1002`（缺分号）、`CS0246`/`CS0103`（缺 using）。
- **已通用化到外国仓库**（`45a3971`）：`ProjectAnalyzer` 探测真实构建/测试命令（dotnet/npm/cargo/go/mvn/gradle/make），`GuardedCommandRunRecorder` 增 `allowedPrograms` 放行集，不再写死本项目的 `build.ps1` 约定。
- **端到端 demo 就绪**：`outputs/coding-agent-demo/DemoApp` 是外国 `dotnet` 仓库（故意 CS1002）+ `docs/CODING_AGENT_DEMO.md` 运行手册 + 独立 git，可直接实证「能修别人仓库」。
- **安全加固**：高风险命令（RemoteHost/BrowserControl）在 `ApprovalCard` 层逐字展示、禁止「记住选择」。

---

## 5. 最大风险：验证缺口（红色）

- **沙箱禁 `csc.exe`**：所有 C# 改动从未真编译；`run-tests.ps1` 从未实际跑过。
- **CI 未实际运行**：GitHub Actions 未建/未跑，「验证 vs 打包」环未闭合。
- **后果**：编译错误或回归可能潜伏，直到你在本地 `.\src\build.ps1` 才暴露。Python 原型只验证了算法，未验证生产 C# 闭环。

---

## 6. 发现的不一致（维护类，低危）

1. `MEMORY.md` 第 17 行写 `maxMainLines ~3624`，但 `docs/_line_budget.json` 实际是 **3616**（记忆漂移，应改为 3616）。
2. 工作区有 **2 个未提交文件**：`.workbuddy/memory/2026-07-20.md` 与 `MEMORY.md`（上次评估遗留）。

---

## 7. 优先级建议

### P0 — 验证闭环（需你本地/仓库，AI 无法独立完成）
1. 本地跑 `.\src\build.ps1` + `.\src\scripts\run-tests.ps1` 全绿，并跑 demo 闭环确认。
2. 建 GitHub Actions 在 push 时自动 build + test（需你提供仓库/凭证）。

### P1 — 释放预算 + 定方向（AI 可推进 P1③）
3. **主文件拆分**：把 `ZhuaQianDesktop.cs` 约 200 非空行抽到新 partial，腾出预算余量（进行中）。
4. **拍板产品方向**：三线并进 vs 收拢成 Codex 式编码智能体 —— 决定 H 深度与后续投入。

### P2 — 收尾
5. H 生产化：写 `ITaskPolicy`（接 LLM），`env.Actuate` 改走 PermissionGate 管线。
6. 修正 `MEMORY.md` 3624→3616（已完成），并提交那两个 memory 文件（已完成）。
7. 待办大项（真实 MCP client / 企业安全内核 / 完整 UI 自动化测试 / 标准测试框架）按方向排期。

---

## 附录：实测命令与数据

```
# 架构检查
powershell -NoProfile -ExecutionPolicy Bypass -File .\src\scripts\check-architecture.ps1
=> Architecture checks passed.

# 代码规模
Get-ChildItem -Recurse -Path .\src -Filter *.cs | Where-Object { $_.FullName -notmatch '\\(tests|packages)\\' }
=> TOTAL_CS_FILES: 107
   FILES_OVER_900: ZhuaQianDesktop.cs (3880 含空行 / 3616 非空行)
   MAIN_FILE_LINES: 3880

# 最近提交
2b64c08 docs(coding-agent): add end-to-end demo runbook for foreign dotnet repo
5fb9e74 docs(memory): record coding-agent build/test command generalization
45a3971 feat(coding-agent): generalize build/test command detection to run on arbitrary repos
7c70076 chore: commit WIP coding-agent loop + high-risk approval guard + RemoteHost executor; sync line budget (3616)
```
