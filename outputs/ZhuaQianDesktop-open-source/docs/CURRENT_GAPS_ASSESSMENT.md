# ZhuaQian Desktop 当前不足评估

> 完成度对齐：截至 2026-07-11，已实现/部分实现/未实现的权威表见 `CODE_COMPLETION_ALIGNMENT.md`。本文中早于该表的“下一步/缺口”描述，以该表和本文后续更新说明为准。
>
> 2026-07-16 更新：`CODE_COMPLETION_ALIGNMENT.md` 已补充当前实现矩阵。本文仍保留为差距分析参考；遇到冲突时，以 `CODE_COMPLETION_ALIGNMENT.md`、`CURRENT_REALITY_2026-07-11.md`、`PROJECT_HANDOFF_2026-07-16.md` 为准。

更新时间：2026-07-10

本文档评估当前 ZhuaQian Desktop 在完成多模型、文件读取、真实导出、知识库 chunk、Command Palette、Outputs、权限细分等功能后的真实不足。目标是帮助下一轮开发避免“继续堆按钮”，把项目从可演示原型推进到可开源协作的稳定版本。

## 当前状态一句话

ZhuaQian Desktop 已经从“聊天框 + 上传”升级到“轻型 Windows AI 工作台原型”，但仍不是成熟产品。现在最大的矛盾不是缺少入口，而是：功能很多、架构仍是单文件、状态与权限模型不够一致、缺少系统测试、知识库还没有真正语义检索、执行闭环还不够完整。

## 已具备的基础

当前版本已经具备：

- Windows WinForms 桌面 exe
- 多任务左侧栏
- `Ask / Draft / Plan / Execute` 模式
- `Cmd` 命令面板
- `Outputs` 产物面板
- Gemini / OpenRouter / Local provider
- Provider 测试按钮
- 文件上传与轻量解析
- 图片/PDF/Gemini 视觉入口
- Screenshot OCR 工作流
- 剪贴板监控
- 本地知识库 chunk 索引
- 真实导出 `.txt/.md/.docx/.pptx/.xlsx`
- 整段聊天导出
- 导出历史
- Power 总开关
- 细分权限面板
- 插件运行安全收窄
- 审计日志
- GitHub Actions 构建文件
- smoke test
- README / ROADMAP / CHANGELOG / 竞品 UI 评估文档

这已经是一个能跑、能演示、能初步开源的原型。

## P0 级不足：必须优先解决

### 1. 源码仍是单文件，已经接近不可维护

当前主源码：

```text
work/zq-desktop/ZhuaQianDesktop.cs
```

文件体积已经接近 200 KB。它同时负责：

- UI 构建
- 配置保存
- 任务存储
- Provider 调用
- 文件解析
- Office 导出
- 知识库索引
- 插件执行
- 权限判断
- 审计日志
- 命令面板
- Outputs
- 多语言

风险：

- 新功能很容易误伤旧功能。
- 很难写单元测试。
- 多人协作会频繁冲突。
- 任何一次 UI 调整都可能影响 provider、导出、权限等无关逻辑。

建议立即拆分：

```text
src/
├─ Program.cs
├─ MainForm.cs
├─ Ui/
│  ├─ CommandPalette.cs
│  ├─ OutputsPanel.cs
│  ├─ SettingsDialog.cs
│  └─ PermissionDialog.cs
├─ Core/
│  ├─ ConfigStore.cs
│  ├─ ChatTaskStore.cs
│  ├─ AuditLog.cs
│  └─ PermissionGate.cs
├─ Providers/
│  ├─ GeminiProvider.cs
│  ├─ OpenRouterProvider.cs
│  └─ LocalProvider.cs
├─ Documents/
│  ├─ DocumentExtractor.cs
│  ├─ OfficeExporter.cs
│  └─ Redactor.cs
├─ Knowledge/
│  ├─ KnowledgeIndex.cs
│  ├─ Chunker.cs
│  └─ KnowledgeSearch.cs
└─ Tools/
   ├─ PluginRunner.cs
   ├─ FolderOrganizer.cs
   └─ ResourceMonitor.cs
```

