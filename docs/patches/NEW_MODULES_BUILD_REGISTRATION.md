# 新模块构建登记（已应用 2026-07-17）

来源：本会话多轮构建。原 `NEW_MODULES_BUILD_REGISTRATION.md` 计划登记 `AgentPlanState.cs` +
`WorkspaceScanner.cs`；本轮已落地并修正。

## 实际登记状态（2026-07-17 本回合）

| 文件 | csproj | build.ps1 | run-tests.ps1 | 说明 |
|---|---|---|---|---|
| `Agent\AgentPlanState.cs` | ✅ | ✅ | ✅ | per-step 状态引擎（P-C 加，本回合补齐 build/tests） |
| `Agent\AgentPlanRunner.cs` | ✅ | ✅ | — | 计划逐步执行器，已被 `MainForm.PlanExecution` 调用；仅需主构建，无单测 |
| `Agent\WorkspaceScanSummary.cs` | ✅ | ✅ | ✅ | Epic D1（取代冗余的 `WorkspaceScanner.cs`） |
| `Agent\CommandRunRecorder.cs` | ✅ | ✅ | ✅ | Epic D2 + `ICommandRecorder` |
| `Agent\CodingAgentSession.cs` | ✅ | ✅ | ✅ | Epic D 编排 |
| `tests\Test{WorkspaceScanSummary,CommandRunRecorder,CodingAgentSession}.cs` | — | — | ✅ | 3 组单测，`TestRunner.Main` 调用 |

## 去重动作
- `rm src/Agent/WorkspaceScanner.cs`：与 `WorkspaceScanSummary.cs` 功能重复的 D1 模块；前者命令写成
  `pwsh src/build.ps1`（与项目实际 `powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1` 不符）
  且从未登记。保留 `WorkspaceScanSummary.cs`（已登记、有单测、命令正确）。
- `rm src/Agent/ICommandHook.cs`（更早一轮）：与 `IPluginHook` 重复的命令钩子死代码；`HookRegistry`
  维持 `IPluginHook`-only 单一契约。
- `rm -rf work/`（更早一轮）：清 `.exe`/`.backup` 旧产物。

## 修复的预存断点
- `build.ps1` 原 `$src` 缺 `AgentPlanState.cs`/`AgentPlanRunner.cs`，但 `MainForm.PlanExecution.cs`
  调用 `new AgentPlanRunner(...)` → `build.ps1` 原本编不过。本回合已补登记，修复。

## 待登记：Epic F1 办公模板库（2026-07-17 后续回合，本会话新增）

新增两个 net-new 文件（不触碰任何现有文件，不撞构建进程在制品）：

- `src/Documents/OfficeTemplateLibrary.cs` —— 模板库本体（SalesPitch/MeetingMinutes/Report/DataTable/Poster）。
- `src/tests/TestOfficeTemplateLibrary.cs` —— 自带 `static int RunAll()` 的环回测试（生成文本 → 交给 OfficeExporter 验证产出合法 zip/png）。

### 需补的构建登记（等构建进程停手后应用，避免与 csproj/build.ps1/run-tests.ps1 当前在制品冲突）

1. `src/ZhuaQianDesktop.csproj`：在 `<Compile Include="Documents\Redactor.cs" />` 之后加一行
   ```
   <Compile Include="Documents\OfficeTemplateLibrary.cs" />
   ```
2. `src/build.ps1`：在 `"Documents\OfficeExporter.cs"` 之后加一行
   ```
   "Documents\OfficeTemplateLibrary.cs"
   ```
3. `src/scripts/run-tests.ps1`：在 `"Documents\OfficeExporter.cs",` 之后加一行
   ```
   "Documents\OfficeTemplateLibrary.cs",
   ```
4. `src/tests/TestRunner.cs` `Main()` 中，在 `failures += TestCodingAgentSession.RunAll();` 之后加一行
   ```
   failures += TestOfficeTemplateLibrary.RunAll();
   ```

### 验证（待用户真机，沙箱禁 csc）
- `powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1`
- `powershell -NoProfile -ExecutionPolicy Bypass -File .\src\scripts\run-tests.ps1`
- 预期测试数 186 → 190（新增 3 组 D 单测 + 1 组 F1 模板库单测）。
