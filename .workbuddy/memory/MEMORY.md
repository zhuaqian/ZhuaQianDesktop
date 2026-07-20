# ZhuaQian Desktop — 项目长期记忆

local-first Windows AI 桌面智能体（C# + .NET Framework 4.8 WinForms）。核心铁律：
**所有真实副作用必须过 `Command → PermissionGate →(Approval)→ Executor → AuditLog + OutputsHub` 单一管道**。

## 产品定位（用户口述 2026-07-20）

用户定义的产品范围：控制电脑 / 改本地文件 / 优化 / PPT / 生成 HTML 演示文稿 / 连服务器(SSH) / 线上改代码 / 连 GitHub(开源上传仓库) / web 登录验证 / 内置开源浏览器。
- 已实现：控制电脑、改文件、优化、PPT、HTML 演示文稿、连服务器(SSH)、线上改代码、**内置浏览器(Playwright/Chromium，开源 MIT，模块 G/H)** —— 代码级，待本地 build.ps1 编译验证。
- **已建(代码级, 待本地 build.ps1 编译 + 用户 PAT)**：① 多平台开源发布 `GitHostPublisher`(`IGitHost`: GitHub/Gitee/GitLab；REST 建公开仓 + git CLI token-in-URL 推送，走 `PublishRepo` 单管道；PAT 存 `ConfigStore` `<host>_token`，由 `/settoken` 或自然语言设置，不进命令参数/审计日志)；② 浏览器登录态保存/恢复(`BrowserAgentClient.SaveSessionAsync` + `BrowserControlExecutor` 的 login/savesession/loadsession，经 `BrowserSessionHub` 共享会话跨命令复用，复用内置 Chromium)。`MainForm.Publish.cs` 路由 `/publish`、`/settoken`、`/browser` 及自然语言。

## 构建 / CI 约定（关键，违反即编译失败）

- 入口点：`src/ZhuaQianDesktop.cs` 的 `Main()`。生产 EXE 由 `src/build.ps1`（csc 逐文件编译）产出。
- **三个死重复文件，任何构建都禁止编译**：
  - `Program.cs` — 重复入口点（CS0017）
  - `MainForm.cs` — 非 partial `MainForm` 与 partial 拆分冲突（CS0262）
  - `TaskInfo.cs` — 与 `ZhuaQianDesktop.cs` 重复类型（CS0101）
- `src/tests/SelfTest.cs` 内联重定义生产类（PermissionGate/ConfigStore/Chunker…），仅供其独立 EXE，**不可与生产源同编**。
- `build.ps1` 与 `run-tests.ps1` 现已**动态枚举** `src/**/*.cs`（build 排除 tests/；run-tests 含 tests
  但排除 SelfTest/ConfigStoreTests），永不漂移。run-tests 用 `/main:TestRunner` 指定测试入口。
- `csproj` 已改为**自同步 glob**（2026-07-20）：`<Compile Include="**\*.cs" Exclude="Program.cs;MainForm.cs;TaskInfo.cs;tests\**;packages\**" />`。新增 `src/*.cs` 自动编入，无需手动登记；排除集与 build.ps1/run-tests.ps1 一致。此前漏登的 `Agent/RemoteHostExecutor.cs` 与 `ui/MainForm.ModelSwitcher.cs` 现已自动编入。
- 行预算：`check-architecture.ps1` 主文件非空行 = `maxMainLines`（**棘轮只降不升**；拆分后当前 **3422**，零余量，任何新增非空行即 FAIL），其他 .cs ≤900。
- **沙箱禁止 `csc.exe`**，C# 改动无法本地编译/跑测；真编译只走用户本地 `build.ps1`/`run-tests.ps1` 或 GitHub Actions。
- **第一个外部 NuGet 依赖 = Microsoft.Playwright**（headless Chromium，用于 JS 渲染/反爬/登录态抓取）。`src/packages.config` 固定 1.48.0；`build.ps1`/`run-tests.ps1` 动态 glob `src/packages/` 解析 `Microsoft.Playwright.dll` + 传递依赖（System.Text.Json / Microsoft.Bcl.AsyncInterfaces / System.Runtime.CompilerServices.Unsafe / System.Threading.Tasks.Extensions），`/reference` 它们；build.ps1 还把运行时 DLL + `runtimes/win-*` 原生驱动复制到 EXE 旁；缺包时清晰报错。csproj 加 `HintPath`(netstandard2.0) 供 VS。浏览器二进制首次 `Playwright.InstallAsync()` 自动下载。
- **`LangVersion` = 7.3**：不可用 C# 8 特性（如 `await using`）。`BrowserRenderClient` 用显式 `CloseAsync`/`DisposeAsync`(try/finally) 释放，不要改回 `await using`。

