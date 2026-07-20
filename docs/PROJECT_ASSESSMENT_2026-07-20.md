# ZhuaQian Desktop — 项目评估报告（2026-07-20）

> 评估范围：截至 `1e9dfca` 的全部源码、构建/CI 脚本、文档与待办。
> 评估性质：**代码级静态评估**。沙箱禁用 C# 编译器，本文所有"已实现"均指代码已落地并通过架构/行数预算检查；**未经真机编译运行验证**（见 §5 风险）。

## 1. 执行摘要

| 维度 | 评分（5 分制） | 结论 |
|---|---|---|
| 功能完成度 | 4.5 | 用户定义的 11 项能力全部代码级落地（含本轮新增的开源发布 / 浏览器登录态 / 双主题） |
| 架构健康度 | 4.0 | 单一命令管道 + 严格行数预算 + 模块化 partial；主文件已到预算上限（技术债） |
| 代码质量 | 3.5 | 集中式样式/单管道设计清晰；但新增模块缺专门单测，部分路径靠烟雾测试 |
| 质量保障 | 3.0 | 自建测试框架 20 类 + CI 已修复；**唯一盲区 = 沙箱/本地未真编译**（CI 修好后可闭环） |
| 可维护性 | 3.5 | 文档较全；主文件 3422 行零余量是最大瓶颈，未来加功能须 in-place 改造 |
| 交付就绪度 | 3.0 | 需用户本地 `build.ps1` + `run-tests.ps1` 验证 + push 触发 CI 才闭环 |

**一句话**：功能面已非常完整（11/11 能力代码就绪），工程约束（预算/管道/CI）已建好；**当前唯一真实风险是"未真编译验证"**，而 CI 修复后该风险可在你本地一次 push 内消除。

---

## 2. 项目规模与结构

- **源码规模**：118 个生产 `.cs` 文件（23,779 非空行）+ 22 个测试文件；仓库共 264 个受跟踪文件，33 次提交，分支 `main`，工作区仅记忆文件有未提交改动。
- **模块分布**：
  - `Agent/` 45 — 命令执行器、管线、Coding Agent、发布器、浏览器控制
  - `ui/` 24 — `MainForm.*` 拆分的多个 partial + 对话框 + 主题
  - `Tools/` 17 — 浏览器客户端、导出、向量索引、截图等
  - `Core/` 8 — 配置、权限、审计、分块等
  - `tests/` 22 — 自建测试框架
- **外部依赖**：仅 **1 个 NuGet** = `Microsoft.Playwright`（+ 传递依赖）；其余全为 .NET Framework 4.8 框架程序集。依赖面极轻，部署简单。
- **运行时依赖**：Playwright 首次需下载 Chromium 二进制（`Playwright.InstallAsync()`）；SSH/远程需本机已装 OpenSSH；发布需用户 PAT + 本机 `git`。

---

## 3. 能力完成度矩阵（用户定义 11 项）

