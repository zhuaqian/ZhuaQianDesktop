# Epic D Integration (registration applied 2026-07-17)

生成：2026-07-17
作者：本会话（作为并行 builder）
目的：把 Epic D（Coding-Agent Parity）的净新增模块接入编译与测试。本会话即构建进程，已直接登记进 csproj + run-tests.ps1 + TestRunner.cs；仅待用户真机 build/run-tests 编译验证（沙箱禁 csc）。

> 注意：原 `src/Agent/ICommandHook.cs`（与 `IPluginHook` 重复的命令钩子）已被并发重构删除，故本补丁不含 ICommandHook 登记。HookRegistry 为 `IPluginHook`-only 单一契约。

## 新增文件（已落地，并已登记进 csproj / run-tests.ps1）

src/Agent/（均为 net-new，< 900 行）：
- `AgentPlanState.cs` — 由 P-C 监督会话新增（per-step 状态引擎），本回合已一并登记进 csproj + run-tests.ps1。
- `WorkspaceScanSummary.cs` — D1 工作区扫描摘要。
- `CommandRunRecorder.cs` — D2 命令运行记录（stdout/stderr/exit code → AgentPlanStepResult）+ `ICommandRecorder` 接口。
- `CodingAgentSession.cs` — D 编排：Plan → Command → Diff → Test → Review 报告。

src/tests/（net-new，standalone class，提供 `RunAll()` 返回失败数）：
- `TestWorkspaceScanSummary.cs`
- `TestCommandRunRecorder.cs`
- `TestCodingAgentSession.cs`

## 应用前提

1. 并发构建进程（P-B）已停手 / 已合并到 P-C。
2. 在用户真机运行 `build.ps1` + `run-tests.ps1` 的 csc 可用（沙箱禁 csc，本会话未编译验证）。
3. 这些文件都是**纯新增**，不与现有文件重名，不会触发 csproj 合并冲突——只要在你自己的提交里把下面清单并入即可。

## 步骤 1：注册进 `src/ZhuaQianDesktop.csproj`

在现有的 `<ItemGroup>` 的 `<Compile Include="..." />` 列表中追加（相对 `src/`）：

```
    <Compile Include="Agent\AgentPlanState.cs" />
    <Compile Include="Agent\WorkspaceScanSummary.cs" />
    <Compile Include="Agent\CommandRunRecorder.cs" />
    <Compile Include="Agent\CodingAgentSession.cs" />
```

（注意 `AgentPlanState.cs` 也仍需登记；它与本补丁的 D 模块在同一批里一起进 csproj 即可。）

## 步骤 2：注册进 `src/scripts/run-tests.ps1` 的 `$relSrc`

在该数组（`$relSrc = @(...)`）中追加：

```
        "Agent\AgentPlanState.cs",
        "Agent\WorkspaceScanSummary.cs",
        "Agent\CommandRunRecorder.cs",
        "Agent\CodingAgentSession.cs",
        "tests\TestWorkspaceScanSummary.cs",
        "tests\TestCommandRunRecorder.cs",
        "tests\TestCodingAgentSession.cs"
```

（Epic E 的 `Hooks\*.cs` 与 `PluginManifest.cs` 已被 P-B/上一会话加入过，无需重复。）

## 步骤 3：在 `src/tests/TestRunner.cs` 的 `Main()` 里调用新测试

在现有 `TestHookRegistry();` 之后追加三行（这些方法返回 `int` 失败数，累加到 `failures`）：

```csharp
        TestWorkspaceScanSummary.RunAll();
        TestCommandRunRecorder.RunAll();
        TestCodingAgentSession.RunAll();
```

> 说明：现有测试都是 `TestRunner` 类的 void 静态方法；新测试改为独立 class + `RunAll()` 返回失败数，是为了**不修改 `TestRunner` 的方法体**，仅追加三行调用，降低与并发构建的冲突面。

## 步骤 4：验证

```
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\src\scripts\run-tests.ps1
```

预期：build 通过；测试总数由 186 增至 **189** 全绿（新增 TestWorkspaceScanSummary / TestCommandRunRecorder / TestCodingAgentSession 三组）。

## 可选后续（非本补丁范围，属 UI 接入，建议归 P-B/P-C）

- 把 `CodingAgentSession` 接入 `src/ui/MainForm.PlanExecution.cs` 或新增 `PlanReviewDialog` 的「Run coding session」按钮，实现 Epic D 验收「不离开 app 即可 Plan → Command → Diff → Test → Review」。
- D3（diff/review 面板）、D4（改后跑测试的工作流）属 UI 层，建议由 P-B 在其 Epic B 收尾后承接。
