# ZhuaQian Desktop 项目交接与深度评估

> 完成度对齐：截至 2026-07-11，已实现/部分实现/未实现的权威表见 `CODE_COMPLETION_ALIGNMENT.md`。本文中早于该表的“下一步/缺口”描述，以该表和本文后续更新说明为准。

本文档给下一个接手的高级智能体阅读。目标不是做宣传，而是帮助它快速理解这个项目的真实状态、产品意图、已实现能力、架构限制、明显不足，以及下一阶段最该补的东西。

## 一句话定位

ZhuaQian Desktop 是一个 Windows 桌面 AI 助手原型，目标是做一个轻量免费版的 Codex / Claude Code / WorkBuddy 风格工具：能聊天、读文件、看截图、做本地知识库、调用云端和本地模型，并逐步拥有受控的电脑操作能力。

当前它不是成熟商业产品，而是一个可运行的 v0.1 原型。它已经有较多功能入口，但很多能力仍是“轻量实现”或“安全占位”，需要继续产品化、模块化和测试化。

## 当前交付物

主要产物：

- `outputs/ZhuaQianDesktop.exe`
- `outputs/ZhuaQianDesktop-open-source.zip`
- `outputs/ZhuaQianDesktop-open-source/`
- 主源码：`work/zq-desktop/ZhuaQianDesktop.cs`

构建方式：

- 使用 Windows 自带 .NET Framework C# 编译器
- 编译器路径通常为 `C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe`
- 开源目录里有 `build.ps1`

项目目前没有使用 Electron、WPF、Avalonia、MAUI、Python GUI 或 Node。它是单文件 WinForms C# 应用。

## 已实现能力

### 1. 桌面聊天与多任务

- WinForms 桌面 UI。
- 左侧可折叠任务栏。
- 多任务会话，任务保存到 `%APPDATA%\ZhuaQianDesktop\tasks`。
- 聊天消息区区分颜色：
  - 用户：蓝色
  - ZhuaQian：绿色
  - 错误：红色
- 支持 Enter 发送，Shift+Enter 换行。

### 2. 多模型接入

已有 Provider：

- Gemini
- OpenRouter
- Local Ollama-compatible API
- Auto 模式

Gemini 默认模型：

- `gemini-flash-lite-latest`

OpenRouter 预设免费模型：

- `meta-llama/llama-3-8b-instruct:free`
- `microsoft/phi-3-medium-128k-instruct:free`
- `google/gemma-2-9b-it:free`

Local 默认：

- URL: `http://localhost:11434/api/chat`
- Model: `llama3.1:8b`

### 3. 文件与文档读取

支持上传：

- 图片：png/jpg/jpeg/gif/webp/bmp
- PDF
- Word：docx
- Excel：xlsx/xlsm
- PPT：pptx
- 文本：txt/md/csv/json/xml/html/log/ini/yaml/py/js/ts/css/sql 等

实现方式：

- 图片和 PDF 作为 inlineData 给 Gemini。
- docx/pptx/xlsx 通过 zip/xml 方式做轻量文本提取。
- 旧格式 `.doc/.xls/.ppt` 目前只是提示用户另存为新格式。

### 4. 截图 OCR

- 左侧按钮 `Screenshot OCR` / `截图识别`。
- 会截取屏幕保存到 `%APPDATA%\ZhuaQianDesktop\screenshots`。
- 截图作为图片附件发送给 Gemini 或 Auto。

注意：这不是本地 OCR。它依赖 Gemini 视觉能力。

### 5. 剪贴板监控

- 可打开/关闭剪贴板监听。
- 新复制文本会被放入输入框并自动总结。
- 有审计日志记录剪贴板读取行为。

风险：当前实现是轮询剪贴板，粒度粗，但简单可用。

### 6. 本地知识库

已有轻量本地索引：

- 选择文件夹扫描。
- 提取文本。
- 保存到 `%APPDATA%\ZhuaQianDesktop\knowledge-index.json`。
- 搜索时按关键词打分。

最近已增强元数据：

- `summary`：本地摘要，截取文本前段生成。
- `tags`：自动标签，如 `finance`、`code`、`meeting`、`contract`、`marketing`、`personal`。
- `layer`：`hot`、`cold`、`temp`。
- `sizeBytes`
- `modifiedAt`
- `path`
- 检索结果包含 path、layer、tags、summary、snippet，便于引用溯源。

重要限制：

- 还不是向量库。
- 没有 embedding。
- 没有增量更新。
- 没有后台监听文件夹。
- 没有去重。
- 没有真正语义搜索。

