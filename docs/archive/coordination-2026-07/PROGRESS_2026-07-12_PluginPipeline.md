# ZhuaQian Desktop 进度记录：插件执行接入 Agent 管道

更新时间：2026-07-12

## 1. 本次完成

本次继续按“真实副作用必须经过 Command / Gate / Executor 管道”的架构约束推进，把第三条高风险副作用链路 `RunPlugin` 接入 Agent 管道。

已完成：

- `work/zq-desktop/Agent/PluginRunExecutor.cs`
  - 新增插件执行 executor。
  - 调用现有 `Tools.PluginRunner`，不重复实现脚本执行逻辑。
  - 支持 stdin、arguments、timeout、max output chars。
  - 插件输出通过 `CommandResult.OutputText` 返回给 UI。
  - 超时、执行错误、非零退出码统一返回 `CommandResult.Failed(...)`。
- `work/zq-desktop/Agent/CommandResult.cs`
  - 增加 `OutputText`，支持非文件类命令返回文本结果。
- `work/zq-desktop/Tools/PluginRunner.cs`
  - `Validate(...)` 现在真正检查插件路径必须位于 trusted plugin folder 内。
  - 空 trusted folder、不存在的 trusted folder、trusted folder 外脚本都会被拒绝。
- `work/zq-desktop/ZhuaQianDesktop.cs`
  - `ConfigureAgentPipeline()` 注册 `PluginRunExecutor`。
  - `RunPlugin(...)` 保留文件选择和 ApprovalCard，但真实执行改为构造 `RunPlugin` command 并调用 `agentPipeline.Run(...)`。
  - 移除主窗体里的 `ExecutePlugin(...)` 与 `ValidatePluginPath(...)` 直执行路径。
- `work/zq-desktop/build.ps1`
  - 加入 `Agent/PluginRunExecutor.cs`。
- `work/zq-desktop/scripts/run-tests.ps1`
  - 加入 `Agent/PluginRunExecutor.cs`。
- `work/zq-desktop/tests/TestRunner.cs`
  - AgentPipeline 正向测试现在会真实运行一个临时 `.ps1` 插件并检查输出。
  - PluginRunner edge 测试新增 trusted folder 外脚本拒绝断言。

## 2. 验证结果

在 `work/zq-desktop/` 下执行：

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1
powershell -ExecutionPolicy Bypass -File .\scripts\run-tests.ps1
```

结果：

- `build.ps1`：通过，生成 `ZhuaQianDesktop.exe`
- `scripts/run-tests.ps1`：`154` passed / `0` failed

## 3. 当前管道覆盖度

已经接入 Agent 管道的真实副作用：

1. `ExportFile`：TXT / MD / Word / PPT / Excel 导出
2. `OrganizeFolder`：按类型整理文件夹，并产出 rollback manifest
3. `RunPlugin`：运行 trusted plugin folder 内的插件脚本

仍未接入的重点副作用：

1. `RollbackFiles`
2. Share / Import / LAN / Relay 等网络和文件混合链路
3. ProcessManage / EndProcess
4. Screenshot / Clipboard / IndexFolder 等系统读取链路

## 4. 下一步建议

下一步优先做：

```text
RollbackFiles -> RollbackFilesExecutor
```

理由：

- rollback 是高风险文件移动/恢复类副作用。
- 当前已有 rollback manifest 与 `Tools.FolderOrganizer.Rollback(...)`，不需要重新实现。
- 可回滚命令契约能推动 `CommandResult.CanRollback` / `RollbackManifestPath` 走得更实。