| # | 能力 | 状态 | 落点（已提交代码） | 备注 |
|---|---|---|---|---|
| 1 | 控制电脑 | ✅ 代码级 | `TaskAgentRunner`（感知→决策→行动→验证闭环）+ `DesktopScreenCapture` + `BrowserControl` | 全过单管道 |
| 2 | 改变本地文件 | ✅ 代码级 | `WriteFileExecutor` / `PatchExecutor` / `OrganizeFolderExecutor` / `ExportFileExecutor` | 含回滚 |
| 3 | 优化 | ✅ 代码级 | `OfficeOptimizer`（非破坏式 OOXML 瘦身） | 只删空段/空行 |
| 4 | PPT / Office | ✅ 代码级 | `OfficeExporter`（ZipArchive 生成 PPTX/Word/Excel）+ `Extract*` 读 + `OfficeTemplateLibrary` | 读+写 |
| 5 | 生成 HTML 演示文稿 | ✅ 代码级 | `SiteGenerator`（调 LLM 生成 index.html/style.css/app.js）→ `WriteFile` 写盘 | — |
| 6 | 连服务器 | ✅ 代码级 | `RemoteHostExecutor`（复用本机 OpenSSH/scp，不外存密码）→ 单管道 | 非 RDP |
| 7 | 线上改代码 | ✅ 代码级 | `CodingAgent` 自愈闭环 + `GitWorkflowExecutor`（diff/commit），支持外部仓库 | 有端到端 demo |
| 8 | 开源上传仓库 | ✅ 代码级（本轮） | `GitHostPublisher`：`IGitHost`(GitHub/Gitee/GitLab) REST 建仓 + git 推；PAT 本地存 | 需真实 PAT |
| 9 | web 登录验证 | ✅ 代码级（本轮） | `BrowserAgentClient.SaveSession` + `BrowserSessionHub` 共享会话 + `login/savesession/loadsession` | 复用内置 Chromium |
| 10 | 内置开源浏览器 | ✅ 代码级 | Playwright/Chromium（MIT）模块 G/H | 已存在，本轮强化登录态 |
| 11 | 双皮肤（浅/深） | ✅ 代码级（本轮） | `ThemeManager` + `MainForm.Theme` + SettingsDialog 主题下拉 | 持久化 theme.json |

**结论**：用户本轮明确的全部诉求（控制/改文件/优化/PPT/HTML 演示/连服务器/线上改码/开源上传/web 登录/内置浏览器）均已代码级覆盖。

---

## 4. 架构健康度

### 4.1 单一命令管道（核心铁律）✅
所有真实副作用过 `Command → PermissionGate →(Approval)→ Executor → AuditLog + OutputsHub`。已注册 **17 个 `ICommandExecutor`**：
`BrowserControl / BrowserFetch / ComputerControl / DiagnoseFix / ExportFile / GitHostPublisher / GitWorkflow / OfficeTemplate / OrganizeFolder / Patch / PluginRun / ProcessManage / RemoteHost / Rollback / ScreenCapture / WebSearch / WriteFile`。
覆盖面广，无已知的"绕过管道"硬副作用。

### 4.2 行数预算（棘轮，只降不升）✅ 通过但有硬约束
- 主文件 `ZhuaQianDesktop.cs` = **3422 非空行，恰好等于预算上限 3422（零余量）**。任何净增行即 FAIL。
- 其它 `.cs` 全部 ≤ 900（最大为 `GitHostPublisher.cs` 342 行）。
- **含义**：未来新功能**不能再往主文件加行**，只能 (a) in-place 同义替换，(b) 新建 partial/独立文件，(c) 拆分主文件。这是项目最大技术债。

### 4.3 模块化与守卫 ✅
- 浏览器相关类型用 `#if PLAYWRIGHT` 守卫（2 文件），默认构建安全。
- `MainForm` 已拆为多个 partial，降低单文件复杂度。
- 集中式样式系统 `zq*` 已主题化，新增 `ThemeManager` 统一调色板。

---

## 5. 质量保障与风险

### 5.1 测试 ✅（框架自建）
- 20 个测试类（19 个 `public static int RunAll()` 入口），由 `TestRunner` 驱动；历史本地运行 **219 断言通过 / 0 失败**（2026-07-18 记录）。
- **盲点**：本轮新增的 `GitHostPublisher` / `ThemeManager` / `BrowserAgentClient` 登录态暂无专门单测，仅依赖 CI 编译 + 入口烟雾测试。

### 5.2 CI ✅ 已修复（关键进展）
- `.github/workflows/tests.yml` 原 `release` 步骤指向不存在的根 `build.ps1` → 已修正为 `src/build.ps1`。
- `build.ps1` / `run-tests.ps1` 已补 `System.Net.Http.dll` + `System.Xml.dll`（修复 `GitHostPublisher` 的 `HttpClient` 编译阻断），`csc` 路径改候选解析，输出改到 gitignored 的 `dist/`。
- **效果**：你 push 到 GitHub 后，CI 自动编译 + 跑测 + 打 tag 打包 → 从根上消除"沙箱禁编译器"的验证盲区。

### 5.3 风险矩阵

