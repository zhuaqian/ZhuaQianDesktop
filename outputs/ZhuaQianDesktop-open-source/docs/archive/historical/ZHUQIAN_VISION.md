# ZhuaQian Desktop — 超越竞品的终极愿景

> 不只是另一个 AI 聊天工具。我们要做 **桌面 AI 操作系统的开源入口**。

## 核心定位

**ZhuaQian Desktop = 本地优先 + 完全开源 + 插件生态 + 真正生产力的桌面 AI 工作台**

对标 Cursor/Codex/WorkBuddy/Claude Code，但我们的差异化在于：

### 1. 真正的本地优先（vs 云端依赖）
- **离线可用**：Ollama + 本地 embedding + 本地知识库 = 完全离线也能工作
- **隐私不妥协**：所有数据存在用户本地，DPAPI 加密，不上传未经确认的内容
- **混合模式智能路由**：简单任务走本地模型，复杂任务自动切云端，用户透明

### 2. 真正的 Agent 工作流（vs 聊天框）
```
Brief → Plan → Approve → Execute → Review → Output → Rollback
```
这不是 prompt 注入，而是**严格的状态机**。每个任务都有生命周期。

### 3. 真正的插件生态（vs 封闭系统）
- **开放 SKILL.md 标准**：任何开发者可写技能包，一行命令安装
- **MCP 协议支持**：接入 OpenCode/Claude 的 MCP 工具生态
- **Plugin SDK**：Python/PS1/EXE 统一 manifest + 权限声明 + 沙盒

### 4. 真正的成果交付（vs 只说不做）
- **Office OOXML 无依赖生成**：Word/PPT/Excel 真实文件，不需要 Office 安装
- **代码级精度**：不是"我给你内容你自己保存"，而是桌面端实际落盘
- **Outputs 成果中心**：每个产物可追溯、可审查、可回滚、可重新生成

### 5. 真正的跨平台野心（vs 仅 Windows）
- 当前：WinForms（快速验证）
- 中期：Tauri/Electron 跨平台（共享核心逻辑）
- 长期：Web + Mobile 联动（LAN 扫码上传，跨设备任务接力）

## 竞品对比矩阵

| 能力 | ZhuaQian 目标 | Cursor | Codex | WorkBuddy | Claude Code | OpenCode |
|------|:------------:|:------:|:-----:|:---------:|:-----------:|:--------:|
| 完全开源 | ✅ MIT | ❌ 商业 | ❌ 商业 | ❌ 商业 | ❌ 商业 | ✅ MIT |
| 本地模型优先 | ✅ | ❌ | ❌ | ❌ | ❌ | ✅ |
| Windows 原生 | ✅ WinForms | ❌ 跨平台 | ✅ 多平台 | ✅ | ❌ CLI | ✅ 多平台 |
| 离线知识库 RAG | ✅ | ❌ | ❌ | ❌ | ❌ | ❌ |
| 真实 Office 导出 | ✅ | ❌ | ❌ | ❌ | ❌ | ❌ |
| 无依赖运行 | ✅ .NET内置 | ❌ Node | ❌ Python | ❌ | ❌ Node | ❌ Node |
| 隐私安全 DPAPI | ✅ | ❌ | ❌ | ❌ | ❌ | ❌ |
| 权限三层模型 | ✅ | ❌ | ❌ | ❌ | ❌ | ✅ |
| 任务状态机 | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |
| 插件/MCP 生态 | ✅ | ❌ | ✅ Skills | ✅ 2.2万 | ✅ | ✅ MCP |
| 屏幕截图 OCR | ✅ | ❌ | ❌ | ❌ | ❌ | ❌ |
| 剪贴板监控 | ✅ | ❌ | ❌ | ❌ | ❌ | ❌ |
| 文件整理回滚 | ✅ | ✅ Git | ❌ | ❌ | ✅ Git | ✅ Git |

## 技术架构蓝图

