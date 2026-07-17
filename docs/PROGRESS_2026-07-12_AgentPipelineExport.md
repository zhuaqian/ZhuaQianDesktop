# ZhuaQian Desktop 进度记录：Agent 管道接入导出与文件整理

更新时间：2026-07-12

## 1. 本次完成

本次按 `docs/ARCHITECTURE_CHARTER.md` 与 `docs/EXECUTION_BACKLOG.md` 的 Epic 0 推进：先让真实副作用动作经过统一的 Command / Gate / Executor 管道。

已完成：

- `work/zq-desktop/Agent/IAgentCommand.cs`
  - 增加 `PermissionName`，让命令类型与权限名分离。
  - 解决旧管道用 `CommandType` 查权限，无法命中 `permFileWrite` 等真实权限的问题。
- `work/zq-desktop/Agent/CommandResult.cs`
  - 增加 `OutputType` 与 `SizeBytes`，便于管道统一记录 outputs。
- `work/zq-desktop/Agent/AgentPipeline.cs`
  - 使用 `PermissionGate.Check(permissionName, target)` 做统一权限判断。
  - Denied / Cancelled / Failed / Success 均写入 `AuditLog` 并 flush。
  - 成功且有产物路径时统一写入 `OutputsHub.RecordOutput(...)`。
- `work/zq-desktop/Agent/ExportFileExecutor.cs`
  - 新增导出执行器，支持 `txt` / `md` / `docx` / `pptx` / `xlsx`。
  - 真正的文件写入由 executor 执行，不再由 UI 直接调用 `OfficeExporter`。
- `work/zq-desktop/Agent/OrganizeFolderExecutor.cs`
  - 文件整理 executor 现在返回 `rollback` 类型产物。
  - rollback manifest 路径和文件大小交给管道统一记录到 outputs。
- `work/zq-desktop/ZhuaQianDesktop.cs`
  - 初始化 `AgentPipeline`，注册 `ExportFileExecutor` 与现有 `OrganizeFolderExecutor`。
  - `SaveTextToTxt(...)` 与 `SaveTextAsFormat(...)` 改为构造 `ExportFile` 命令并调用管道。
  - `OrganizeFolder(...)` 保留 UI 预览和 ApprovalCard，但真实文件移动改为构造 `OrganizeFolder` 命令并调用管道。
  - UI 仍负责选择保存路径、展示成功/失败状态，不再负责真实写文件。
- `work/zq-desktop/build.ps1`
  - 加入 `Agent/ExportFileExecutor.cs`。
- `work/zq-desktop/scripts/run-tests.ps1`
  - 加入 Agent 管道相关源文件。
- `work/zq-desktop/tests/TestRunner.cs`
  - 新增 AgentPipeline 正向测试：允许写入时，真实生成文件、写 outputs、写 audit。
  - 新增 AgentPipeline 文件整理测试：允许移动时，真实移动文件、生成 rollback manifest、写 outputs。
  - 新增 AgentPipeline 拒绝测试：权限拒绝时不落盘、不写 outputs。

## 2. 验证结果

在 `work/zq-desktop/` 下执行：

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1
powershell -ExecutionPolicy Bypass -File .\scripts\run-tests.ps1
```

结果：

- `build.ps1`：通过，生成 `ZhuaQianDesktop.exe`
- `scripts/run-tests.ps1`：`151` passed / `0` failed

## 3. 当前结论

Epic 0 的最小验收已达到：

- 导出文件与文件夹整理两个真实副作用动作不再由 UI 直接执行，而是经过 Agent 管道。
- 管道统一经过 `PermissionGate`。
- 管道统一写入 audit。
- 管道统一写入 outputs。
- Executor 内没有重复实现权限判断和日志写入。

这只是第一条样板链路，不代表所有副作用入口已经统一。

## 4. 仍未完成

下一步建议继续接线，而不是新增功能：

1. 把插件执行 `RunPlugin` 改造成 `PluginRunCommand + PluginRunExecutor`。
2. 把 `RecordAction(...)` 与 `LogAction(...)` 的主路径逐步收口到管道，避免 UI 自己决定 action 写法。
3. 给每个高风险命令补充更明确的 command contract：
   - 参数 schema
   - 副作用类型
   - 是否可回滚
   - rollback manifest 路径
4. 继续替换复杂 `MessageBox`，让审批统一走 `ApprovalCard`。

## 5. 给下一个开发者 / AI 的入口

优先阅读和修改：

- `work/zq-desktop/Agent/AgentPipeline.cs`
- `work/zq-desktop/Agent/ExportFileExecutor.cs`
- `work/zq-desktop/Agent/OrganizeFolderExecutor.cs`
- `work/zq-desktop/ZhuaQianDesktop.cs` 中：
  - `ConfigureAgentPipeline()`
  - `SaveTextToTxt(...)`
  - `SaveTextAsFormat(...)`
  - `OrganizeFolder()`
- `work/zq-desktop/tests/TestRunner.cs` 中：
  - `TestAgentPipeline()`
  - `TestAgentPipelineEdge()`
