# ZhuaQian Desktop 开发路线图（2026-07-20）

> 本文基于用户对代码库的多轮了解（单一命令管道、`ICommandExecutor`、PermissionGate、AuditLog、Rollback、Coding Agent 闭环、浏览器控制、远程主机、GitHostPublisher、SecretProtector）整理而成。
> 用户的原始建议按"近期 / 中期 / 长期"三层给出；本文在落盘时做了**吸收与改进**：每条都补齐了具体的代码落点、与现有机制的连接方式、以及工程约束（架构棘轮、沙箱限制），并在结尾给出明确的优先取舍。
> 配套文档：`docs/PROJECT_ASSESSMENT_2026-07-20.md`、`docs/CAPABILITY_VERIFICATION.md`、`docs/TASK_AGENT_LOOP.md`、`docs/CODING_AGENT_DEMO.md`。

---

## 0. 总纲：信任基础设施 > 能力列表

用户的收尾判断是本文的纲：**这类工具两三年后的竞争，不会是"谁的能力列表更长"，而是"信任基础设施做得多细"**——批准体验、回滚深度、审计能否经得起事后追责。

本项目从第一版就把 `Command → PermissionGate → Executor → AuditLog + OutputsHub` 定为铁律，今天看是工程负担，未来看是护城河。因此路线图的所有条目都用同一把尺子衡量：**它是在加固信任基础设施，还是只在延长能力清单？** 优先做前者。

承接上一轮（commit `8632b1c`）已关闭的两个安全洞——PAT 不再落盘 `.git/config`、浏览器会话用 DPAPI（`SecretProtector`）加密——本文的中期/长期条目是那次修复的自然延伸，不是另起炉灶。

### 现状快照（已核对代码）
- **17 个 `ICommandExecutor`**（`AgentPipelineFactory` 注册计数）：`ComputerControl`、`ExportFile`、`DiagnoseFix`、`GitWorkflow`、`OfficeTemplate`、`OrganizeFolder`、`Patch`、`PluginRun`、`WriteFile`、`WebSearch`、`Rollback`、`RemoteHost`、`ProcessManage`、`BrowserFetch`、`BrowserControl`、`ScreenCapture`、`PublishRepo`（GitHostPublisher）。
- **管道是单线程同步的**：`AgentPipeline.Run` 顺序执行单个 Plan 内的 Command，`RequestApproval` 是同步回调。
- **`AuditLog` 是本地普通文件**（`Core/AuditLog.cs`），`RollbackExecutor` 提供单动作级回滚。
- **`PluginManifest` 已有路径穿越防护**，下一步是加签名 + 能力声明。
- **`Redactor` 已存在**，是脱敏机制的正向压力测试对象。
- **工程约束（务必遵守）**：
  - 架构棘轮：`check-architecture.ps1` 要求主文件非空行 **≤3422**（当前已满，零余量），其他 `.cs` **≤900**。`Register` 新 Executor 的"组合根"接线必须保持精简，新增模块拆成小文件，不要让 `ZhuaQianDesktop.cs` 增加非空行。
  - 沙箱禁 `csc`：所有 C# 改动只能代码级验证，真编译走用户本地 `build.ps1` / `run-tests.ps1` 或 GitHub Actions（`.github/workflows/tests.yml`）。本文是设计文档，不等同于已落地。

---

## 一、近期：顺着现有管道自然长出（不改架构）

这些能力在技术上 = "再 `pipeline.Register(new XxxExecutor(...))` 一次"，风险在于**产品侧**（审批疲劳、越权），不在架构侧。关键纪律：**任何新 Executor 都不能绕过 PermissionGate，首次批准绝不自动等价于永久批准。**

### 1.1 邮件 / 日历读写 Executor
- **落点**：新增 `src/Agent/MailCalendarExecutor.cs`（`ICommandExecutor`，≤900 行），命令类型如 `MailRead` / `CalendarQuery` / `MailDraft`。
- **权限分级**：与 `RemoteHostExecutor` 同级（影响范围超出本机），复用"高风险审批卡片**逐字展示内容**"这条已在用的规则——草稿正文、收件人、会议主题必须原样进 `command.DisplaySummary` 让用户在卡片上看到。
- **为什么值得做**：桌面 Agent 碰邮箱是高频场景（"回这封邮件""明天会议提醒我准备什么"），且它是检验"跨本机副作用 + 高风险审批"组合的最佳压力测试。
- **检查点**：只读类（`MailRead`/`CalendarQuery`）可走 `PermissionDecision.Ask` 轻量确认；写类（`MailDraft` 发送）必须走完整高风险卡片。