## 模块族（按 Epic）

- A 文档/知识：OfficeExporter, Redactor, Chunker, VectorIndex, OfficeTemplateLibrary(F1)
- B UI：MainForm 拆成 `ui/MainForm.*.cs` 多个 partial（含 `MainForm.LlmDocHelpers.cs` 抽出的 LLM 辅助 + 文档解析方法）+ 主文件 `ZhuaQianDesktop.cs` 持核心 partial。**双皮肤（2026-07-20）**：`src/ui/ThemeManager.cs`(`ThemeName{Light,Dark}` + 静态调色板 + `Apply(Control)` 递归换肤含 tool strip + `theme.json` 持久化) + `src/ui/MainForm.Theme.cs`(`OnLoad` 调 `Apply`+`RestyleTree` 重放按钮 role，订阅 `ThemeChanged`)+ `SettingsDialog` "主题"下拉；`zq*` 11 字段改为读 `ThemeManager` 的计算属性，让 `StyleButton/StyleInput/StyleList` 自动随主题。
- C Agent Loop：AgentPlan, AgentPlanState/Runner, IAsyncCommandExecutor/CommandResult, AgentPipeline.RunAsync
- D Coding Agent：CodingAgentSession, WorkspaceScanSummary, CommandRunRecorder, **PatchExecutor(补丁内核), FixLoopRunner(自愈闭环+IFixStrategy), GitWorkflowExecutor(git 工作流)**；AgentPipelineFactory 已注册后两者。闭环算法有 `outputs/coding-loop/` Python 原型端到端验证（沙箱禁 csc，C# 需本地 build.ps1）。**2026-07-20 通用化（commit 45a3971）**：`WorkspaceScanSummary.BuildCommand/TestCommand` 与 `CodingAgentSession.Run` 改用 `ProjectAnalyzer` 探测真实构建/测试命令（不再写死 build.ps1）；`GuardedCommandRunRecorder` 增 `allowedPrograms`，解析 `powershell -Command <prog>` 包装后的真实程序，允许外国仓库的 dotnet/npm/cargo/go/mvn/gradle/make 等命令在 root 内执行（默认 PermissionGate 不再 Deny）。`CollectRiskNotes` 的主文件 3895 规则仍本项目特定，但已用 `File.Exists` 门控且被 `TestWorkspaceScanSummary` 锁死，外国仓库安全 no-op。
  **端到端 demo 已就绪（2026-07-20）**：`outputs/coding-agent-demo/DemoApp` 是外国 dotnet 仓库（net8.0 控制台，故意缺 `;` 触发 CS1002），已独立 `git init` 提交；`docs/CODING_AGENT_DEMO.md` 是运行手册。验证需用户本地 `build.ps1` + .NET 8 SDK：点 "Diagnose & Fix Project" 选该文件夹 → 闭环自动补 `;` → 重建通过 → 报告可提交。这是"能修别人仓库"的实证。
- E Tool Ecosystem：Plugins/PluginManifest + Agent/Hooks/{HookKind,HookContext,IPluginHook,HookRegistry}
  （HookRegistry.Run 同步隔离，抛错不中断管线；AgentPipeline 在命令前后跑 BeforeCommand/AfterCommand）
