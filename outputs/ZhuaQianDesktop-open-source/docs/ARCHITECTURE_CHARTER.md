# ZhuaQian Desktop 架构理念

更新时间：2026-07-11

本文档是仓库的核心架构文档之一，定位为开源前的架构定调。

它不是又一份分析或评估文档，而是约束性文档：后续任何模块设计、PR review、贡献者接入，都应以本文档的不变量为准绳。

## 0. 为什么需要这份文档

在准备开源之前，本项目暴露过三个反复出现的结构性问题：

1. 模块已经拆分，包含 `Core/`、`Documents/`、`Knowledge/`、`Tools/`、`providers/`，但主窗体仍然内联重写了同样的逻辑，导致拆分类编译进 exe 却没有真正成为主路径。
2. 权限检查、审计写入、Approval 确认，各个 Tool 各自实现了一套，导致多处 `MessageBox` 绕过了统一的 `ApprovalCard`，也产生了并行的日志和产物记录。
3. 仓库同时存在 `work/`、`src/`、`outputs/` 三套并行源码/发布树，没有机制强制它们收敛为一个事实源。

这三个问题的共同根因是：架构分层只是约定，没有任何东西强制贡献者遵守它。

本文档的核心目的，就是把这些约定逐步变成编译期或运行期无法绕过的约束。

## 1. 核心理念：一切副作用必须经过同一条管道

本项目不是“聊天框 + 工具集合”，而是“所有副作用都必须经过同一条管道的桌面 Agent”。

任何会产生真实副作用的动作，包括导出文件、整理文件夹、运行插件、云端上传，以及未来的桌面自动化操作，都必须通过同一条管道执行。不允许任何模块自行实现“检查权限 -> 执行 -> 写日志”这一组合逻辑。

```text
IAgentCommand
  -> PermissionGate.Evaluate(command)
  -> CommandExecutor.Run(command)
  -> ActionRecord + OutputRecord
```

这条管道要解决的历史问题：

| 历史问题 | 管道如何解决 |
|---|---|
| `MessageBox` 绕过 `ApprovalCard` | Approval 环节是管道的强制一环，Executor 不应拥有跳过审批直接执行的路径 |
| 并行日志无单一事实源 | `ActionRecord` / `OutputRecord` 由管道统一写入，Executor 不再自行决定写哪个文件 |
| 权限模型不一致 | 所有 Command 走同一个 `PermissionGate.Evaluate`，不存在 Tool 自己判断权限的分支 |

### 1.1 命令契约

每个 Command 不只是“最后落盘”这个结果性检查，而是一个显式契约。

契约至少包含：

- 输入 schema：参数是什么，合法范围是什么
- 副作用类型：写文件、删文件、移动文件、网络请求、进程操作
- 是否可回滚：如果可以，rollback manifest 的结构是什么
- 失败时的可观察状态：不允许静默吞错，失败必须落到 `ActionRecord.Status = Failed` 并带可读错误信息

契约的直接收益是：未来的 Agent 状态机 `Plan -> Approval -> Execute -> Review` 中，Approval 环节可以直接读取契约生成审批卡片文案，而不需要为每种命令类型手写 UI 分支。

## 2. Provider 层只做协议翻译

`IProviderClient` 的职责收窄为：把统一消息格式翻译成某家 API 的请求和响应。

其他逻辑只允许存在于 `ProviderManager`：

- fallback 顺序
- streaming 通道选择
- 错误归类
- 当前模型切换
- API key 与 endpoint 选择

UI 层、Tools 层不允许直接实例化或调用某个具体 `*Client`，即使是测试连接这种看起来无害的场景，也必须经过 `ProviderManager`。

原因很简单：一旦编排逻辑出现第二个入口，两处就会随时间静默分叉。这正是本项目已经发生过的问题模式。

## 3. UI 层只渲染状态，只发出 Command

WinForms 没有现成的 MVVM 框架，但可以用朴素方式达成同等约束。

目标：

- `MainForm` 只渲染 `AppState`
- 任何改状态的动作必须构造 `IAgentCommand` 发给管道
- 管道执行完成后更新状态，UI 再重绘
- UI 层不直接持有 `ConfigStore`、`PermissionGate`、`AuditLog` 等 Core 对象的写权限