| 风险 | 等级 | 说明 | 缓解 |
|---|---|---|---|
| 沙箱/本地未真编译 | 🔴 高 | 所有 C# 仅代码级，未真机跑通 | CI 已修好；你本地 push 一次即闭环 |
| 主文件 3422 行零余量 | 🔴 高 | 加功能只能 in-place/拆文件 | 已建立"在档改造"纪律；长期需拆分主文件（高风险重构） |
| 新模块无专门单测 | 🟡 中 | 发布/主题/登录态靠烟雾测试 | 建议补 `TestGitHostPublisher` / `TestThemeManager` |
| Playwright 本机下载 | 🟡 中 | 首次需 `Playwright.InstallAsync()` 拉 Chromium | 文档已注明；属一次性 |
| 发布需真实凭证 | 🟡 中 | GitHub fine-grained PAT 用 `x-access-token:` 前缀 | 代码注释已写；PAT 本地存不进日志 |
| 文档过时 | 🟢 低 | 已修正"做网站 ❌"等 | CAPABILITY_VERIFICATION 已同步 |

---

## 5.4 本轮已修复的安全缺口（用户审查发现）

用户审查本轮新增功能时指出两个凭证/会话明文存储风险，已全部代码修复（代码级，待本地编译验证）：

| 缺口 | 原实现 | 修复 |
|---|---|---|
| 开源发布的 PAT 持久化到 `.git/config` | `git remote add origin https://<PAT>@host/...` 把令牌长期明文写入持久化 remote，`git remote -v` 原样回显 | Push 改为 `git push <带PAT的URL>`，令牌只存在于本次命令；推送后立即 `remote remove origin` 并 `remote add origin <无凭证 clone URL>`，`.git/config` 永不存令牌（新增 `IGitHost.CleanCloneUrl`） |
| 浏览器登录会话明文落盘 | `StorageStateAsync(Path=path)` 把 cookies/localStorage 明文写盘（等同免密登录凭证） | 新增 `Core/SecretProtector`（DPAPI `CurrentUser`，复用 `MainForm.Settings.cs` 的 API key 加密机制）；`SaveSessionAsync` 导出到临时明文→DPAPI 加密写盘→删临时；`StartAsync` 解密到临时文件→`StorageStatePath`→context 创建后立即删临时 |

修复后：两个敏感能力（发布凭证、登录会话）的落盘数据均为本机当前用户的 DPAPI 密文，不再以明文或持久化令牌形式暴露。详见 `docs/` 记忆与 `src/Core/SecretProtector.cs`。

## 6. 路线图与建议

**立即可做（你侧，无需我改代码）**
1. 本地跑 `src/build.ps1` + `src/scripts/run-tests.ps1`，确认编译全绿（预期无新增测试，契约不变）。
2. `git push` 触发 CI，看 Actions 是否绿——这是"真编译验证"的闭环。
3. 端到端验证 11 能力，重点：发布需真实 PAT + 本机 git；浏览器登录态需 Playwright 二进制已下载。

**短期（我可做）**
4. 补 `TestGitHostPublisher` / `TestThemeManager` / `TestBrowserSession` 单测，消除 §5.3 中风险。
5. 把"浏览器登录态保存/恢复"接进设置对话框（目前仅 `/browser` 命令）。
6. 发布前自动生成 README（目前只生成 `.gitignore`）。

**中期（需谨慎，建议单独规划）**
7. **主文件拆分**：解除 3422 行瓶颈，是长期可维护性的关键，但属高风险重构，应单独立项、配回归测试。
8. 真实 MCP client / 企业安全内核监控 / 完整 UI 自动化测试——当前待办清单里尚未启动。

---

## 7. 验证状态声明（诚实边界）

- ✅ 已静态验证：架构预算、管线注册、文件存在性、依赖清单、CI 脚本正确性、文档一致性。
- ⚠️ 未验证：C# 真编译、运行时行为、端到端能力、Playwright 二进制可用性。
- 全部"已实现"均指**代码已落地 + 通过静态检查**，不代表已真机运行。请按 §6 第 1–2 步闭环验证。