### 2. 权限模型已有雏形，但一致性还不够

当前已经有：

- Power 总开关
- 细分权限
- 文件写入
- 文件移动/删除
- 进程管理
- 插件执行
- 截图
- 剪贴板监控

本轮已补：

- 文件读取已有独立权限 `Read local files`。
- 网络/云端上传已有独立权限 `Cloud/network upload`。
- 上传文件、知识库索引、Excel 助手、批量报告、附件读取已接入文件读取权限。
- 云端 Gemini/OpenRouter/Auto 云端路径已接入网络上传权限。

仍不足：

- 自动化点击/键盘已经有 `permAutomationInput` 权限预留和基础电脑控制 executor，但还缺完整的桌面自动化工作流、持续状态展示和更细的目标限制。
- `Save File` 不需要 Power，但模板草稿写入需要 Power，体验上不完全一致。
- Outputs 面板打开本地文件没有权限审计。
- `Use Current Context` 已在剪贴板权限关闭时跳过剪贴板正文，但还没有弹出“是否临时授权”的体验。
- Provider 调用已有网络上传权限闸门；长文本、附件、inline 文件上传前已有摘要确认。

下一步建议：

- 继续细化 `permAutomationInput`：区分打开目标、键盘输入、鼠标点击、窗口读取、批量自动化等子权限。
- 所有打开本地文件、读取任务上下文、读取剪贴板的路径继续统一到 `PermissionGate`。
- 权限拒绝时要提供“打开设置”的按钮。
- 每次云端调用前，如果有附件或大量文本，应显示 provider 与可能上传内容摘要。

### 3. 真实副作用闭环还不完整

已经修复“模型说生成文件但文件不存在”的问题，也增加了真实导出。

本轮已补：

- 文件整理会生成 rollback manifest：
  `%APPDATA%\ZhuaQianDesktop\rollback\organize-*.json`
- rollback manifest 会进入 Outputs history。

本轮继续补：

- 已增加 `Tools -> Rollback Files` 和 `Cmd -> Rollback organized files`。
- 可读取 organize rollback manifest，预览路径并尽量将文件移回原位置。

仍不足：

- 结束进程不可回滚，风险提示仍粗。
- 插件执行结果只放入输入框，缺少产物化记录。
- 计划模式只是提示模型写计划，没有真正的 `Approve Plan / Edit Plan / Cancel` 审批卡。
- Execute 模式仍主要是 prompt 约束，不是严格工作流状态机。

建议：

```text
Plan -> Approval Card -> Execute Tool -> Audit Log -> Output / Rollback
```

每个本地动作记录：

- actionId
- actionType
- requestedAt
- approvedAt
- affectedPaths
- before/after
- result
- rollbackPossible
- rollbackCommand / rollbackManifest

## P1 级不足：影响产品体验

### 4. UI 仍然偏“按钮堆叠”

已经新增：

- 顶部模式选择
- Cmd 命令面板
- Outputs 面板
- Tools 弹窗

仍不足：

- Tools 弹窗和 Command Palette 功能重复。
- 左侧任务列表已有文本状态分组；仍缺少更成熟的 Agent View、颜色/筛选、后台任务监督。
- 右侧没有 Context / Files / Outputs / Knowledge / Audit tabs。
- 聊天消息没有 Copy / Save / Regenerate / Add to KB。
- 产物只是弹窗列表，不是持久右侧面板。
- 模式选择是顶部下拉框，状态不够强。
- 用户不容易看出“AI 当前看到了什么上下文”。

建议下一轮 UI：

```text
Left: Task Status
Center: Chat / Plan / Execution Timeline
Right: Context | Files | Outputs | Knowledge | Audit
Bottom: Context chips + input
Top: Mode + Model + Power + Command
```

### 5. Command Palette 只是第一版

当前 Cmd 能搜索并运行命令。

仍不足：

- 没有快捷键。
- 没有最近命令。
- 没有命令分类。
- 没有权限状态提示。
- 没有命令别名/模糊匹配。
- 没有命令参数。

建议：