### 1.2 剪贴板历史 + 跨应用上下文捕获
- **落点**：扩展现有 `SystemDiagnostics`（已在采集窗口标题）到剪贴板与当前应用上下文；新增 `src/Agent/ContextCaptureExecutor.cs` 或一个后台采集器（后台采集器不走管道，但**导出/利用**上下文时走管道）。
- **与 `Redactor` 的连接（重要）**：这类能力天然会采集可能含敏感信息的文本，必须让 `Redactor` 全程介入——这是把现有脱敏机制推到真实负载下的正向压力测试。建议在 `Redactor` 增加"剪贴板/上下文"专用策略。
- **产品价值**：让用户"正在 Excel 里问这列怎么用公式"时无需解释上下文。
- **检查点**：采集到的原始上下文**绝不**直接进 AuditLog 明文，必须经 `Redactor` 后才可记录摘要。

### 1.3 定时 / 触发式任务（Scheduled Agent）
- **落点**：给 `AgentPlanRunner` 增加"Windows 计划任务"触发源；新增 `src/Agent/ScheduledTaskExecutor.cs` 或在 `MainForm` 侧加调度层。**不要改 `AgentPipeline` 本身**——管道保持同步，调度器只是"按时新建一个 Plan 丢给管道"。
- **关键设计点（用户已指出，必须实现）**：**定时任务的首次批准 ≠ 永久批准**。需要独立的"信任期限（trust TTL）"概念，例如"允许这个计划任务自动整理下载文件夹，直到我手动关闭 / 直到 7 天后"。信任期限到期或用户撤销后，下一次执行重新走 `PermissionGate`。
- **为什么是分水岭**：从"工具"到"助理"的分界线通常就在"能不能替我按时主动做事"。但实现错了就会变成绕开审批的后门——所以信任期限是**硬约束**，不是可选项。
- **检查点**：调度层的所有执行仍逐次进 `AuditLog`，并标注 `scheduled=true` 与当时的信任期限 ID，便于事后复盘"这一步是定时跑的还是我手动点的"。

### 1.4 插件市场化 + 签名清单
- **落点**：扩展 `src/Plugins/PluginManifest.cs`——在 manifest 里增加 `PublisherSignature`（发布者签名）与 `CapabilityDeclarations`（如 `network`、`fileWrite`、`clipboard`）。UI 上像手机 App 权限提示一样逐条展示。
- **机制升级**：把现有"人写的 allowlist"升级为"插件自己声明、系统强制核实"——加载插件时校验签名 + 把声明的能力映射到对应的 `PermissionGate` 权限名，未声明的能力一律 Deny。
- **检查点**：签名验证失败 / 声明越权 → 插件禁止加载，并在日志留痕。这直接复用上一轮 `SecretProtector` 的密钥管理能力做发布者公钥存储。

### 近期落地顺序建议
| 优先级 | 条目 | 理由 |
|---|---|---|
| 1 | 1.4 插件签名 | 投入可控、复用 SecretProtector、且是信任基础设施基座 |
| 2 | 1.3 定时任务（含信任期限） | 产品分水岭，但必须先有信任期限原语才安全 |
| 3 | 1.1 邮件/日历 | 复用高风险审批卡片，风险可控 |
| 4 | 1.2 上下文捕获 | 依赖 Redactor 先被推稳，最后做 |

---

## 二、中期：需要新架构原语才能做好的能力

这几项若现在硬塞进现有管道，会做出"看起来能用、实际有隐患"的东西。**先补原语，再做能力。**

### 2.1 多 Agent 并行协作（非单一 Plan 顺序执行）
- **现在的缺口**：`AgentPipeline` 是单线程顺序执行一个 Plan 的多个 Command。真实场景需要"一个 Agent 写代码、另一个同时跑安全审查、第三个整理文档"。
- **需补的原语**：
  1. `AuditLog` / `OutputsHub` 的**并发写入安全**（加锁或 per-agent 分片 + 合并），否则并发写会交错甚至丢记录。
  2. `PermissionGate` 的**聚合审批**——同一批审批下多个 Agent 各自要什么权限，合并成一张"这批任务总共需要这些权限"的卡片，而不是十个并发弹窗。