### 7. 工作流工具箱

顶部 `Tools` 工具箱包括：

- 整理文件夹
- 模板/邮件草稿
- Excel 助手
- 资源监控
- 语义文件搜索入口
- 审计日志
- 隐私脱敏测试
- 语音/手机计划
- 使用当前窗口上下文
- Agent 任务规划
- 职业技能库

其中部分功能是轻量可用，部分是规划入口。

### 8. Power 权限开关

顶部有：

- `Power: Off`
- `Power: On`

它是电脑操作权限开关。

被保护的动作：

- 整理/移动文件
- 生成本地草稿文件
- 运行插件脚本
- 结束指定进程

即使 `Power: On`，高风险动作仍会继续弹窗确认，并写入审计日志。

这是正确方向：权限必须显式打开，不能让模型默认拥有系统操作权。

### 9. 真实文件导出（TXT / Word / PPT / Excel）

之前存在问题：模型可能说“已生成 txt 文件”或“已生成 Word/PPT/Excel”，但文件实际不存在。

已修复：

- 底部按钮已升级为 `Save File`。
- 可把最近一次 AI 回复真实保存为 `.txt`、`.docx`、`.pptx`、`.xlsx`。
- 如果用户明确要求生成/导出/保存 TXT、Word、PPT、Excel，回复完成后弹保存对话框，实际写文件。
- `.docx/.pptx/.xlsx` 通过 Office Open XML zip 结构写入，不依赖本机安装 Office。
- Excel 导出会优先识别 Markdown 表格或 CSV 风格内容，否则按行写入第一列。
- 保存成功后聊天区显示真实路径。
- 系统提示要求模型不要谎称已创建本地文件；模型只负责生成内容，桌面端负责落盘。

### 10. 多语言

已加入：

- 简体中文
- 繁體中文
- English

设置页可切换语言，保存后主界面会重建刷新。

注意：目前多语言覆盖了主要 UI，但不是全量覆盖。部分弹窗、内部提示、工具结果仍有英文或中文硬编码。

### 11. UI 竞品评估后的第一轮落地

根据 `outputs/UI_COMPETITOR_RESEARCH.md` 对 Cursor、Trae/CodeBuddy、VS Code、OpenCode、Claude Code、Codex、WorkBuddy 的评估，已先落地一层工作台骨架：

- 顶部新增 `Ask / Draft / Plan / Execute` 模式选择。
- `Ask`：普通问答。
- `Draft`：提示模型生成可落盘内容。
- `Plan`：提示模型先输出安全计划、权限、风险、回滚，不直接执行。
- `Execute`：提示模型只辅助执行，不能谎称本地副作用。
- 顶部新增 `Cmd` 命令面板，可快速执行上传、截图 OCR、索引、搜索、导出、产物、审计、设置、权限、模式切换等命令。
- 顶部新增 `Outputs` 产物入口，读取 `%APPDATA%\ZhuaQianDesktop\export-history.jsonl`，支持打开产物或在文件夹中定位。
- Settings 中新增 `Permissions` 权限细分面板：
  - 写入/导出文件
  - 移动/删除文件
  - 结束进程
  - 运行插件
  - 截图识别
  - 剪贴板监控读取
- 已将导出、整理文件、模板草稿、结束进程、截图、剪贴板监控、插件执行接入权限检查。

这不是最终 UI，只是把“聊天框”升级成“工作台”的第一步。下一步应继续做右侧 Context / Files / Outputs / Knowledge / Audit tabs、任务状态分组、消息级 Copy/Save/Regenerate、计划审批卡片。

## 核心架构理解

当前应用是单文件 WinForms：

```text
MainForm
├─ 配置加载/保存
├─ UI 构建
├─ 任务持久化
├─ Provider 调用
│  ├─ Gemini
│  ├─ OpenRouter
│  └─ Local Ollama
├─ 文件解析
├─ 本地知识库
├─ 工具箱
├─ Power 权限
└─ 审计日志
```

优点：

- 极低依赖。
- exe 很容易生成。
- 用户双击即可运行。
- 适合原型快速验证。

缺点：

- 单文件已过大。
- UI、业务逻辑、模型调用、文件解析、权限控制混在一起。
- 很难测试。
- 很难多人协作。
- 继续堆功能会迅速失控。

## 重要设计判断

### 1. 不要让模型“假装执行”

这是当前最关键的产品规则。

模型只能生成内容、计划、建议。实际文件写入、移动、删除、进程结束、插件运行，必须由桌面端执行，并由桌面端返回真实结果。

