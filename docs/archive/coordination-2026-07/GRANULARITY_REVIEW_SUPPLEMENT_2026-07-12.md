# ZhuaQian Desktop 颗粒度补充评估

更新时间：2026-07-12

## 最新事实

在 `docs/GRANULARITY_REVIEW_2026-07-12.md` 之后，第三条副作用链路已经完成接线：

- `RunPlugin` 已改为 `PluginRunExecutor`。
- UI 仍负责文件选择、ApprovalCard 和展示结果。
- 真实插件执行已进入 `AgentPipeline`。
- `PluginRunner.Validate(...)` 已补上 trusted plugin folder 边界检查。
- 验证结果：`work/zq-desktop/scripts/run-tests.ps1` 为 `154` passed / `0` failed。

## 当前副作用管道覆盖

已进入 Agent 管道：

1. `ExportFile`
2. `OrganizeFolder`
3. `RunPlugin`

仍建议继续抽出：

1. `RollbackFiles`
2. `Share / Import / LAN / Relay`
3. `ProcessManage / EndProcess`
4. `Screenshot / Clipboard / IndexFolder`

## 下一步派工

```text
把 RollbackFiles 改造成 RollbackFilesExecutor，并补测试。
```

验收标准：

- UI 不直接执行 rollback。
- Executor 不弹窗、不判断权限。
- AgentPipeline 统一写 audit。
- rollback manifest 路径保留在 `CommandResult.RollbackManifestPath`。
- 至少一条正向测试和一条拒绝/失败测试。