- `Ctrl+K` 或 `Ctrl+Shift+P` 唤起。
- 命令列表显示图标、分类、权限状态。
- 支持搜索 `export`、`导出`、`save`。
- 高风险命令显示 `Requires Power + Permission`。

### 6. Outputs 面板还太弱

当前 Outputs 读取：

```text
%APPDATA%\ZhuaQianDesktop\export-history.jsonl
```

支持打开文件和定位文件夹。

仍不足：

- 只记录导出历史，不记录插件产物、批处理产物、文件整理 manifest。
- 没有缩略图、类型图标、大小、是否存在。
- 文件不存在时只弹错，不提供清理记录。
- 没有 rename、delete record、add to KB、regenerate。
- 没有按任务过滤。

建议：

把 Outputs 升级为统一产物数据库：

```json
{
  "outputId": "...",
  "taskId": "...",
  "type": "docx|pptx|xlsx|txt|md|eml|folder|log",
  "path": "...",
  "createdAt": "...",
  "sourceAction": "export|batch|plugin|organize",
  "exists": true,
  "metadata": {}
}
```

## P1 级不足：知识库与 RAG

### 7. 知识库已经 chunk，但还不是语义 RAG

已完成：

- 文件拆 chunk
- chunkId
- heading
- path
- summary
- tags
- layer
- keyword scoring

仍不足：

- 没有 embedding。
- 没有向量库。
- 没有 chunk 重排。
- 没有增量索引。
- 没有删除/更新失效 chunk。
- 没有引用卡片 UI。
- 没有“加入知识库”按钮。
- 没有知识库范围选择。

建议下一步：

- Ollama `/api/embeddings`
- 默认 `nomic-embed-text`
- JSONL 或 SQLite 存储
- cosine similarity
- keyword + vector hybrid retrieval
- 检索结果必须显示 chunkId 与原文件路径

### 8. 文档解析仍很粗糙

当前支持轻量读取：

- docx
- pptx
- xlsx/xlsm
- txt/md/csv/json 等文本
- 图片/PDF 走 Gemini inlineData

仍不足：

- PDF 没有本地文本提取。
- 扫描 PDF 只能靠云端视觉。
- Excel sharedStrings、日期、公式、格式仍可能不准。
- PPT notes、布局顺序、表格解析不完整。
- 旧格式 `.doc/.xls/.ppt` 不能直接解析。
- 大文件只截断，缺少分页/分块读取 UI。

建议：

- 引入可选本地 PDF text extractor。
- Office 解析单独模块化。
- Excel 单元格类型要处理 sharedStrings、inlineStr、number、date、formula。
- 文件解析失败时提供明确提示和替代方案。

## P2 级不足：模型与 Provider

### 9. Provider 测试按钮可用，但不够产品化

当前 Settings 里有：

- Test Gemini
- Test OpenRouter
- Test Local
- Test Embedding

仍不足：

- 测试过程会阻塞 UI。
- 没有 loading 状态。
- 没有保存测试结果。
- 没有展示 quota/rate limit 友好解释。
- 没有自动获取可用模型列表。
- Auto provider 的决策不透明。

建议：

- 测试按钮异步化。
- 显示结果灯：OK / Warning / Error。
- 增加 Model Presets。
- Auto 模式显示实际使用了哪个 provider。

### 10. Prompt 模式只是提示词，不是强约束

当前 `Ask/Draft/Plan/Execute` 会注入 mode instruction。

仍不足：

- Execute 没有真正工具调用协议。
- Plan 没有结构化 plan schema。
- Draft 没有产物模板选择。
- Ask 不能保证不触发自动导出，因为导出检测仍看用户文本。

建议：

定义结构化 agent 协议：

```json
{
  "mode": "plan",
  "intent": "...",
  "requiresPermission": [],
  "proposedActions": [],
  "outputs": []
}
```

## P2 级不足：测试和质量

### 11. 只有 smoke test，缺少真正测试体系

当前 smoke test 验证：

- exe 可加载
- docx/pptx/xlsx 可生成 zip 包

仍不足：