如果模型说“我已保存文件”，但桌面端没有写入路径，那就是严重 bug。

### 2. 高风险动作必须走三层保护

建议标准：

1. `Power: On`
2. 动作级确认弹窗
3. 审计日志

未来如果加入删除文件、清理磁盘、自动操作应用、运行命令，必须继续遵守这个规则。

### 3. 本地知识库应优先做“可溯源”

不要急着只追求向量搜索。对办公用户来说，最重要的是：

- 这个答案来自哪个文件？
- 文件路径是什么？
- 引用了哪一段？
- 文件何时修改？
- 是否可能过期？

当前 metadata 增强是正确方向。

### 4. 本地模型不是“打包进 exe”

不要试图把 Llama/Qwen 模型塞进 exe。

正确方向：

- 接 Ollama
- 接 llama.cpp server
- 接 LM Studio 本地 OpenAI-compatible API
- UI 里做本地模型配置和状态检测

## 当前明显不足

### 1. 源码结构不可持续

`ZhuaQianDesktop.cs` 已经承担太多职责。

建议拆分：

```text
src/
├─ Program.cs
├─ MainForm.cs
├─ Config.cs
├─ ChatTaskStore.cs
├─ Providers/
│  ├─ GeminiProvider.cs
│  ├─ OpenRouterProvider.cs
│  └─ LocalProvider.cs
├─ Documents/
│  ├─ DocumentExtractor.cs
│  └─ OfficeTextExtractor.cs
├─ Knowledge/
│  ├─ KnowledgeIndex.cs
│  └─ KnowledgeSearch.cs
├─ Security/
│  ├─ Redactor.cs
│  ├─ PermissionGate.cs
│  └─ AuditLog.cs
└─ Tools/
   ├─ WorkflowTools.cs
   └─ PluginRunner.cs
```

### 2. 没有测试

风险最高的地方：

- JSON 解析
- 任务保存/恢复
- 文件解析
- 脱敏
- Power 权限检查
- TXT 实际保存
- 本地知识库索引

已补一层最小 smoke test：

- 开源包新增 `scripts/smoke-test.ps1`。
- GitHub Actions 会构建 exe，并用反射验证 `.docx/.pptx/.xlsx` 真实导出函数能生成有效 Office zip 包。

仍不足：

- 还没有完整单元测试框架。
- Provider、任务存储、文件解析、脱敏、权限检查还需要拆成无 UI 类后再补测试。

### 3. 文档解析很粗糙

docx/pptx/xlsx 解析只是轻量 XML 文本提取。

问题：

- Excel sharedStrings 处理粗糙。
- 单元格格式、日期、公式可能错。
- PPT 顺序和备注不完整。
- PDF 没有本地文本提取。
- 图片 OCR 依赖云端。

### 4. 本地知识库还不是真 RAG

当前已从“整文件记录”升级为“chunk 记录 + 关键词检索”。

已补：

- 每个文件会拆成多个 chunk。
- chunk 记录 `docId`、`chunkId`、`path`、`name`、`heading`、`text`、`summary`、`tags`、`layer`、`offset`、`modifiedAt`。
- 检索结果会返回 chunkId、heading、path、summary、snippet，便于引用溯源。

仍不足：

- 还不是向量 RAG。
- 还没有 embedding 召回和重排。

下一步建议：

- 支持 Ollama embedding：`nomic-embed-text`
- 支持 `bge-m3`
- 本地保存向量到 JSONL 或 SQLite
- 分 chunk
- chunk 带 source path、offset、heading
- 检索结果必须引用 chunk

### 5. 插件系统有安全风险

当前已缓解一层：默认只允许 `.py/.ps1`，`.exe/.bat/.cmd` 必须在 Settings 里打开高级插件开关。

虽然有 Power 开关，但仍然危险。

已补：

- 默认只允许 `.py` 和 `.ps1`
- 插件目录白名单
- 执行前显示脚本路径、参数、stdin 摘要
- `.exe/.bat/.cmd` 另设高级权限
- 加超时和输出大小限制，目前已有 30 秒超时，但还不够

### 6. 多语言不完整

现在只是主要 UI 翻译。

问题：

- 很多工具结果仍是英文。
- 有些硬编码中文。
- 不是真正资源文件。
- 应改成统一 i18n 字典。

### 7. UI 仍然偏原型

WinForms 默认控件能用，但不够现代。

问题：

- 按钮拥挤。
- 工具入口越来越多。
- 设置页空间紧。
- 聊天消息没有复制按钮、重新生成按钮、保存单条按钮。
- 模型状态测试按钮已补：Gemini、OpenRouter、Local、Embedding。

