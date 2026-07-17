# ZhuaQian Desktop Windows 监工智能体与外挂监测可行性报告

> 完成度对齐：截至 2026-07-11，当前代码只实现一次性的 `Resource Monitor`，尚未实现持续监控 Agent、外挂检测或后台监工。整体完成度以 `CODE_COMPLETION_ALIGNMENT.md` 为准。

更新时间：2026-07-11

## 1. 结论

本功能应作为 ZhuaQian Desktop 的后续安全监控方向，而不是马上塞进现有 `Resource Monitor` 按钮。当前项目已经具备一些可承接基础：

- WinForms Windows 桌面端。
- `Power` 总开关与细分权限。
- 本地审计日志 `audit.log`。
- 结构化动作日志 `actions.jsonl`。
- 任务状态：`draft`、`needs_input`、`running`、`ready_for_review`、`failed`、`done`。
- `Resource Monitor` 可查看进程并在确认后结束 PID。
- `Agent Planner` 可生成安全执行计划。
- 插件运行已有 trusted folder、权限与确认。

但当前还没有真正的“监工外挂功能”：

- 没有后台常驻采集 Agent。
- 没有持续进程/模块/窗口/文件行为监控。
- 没有可疑外挂风险评分。
- 没有告警归并、证据包、人工复核队列。
- 没有 Agent 心跳、悬挂检测、自校验。
- 没有内核态驱动，也不建议 MVP 一开始做。

建议路线：先做用户态 Windows Monitoring Agent，再接入监工智能体和外挂监测智能体；内核态反外挂能力单独立项评估。

## 2. 和现有项目的关系

### 2.1 现有功能不是外挂监控

`ShowResourceMonitor()` 当前只是一次性枚举进程，并按内存占用排序展示。它适合作为系统观察工具，但不等于外挂监控。

现有能力可以复用为：

| 现有模块 | 可复用点 | 需要补强 |
| --- | --- | --- |
| Resource Monitor | 进程枚举、PID、结束进程确认 | 持续采集、签名校验、模块加载、风险评分 |
| PermissionGate | Power + 权限开关 | 新增安全监控权限、临时授权、告警处置权限 |
| AuditLog | 敏感动作审计 | 标准化事件字段、证据链、脱敏 |
| ActionLog | 结构化动作记录 | 增加 caseId、hostId、riskLevel、evidenceRefs |
| Task Status | 任务状态分组 | 增加 monitoring、suspended、manual_review |
| Agent Planner | 生成安全计划 | 升级为监工智能体任务编排入口 |
| Plugin Runner | 外部能力扩展 | 插件 manifest、签名、沙盒 |

### 2.2 不建议直接做成新按钮

外挂监测不是一个单次工具，而是一条长期运行链路：

```text
本地采集 -> 本地初筛 -> 风险事件 -> 监工归并 -> 取证 -> 人工复核 -> 策略回流
```

如果直接加一个 `外挂扫描` 按钮，很容易变成误报高、证据弱、难维护的玩具功能。

## 3. 推荐架构

### 3.1 四层架构

```text
ZhuaQian Desktop UI
  - 监工看板
  - 告警队列
  - 人工复核
  - 策略配置

Supervisor 智能体
  - 归并告警
  - 检查证据链
  - 分派分析任务
  - 生成处置建议

Windows Monitoring Agent
  - 进程/模块/窗口/文件事件采集
  - 心跳
  - 本地缓存
  - 本地规则初筛

Local Evidence Store
  - monitoring-events.jsonl
  - monitoring-cases.jsonl
  - policy.json
  - whitelist.json
```

### 3.2 MVP 不需要后台服务器

第一版可以完全本地运行，数据放在：

```text
%APPDATA%\ZhuaQianDesktop\monitoring\
```

建议文件：

```text
monitoring-events.jsonl
monitoring-cases.jsonl
monitoring-policy.json
monitoring-whitelist.json
monitoring-agent-heartbeat.json
```

后续如果要做多设备或团队版，再把事件上报到服务端。

## 4. Windows Monitoring Agent MVP

第一版建议使用用户态 Agent，不做内核驱动。

### 4.1 采集信号

| 信号 | MVP | 说明 |
| --- | --- | --- |
| 进程快照 | 必做 | 进程名、PID、路径、父进程、启动时间 |
| 文件签名 | 必做 | 发行者、是否签名、签名是否有效 |
| 模块加载 | 必做 | 目标进程加载的 DLL、路径、签名 |
| 窗口标题/类名 | 必做 | 可疑悬浮窗、注入器、调试器窗口 |
| 目标目录变更 | 必做 | 可疑 DLL、脚本、配置文件落地 |
| 调试工具痕迹 | 必做 | 常见调试器、内存扫描器、注入工具 |
| 网络连接 | 可选 | 可疑进程对外连接、本地代理 |
| 输入行为 | 可选 | 固定间隔点击、脚本化输入、连点器 |
| 截图/叠加层 | 可选 | 可疑 overlay，需谨慎处理隐私 |

### 4.2 Agent 状态

建议状态：

```text
stopped
starting
running
degraded
suspended
policy_outdated
tamper_suspected
failed
```

### 4.3 悬挂检测

需要检查：