- F 办公模板：OfficeTemplateLibrary + TestOfficeTemplateLibrary
- G 浏览器渲染抓取（2026-07-18 新增）：BrowserRenderClient(Playwright 封装，产出 WebPageFetchResult+Html 字段) + BrowserFetchExecutor(命令 BrowserFetch, permNetworkUpload) + WebResearchFetcher(静态 FetchPage 优先 + 浏览器回退，喂 WebPageReportBuilder)。详档 `docs/patches/BROWSER_RENDER_INTEGRATION.md`。
- H 浏览器交互 + 桌面控制闭环（2026-07-18 新增，2026-07-20 生产化）：BrowserAgentClient(持久会话交互内核：Navigate/Click/ClickText/Fill/Type/PressKey/Submit/Screenshot/DomSnapshot/Text/Title/Url) + BrowserControlExecutor(命令 BrowserControl, permNetworkUpload) + DesktopScreenCapture(GDI `CopyFromScreen` 截屏，需 System.Drawing 引用) + ScreenCaptureExecutor(命令 ScreenCapture, permScreenshot) + TaskAgentRunner(感知→决策→行动→验证闭环，IEnvironment/ITaskPolicy + 浏览器/桌面适配器 + Scripted/Delegate 策略)。均过 AgentPipeline 单一管道。**2026-07-20 生产化落地**：`TaskAgentRunner.cs` 新增 `LlmTaskPolicy`（接 LLM 的 `ITaskPolicy` 实现，仅依赖聊天函数委托、`JavaScriptSerializer` 解析模型 JSON 决策、解析失败非严格回退 wait）；`RunAsync` 增可选 `actuateOverride`；`TaskAgentGating.GatedActuator(AgentPipeline, taskId, sharedSession)` 把动作改走管道（浏览器动词映射到 `BrowserControl` 命令并共享 `BrowserAgentClient` 会话，桌面动词透传）。`docs/TASK_AGENT_LOOP.md` 是使用与接线说明；`src/tests/TestLlmTaskPolicy.cs` 为假聊天函数单测。**仅剩组合根接线**（在 `MainForm` 把 `LlmTaskPolicy`+`GatedActuator` 接进 UI 入口）待做。闭环算法有 `outputs/browser-task-loop/` Python 原型端到端验证（沙箱禁 csc，C# 需本地 build.ps1）。新增 .cs 已被 build.ps1 动态 glob 收录，且 csproj 现已自同步 glob（无需手动登记）；DesktopScreenCapture 用到 System.Drawing，确认 build.ps1 的 csc `/reference` 含 System.Drawing.dll。

## 待办（用户拍板）

- push GitHub（需仓库 + 凭证）关闭"验证 vs 打包"环
- Installer / 真实 MCP client / 企业安全内核监控 / 完整 UI 自动化测试 / 标准测试框架

## 能力边界（2026-07-20 验证 + 2026-07-19 补齐实现，见 docs/CAPABILITY_VERIFICATION.md）
- ✅ 真实：改本地文件/存储（`AgentPipeline`+`ExportFileExecutor`/`OrganizeFolderExecutor`/`PatchExecutor`/`RollbackExecutor`+`VectorIndex`/`AuditLog`/`ConfigStore`）；PPT/Word/Excel **读+写**（`OfficeExporter` ZipArchive 生成 OOXML + `Extract*` 读）；Coding/vibe coding（`DiagnoseFix` 自愈闭环）；修 bug（同上闭环，支持外部仓库 root）。
- ✅ 已补齐（2026-07-19，commit `b3762b4`；**仍待用户本地 `build.ps1`+`run-tests.ps1` 编译验证**，沙箱禁 `csc`）：
  - 落盘 md 文档：`WriteFileExecutor`(命令 `WriteFile`, `permFileWrite`) + `LlmBridge` 模型注入，经单管道审批写盘（`MainForm.Capabilities.cs` 的 `ExecuteSaveDocument`）。
  - 做网站：`SiteGenerator`(调 LLM 生成 `index.html`/`style.css`/`app.js`) + `MainForm` "build website" 意图，文件经 `WriteFile` 管道写盘。
  - Office 优化：`OfficeOptimizer` 非破坏式 OOXML 瘦身（删空 `<w:p>`/`<a:p>`/`<row>`），产出 `*_optimized` 副本，经 `WriteFile` base64 管道写盘。
  - 拓宽自愈：`ModelFixStrategy`(`FixLoopRunner.IFixStrategy`) 在 `RuleBasedFixStrategy` 找不到安全修复时调 LLM 出 JSON 补丁，回填 `DiagnoseFixFixStrategy`；`WireLlmBridge()` 已接进 DiagnoseFix 路径。
- ⚠️ 仍边界：`OfficeOptimizer` 只删空段落/空行、不动版式、不整页/整表删除（避免破坏关系索引）；模型补丁若 `LlmBridge` 未接则诚实报告"无法修复"；自主写码覆盖面仍取决于模型质量。
- 验证方法：沙箱禁 `csc`，只能代码级验证；真机需用户本地 `build.ps1`+`run-tests.ps1`。
