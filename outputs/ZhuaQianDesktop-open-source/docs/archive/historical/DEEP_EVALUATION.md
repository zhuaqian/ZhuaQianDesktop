# ZhuaQian Desktop — 深度评估报告

> 完成度对齐：截至 2026-07-11，已实现/部分实现/未实现的权威表见 `CODE_COMPLETION_ALIGNMENT.md`。本文中早于该表的“下一步/缺口”描述，以该表和本文后续更新说明为准。

**评估时间**：2026-07-11
**评估对象**：`work/zq-desktop/ZhuaQianDesktop.cs` + `work/zq-desktop/zq_desktop.ps1` + `outputs/` 分析文档
**文件版本**：单文件 ~3980 行，v0.1 原型

---

## 目录

1. [一句话定位](#1-一句话定位)
2. [评分总览](#2-评分总览)
3. [项目全景](#3-项目全景)
4. [架构深度分析](#4-架构深度分析)
5. [功能模块逐项评估](#5-功能模块逐项评估)
   - 5.1 AI Provider 接入
   - 5.2 文件解析
   - 5.3 Office 导出
   - 5.4 知识库
   - 5.5 权限安全模型
   - 5.6 插件系统
   - 5.7 工具箱
   - 5.8 多语言
   - 5.9 构建与发布
6. [安全风险矩阵](#6-安全风险矩阵)
7. [与竞品的关键差距](#7-与竞品的关键差距)
8. [架构重构建议](#8-架构重构建议)
9. [路线图建议](#9-路线图建议)
10. [总结](#10-总结)

---

## 1. 一句话定位

ZhuaQian Desktop 是一个 **Windows AI 办公助手原型**，目标对标 OpenAI Codex Desktop / Claude Code / Tencent WorkBuddy 的轻量免费替代品。当前为 **v0.1 原型**，功能密度极高但架构处于"单文件堆功能"阶段。

---

## 2. 评分总览

评分标准：1（极差）– 10（优秀）

| 维度 | 评分 | 依据 |
|------|:----:|------|
| **功能广度** | **8** | 聊天、多模型、文件解析、截图 OCR、知识库 chunk、工具箱、权限细分、审计日志、多语言、命令面板、Outputs、回滚 |
| **安全意识** | **7** | DPAPI 密钥加密、Power 总开关、8 种细粒度权限、云端上传确认弹窗、审计日志、插件白名单 |
| **文件导出** | **7** | 真实 OOXML 生成 (docx/pptx/xlsx)，纯代码实现 zip + XML，不依赖 Office 安装 |
| **原型可用性** | **7** | 双击 exe 即可运行，配置简单，核心链路可走通 |
| **产品方向** | **8** | 方向正确——轻量本地优先 + 安全可控，竞品差距分析清晰（见 COMPETITIVE_GAP_ANALYSIS.md） |
| **架构健康** | **3** | **最大问题**：单文件 3980 行，UI / Provider / 文件解析 / 权限 / 导出 / 知识库全部耦合 |
| **测试质量** | **2** | 仅有 `scripts/smoke-test.ps1` 验证 exe 加载和 OOXML 生成，无单元测试，无 mock，无 UI 自动化 |
| **Provider 接入** | **5** | 多 Provider + fallback 链设计合理，但无 streaming，同步阻塞，Auto 模式决策不透明 |
| **知识库** | **4** | chunk 设计正确（带 heading/summary/tags/layer），但关键词检索落后，无 embedding / 向量库 |
| **UI 成熟度** | **4** | WinForms 按钮堆叠，已有任务状态分组第一版，无右侧面板 tabs，无消息级操作（Copy/Save/Regenerate） |
| **可维护性** | **3** | 任何改动可能误伤，多人协作冲突概率高，无代码规范约束 |
| **开源准备** | **6** | 有 README / ROADMAP / CHANGELOG / SECURITY / CONTRIBUTING / LICENSE / GitHub Actions |

**综合健康度**：**~5/10** — 功能密度高但架构不可持续，需要优先重构而非堆功能。

---

## 3. 项目全景

### 目录结构

```
c-users-workbuddy-2026-07-10/
├── work/zq-desktop/                # 源码目录
│   ├── ZhuaQianDesktop.cs          # 主程序（单文件 3980 行）
│   ├── zq_desktop.ps1              # PowerShell 版本（574 行，早期版本）
│   ├── package.sed                 # IExpress 打包配置
│   └── screenshot-*.png            # 各版本 UI 截图
├── outputs/                        # 构建产物与分析文档
│   ├── ZhuaQianDesktop.exe         # 编译 exe
│   ├── ZhuaQianDesktop-open-source.zip
│   ├── ZhuaQianDesktop-open-source/ # 开源发布目录
│   ├── COMPETITIVE_GAP_ANALYSIS.md  # 竞品差距分析
│   ├── CURRENT_GAPS_ASSESSMENT.md   # 当前不足评估
│   ├── NEXT_SUPER_AGENT_BRIEF.md    # 项目交接文档
│   └── UI_COMPETITOR_RESEARCH.md    # 竞品 UI 研究
```

### 构建方式

构建通过 `package.sed`（IExpress）将 `zq_desktop.ps1` 打包为单 exe：

```xml
[Strings]
FILE0=zq_desktop.ps1
```

当前 **两个独立的实现** 共存：
- **PowerShell 原型**（`zq_desktop.ps1`，574 行）：早期快速验证版本，仅 Gemini 单 Provider
- **C# 版本**（`ZhuaQianDesktop.cs`，3980 行）：较完整的 WinForms 版本，多 Provider + 工具箱 + 知识库 + 权限

C# 版本使用 .NET Framework 4.x 自带的 `csc.exe` 编译：

```
C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe
```

---

## 4. 架构深度分析

### 4.1 当前架构全景图

```
ZhuaQianDesktop.cs (MainForm, ~3980 lines)
├── 1. 状态字段 (~130 行, L19-L148)
│   ├── 配置字段: apiKey, model, provider, openRouterApiKey 等 20+ 个
│   ├── 运行时状态: messages, pendingParts, pendingLabels 等
│   ├── 权限布尔值: 8 个 perm* 字段
│   └── 常量: DefaultModel, MaxExtractedChars, HotkeyId 等
│
├── 2. UI 构建 (~420 行, L173-L553)
│   ├── BuildUi(): 完整 UI 布局，含 ~40 个子控件
│   ├── LayoutBottom(): 底部输入区自适应布局
│   ├── ToggleSidebar() / ApplySidebarState(): 侧栏折叠
│   └── RebuildUiForLanguage(): UI 语言重建
│
├── 3. 配置管理 (~100 行, L1345-L1470)
│   ├── LoadConfig() / SaveConfig()
│   ├── ProtectSecret() / UnprotectSecret(): DPAPI 加密
│   └── NormalizeModel(): 模型名兼容
│
├── 4. 任务管理 (~140 行, L1120-L1314)
│   ├── LoadTasks() / SaveCurrentTask()
│   ├── CreateNewTask() / LoadTask()
│   ├── RenameCurrentTask()
│   └── GenerateTaskTitle()
│
├── 5. Provider 调用 (~380 行, L3236-L3598)
│   ├── CallSelectedProvider(): 路由到对应 provider
│   ├── CallGemini(): Gemini API + fallback 链
│   ├── CallOpenRouter(): OpenRouter API
│   ├── CallLocalApi(): Ollama 兼容 API
│   ├── PostGemini() / ExtractReply(): Gemini 底层
│   └── IsRetryableModelError() / IsModelNotFoundError()
│
├── 6. 文件解析 (~200 行, L3645-L3777)
│   ├── ExtractTextDocument(): 按分派
│   ├── ExtractDocx() via ExtractZipXml()
│   ├── ExtractPptx()
│   ├── ExtractXlsx() (含 sharedStrings 处理)
│   └── ExtractXmlText(): XML 文本提取
│
├── 7. Office 导出 (~300 行, L879-L1118)
│   ├── SaveDocxFile() / SavePptxFile() / SaveXlsxFile()
│   ├── BuildSlideBlocks() / BuildSpreadsheetRows(): 内容解析
│   ├── PptMasterXml() / PptThemeXml() / PptLayoutXml(): XML 模板
│   └── AddZipText() / ColumnName(): 工具方法
│
├── 8. 知识库 (~300 行, L2699-L3016)
│   ├── IndexFolder() / CollectIndexFiles()
│   ├── AddKnowledgeChunks() / SplitKnowledgeChunks()
│   ├── SearchKnowledge() / BuildKnowledgeContext()
│   ├── ScoreDoc() / ExtractSnippet(): 关键词打分
│   └── InferKnowledgeTags() / InferKnowledgeLayer()
│
├── 9. 工具箱 (~380 行, L1834-L2549)
│   ├── ShowTools() / AddToolButton()
│   ├── OrganizeFolder() / CreateTemplateOrEmailDraft()
│   ├── PrepareExcelAssistant() / ShowResourceMonitor()
│   ├── ShowSkillLibrary() / InsertSkillPrompt()
│   └── UseActiveWindowContext() / PrepareAgentPlan()
│
├── 10. 权限控制 (~100 行, L638-L662, L3329-L3360)
│   ├── EnsurePermission() / EnsureComputerControlPower()
│   ├── ToggleComputerControlPower()
│   └── ConfirmCloudUploadIfNeeded()
│
├── 11. 插件系统 (~100 行, L3065-L3155)
│   ├── RunPlugin() / ExecutePlugin()
│   ├── ValidatePluginPath(): 白名单路径校验
│   └── ConfirmPluginRun(): 执行确认
│
├── 12. UI 面板 (~350 行, L1885-L2152)
│   ├── ShowCommandPalette() / BuildCommands()
│   ├── ShowOutputsPanel() / LoadExportHistoryRows()
│   └── ShowRollbackPanel() / ExecuteRollbackManifest()
│
├── 13. 安全工具 (~50 行, L3779-L3799)
│   ├── ApplyRedaction(): 手机/身份证/银行卡/邮箱脱敏
│   └── LogAction(): 审计日志
│
├── 14. 剪贴板监控 (~80 行, L2644-L2697)
│   ├── ToggleClipboardMonitor() / ClipboardTimerTick()
│   └── GetClipboardText()
│
├── 15. 快捷键与窗口 (~100 行, L3865-L3963)
│   ├── RegisterHotKey / UnregisterHotKey / WndProc
│   ├── ToggleWindowVisible()
│   └── CaptureActiveWindowContext()
│
└── 16. 入口 (~15 行, L3965-L3980)
    └── Main(): STAThread + Application.Run
```

### 4.2 架构问题深度分析

#### 问题 1：单文件耦合（严重程度：P0）

3980 行代码在一个类中，UI 事件处理与业务逻辑完全混合。例如 `OrganizeFolder()`（L2347-L2421）同时负责：
- 权限检查（`EnsurePermission`）
- 文件夹选择对话框
- 文件扫描与计划生成
- 确认弹窗
- 实际文件移动
- rollback manifest 生成
- audit 日志记录
- UI 更新（`AppendChat`）

任何一个环节改动都可能影响其他功能。

#### 问题 2：catch { } 空块（严重程度：P1）

在全文中出现了至少 **15+ 处** 空的 `catch { }` 块：

```csharp
// L1386
catch { }

// L2168
catch { }

// L2722
catch { }

// L2752
catch { }

// L2854
catch { }

// L3798
catch { }
```

这使得错误被静默吞没，用户不知道操作失败原因，也无法在审计日志中追溯。

#### 问题 3：同步阻塞调用（严重程度：P1）

`CallGemini()`、`CallOpenRouter()`、`CallLocalApi()` 均使用同步的 `WebClient.UploadString()`。虽然 `SendMessage()` 使用了 `Task.Run()` 包装，但这只是把阻塞移到线程池，而不是真正的异步：

```csharp
// L3216 - Task.Run 包装同步调用
string reply = await Task.Run(() => CallSelectedProvider(parts));
```

这导致：
- 线程池线程被阻塞
- 取消请求困难
- 无法实现 streaming 输出

#### 问题 4：硬编码字符串与多语言（严重程度：P2）

多语言通过 `Tr(en, zhHans, zhHant)` 三参数方法实现（L1472-L1477），但：
- 很多地方仍是硬编码英文（如 L2581 "Planned next:", L3329 "No exported outputs yet."）
- 不是 i18n 资源文件，无法外部翻译
- 运行时切换语言会重建整个 UI（`RebuildUiForLanguage()`，L586-L603）

#### 问题 5：WinForms 不是强类型绑定（严重程度：P2）

所有数据通过 `Dictionary<string, object>` + `ArrayList` 传递，大量运行时类型转换：
- `Convert.ToString(data["role"])` 模式在代码中出现 **30+ 次**
- JSON 序列化使用过时的 `JavaScriptSerializer`（非 `JsonSerializer`/`Newtonsoft.Json`）
- 无编译时类型安全

---

## 5. 功能模块逐项评估

### 5.1 AI Provider 接入

| 维度 | 状态 | 详情 |
|------|------|------|
| Provider 数量 | ✅ 3 + 1 Auto | Gemini / OpenRouter / Local / Auto |
| 模型 fallback | ✅ | Gemini 有 fallbackModels 链 (`ZhuaQianDesktop.cs:69`) |
| 测试按钮 | ✅ | Settings 里 4 个 Test 按钮 (L1585-L1588) |
| 密钥安全 | ✅ | DPAPI 加密存储 (L1441-L1470) |
| **Streaming** | ❌ **缺失** | 全部同步请求，无 SSE/chunk 响应 |
| **并发控制** | ❌ | 无请求取消/重试策略 |
| **Token 计数** | ❌ | 无法预估或限制 token 消耗 |
| **模型列表自动获取** | ❌ | OpenRouter 只预设 3 个 free 模型 (L1541-L1545) |
| **Auto 模式透明性** | ❌ | 用户不知道实际用了哪个 provider |

关键代码路径：
```
SendMessage(L3157) → CallSelectedProvider(L3289)
  ├── CallGemini(L3236) → PostGemini(L3524) → WebClient.UploadString
  ├── CallOpenRouter(L3372) → WebClient.UploadString
  └── CallLocalApi(L3428) → WebClient.UploadString
```

### 5.2 文件解析

| 格式 | 支持 | 实现方式 | 质量评估 |
|------|:----:|----------|:--------:|
| PNG/JPG/GIF/WebP/BMP | ✅ | 直接读取 bytes → base64 inlineData | **好**，交给 Gemini 视觉 |
| PDF | ✅ | 同上（依赖 Gemini 视觉） | **中**，无本地文本提取 |
| DOCX | ✅ | ZIP → XML → 文本提取 | **中**，无格式/样式/图片 |
| PPTX | ✅ | ZIP → slide XML → 文本提取 | **中**，顺序正确但无备注 |
| XLSX/XLSM | ✅ | ZIP → sharedStrings + worksheet XML | **弱**，sharedStrings 处理粗糙，日期/公式/格式丢失 |
| TXT/MD/CSV/JSON | ✅ | `File.ReadAllText` | **好** |
| DOC/XLS/PPT（旧格式） | ❌ | 提示用户另存为新格式 | 合理 |

关键限制：
- 无 PDF 本地文本提取（`ZhuaQianDesktop.cs:3651-3653` 返回提示）
- XLSX sharedStrings 通过简单的 `Split` 换行处理，可能错位（L3707）
- 大文件仅截断，无分页读取 UI

### 5.3 Office 导出

| 格式 | 实现 | 质量 |
|------|------|:----:|
| TXT | `File.WriteAllText` with UTF8 BOM | **好** |
| MD | TXT 同 | **好** |
| DOCX | 手动构建 Open XML zip（`[Content_Types].xml` + `word/document.xml` 等） | **中**，纯文本段落，无样式/图片/表格 |
| PPTX | 手动构建 zip，含 `slide.xml`、`slideMasters`、`theme`、`layouts` | **中**，只有标题+正文布局，无设计/图片 |
| XLSX | 手动构建 zip，含 `workbook.xml` + `sheet1.xml` + `styles.xml` | **中**，只有 inlineStr，无数字/日期/公式 |

亮点：**不依赖本机 Office 安装**，全部通过手写 OOXML zip 结构实现。

```
SaveDocxFile(L892) → Create OOXML zip 包
SavePptxFile(L917) → BuildSlideBlocks → BuildPptSlideXml
SaveXlsxFile(L1031) → BuildSpreadsheetRows → 生成 sheet XML
```

### 5.4 知识库

| 维度 | 状态 | 评估 |
|------|------|:----:|
| Chunk 拆分 | ✅ 已实现 | 按 ~1800 字符 + 标题分割（L2783-L2803） |
| Metadata | ✅ 丰富 | docId/chunkId/heading/summary/tags/layer/path/size/modifiedAt |
| 关键词检索 | ✅ `ScoreDoc` | 基于词频 + 加权（标题×5, 标签×4）（L2936-L2953） |
| 标签自动推断 | ✅ | 7 类标签：finance/code/meeting/contract/marketing/personal/general（L2981-L2993） |
| 层级推断 | ✅ | hot/cold/temp 基于路径和修改时间（L3008-L3016） |
| **Embedding** | ❌ **缺失** | 配置和 Test 按钮已有，但未接入真正向量检索 |
| **向量库** | ❌ **缺失** | 索引存 JSON 文件，不是向量 DB |
| **增量索引** | ❌ | 需要全量重建（L2699-L2732） |
| **文件监听** | ❌ | 无后台文件变更监听 |
| **引用卡片 UI** | ❌ | 搜索结果纯文本，无可点击的引用卡片 |
| **Add to KB 入口** | ❌ | 无此按钮 |

知识库搜索流程：
```
SearchKnowledge(L2880) → BuildKnowledgeContext(L2904)
  → ScoreDoc(L2936) 对所有 chunk 打分
  → 取 Top N → 拼接上下文 → 存入 pendingParts → 发送给 AI
```

### 5.5 权限安全模型

| 安全机制 | 状态 | 代码位置 |
|----------|------|----------|
| Power 总开关 | ✅ | L610-L636, 控制所有本地副作用 |
| 细粒度权限（8 种） | ✅ | L649-L662: fileRead, fileWrite, fileMoveDelete, processManage, pluginRun, screenshot, clipboard, networkUpload |
| 权限设置 UI | ✅ | `ShowPermissionSettings()` L1628-L1677 |
| DPAPI 密钥加密 | ✅ | `ProtectSecret()`/`UnprotectSecret()` L1441-L1470 |
| 云端上传确认 | ✅ | `ConfirmCloudUploadIfNeeded()` L3329-L3360 |
| 审计日志 | ✅ | `LogAction()` L3790-L3799 |
| 文件脱敏 | ✅ | `ApplyRedaction()` L3779-L3788 |
| 插件白名单 | ✅ | `ValidatePluginPath()` L3130-L3147 |

**待补充**：
- ❌ 自动化输入/键盘无权限预留 (`permAutomationInput`)
- ❌ Outputs 打开本地文件无权限审计
- ❌ 权限拒绝时无"打开设置"快捷按钮

### 5.6 插件系统

| 机制 | 状态 |
|------|------|
| 支持类型 | .py / .ps1 / .exe / .bat / .cmd |
| 高级插件开关 | 默认关，.exe/.bat/.cmd 需在 Settings 开启 |
| 白名单目录 | 必须位于 `pluginDir` 内 |
| 执行前确认 | 显示路径、输入长度、stdin 预览 |
| 超时控制 | 30 秒 |
| 输出大小限制 | 20000 字符 |
| **无 manifest** | ❌ 无权限声明、无输入/输出 schema、无版本号 |
| **无签名** | ❌ 无法验证插件来源 |
| **无沙盒** | ❌ 插件运行在用户进程中 |

### 5.7 工具箱

已实现的工具（通过 `ShowTools()` 弹窗 + `ShowCommandPalette()`）：

| 工具 | 实现程度 | 安全接入 |
|------|:--------:|:--------:|
| Organize Folder | ✅ 完整 | Power + permFileMoveDelete |
| Template / Email Draft | ✅ 完整 | Power + permFileWrite |
| Excel Assistant | ✅ 完整 | permFileRead |
| Resource Monitor | ✅ 完整 | Power + permProcessManage |
| Semantic File Search | ✅ 完整 | 关键词检索 |
| Audit Log | ✅ 查看 | 无权限限制 |
| Privacy Test | ✅ 脱敏预览 | 无权限限制 |
| Use Current Context | ✅ 完整 | permClipboard |
| Agent Planner | ✅ 初步 | 仅生成 prompt |
| Skill Library | ✅ 6 种技能 | 仅 prompt 注入 |

### 5.8 多语言

| 语言 | 状态 |
|:----|:----:|
| 简体中文 | ✅ 主要 UI |
| 繁体中文 | ✅ 主要 UI |
| English | ✅ 主要 UI |

实现方式：`Tr(en, zhHans, zhHant)` 三参数方法（L1472-L1477）。
问题：非资源文件，部分弹窗和工具结果仍硬编码英文。

### 5.9 构建与发布

| 维度 | 状态 |
|------|------|
| 编译器 | .NET Framework csc.exe |
| 构建脚本 | `package.sed` IExpress |
| CI | GitHub Actions (`build.yml`) |
| Smoke test | `scripts/smoke-test.ps1` |
| CHANGELOG | ✅ |
| **无版本号注入** | ❌ UI 显示 "v0.1" 但无代码级别版本 |
| **无安装器** | ❌ 仅 exe |
| **无自动更新** | ❌ |
| **无 SHA256 发布** | ❌ |
| **无 Release notes 模板** | ❌ |

---

## 6. 安全风险矩阵

| 风险 | 等级 | 现有缓解 | 改进建议 |
|------|:----:|----------|----------|
| 插件执行任意脚本 | **高** | 白名单目录 + 限制扩展名 + 执行前确认 | manifest 模式 + 权限声明 + 数字签名 |
| 文件整理误操作 | **高** | rollback manifest + 移动前预览 | 一键回滚分离 UI |
| 结束进程丢数据 | **高** | 确认弹窗 + 审计日志 | 记录进程名和启动参数 |
| 云端上传敏感文本 | **高** | 脱敏 (`ApplyRedaction`) + 摘要确认 + 网络上传权限 | 临时允许/始终允许 + 内容 diff |
| 剪贴板泄露 | **中** | 权限开关 + 审计日志 | 内容摘要而不是完整记录 |
| 密钥跨账户无法恢复 | **中** | DPAPI 绑定当前用户 | 提供导出/导入加密密钥功能 |
| 审计日志含敏感路径 | **中** | 无 | 路径脱敏或排除规则 |
| `Process.Start(path)` 触发关联程序 | **中** | 无 | 增加文件类型白名单 |
| 输出文件覆盖已有文件 | **低** | SaveFileDialog 默认 | 提供覆盖前备份 |

---

## 7. 与竞品的关键差距

| 维度 | ZhuaQian 当前 | 竞品参考 | 差距 | 优先级 |
|------|:------------:|----------|:----:|:------:|
| **任务状态管理** | 聊天任务已有状态字段与分组第一版 | Claude Code Agent View: Needs input / Working / Review / Done | **高** | P0 |
| **执行闭环** | 无审批卡，无 timeline | WorkBuddy: Brief→Plan→Approve→Execute→Output→Review | **高** | P0 |
| **产物中心** | `export-history.jsonl` 简单列表 | WorkBuddy: 可验收工作成果中心（类型图标、预览、重新生成、加入知识库） | **高** | P1 |
| **知识库 RAG** | 关键词 chunk 检索 | Codex/Claude: embedding + 向量检索 + 重排 + 引用卡片 | **高** | P1 |
| **插件生态** | 无 manifest、无签名 | Codex Skills / Trae MCP / WorkBuddy 2.2万技能 | **高** | P2 |
| **工程架构** | 单文件 3980 行 | 所有竞品模块化 + 测试体系 | **极高** | P0 |
| **自动化任务** | 无 | Codex Automations：定时任务 + inbox review | **高** | P2 |
| **Streaming** | 无（同步阻塞） | 所有竞品支持流式响应 | **高** | P1 |
| **Undo/Rollback** | 仅文件整理 | OpenCode: 全面 undo/redo | **中** | P1 |
| **文件 diff/审查** | 无 | Cursor/Codex: diff review | **高** | P2 |
| **跨端/手机联动** | 无 | WorkBuddy: 微信扫码 | **高** | P3 |
| **测试质量** | 仅 smoke test | 竞品有完整 CI + 单元 + 集成测试 | **极高** | P0 |
| **知识库去重** | 无 | 竞品 RAG 系统有去重和增量更新 | **中** | P2 |

---

## 8. 架构重构建议

### 目标架构

```
src/
├── Program.cs                         # 入口点
├── MainForm.cs                        # 仅 UI 编排
│
├── Core/
│   ├── ConfigStore.cs                 # 配置加载/保存/DPAPI
│   ├── ChatTaskStore.cs               # 任务持久化/RenderMessages
│   ├── AuditLog.cs                    # 审计日志写入/读取
│   └── PermissionGate.cs              # 权限检查/确认
│
├── Providers/
│   ├── IProvider.cs                   # Provider 接口
│   ├── GeminiProvider.cs              # Gemini API (含 fallback)
│   ├── OpenRouterProvider.cs          # OpenRouter API
│   └── LocalProvider.cs               # Ollama 兼容 API
│
├── Documents/
│   ├── DocumentExtractor.cs           # 文件解析调度
│   ├── DocxExtractor.cs               # DOCX 解析
│   ├── PptxExtractor.cs               # PPTX 解析
│   ├── XlsxExtractor.cs               # XLSX 解析（含 sharedStrings）
│   ├── OfficeExporter.cs              # OOXML 导出（docx/pptx/xlsx）
│   └── Redactor.cs                    # 脱敏
│
├── Knowledge/
│   ├── KnowledgeIndex.cs              # 索引构建/保存/加载
│   ├── Chunker.cs                     # 文本分块
│   └── KnowledgeSearch.cs             # 关键词 + 向量检索（未来）
│
├── Tools/
│   ├── PluginRunner.cs                # 插件执行/验证
│   ├── FolderOrganizer.cs             # 文件整理 + rollback
│   ├── ResourceMonitor.cs             # 资源监控
│   └── TemplateEngine.cs              # 模板/邮件草稿
│
├── Ui/
│   ├── SettingsDialog.cs              # 设置弹窗
│   ├── PermissionDialog.cs            # 权限弹窗
│   ├── CommandPalette.cs              # 命令面板
│   ├── OutputsPanel.cs                # 产物面板
│   ├── RollbackPanel.cs               # 回滚面板
│   └── ToolsDialog.cs                 # 工具箱弹窗
│
└── Security/
    └── ClipboardMonitor.cs            # 剪贴板监控
```

### 拆分收益

- 每个类 ≤ 500 行
- 可独立单元测试
- 多人可并行开发
- 功能改动不会误伤 UI 布局

---

## 9. 路线图建议

### 阶段 1：结构稳定化（P0）

| # | 任务 | 预估 |
|---|------|:----:|
| 1 | **拆分单文件** 为 Core/Providers/Documents/Tools/Ui 模块 | 2-3 天 |
| 2 | **提取 PermissionGate** 并写单元测试 | 0.5 天 |
| 3 | **提取 ConfigStore** 并写单元测试 | 0.5 天 |
| 4 | **提取 AuditLog** 并写单元测试 | 0.5 天 |
| 5 | **提取 Redactor** 并写单元测试 | 0.5 天 |
| 6 | **给 OfficeExporter 补测试**（验证 zip 结构） | 1 天 |
| 7 | **给 Chunker 补测试** | 0.5 天 |
| 8 | **清理所有 `catch { }`** → 至少写入审计日志 | 0.5 天 |

### 阶段 2：任务工作流（P1）

| # | 任务 | 预估 |
|---|------|:----:|
| 1 | **扩展任务状态机与 Agent 状态联动**（pending/needs_input/running/ready_for_review/completed/failed） | 1 天 |
| 2 | **增强左侧任务状态分组** | 1 天 |
| 3 | **Approval Card 实现**（Plan → [Approve] [Edit] [Cancel]） | 2 天 |
| 4 | **Outputs 升级为产物数据库**（JSONL 升级为结构化记录） | 1.5 天 |
| 5 | **Provider 调用改为 HttpClient + Streaming** | 2 天 |
| 6 | **Command Palette 加快捷键 + 权限状态** | 0.5 天 |

### 阶段 3：知识库 + 生态（P2）

| # | 任务 | 预估 |
|---|------|:----:|
| 1 | **接入 Ollama embedding**（`nomic-embed-text` + cosine similarity） | 2 天 |
| 2 | **keyword + vector hybrid 检索** | 1 天 |
| 3 | **增量索引 + 文件监听** | 1 天 |
| 4 | **引用卡片 UI** | 1 天 |
| 5 | **插件 manifest 格式**（id/name/version/permissions/inputs/outputs） | 1 天 |
| 6 | **PDF 本地文本提取**（iText 或 PdfPig 等） | 1 天 |

### 阶段 4：产品化（P3）

| # | 任务 |
|---|------|
| 1 | 跨端：LAN 扫码上传 |
| 2 | 自动化任务：本地 scheduler + review inbox |
| 3 | 手机端远程触发任务 |
| 4 | MCP 协议支持 |
| 5 | 技能市场雏形 |
| 6 | 安装器 + 自动更新 |

---

## 10. 总结

### 核心结论

**ZhuaQian Desktop 是一个功能密度很高的早期原型。** 多 provider 接入、真实文件导出、权限细分、知识库 chunk、审计日志、rollback 等设计思路都是对的方向。它与竞品（Codex、Claude Code、WorkBuddy）的产品理解差距已经通过 `COMPETITIVE_GAP_ANALYSIS.md` 清晰识别。

### 最大矛盾

**功能越多，单文件架构越危险。** 3980 行单文件已经接近不可维护：

- 新功能容易误伤旧功能
- 无法写单元测试
- 多人协作必然冲突
- 任何 UI 调整都可能影响 provider、导出、权限等无关逻辑

### 一句话行动建议

> **下一阶段的核心任务不是加更多功能，而是：把单文件拆成模块、补上测试体系、给任务加上状态机、把知识库接入 embedding——让项目从"可演示原型"变成"可维护、可协作、可扩展"的开源产品。**

---

*本文档基于对 `ZhuaQianDesktop.cs`（3980 行）、`zq_desktop.ps1`（574 行）、`package.sed`、`outputs/` 下全部分析文档的完整阅读和评估生成。*