- 心跳是否超时。
- 事件缓存是否持续增长但没有被消费。
- 采集线程是否异常退出。
- Agent 服务是否被停止。
- Agent 二进制是否被替换。
- 策略文件是否损坏或过期。

处置方式：

- 自动重启 Agent。
- 降级到只读监控。
- 标记当前主机为 `monitoring_untrusted`。
- 写入审计日志。
- 在监工看板显示人工处理。

## 5. 外挂监测智能体

外挂监测智能体不直接做系统调用，它读取 Agent 上报事件并生成解释、风险等级与证据摘要。

### 5.1 输入

```json
{
  "caseId": "case_...",
  "hostId": "local",
  "events": ["event_1", "event_2"],
  "targetProcess": "string",
  "timeRange": "string",
  "policyVersion": "string"
}
```

### 5.2 输出

```json
{
  "caseId": "case_...",
  "riskLevel": "low|medium|high|critical",
  "confidence": 0.0,
  "summary": "string",
  "evidenceRefs": ["event_1"],
  "recommendedAction": "log|warn|limit|block|manual_review",
  "falsePositiveNotes": "string"
}
```

### 5.3 风险评分

建议采用组合评分：

| 来源 | 示例 |
| --- | --- |
| 规则命中 | 可疑进程名、已知注入器、未签名 DLL |
| 行为关联 | 目标程序运行期间出现跨进程读写、调试器、异常模块 |
| 文件信誉 | 签名状态、哈希、路径、发行者 |
| 上下文 | 是否用户主动打开、是否位于开发工具目录、是否白名单 |

处置不要只依赖单条规则，至少需要“规则 + 行为 + 上下文”形成证据链。

## 6. 监工智能体

监工智能体是整个功能的总控，不是外挂检测器本身。

职责：

- 监控 Windows Agent 是否在线。
- 合并重复告警。
- 判断证据是否足够。
- 分派给外挂监测、取证、策略、误报复核智能体。
- 决定是否进入人工复核。
- 生成日报、周报和策略优化任务。

建议子智能体：

| 智能体 | 职责 |
| --- | --- |
| 采集智能体 | 管理本地 Agent、心跳、版本、策略 |
| 外挂监测智能体 | 风险评分与解释 |
| 取证智能体 | 生成时间线、哈希、签名、路径证据 |
| 策略智能体 | 维护规则、白名单、灰度策略 |
| 误报复核智能体 | 分析误判并提出降噪规则 |
| 安全智能体 | 检查 Agent 自身权限、篡改风险 |

## 7. 权限与合规

必须新增独立权限，而不是复用 `Process management`。

建议新增权限：

- `Monitor processes`
- `Inspect loaded modules`
- `Watch selected folders`
- `Collect security evidence`
- `Send monitoring report`
- `Block or limit suspicious process`

原则：

- 默认关闭高侵入权限。
- 用户明确开启本地安全监控。
- 不采集无关个人文件内容。
- 上传前脱敏路径中的用户名等敏感信息。
- 高风险处置必须人工复核，MVP 不建议自动封禁。

## 8. 实施路线

### Phase M0：文档与边界

- 确定监控目标：游戏、办公软件、插件系统，还是泛 Windows 环境。
- 确定采集清单和隐私说明。
- 在 README/ROADMAP 标注该功能为规划中，避免用户误以为已实现。

### Phase M1：本地事件模型

- 新增 `monitoring-events.jsonl`。
- 新增 `monitoring-cases.jsonl`。
- 定义 `MonitoringEvent`、`MonitoringCase`、`RiskScore`。
- 将现有 Resource Monitor 的进程快照改造成可记录事件的服务类。

### Phase M2：用户态 Agent

- 增加本地后台监控循环。
- 采集进程、模块、窗口、目标目录文件变更。
- 写心跳。
- UI 显示 Agent 状态。
- 支持暂停、恢复、导出证据。

### Phase M3：监工看板

- 新增 `Monitoring` 面板。
- 显示 Agent 在线状态、最近事件、风险 case、待人工复核。
- 接入 `actions.jsonl` 和 `audit.log`。

### Phase M4：外挂监测智能体

- 对可疑 case 生成风险解释。
- 输出证据摘要和建议动作。
- 支持误报标记与白名单回流。

### Phase M5：对抗增强

- Agent 自校验。
- 策略灰度发布和回滚。
- 更严格的插件 manifest 与签名。
- 单独评估是否需要内核驱动。

## 9. 不做清单

短期不要做：

- 不要做隐蔽监控。
- 不要默认开机常驻采集。
- 不要一条规则命中就自动封禁。
- 不要一开始写内核驱动。
- 不要采集用户私人文件正文。
- 不要把外挂检测包装成“百分百可靠”。

## 10. 下一步最小落地

最小可执行任务：

1. 在 ROADMAP 中新增 `Windows monitoring supervisor` 规划项。
2. 在权限模型中预留 `Security monitoring` 权限。
3. 把 `ShowResourceMonitor()` 拆出为 `ProcessSnapshotCollector`。
4. 新增 `monitoring-events.jsonl` 写入格式。
5. 新增一个只读 `Monitoring` 报告面板，不做阻断。

这样能在不破坏当前原型的情况下，把“监工外挂功能”接入正确架构。
