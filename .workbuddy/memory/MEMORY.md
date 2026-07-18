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
- **第一个外部 NuGet 依赖 = Microsoft.Playwright**（headless Chromium，用于 JS 渲染/反爬/登录态抓取）。`src/packages.config` 固定 1.48.0；`build.ps1`/`run-tests.ps1` 动态 glob `src/packages/` 解析 `Microsoft.Playwright.dll` + 传递依赖（System.Text.Json / Microsoft.Bcl.AsyncInterfaces / System.Runtime.CompilerServices.Unsafe / System.Threading.Tasks.Extensions），`/reference` 它们；build.ps1 还把运行时 DLL + `runtimes/win-*` 原生驱动复制到 EXE 旁；缺包时清晰报错。csproj 加 `HintPath`(netstandard2.0) 供 VS。浏览器二进制首次 `Playwright.InstallAsync()` 自动下载。
- **`LangVersion` = 7.3**：不可用 C# 8 特性（如 `await using`）。`BrowserRenderClient` 用显式 `CloseAsync`/`DisposeAsync`(try/finally) 释放，不要改回 `await using`。

## 模块族（按 Epic）

- A 文档/知识：OfficeExporter, Redactor, Chunker, VectorIndex, OfficeTemplateLibrary(F1)
- B UI：MainForm 拆成 `ui/MainForm.*.cs` 多个 partial + 主文件 `ZhuaQianDesktop.cs` 持核心 partial
- C Agent Loop：AgentPlan, AgentPlanState/Runner, IAsyncCommandExecutor/CommandResult, AgentPipeline.RunAsync
- D Coding Agent：CodingAgentSession, WorkspaceScanSummary, CommandRunRecorder, **PatchExecutor(补丁内核), FixLoopRunner(自愈闭环+IFixStrategy), GitWorkflowExecutor(git 工作流)**；AgentPipelineFactory 已注册后两者。闭环算法有 `outputs/coding-loop/` Python 原型端到端验证（沙箱禁 csc，C# 需本地 build.ps1）。`WorkspaceScanSummary` 仍硬编码本项目，待重构成通用扫描器。
- E Tool Ecosystem：Plugins/PluginManifest + Agent/Hooks/{HookKind,HookContext,IPluginHook,HookRegistry}
  （HookRegistry.Run 同步隔离，抛错不中断管线；AgentPipeline 在命令前后跑 BeforeCommand/AfterCommand）
- F 办公模板：OfficeTemplateLibrary + TestOfficeTemplateLibrary
- G 浏览器渲染抓取（2026-07-18 新增）：BrowserRenderClient(Playwright 封装，产出 WebPageFetchResult+Html 字段) + BrowserFetchExecutor(命令 BrowserFetch, permNetworkUpload) + WebResearchFetcher(静态 FetchPage 优先 + 浏览器回退，喂 WebPageReportBuilder)。详档 `docs/patches/BROWSER_RENDER_INTEGRATION.md`。
- H 浏览器交互 + 桌面控制闭环（2026-07-18 新增）：BrowserAgentClient(持久会话交互内核：Navigate/Click/ClickText/Fill/Type/PressKey/Submit/Screenshot/DomSnapshot/Text/Title/Url) + BrowserControlExecutor(命令 BrowserControl, permNetworkUpload) + DesktopScreenCapture(GDI `CopyFromScreen` 截屏，需 System.Drawing 引用) + ScreenCaptureExecutor(命令 ScreenCapture, permScreenshot) + TaskAgentRunner(感知→决策→行动→验证闭环，IEnvironment/ITaskPolicy + 浏览器/桌面适配器 + Scripted/Delegate 策略)。均过 AgentPipeline 单一管道。闭环算法有 `outputs/browser-task-loop/` Python 原型端到端验证（沙箱禁 csc，C# 需本地 build.ps1）。`ITaskPolicy` 生产实现（接 LLM）待写；生产化需把 env.Actuate 改走 PermissionGate 管线。新增 .cs 已被 build.ps1 动态 glob 收录，但 VS 的 csproj 手写清单需手动加；且 DesktopScreenCapture 用到 System.Drawing，确认 build.ps1 的 csc `/reference` 含 System.Drawing.dll。

## 待办（用户拍板）

- push GitHub（需仓库 + 凭证）关闭"验证 vs 打包"环
- Installer / 真实 MCP client / 企业安全内核监控 / 完整 UI 自动化测试 / 标准测试框架