- 没有单元测试。
- 没有 UI 自动化截图测试。
- 没有权限矩阵测试。
- 没有 provider mock。
- 没有知识库 chunk 测试。
- 没有导出历史测试。
- 没有插件安全测试。

建议：

- 先拆无 UI 类，再做测试。
- 加 `tests/`。
- 对 Redactor、Chunker、OfficeExporter、PermissionGate、ConfigStore 写单元测试。
- 用 Playwright/WinAppDriver 或轻量 WinForms 启动测试做 UI 冒烟。

### 12. 错误处理不统一

源码中仍有不少：

```csharp
catch { }
```

问题：

- 用户不知道失败原因。
- 调试困难。
- 审计日志不完整。

建议：

- 所有 catch 至少写入 audit log 或 debug log。
- UI 上给用户可读提示。
- 高风险动作失败要记录 actionId 和原因。

## P2 级不足：开源发布

### 13. 开源结构已有雏形，但还不够正式

已有：

- README
- ROADMAP
- CHANGELOG
- SECURITY
- CONTRIBUTING
- LICENSE
- GitHub Actions
- smoke test
- SHA256

仍不足：

- 没有正式版本号注入 UI。
- 没有 installer。
- 没有 Release notes 模板。
- issue templates 已有第一版。
- PR template 已有第一版。
- 没有 docs screenshots。
- 没有架构图。
- 没有“如何贡献插件”的文档。

建议：

```text
.github/
├─ ISSUE_TEMPLATE/
├─ pull_request_template.md
docs/
├─ architecture.md
├─ permissions.md
├─ plugins.md
├─ screenshots/
```

## 安全风险清单

### 高风险

- 插件运行仍可执行本地脚本。
- 文件整理会移动文件，已有 rollback manifest 和回滚入口；仍需更完整的冲突处理与通用 undo。
- 结束进程可能造成数据丢失。
- 云端 provider 可能上传敏感文本。
- 剪贴板监控可能读取隐私内容。

### 中风险

- API key 已改为 Windows DPAPI protected value，旧明文 key 可迁移读取，新保存会写入 `apiKeyProtected` / `openRouterApiKeyProtected`。
- 审计日志可能记录敏感路径或内容摘要。
- 输出文件可能覆盖用户选择的已有文件。
- `Process.Start(path)` 打开文件可能触发系统关联程序风险。

### 建议

- API key 已使用 Windows DPAPI 加密，后续需处理跨账户迁移和异常恢复提示。
- 审计日志避免记录敏感正文。
- 文件移动已有 rollback manifest，后续需提供一键回滚 UI。
- 云端调用前显示上传内容摘要。
- 插件增加签名/manifest/权限声明。

## 当前评分

满分 10 分：

- 原型可用性：8
- 产品方向清晰度：8
- UI 工作台雏形：6
- 安全意识：7
- 权限模型：6
- 文件真实落盘能力：7
- 知识库能力：5
- Provider 接入：6
- 测试质量：3
- 架构健康：3
- 开源准备：6
- 长期可维护性：3

综合判断：

当前版本已经能作为开源原型发布，适合吸引开发者参与。但它还不适合承诺“安全稳定办公自动化”。下一阶段必须优先做架构拆分、权限闭环、测试体系和知识库 embedding，而不是继续堆更多按钮。

## 下一轮开发优先级

### 第一优先级

1. 拆分单文件架构。
2. 把 PermissionGate、AuditLog、OfficeExporter、Chunker、Redactor 抽成可测试类。
3. 给这些类补单元测试。
4. 给回滚操作增加更完整的冲突处理和结果 manifest。
5. 给云端调用增加“本次临时允许/始终允许”的更细体验。

### 第二优先级

1. 右侧 Context / Files / Outputs / Knowledge / Audit tabs。
2. 左侧任务状态分组。
3. 消息级 Copy / Save / Regenerate / Add to KB。
4. Outputs 升级为产物数据库。
5. Command Palette 增加快捷键和权限状态。

### 第三优先级

1. 本地 embedding。
2. 向量检索 + keyword hybrid。
3. 增量索引。
4. PDF 本地文本提取。
5. Installer 与正式 GitHub Release 流程。
