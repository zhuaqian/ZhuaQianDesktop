# ZhuaQian Desktop — 项目长期记忆

local-first Windows AI 桌面智能体（C# + .NET Framework 4.8 WinForms）。核心铁律：
**所有真实副作用必须过 `Command → PermissionGate →(Approval)→ Executor → AuditLog + OutputsHub` 单一管道**。

## 构建 / CI 约定（关键，违反即编译失败）

- 入口点：`src/ZhuaQianDesktop.cs` 的 `Main()`。生产 EXE 由 `src/build.ps1`（csc 逐文件编译）产出。
- **三个死重复文件，任何构建都禁止编译**：
  - `Program.cs` — 重复入口点（CS0017）
  - `MainForm.cs` — 非 partial `MainForm` 与 partial 拆分冲突（CS0262）
  - `TaskInfo.cs` — 与 `ZhuaQianDesktop.cs` 重复类型（CS0101）
- `src/tests/SelfTest.cs` 内联重定义生产类（PermissionGate/ConfigStore/Chunker…），仅供其独立 EXE，**不可与生产源同编**。
- `build.ps1` 与 `run-tests.ps1` 现已**动态枚举** `src/**/*.cs`（build 排除 tests/；run-tests 含 tests
  但排除 SelfTest/ConfigStoreTests），永不漂移。run-tests 用 `/main:TestRunner` 指定测试入口。
- `csproj` 仍是**手写 Compile 清单**，新增 `src/*.cs` 必须手动登记（或改为
  `<Compile Include="**\*.cs" Exclude="Program.cs;MainForm.cs;TaskInfo.cs;tests\**" />` 自同步）。
- 行预算：`check-architecture.ps1` 实测主文件 ~3624 非空行 = `maxMainLines`（零余量），其他 .cs ≤900。
- **沙箱禁止 `csc.exe`**，C# 改动无法本地编译/跑测；真编译只走用户本地 `build.ps1`/`run-tests.ps1` 或 GitHub Actions。

## 模块族（按 Epic）

- A 文档/知识：OfficeExporter, Redactor, Chunker, VectorIndex, OfficeTemplateLibrary(F1)
- B UI：MainForm 拆成 `ui/MainForm.*.cs` 多个 partial + 主文件 `ZhuaQianDesktop.cs` 持核心 partial
- C Agent Loop：AgentPlan, AgentPlanState/Runner, IAsyncCommandExecutor/CommandResult, AgentPipeline.RunAsync
- D Coding Agent：CodingAgentSession, WorkspaceScanSummary, CommandRunRecorder
- E Tool Ecosystem：Plugins/PluginManifest + Agent/Hooks/{HookKind,HookContext,IPluginHook,HookRegistry}
  （HookRegistry.Run 同步隔离，抛错不中断管线；AgentPipeline 在命令前后跑 BeforeCommand/AfterCommand）
- F 办公模板：OfficeTemplateLibrary + TestOfficeTemplateLibrary

## 待办（用户拍板）

- push GitHub（需仓库 + 凭证）关闭"验证 vs 打包"环
- Installer / 真实 MCP client / 企业安全内核监控 / 完整 UI 自动化测试 / 标准测试框架
