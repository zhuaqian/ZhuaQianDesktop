# Coding Loop Design — 编程动手能力闭环

> 对标 Codex / Claude Code / Trae:让 ZhuaQian 能稳定地"读项目 → 改文件 → 跑命令 → 看失败 → 继续修 → 产出 diff/commit"。

## 1. 为什么写这个

ZhuaQian 已有完整的权限管道(`IAgentCommand → PermissionGate → Executor → AuditLog`)、计划状态机(`AgentPlanExecutionState`)、命令执行器(`CommandRunRecorder`)。但缺一条**编程闭环**:能跑 build/test、读错误、改代码、再跑,直到通过。`CodingAgentSession` 是一次性报告者,`AgentPlanRunner` 是直线(遇失败即停)。本设计补上闭环。

## 2. 五个能力模块

全部位于 `src/Agent/Coding/`,命名空间 `ZhuaQianDesktopApp.Agent.Coding`,是纯库(不实现 `ICommandExecutor`),被 `CodingLoopSession` 组合,内部各自走权限门。

| 能力 | 模块 | 职责 |
|------|------|------|
| 项目扫描器 | `ProjectAnalyzer` | 递归读目录,识别语言/框架/构建命令/测试命令/入口文件/包文件 → `ProjectProfile` |
| 代码编辑器内核 | `CodePatcher` | 可控 patch(Create/Modify/Delete)+ unified diff + .bak 回滚 + dry-run + 路径防逃逸,走 `permFileWrite` |
| 构建错误解析 | `ErrorParser` | 从 stdout/stderr 提取结构化错误(文件:行:列:码),支持 MSBuild/csc/PowerShell/Python/Go/Rust/Jest |
| 命令执行闭环 | `BuildFixLoop` + `IFixStrategy` | 跑 build→解析错误→策略生成 patch→应用→再跑,带迭代上限/超时/收敛检测 |
| Git 工作流 | `GitWorkflow` | Status/Diff/SuggestCommitMessage/CreateBranch/ExportPatch/Add/Commit,走 `permCommandRun` |
| 任务状态机 | `CodingLoopSession` | 顶层编排:Analyze→Plan→Execute→Review→Done,复用上述全部模块 |

## 3. 闭环状态机

```
用户输入: "帮我检查这个项目为什么编译失败,并修复"

  ┌─────────┐
  │ Analyze │  ProjectAnalyzer.Analyze(root) → ProjectProfile
  │         │  识别语言/构建命令/测试命令/入口文件
  └────┬────┘
       │
  ┌────▼────┐
  │  Plan   │  选择 IFixStrategy(当前 RuleBasedFixStrategy,未来接模型)
  └────┬────┘
       │
  ┌────▼──────────────────────────────────────┐
  │ Execute (BuildFixLoop, 迭代)               │
  │                                            │
  │  ┌─────────┐  失败   ┌──────────┐  有patch  │
  │  │ RunTest │───────▶│ ErrorParse│────────▶│
  │  └────┬────┘         └────┬─────┘          │
  │       │ 通过               │ 无patch        │
  │       │                    ▼                │
  │       │              CannotFix(停)          │
  │  ┌────▼────┐  应用patch                    │
  │  │  Done   │◀──── CodePatcher.Apply       │
  │  └─────────┘                               │
  └────────────────────────────────────────────┘
       │
  ┌────▼────┐
  │ Review  │  GitWorkflow: diff + SuggestCommitMessage + 变更文件列表
  │         │  收集所有 PatchResult.DiffText → AllDiffs
  └────┬────┘
       │
  ┌────▼────┐
  │  Done   │  CodingLoopReport (ToMarkdown) → UI 渲染
  └─────────┘
```

## 4. 权限与审计

每个副作用都走现有管道,不绕过:
- `CodePatcher.Apply` → `PermissionGate.Check("permFileWrite", fullPath)` → Deny 则拒绝,Ask 则由上层审批
- `GitWorkflow.RunGit` → `PermissionGate.Check("permCommandRun", command)` → 同上
- `BuildFixLoop` 通过 `ICommandRecorder`(生产用 `GuardedCommandRunRecorder`,测试用 fake)跑 build/test,继承权限门

所有结果记录在 `BuildFixLoopReport`(每轮 build/test 结果 + 应用的 patch + 解析的错误)和 `CodingLoopReport`(项目画像 + git 摘要 + 建议 commit + review notes),可审计。

## 5. 可演示场景

**场景:缺分号自动修复**
1. `Program.cs` 第 6 行 `Console.WriteLine("hello")` 缺分号
2. `CodingLoopSession.Run("fix build")` 启动
3. Analyze:识别为 C# 项目,build = `build.ps1`,test = `run-tests.ps1`
4. Execute 第 1 轮:build 失败 → ErrorParser 解析出 `CS1002 at Program.cs:6`
5. RuleBasedFixStrategy 生成 patch:在第 6 行末尾加 `;`
6. CodePatcher.Apply 写盘(保留 .bak)
7. Execute 第 2 轮:build 通过 → test 通过
8. Review:GitWorkflow 显示变更文件 + 建议 commit `fix(src): update Program`
9. Done:返回 CodingLoopReport,用户看到改了什么 + diff + 建议 commit

## 6. 当前限制与演进路径

| 限制 | 演进 |
|------|------|
| `RuleBasedFixStrategy` 只做安全修复(CS1002 缺分号、CS0246/CS0103 缺 using) | 接模型:`ModelFixStrategy` 把 `BuildError` 列表 + 项目画像喂给代码模型,生成 `CodePatch` |
| 闭环不接 UI(`CodingLoopSession.Run` 是同步调用) | 接 WinForms:在 `MainForm.CodingAgentReview.cs` 里异步调用 + 进度回调 |
| Git 工作流只到 commit,不开 PR | 后续接 `git push` + GitHub API 开 PR(需 remote + 凭证) |
| 沙箱禁 csc,无法本地编译验证 | 依赖用户 `build.ps1` + `run-tests.ps1` 全绿确认 |

## 7. 测试覆盖

6 个测试文件,全部 `static int RunAll()` 模式,在 `TestRunner.Main` 注册:
- `TestProjectAnalyzer`:C#/Node/Python 项目识别 + 空目录 + 不存在目录
- `TestCodePatcher`:Create/Modify/Delete + diff + 回滚 + dry-run + 路径逃逸 + 权限拒绝 + 前置条件
- `TestErrorParser`:MSBuild/PowerShell/Python/Go/Rust/Generic 格式 + 空 + ToMarkdown
- `TestBuildFixLoop`:一次通过 / 修复后通过 / 无法修复 / 非收敛 / 迭代上限 + RuleBasedFixStrategy 真实修复(缺分号、缺 using)
- `TestGitWorkflow`:Status 解析 / commit message 建议 / diff / branch / 权限拒绝 / patch 导出 / 非法分支名
- `TestCodingLoopSession`:端到端(缺分号→自动修→通过)/ 无法修复诚实报告 / ToMarkdown

## 8. 与并行进程的协调

本仓库有并行 WorkBuddy 进程在 `src/Agent/` 根目录创建了功能相近的模块(`FixLoopRunner`/`ProjectScanner`/`DiffEngine`/`PatchExecutor`/`DiagnoseFixExecutor`/`CodingLoop`/`TaskAgentRunner`),命名空间 `ZhuaQianDesktopApp.Agent`。本设计的模块在 `ZhuaQianDesktopApp.Agent.Coding` 子命名空间,类名不冲突。两套实现可共存;后续应由用户决定合并策略(本套有完整测试 + csproj 注册 + 权限门设计)。
