# ZhuaQian Desktop 项目理解文档

生成时间：2026-07-11
依据：通读 `docs/` 全部核心文档 + 源码实勘

---

## 一、一句话定位

ZhuaQian Desktop 是一个**本地优先、完全开源、Windows 原生**的 AI 工作台原型。
它不是单纯的聊天工具，而是能理解文档、调用多模型、生成真实文件、管理任务状态、受权限控制执行桌面操作的轻型 Agent 产品。

---

## 二、技术栈

| 层面 | 选型 |
|------|------|
| 语言 | C# |
| UI 框架 | WinForms |
| 运行时 | .NET Framework 4.8 |
| 编译器 | `csc.exe`（Windows 内置，零依赖 VS） |
| 构建 | PowerShell 脚本 `build.ps1` |
| 测试 | 自建 `TestRunner` + `SelfTest` 体系 |

---

## 三、产品能力

### 已实现（v0.1 原型）

- **多模型聊天**：Gemini / OpenRouter / Local Ollama / OpenAI 兼容扩展
- **多任务管理**：左侧任务栏 + 状态分组（Needs input / Running / Ready / Failed / Draft / Done）
- **工作模式**：Ask / Draft / Plan / Execute
- **文档读取**：图片、PDF、Word、PPT、Excel、Markdown、TXT、CSV、JSON
- **真实文件导出**：用户要求生成文件时，桌面端真实落盘 `.docx` / `.pptx` / `.xlsx` / `.md` / `.txt`
- **Outputs 成果中心**：每次导出记录到 `outputs.jsonl`，可在 UI 中查看/打开/定位
- **本地知识库**：文件夹索引 → chunk 分块 → metadata 标记 → keyword + vector 混合检索
- **权限控制**：Power 总开关 + 8 项细分权限 + DPAPI 密钥保护
- **审计日志**：`actions.jsonl` 记录关键动作，`audit.log` TSV 格式
- **工具集**：截图 OCR、剪贴板监控、文件整理（含 rollback）、插件运行（Python/PS1）、资源监控
- **多语言**：简体中文 / 繁体中文 / English

### 模块化架构（目标层）

```
UI Layer (WinForms)
├─ MainForm                 → 主窗体（当前 ~4380 行，目标降至 ~1500 行）
├─ SettingsDialog           → 设置界面
├─ ApprovalCard             → 统一审批组件
├─ OutputsPanel             → 产物展示
├─ RightPanel               → 右侧面板
├─ MainForm.Share.cs        → 分享/导入/中继（partial）
└─ MainForm.LiveSession.cs  → 实时协作（partial）

Agent Layer
├─ TaskCoordinator          → 任务协调
├─ TaskStateMachine         → 目标：状态机（未完成）
└─ SkillRegistry            → 目标：SKILL.md 文件系统发现（未完成）

Core Layer
├─ ConfigStore              → 配置持久化（DPAPI 保护）
├─ PermissionGate           → 权限判断（allow/ask/deny）
├─ AuditLog                 → 审计日志
└─ OutputsHub               → 产物中心

Provider Layer
├─ ModelRegistry
├─ ProviderManager
├─ GeminiClient / OpenRouterClient / OpenAIClient / LocalClient
├─ TencentWorkBuddyClient / AlibabaQianwenClient / ZhipuAIGLMClient
├─ ShareClient              → 中继分享
└─ StreamingBridge          → 流式响应（已实现但 MainForm 未用）

Documents Layer
├─ OfficeExporter           → OOXML 无依赖生成 Word/PPT/Excel
└─ Redactor                 → PII 脱敏

Knowledge Layer
├─ Chunker                  → 智能分块
└─ VectorIndex              → 向量持久化

Tools Layer
├─ FolderOrganizer          → 文件整理 + rollback manifest
├─ PluginRunner             → 插件执行
├─ ProcessSnapshotCollector → 进程快照
├─ CommandParser / SmartCommand / ExecutionTimeline
├─ SandboxProgressPanel / TimelineControl / UndoRedoManager
└─ ApprovalCard             → 审批卡片组件
```

---

## 四、当前最大风险（"伪模块化"）

这是理解本项目最关键的一点：

**`work/zq-desktop/ZhuaQianDesktop.cs`（~4380 行）没有消费任何拆分类**，而是把 `ConfigStore` / `AuditLog` / `PermissionGate` / `OfficeExporter` / `Chunker` / `FolderOrganizer` / 各 Provider 客户端 / `ApprovalCard` / `StreamingBridge` 的逻辑**全部内联重写了**。

后果：
1. 拆分类成为**编译进 exe 却永不执行的死代码**
2. 内联版与拆分类行为可能静默分叉
3. 唯一的模块化消费方是测试 harness
4. `src/` 树反而更模块化，devel 树在加功能时**倒退**了模块化

---

## 五、仓库资产现状

### 三套并行源码