```
┌─────────────────────────────────────────────────────────────┐
│                    ZhuaQian Desktop Architecture              │
├─────────────────────────────────────────────────────────────┤
│  UI Layer (WinForms → Tauri/Electron)                        │
│  ├── MainForm / MainWindow                                   │
│  ├── CommandPalette                                          │
│  ├── OutputsPanel / OutputsHub                               │
│  ├── ApprovalCard                                            │
│  └── SettingsDialog / PermissionDialog                       │
├─────────────────────────────────────────────────────────────┤
│  Agent Layer (状态机)                                        │
│  ├── AgentEngine (Ask/Draft/Plan/Execute)                    │
│  ├── TaskStateMachine (Created→NeedsInput→Running→Review→Done)│
│  └── SkillRegistry (SKILL.md 文件系统发现)                   │
├─────────────────────────────────────────────────────────────┤
│  Core Layer                                                  │
│  ├── ConfigStore (DPAPI 保护)                                │
│  ├── PermissionGate (allow/ask/deny + pattern match)         │
│  ├── AuditLog (actions.jsonl + audit.log)                     │
│  └── OutputsHub (outputs.jsonl 产物数据库)                   │
├─────────────────────────────────────────────────────────────┤
│  Provider Layer (可插拔)                                     │
│  ├── GeminiProvider (含 fallback 链)                         │
│  ├── OpenRouterProvider (100+ 模型)                          │
│  ├── LocalProvider (Ollama/LM Studio)                        │
│  ├── OpenAICompatibleProvider (任意兼容 API)                 │
│  └── UnifiedStreaming (SSE 流式响应)                        │
├─────────────────────────────────────────────────────────────┤
│  Knowledge Layer (RAG)                                       │
│  ├── Chunker (智能分块)                                      │
│  ├── EmbeddingEngine (Ollama nomic-embed-text)               │
│  ├── VectorStore (JSONL → SQLite)                             │
│  └── HybridSearch (keyword + vector + rerank)                │
├─────────────────────────────────────────────────────────────┤
│  Documents Layer                                             │
│  ├── DocumentExtractor (docx/pptx/xlsx/pdf)                  │
│  ├── OfficeExporter (OOXML zip 无依赖)                       │
│  └── Redactor (PII 脱敏, CN ID/phone/card/email)            │
├─────────────────────────────────────────────────────────────┤
│  Tools Layer                                                 │
│  ├── FolderOrganizer (+ rollback manifest)                   │
│  ├── PluginRunner (manifest + permissions + timeout)         │
│  ├── ResourceMonitor (进程快照 + 安全终止)                   │
│  └── ClipboardMonitor (智能轮询 + 审计)                      │
└─────────────────────────────────────────────────────────────┘
```

## 三大核心差异化

### 差异化 1：本地优先 RAG 知识库（竞品都不做）

大多数竞品依赖云端向量数据库。ZhuaQian 的本地 RAG：
- **完全离线**：所有 embedding 在本地 Ollama 完成
- **隐私保障**：文档不离机，敏感文件不碰云端
- **混合检索**：keyword + vector + layer 评分
- **可溯源**：每个答案附带 chunkId、文件路径、heading

### 差异化 2：真实 Office 生产力（竞品只聊天不干活）

- **不依赖 Office 安装**：手写 OOXML zip 结构，Word/PPT/Excel 直接生成
- **从聊天到文件的一步路径**：用户说"生成周报PPT"→ 模型产出内容 → 桌面端落盘 → Outputs 可追踪
- **模板引擎**：内置财务/销售/项目模板，一键生成专业文档

### 差异化 3：开源 + 插件生态 = 社区驱动

- **MIT 许可证**：完全开放，可商用，可二次开发
- **SKILL.md 标准**：任何人可写技能包，像安装 VS Code 扩展一样简单
- **MCP 协议兼容**：直接接入 OpenCode/Claude 的 MCP 工具生态
- **Plugin SDK**：Python/PS1 开发者 10 分钟写一个插件

## 迭代路线图

### Phase 1 (现在 - 2026 Q3)：架构重构 + 核心差异化
- ✅ 单文件拆分模块
- ✅ PermissionGate 三层权限
- ✅ ApprovalCard 统一审批
- ✅ OutputsHub 成果中心
- 📍 Streaming 响应
- 📍 本地 embedding RAG

### Phase 2 (2026 Q4)：Agent 生态
- 📍 Agent 状态机（非 prompt 注入）
- 📍 SKILL.md 文件系统技能
- 📍 Command 文件系统命令
- 📍 Plugin SDK v1
- 📍 Undo/Redo 统一

### Phase 3 (2027 Q1)：跨平台 + 生态
- 📍 Tauri 跨平台桌面
- 📍 MCP 协议客户端
- 📍 LAN 手机扫码上传
- 📍 技能市场雏形
- 📍 Web 远程任务触发

## 一句话总结

> **ZhuaQian Desktop 不是又一个人工智能聊天工具。它是 Windows 上第一个真正本地优先、完全开源、可扩展的桌面 AI 工作台——让每个人都能拥有一个属于自己的 AI 办公助手，不需要付费订阅，不需要上传隐私数据，不需要成为程序员才能使用。**