仍不足：

- 按钮拥挤。
- 工具入口越来越多。
- 设置页仍偏长。
- 聊天消息还没有复制按钮、重新生成按钮、保存单条按钮。

### 8. 权限模型还太粗

现在只有一个总开关 `Power`。

未来应细分：

- 文件写入
- 文件移动/删除
- 进程管理
- 插件执行
- 屏幕截图
- 剪贴板读取
- 网络请求
- 自动化点击/键盘

每类权限应可单独打开/关闭。

### 9. 没有真正的跨应用自动化

当前“当前窗口上下文”只记录：

- 前台窗口标题
- 进程名
- 剪贴板文本

还不能读取：

- 当前选中文本
- 浏览器页面 DOM
- Excel 当前选区
- Word 当前文档
- Outlook 当前邮件

如果要做深度办公自动化，建议走插件/适配器：

- Office COM
- 浏览器扩展或 Playwright
- UI Automation
- Outlook COM
- WeChat/企业微信只能谨慎处理，注意隐私和法律风险

### 10. 构建与发布还很原始

当前已从“只有 exe 和 zip”补到：

- GitHub Actions 构建：`.github/workflows/build.yml`
- 最小 smoke test：`scripts/smoke-test.ps1`
- changelog：`CHANGELOG.md`

仍建议：

- Release 版本号
- 签名或至少 SHA256
- 安装器
- 自动更新机制

## 给下一个智能体的优先任务建议

### P0：防止继续“幻觉执行”

检查所有用户可能要求“生成/保存/导出/移动/删除/运行”的路径。

原则：

- 模型不能宣称完成本地副作用。
- 所有副作用由桌面端实际执行。
- 成功后必须显示真实路径或真实结果。

### P1：拆分源码

不要继续无限加到单文件。

最小拆分：

- Config
- Providers
- File extraction
- Knowledge index
- Permission/Audit
- MainForm

### P1：本地知识库升级为 chunk

已完成第一版 chunk 化，不再只按整个文件保存 `Text`。

已实现字段：

- docId
- chunkId
- path
- name/title
- heading
- text
- summary
- tags
- layer
- offset
- modifiedAt

搜索已返回 chunk，而不是整个文件。下一步是 embedding。

### P2：接本地 embedding

优先接 Ollama：

- `nomic-embed-text`
- `bge-m3`

Settings 里已加入 embedding model 配置和 Test Embedding 按钮。

仍未完成：

- 真正把 embedding 写入索引。
- 向量检索。
- chunk 重排。

### P2：做模型连通性测试

Settings 里应该有：

- Test Gemini
- Test OpenRouter
- Test Local
- Test Embedding

已完成第一版，避免用户不知道 API Key 是否能用。

### P2：改进文件导出

现在支持保存最近 AI 回复为 txt/md/docx/pptx/xlsx，也支持导出整段聊天，并写入本地 export history。

已补：

- 保存整段聊天为 txt/md/docx
- 保存报告为 md/docx/pptx/xlsx（通过 Save File / 自动格式识别）
- 支持用户指定文件名（SaveFileDialog）
- 最近导出记录：`%APPDATA%\ZhuaQianDesktop\export-history.jsonl`

仍不足：

- 保存知识库检索结果还没有独立按钮。
- 还没有默认目录策略。
- 还没有 export history UI。

## 安全原则

绝对不要默认启用危险能力。

所有危险能力必须满足：

- 用户主动打开权限
- 用户明确发起动作
- 执行前确认
- 执行后记录审计
- 出错时可读提示

危险能力包括但不限于：

- 删除文件
- 移动大量文件
- 结束进程
- 运行脚本
- 自动点击其他应用
- 发送邮件/消息
- 上传个人文件到第三方
- 改系统设置

## 当前项目真实状态评分

满分 10 分：

- 原型可用性：7
- 产品方向清晰度：8
- 安全意识：7
- 架构健康：4
- UI 成熟度：4
- 文档与开源准备：6
- 本地知识库能力：4
- Agent 能力：3
- 长期可维护性：4

综合判断：

这是一个功能密度很高的早期原型，已经能展示“本地 AI 办公助手”的雏形。但它需要尽快从“单文件功能堆叠”转向“模块化产品架构”。否则继续加功能会降低稳定性，并增加安全风险。

## 下一阶段最重要的一句话

不要再只加按钮。下一步应该是：拆架构、补完整测试、把当前 chunk 知识库接入本地 embedding + 重排，并继续完善真实文件副作用闭环。