| 目录 | 角色 | 构建 | 测试 | 说明 |
|------|------|------|------|------|
| `work/zq-desktop/` | **已验证运行基线** | 通过 | 139/139 通过 | 当前最可信的入口 |
| `src/` | 更模块化同步树 | 通过 | 编译失败 | 测试脚本漂移，API 不一致 |
| `outputs/ZhuaQianDesktop-open-source/` | 发布快照 | 通过 | 无 | 可编译的开源发布包 |

### 测试状态

| 测试脚本 | 状态 | 说明 |
|----------|------|------|
| `work/zq-desktop/scripts/run-tests.ps1` | ✅ 139 passed / 0 failed | 核心模块测试 |
| `work/zq-desktop/scripts/smoke-test.ps1` | ✅ 通过 | 有 3 个空 catch 警告 |
| `work/zq-desktop/build_tests.ps1` | ✅ 50 断言通过 | 自包含镜像测试 |
| `work/zq-desktop/build_perm_test.ps1` | ✅ 30 断言通过 | 权限引擎测试 |
| `work/zq-desktop/build_fallback_test.ps1` | ❌ 编译失败 | Provider 依赖未对齐 |
| `work/zq-desktop/build_failover_test.ps1` | ❌ 编译失败 | RegisterClient 可见性未对齐 |
| `src/scripts/run-tests.ps1` | ❌ 编译失败 | PermissionGate/OutputsHub API 漂移 |

### 已修复的回归缺陷（上一轮）

1. `StreamingBridge.ExtractDelta` — JSON 数组类型转换导致永远返回空串
2. `FolderOrganizer.Rollback` — ArrayList 强转 null 导致永远恢复 0 个文件
3. `OutputsHub.RecordExportHistory` — 重复写两份导致产物列表重复
4. `PermissionGate.FromJson` — pattern 丢失
5. `ProcessSnapshotCollector.CloseCase` — case 顺序反转

---

## 六、关键数据结构

### Task
```
taskId / title / status / lastAction / createdAt / updatedAt / provider / model / messages
```

### ActionRecord
```
actionId / taskId / type / status / requestedAt / approvedAt / detail / affectedPaths / result / rollbackManifest
```

### OutputRecord
```
outputId / taskId / kind / path / createdAt / sourceActionId / exists
```

### PermissionDecision
```
permissionName / target / mode / decision / rememberPolicy
```

---

## 七、关键工作流（当前实现 vs 目标）

### 文档分析
```
上传 → 解析 → 权限判断 → 调用模型 → 写入消息 → 如需导出则落盘 → 记录 Output
```

### Agent 执行（目标，未完全实现）
```
Brief → Plan → Approve → Execute → Review → Output → Rollback
```

### 文件整理（已实现）
```
选择目录 → 权限确认 → 执行整理 → 生成 rollback manifest → 记录 action → 记录 output
```

---

## 八、文档阅读指引

### 如果只能读 4 份文档

1. `docs/PRODUCT_REQUIREMENTS.md` — 产品定位与需求范围
2. `docs/PRODUCT_ARCHITECTURE.md` — 架构分层与模块边界
3. `docs/CURRENT_REALITY_2026-07-11.md` — 当前代码、测试、发布包实测状态
4. `docs/CODE_COMPLETION_ALIGNMENT.md` — 功能完成度对齐表（事实基准）

### 如果需要开始改代码

1. 先以 `work/zq-desktop/` 为基线
2. 先跑 `build.ps1`、`scripts/run-tests.ps1`、`scripts/smoke-test.ps1`
3. 不要在未说明前默认把 `src/` 当唯一权威源
4. 参考 `docs/NEXT_STEP_PLAN_2026-07-11.md` 的接线优先级

---

## 九、当前禁止事项

1. 不要继续在根目录新增零散分析文档
2. 不要继续在主窗体里复制模块逻辑
3. 不要未修测试就宣布"重构完成"
4. 不要把 `src/` 直接写成唯一事实源
5. 不要在 `EXECUTION_BACKLOG.md` 的 Epic A（统一代码事实源）完成前增加新功能

---

## 十、下一步优先级（按 NEXT_STEP_PLAN）

### P0 — 消除伪模块化与仓库分裂
1. 把 `MainForm` 接到已有拆分类（OfficeExporter / Chunker / Redactor / PermissionGate / ConfigStore / AuditLog）
2. 删除内联重复实现
3. 收敛三套源码树为单一权威源

### P0.5 — 接线已被测试但被绕过的成品功能
1. `ApprovalCard` 接入 UI（替换 ~30 处 MessageBox）
2. `StreamingBridge` 接入 `SendMessage`（流式输出）
3. `ProcessSnapshotCollector` 作为监工看板底座

### P1 — 数据模型与错误处理收敛
1. 统一日志事实源（`actions.jsonl` + `outputs.jsonl`）
2. 消除 `catch {}` 静默吞错
3. 权限模型升级（`allow/ask/deny` + pattern matching）