这不是要求立刻完全重写 UI，而是逐步把新增功能入口都变成 Command 入口。新贡献者想加功能时，不应再有机会在 UI 中内联业务逻辑。

## 4. 代码组织：多项目 .sln 是最终目标

### 4.1 为什么现状不够

当前构建方式仍依赖 `csc.exe` + 手写 PowerShell 脚本。它对开源贡献者不够友好：

- 路径和运行环境容易漂移
- 没有标准项目文件
- IDE 智能提示和调试体验弱
- CI 难以标准化

### 4.2 目标结构

```text
ZhuaQian.sln
├─ ZhuaQian.Core
├─ ZhuaQian.Providers
├─ ZhuaQian.Documents
├─ ZhuaQian.Knowledge
├─ ZhuaQian.Tools
├─ ZhuaQian.Agent
├─ ZhuaQian.App
└─ ZhuaQian.Tests
```

职责：

- `ZhuaQian.Core`：`ConfigStore`、`AuditLog`、`PermissionGate`、`OutputsHub`
- `ZhuaQian.Providers`：`IProviderClient`、各 provider client、`ProviderManager`
- `ZhuaQian.Documents`：`OfficeExporter`、`Redactor`、`DocumentExtractor`
- `ZhuaQian.Knowledge`：`Chunker`、`VectorIndex`
- `ZhuaQian.Tools`：`FolderOrganizer`、`PluginRunner`、`ProcessSnapshotCollector`
- `ZhuaQian.Agent`：Command / Gate / Executor 管道
- `ZhuaQian.App`：WinForms UI，唯一允许引用全部模块的项目
- `ZhuaQian.Tests`：标准测试项目

### 4.3 强制约束

- `Core`、`Providers`、`Documents`、`Knowledge`、`Tools`、`Agent` 不引用 `App`
- 只有 `App` 可以引用全部模块
- 模块之间只允许单向依赖
- `Agent` 可以引用 `Core`，`Core` 不引用 `Agent`

一旦这条规则体现在 `.csproj` 的 `<ProjectReference>` 中，UI 内联重写业务逻辑这类问题就不再只依赖 code review。

### 4.4 .NET 版本策略

是否升级到 .NET 8 `net8.0-windows` + WinForms，取决于是否仍需要兼容旧版 Windows 或旧版 .NET Framework 环境。

如果没有旧系统兼容负担，升级到 .NET 8 对开源贡献者体验会更好，因为 `dotnet build` 和 `dotnet test` 可以成为标准入口。

## 5. 测试体系：最终迁移到 xUnit

手写 `TestRunner.cs` / `SelfTest.cs` 是当前阶段能工作的过渡方案，但不应作为开源后的长期形态。

最终目标是 xUnit：

- `dotnet test` 全平台一致
- GitHub Actions 标准化
- 外部贡献者零学习成本
- 每个 `ICommandExecutor` 实现都附带对应测试类

历史上 `SelfTest.cs` 内联镜像类与真实模块 API 不同步，这说明手写 runner 容易制造第二套测试语义。

## 6. 落地顺序

1. 在现有 `work/zq-desktop` 中先补出 Command / Gate / Executor 管道，不必等待多项目迁移。
2. 借这条管道完成 `ApprovalCard`、`StreamingBridge` 和高风险副作用入口的接线。
3. 管道跑通且测试全绿后，再做多项目拆分迁移。
4. 拆分完成、三棵源码树收敛为一棵之后，再公开仓库、选定许可证、完善 `CONTRIBUTING.md`。
5. 最后推进 Agent 状态机 `Plan -> Approval -> Execute -> Review`。

## 7. 面向贡献者的约束清单

PR review 时对照：

- [ ] 新增的副作用类操作是否实现了 `ICommandExecutor`，是否经由 Agent 管道执行？
- [ ] 是否存在自行判断权限、自行写日志的代码？
- [ ] 是否存在 UI 层直接调用具体 Provider Client 或直接修改 Core 对象的代码？
- [ ] 新 Executor 是否附带测试？
- [ ] 是否有空 `catch {}` 或吞掉失败状态的代码？
- [ ] 改动是否只涉及一棵源码树，是否仍在制造第二份实现？
