# ZhuaQian Desktop 颗粒度评估与优化记录

更新时间：2026-07-12

## 1. 结论

当前文档颗粒度已经比早期好：核心文档在 `docs/` 根目录，历史分析进入 `docs/archive/`。但代码颗粒度仍然偏粗，最大风险仍是 `work/zq-desktop/ZhuaQianDesktop.cs`，它约 230 KB，仍同时承担 UI、任务、工具编排、权限、输出、聊天流等职责。

本轮优化不再新增分析文档后停下，而是继续按架构文档落地：把第二条真实副作用链路 `OrganizeFolder` 接入 Agent 管道。

## 2. 文档颗粒度现状

### 主干文档

推荐以后只把以下文档作为派工依据：

- `docs/ARCHITECTURE_CHARTER.md`
- `docs/PRODUCT_REQUIREMENTS.md`
- `docs/PRODUCT_ARCHITECTURE.md`
- `docs/EXECUTION_BACKLOG.md`
- `docs/CURRENT_REALITY_2026-07-11.md`
- `docs/CODE_COMPLETION_ALIGNMENT.md`
- `docs/GRANULARITY_REVIEW_2026-07-12.md`

### 历史文档

`docs/archive/` 中有大量竞品、评估、重构、安装、历史计划文档。它们适合查背景，不适合作为当前派工依据。

主要问题：

- 历史文档体积大，容易让后续 AI 重复阅读过时判断。
- 一些旧文档会把 `src/` 或拆分状态写得过于乐观。
- 文档主干与执行进度文档仍需要持续对齐。

优化建议：

- 新增执行结果只写 `docs/PROGRESS_日期_主题.md`。
- 派工只看 `EXECUTION_BACKLOG.md` 与最新 `PROGRESS_*`。
- 不再新增大而全的评估文档，除非它能替代旧文档成为新的事实源。

## 3. 代码颗粒度现状

当前最大文件：

- `work/zq-desktop/ZhuaQianDesktop.cs`：约 232 KB，仍是主要风险源。
- `work/zq-desktop/tests/SelfTest.cs`：约 35 KB，属于旧式自测入口。
- `work/zq-desktop/tests/TestRunner.cs`：约 28 KB，当前仍承担集中测试 runner。
- `work/zq-desktop/Documents/OfficeExporter.cs`：约 21 KB，功能集中但边界清晰。
- `work/zq-desktop/ui/SettingsDialog.cs`：约 21 KB，设置 UI 偏大但可接受。

判断：

- `ZhuaQianDesktop.cs` 仍应继续降重。
- 优先拆“真实副作用执行逻辑”，因为它最容易造成权限、审计、产物记录分叉。
- 不建议先做全量 MVVM 或多项目迁移；当前更适合继续小步接线。

## 4. 本轮已优化

已完成：

- `ExportFile` 真实写文件已走 Agent 管道。
- `OrganizeFolder` 真实移动文件已走 Agent 管道。
- `OrganizeFolderExecutor` 返回 rollback manifest 作为 outputs 产物。
- UI 只保留：
  - 选择文件夹
  - 生成预览
  - 触发 ApprovalCard
  - 构造 Command
  - 展示执行结果
- 权限、执行、audit、outputs 由管道统一处理。

验证：

- `work/zq-desktop/build.ps1`：通过
- `work/zq-desktop/scripts/run-tests.ps1`：`151` passed / `0` failed

## 5. 下一步颗粒度优化顺序

### P0：继续抽副作用入口

1. `RunPlugin` -> `PluginRunExecutor`
2. `RollbackFiles` -> `RollbackFilesExecutor`
3. `Share / Import / LAN / Relay` -> 网络与文件命令分离
4. `ProcessManage` -> `ProcessCommandExecutor`

验收标准：

- UI 不直接执行副作用。
- Executor 不自己弹窗、不自己判断权限。
- AgentPipeline 统一写 audit / outputs。
- 每条命令至少有一个正向测试和一个拒绝/失败测试。

### P1：拆状态与消息编排

目标：

- 把 `SendMessage` 的 provider 编排、streaming、自动导出从 UI 中继续移出。
- 让 UI 只负责读取输入、展示消息、刷新任务状态。

### P2：测试颗粒度优化

目标：

- 保留当前 `TestRunner.cs` 作为短期绿线。
- 后续迁移到 xUnit 时，按模块拆测试类。
- 不再新增 `SelfTest.cs` 风格的大型镜像测试。

## 6. 给下一个 AI 的任务

下一步不要新增功能，直接做：

```text
把 RunPlugin 改造成 PluginRunExecutor，并补测试。
```

理由：

- 插件执行属于高风险副作用。
- 当前已有 `Tools.PluginRunner`，不需要重新实现。
- 非常适合作为 Agent 管道第三条样板链路。