- **复用自身经验（用户关键洞察）**：本项目自己的开发方式（`.workbuddy` 多智能体协调、用协调文件解决"谁在改什么、避免冲突"）天然趟过这个坑。**直接把踩过的坑变成产品功能**——那套"多 Agent 任务面板 / 协调文件"经验可 1:1 复用到给最终用户的并行任务可视化。
- **落点草图**：新增 `AgentSwarm`（协调器）+ `IAuditSink`（并发安全写入接口），`AgentPipeline` 保持不动，由 `AgentSwarm` 调度多个 `AgentPipeline` 实例。

### 2.2 沙箱化执行环境（容器 / 虚拟化隔离）
- **现在的缺口**：`CodingAgentSession` / `GuardedCommandRunRecorder` 直接在用户真实系统跑 `dotnet build`。修自己项目的一个分号错误没问题；但一旦被用来"跑从网上下载的开源项目"，宿主机执行就是明确攻击面（恶意 build 脚本、恶意 postinstall 钩子）。
- **需补的原语**：**高风险来源代码 = 强制走 Windows Sandbox / WSL2 隔离容器执行**的分级机制。这是 `PermissionGate` 分级思想的延伸——不只是"要不要批准"，还有"批准之后**在哪跑**"。
- **落点草图**：`GuardedCommandRunRecorder` 增加 `ExecutionTier`（Host / WSL2 / WindowsSandbox），由策略（见 2.4）根据"代码来源是否可信"决定 tier；不可信来源强制 Sandbox。
- **行业对标**：Codex / Claude Code 走云端沙箱，桌面版对应物是本地轻量虚拟化。这是中期必须补的攻击面收敛。

### 2.3 可验证的审计链（防篡改，非普通文件）
- **现在的缺口**：`AuditLog` 是本地可被同机其他进程篡改的普通文件。要进企业场景（团队共用、合规审计），审计记录需**防篡改**。
- **需补的原语**：哈希链（每条记录带上一条的哈希）+ 可选异地同步。技术上**不需要区块链**——简单的 `hash(prevHash + record)` 链就能达到"事后改了日志能被检测到"。
- **落点草图**：`Core/AuditLog.cs` 从"追加文本"改为"追加 `[seq, prevHash, payload, hash]`"记录；提供 `VerifyChain()` 自检。异地同步作为可选 sink（复用 2.1 的 `IAuditSink`）。
- **为什么接上一轮**：这是 `8632b1c` 安全修复的自然下一环——既然已经不把凭证落明文，就该让记录凭证使用情况的日志本身也防篡改。
- **状态：✅ 代码级已实现（commit `e69876b`，2026-07-20）**——`AuditLog` 每行追加 SHA-256 链哈希 + `VerifyChain()`（返回 `AuditChainResult`，含首个被破坏行号）；`TestAuditLog.cs` 覆盖"写入后完整 / 篡改被检出 / 旧格式行容忍"三场景；架构预算 PASSED（主文件 3422 零改动，AuditLog 206 行 / TestAuditLog 77 行均 <900）。**待用户本地 `build.ps1`+`run-tests.ps1` 真编译验证**。

### 2.4 自然语言权限策略（而非硬编码 allowlist）
- **现在的缺口**：权限逻辑是代码里写死的规则（`allowedPrograms`、`allowedToolNews` 等）。每加一条策略就要改代码。
- **需补的原语**：**策略解释层**——用户用自然语言定义（"不要让它未经确认删除任何 >10MB 文件""晚 10 点后不要联网"），由一个专门组件把描述编译成 `PermissionGate` 能执行的规则对象。
- **落点草图**：新增 `PolicyCompiler`（输入自然语言/半结构化策略 → 输出 `IPermissionRule` 列表），`PermissionGate` 从"硬编码规则"改为"规则列表 + 可热加载"。这是 Agent 领域明显趋势——权限管理本身也在被 Agent 化。
- **依赖**：2.4 是 2.2（在哪跑）和 1.3（信任期限）的策略来源；三者共享同一套 `IPermissionRule` 抽象最划算。

---

## 三、长期：品类方向判断（两三年）

这五条是用户对"桌面 / 本地 Agent 这个品类往哪走"的判断，本文补充了落到本项目的接口级含义。

### 3.1 审批疲劳是头号体验杀手
能力越多（11 项能力、17+ Executor），用户越快被弹窗淹没，进而"无脑点批准"——这比技术漏洞更危险，因为它让整套权限设计形同虚设。长期需要**信任分级 + 批量批准 + 可撤销**："这类操作我已批准 20 次，要不要以后自动放行，但保留随时撤回、随时看历史"。**不是每次同一种模态弹窗。**

### 3.2 本地小模型承担隐私判断，云端大模型负责复杂推理
高频、低复杂度、涉隐私的判断（"这窗口标题要不要脱敏""这文件是不是敏感"）下沉到本地小模型 / 规则引擎；只把真正需推理的部分（生成代码、写文档）送云端。**"哪些数据离开过这台电脑"会变成用户最在意的可信度指标**——对一个主打"本地、可审计"的工具尤其如此。落点：在 `LlmBridge` 增加 `Local` / `Cloud` 路由策略，默认敏感判断走 Local。

### 3.3 ComputerControl / BrowserControl 从"坐标"走向"界面语义"
现在大概率基于坐标 / DOM 选择器，界面改版后大面积失效。行业方向是用视觉模型理解屏幕、像人一样语义定位。**接口应设计成"意图描述"而非"具体坐标"**：`ClickButton("提交")` 而非 `ClickAt(320,480)`。底层从坐标匹配换成视觉语义匹配时，上层调用无需大改。落点：`ComputerControlExecutor` / `BrowserControlExecutor` 的命令参数优先用语义意图，坐标作为 fallback。

### 3.4 审计 / 回滚从卖点变准入门槛，护城河在"回滚粒度"
今天很多桌面 Agent 没有 Rollback；本项目从第一版就有，是差异化。两三年内"能不能审计、能不能回滚"会变基本准入线（像今天没人用不能撤销的文件管理器）。**真正持续的差异化是"回滚粒度与可靠性"**——能否回滚一个跨多 Executor 的复合 Plan（改文件 + 跑 git commit + 发远程仓库，这一整条链能否一键撤销）。现状 `RollbackExecutor` 是单动作级，跨复合 Plan 回滚是值得长期投入的护城河。落点：给 `AgentPlan` 增加"补偿操作（compensation）"记录，回滚时逆序执行补偿。

### 3.5 "这是谁的决定"必须能复盘
Agent 能自主写码、发仓库、连远程后，出问题时第一问永远是"这步是用户批准的，还是 Agent 自己跑的，当时上下文是什么"。`AuditLog` 现记"做了什么"，长期需记到"**当时 Agent 的推理依据 + 用户看到的审批卡片具体措辞**"粒度，才能真复盘而非只"知道发生过"。落点：审批卡片渲染前把最终文案快照进 `AuditLog`（经 `Redactor`），Agent 决策路径同时落链。

---

## 四、改进后给出的明确取舍

用户原始建议是"三层清单"。改进后的核心追加是：**不要平铺执行，要按"信任基础设施"主线排优先级。**

**建议的首个落地组合（高杠杆、投入可控、承接上一轮安全修复）：**
1. **近期 1.4 插件签名清单** —— 复用 `SecretProtector`，把"人写 allowlist"升级为"插件自声明 + 系统核实"，是信任基座。
2. **中期 2.3 可验证审计链** —— 给 `AuditLog` 加哈希链，让记录凭证使用情况的日志本身防篡改，与 `8632b1c` 形成闭环。
3. **中期 2.4 策略解释层（先做最小可用：把硬编码 allowlist 抽象成 `IPermissionRule` 列表）** —— 为 2.2"在哪跑"和 1.3"信任期限"提供统一策略源。

**明确 NOT NOW（避免为了堆能力清单而稀释信任设计）：**
- 不要在没有"信任期限"原语前上线 1.3 定时任务（会变成后门）。
- 不要在 `AuditLog` 并发安全（2.1）落地前做多 Agent 并行。
- 不要为把 Executor 数量从 17 刷到更高而无视审批疲劳（3.1）——能力清单会很快变成大宗商品。

---

## 五、与既有文档 / 约束的衔接
- 架构棘轮：`check-architecture.ps1` 主文件 3422 行上限、其他 `.cs` ≤900——所有新增模块拆小文件，组合根接线零新增非空行。
- 沙箱禁 `csc`：本文为设计文档，落地需用户本地 `build.ps1` + `run-tests.ps1` 或 push 触发 CI 真编译。
- 安全连续性：上一轮 `8632b1c`（PAT 不落盘、会话 DPAPI）是 2.3 / 2.4 的前置；近期 1.2 上下文捕获必须过 `Redactor`，与既有脱敏机制同源。
- 交叉引用：`PROJECT_ASSESSMENT_2026-07-20.md`（整体评估）、`CAPABILITY_VERIFICATION.md`（能力真伪核对）、`TASK_AGENT_LOOP.md`（浏览器/桌面闭环）、`CODING_AGENT_DEMO.md`（自愈闭环 demo）。
