# ZhuaQian Desktop 竞品 UI 与按钮功能深度评估

> 完成度对齐：截至 2026-07-11，已实现/部分实现/未实现的权威表见 `CODE_COMPLETION_ALIGNMENT.md`。本文中早于该表的“下一步/缺口”描述，以该表和本文后续更新说明为准。

更新时间：2026-07-10

本文档评估 Cursor、Trae / CodeBuddy、VS Code、OpenCode、Claude Code、OpenAI Codex、腾讯 WorkBuddy 的 UI 结构、按钮入口、核心交互优点，并提炼给 ZhuaQian Desktop 的产品设计建议。

> 说明：用户提到的 “open code” 本文按 OpenCode / opencode 理解。Trae 与 CodeBuddy 在公开资料中都呈现为 AI IDE / AI 工作助手路线，本文将 Trae 的官网定位与 CodeBuddy 的详细 IDE 文档合并参考。

## 一句话结论

ZhuaQian Desktop 不应该只做“聊天框 + 文件上传”。优秀产品的共同点是：把 AI 能力拆成稳定入口，让用户清楚知道“问答、编辑、计划、执行、审查、授权、导出、回滚”分别发生在哪里。

ZhuaQian 下一步 UI 应采用：

- 左侧：任务 / 项目 / 工作区导航
- 中间：聊天与执行流
- 右侧或弹层：文件、知识库、产物、工具、审计
- 顶部：模型、权限、模式、状态
- 底部：输入框 + 附件 + 引用 + 发送 + 运行/保存按钮
- 每个本地副作用必须有“计划 -> 确认 -> 执行 -> 结果路径/日志”的闭环

## 竞品总览表

| 产品 | 主界面形态 | 关键按钮/入口 | 最大优点 | 对 ZhuaQian 的启发 |
|---|---|---|---|---|
| Cursor | VS Code 风格 IDE + AI 侧栏/Composer/Inline Edit | Chat、Composer/Agent、Inline Edit、Review、Rules、Model、Terminal | AI 与代码编辑器融合深，编辑和审查距离很短 | 借鉴“侧栏任务 + diff/产物审查 + 规则文件” |
| Trae / CodeBuddy | AI IDE + Ask/Craft/Plan 多模式 | + 新会话、@ 引用、Inline Chat、Sidebar Chat、Ask/Craft/Plan、Custom Agents | 模式分工清晰，用户知道 AI 是否会改代码 | ZhuaQian 应明确“问答 / 计划 / 执行 / 工具”模式 |
| VS Code | 通用 IDE 工作台 | Explorer、Search、Source Control、Run、Extensions、Command Palette | 信息架构成熟，所有功能可被命令面板统一调度 | ZhuaQian 需要 Command Palette / 快捷命令中心 |
| OpenCode | Terminal TUI / Desktop / IDE extension | Prompt 输入、Session、Share、Undo、Provider config | 极简、低干扰，适合开发者工作流 | 给高级用户保留键盘流和可脚本化接口 |
| Claude Code | Terminal agent + Agent View | claude、resume、pipe、slash commands、Agent View、Needs input/Working/Completed | 多 Agent 监督视图非常适合长任务 | ZhuaQian 应做任务状态分组：等待确认、运行中、已完成、失败 |
| OpenAI Codex | CLI / IDE extension / Desktop app / Cloud | Codex Sidebar、Composer、Local/Cloud、Diff Review、Threads、Skills、Automations | 多表面统一，任务可本地或云端委派 | ZhuaQian 应从“单聊天”升级为“多任务指挥台” |
| Tencent WorkBuddy | 桌面 AI 办公工作台 + 手机远控 + 技能生态 | 新建任务、确认计划、授权文件夹、技能/专家、沙盒执行、查看结果、远控绑定 | 面向办公结果交付，强调可验收产物 | ZhuaQian 应把“生成文件/报告/表格/归档”做成真实产物中心 |

## 1. Cursor

### UI 理解

Cursor 本质是一个熟悉的代码编辑器外壳，加上 AI Agent 侧栏、Composer、Inline Edit、代码补全、变更审查。它的优势不是按钮多，而是 AI 入口嵌在开发者实际停留的地方：编辑器、选中文本、终端、文件树、diff。

公开资料显示 Cursor 强调 agent 能理解整个代码库、计划和修改代码，并支持跨桌面、CLI、Web、移动等工作面。官方产品页展示的任务流包括读取文件、搜索快捷键处理器、编辑文件、完成后提示用户按快捷键使用功能。

### 关键按钮/入口

- Chat / AI 侧栏：解释代码、问项目问题
- Composer / Agent：多文件任务和较复杂改动
- Inline Edit：选中代码后直接改
- Accept / Reject / Review：审查 AI 产生的变更
- Rules：项目规则、团队约束
- Terminal：让 agent 跑命令、测试、构建
- Model / Settings：模型选择、隐私、上下文设置

### 优点总结

- 用户不用离开代码上下文
- AI 既能问答，也能修改
- 小任务用 Inline，大任务用 Composer/Agent
- Review 入口降低误改风险
- Rules 把经验固化到项目

### ZhuaQian 可借鉴

ZhuaQian 的办公场景也应分层：

- 快问：普通聊天，不改文件
- 写作：生成 Word/PPT/Excel/TXT 产物
- 执行：会动本地文件，必须确认
- 审查：显示将要写入/移动/删除的文件
- 规则：用户定义常用格式、公司模板、隐私要求

## 2. Trae / CodeBuddy

### UI 理解

Trae 官网定位为 “TRAE Work: Professional AI Work Assistant” 与 “TRAE IDE: 10x AI Coding Engineer”。CodeBuddy 文档更具体地展示了 AI IDE 的交互结构：Inline Chat、Sidebar Chat、新会话、`@` 引用文件/文件夹/代码片段、截图粘贴、Figma 链接导入、Ask/Craft/Plan 三种模式、自定义 Agents、Memories、Rules、Skills、MCP、自定义模型。

### 关键按钮/入口

- `+` 新会话
- Inline Chat：代码内局部交互
- Sidebar Chat：侧边栏完整聊天
- `@` 引用：文件、文件夹、当前代码片段
- Ask：只问不改
- Craft：局部生成/修改
- Plan：复杂任务先计划，确认后执行
- Custom Agent：自定义业务型 agent
- Memory / Rules / Skills / MCP / Custom Models

### 优点总结

- 模式命名清晰：Ask 不改，Craft 小改，Plan 大任务
- `@` 引用让上下文选择可见
- Plan 模式天然适合安全确认
- 自定义 agent 和技能系统适合团队扩展
- 截图/Figma 等多模态入口贴近 UI 开发

### ZhuaQian 可借鉴

ZhuaQian 应在顶部或输入框旁加入模式切换：

- Ask：只回答
- Draft：生成草稿/文档
- Plan：规划多步骤任务
- Execute：执行本地动作，受 Power 权限控制

输入框应支持明显的引用入口：

- `@文件`
- `@文件夹`
- `@知识库`
- `@剪贴板`
- `@屏幕截图`
- `@当前窗口`

## 3. VS Code

### UI 理解

VS Code 的成功在于信息架构稳定：Activity Bar、Side Bar、Editor、Panel、Status Bar、Command Palette。它不是把所有按钮堆出来，而是用统一布局承载文件、搜索、Git、调试、扩展、终端。

官方文档强调 Command Palette 是访问所有功能的中心，可运行命令、打开文件、搜索符号、查看文件大纲；快捷键如 `Ctrl+Shift+P`、`Ctrl+P`、`Ctrl+G` 都服务于“键盘优先”。

### 关键按钮/入口

- Activity Bar：Explorer / Search / Source Control / Run / Extensions
- Command Palette：统一命令入口
- Quick Open：快速打开文件
- Source Control：暂存、提交、分支、差异
- Panel：Terminal / Problems / Output / Debug Console
- Status Bar：语言、分支、错误、环境状态

### 优点总结

- 功能很多但位置稳定
- 命令面板降低 UI 拥挤
- 状态栏承担低频但重要状态
- 文件、搜索、Git、终端各有明确区域
- 扩展体系让产品不必把所有功能做死

### ZhuaQian 可借鉴

ZhuaQian 需要一个“命令面板”：

- 搜索工具
- 新建任务
- 上传文件
- 截图 OCR
- 导出聊天
- 索引知识库
- 测试模型
- 打开审计日志
- 打开设置

这样左侧按钮可以减少，UI 不会越来越挤。

## 4. OpenCode / opencode

### UI 理解

OpenCode 是开源 AI coding agent，官方文档说明它可作为 terminal TUI、desktop app 或 IDE extension 使用。TUI 入口简单：在当前目录运行 `opencode`，进入交互界面后输入 prompt。

官网还强调 LSP enabled、多 session、share links、可用 Copilot 或 ChatGPT 账号登录等。

### 关键按钮/入口

- TUI Prompt 输入
- Session 管理
- Share session
- Undo changes
- Provider / Account 登录
- IDE / Desktop surface

### 优点总结

- 极简，启动快
- 适合键盘用户和开发者
- 多 session 适合并行探索
- Share link 对团队协作有价值
- Undo 是 agent 工具必须具备的信任入口

### ZhuaQian 可借鉴

ZhuaQian 不一定要做终端 UI，但要保留高级入口：

- 快捷键命令面板
- 可导出任务链接/任务包
- 每次本地操作生成可回滚记录
- 对开发者暴露插件/脚本 SDK

## 5. Claude Code

### UI 理解

Claude Code 的主要形态是 terminal agent。官方 CLI 文档列出 `claude` 启动交互会话、`claude "query"` 带初始提示启动、`claude -p` 查询后退出、管道输入、继续最近对话等。

Claude Code 的 Agent View 更值得关注：它把多个后台 session 放在一个屏幕，显示哪些需要输入、哪些正在工作、哪些已完成。官方描述强调用户无需滚动 transcript，只要看状态并在需要时介入。

### 关键按钮/入口

- CLI：`claude`
- 初始 prompt：`claude "query"`
- Pipe：`cat file | claude -p`
- Continue / Resume
- Slash commands
- Agent View：Needs input / Working / Completed
- Background sessions

### 优点总结

- CLI 对开发者非常自然
- session 可恢复，适合长任务
- Agent View 很适合监督多任务
- 需要用户输入的任务被单独分组，降低遗漏

### ZhuaQian 可借鉴

ZhuaQian 左侧任务列表应该升级为状态视图：

- 需要确认
- 运行中
- 已完成
- 失败
- 已暂停

每个任务卡片显示：

- 当前步骤
- 最近产物
- 是否需要授权
- 是否有错误
- 可继续 / 取消 / 打开结果

## 6. OpenAI Codex

### UI 理解

Codex 现在是多表面产品：CLI、本地 IDE extension、desktop app、cloud。官方 IDE 文档强调从已打开的上下文开始：打开文件、选中代码、最近线程都可以加入 composer；用户可以在代码旁审查变更，保留需要的修改；任务变大时可在本地和 cloud 之间委派。

Codex app 官方介绍把它定位为 “command center for agents”，支持多个 agents 并行、skills、automations、安全可配置。桌面 app 的重点不是单个聊天，而是管理多个长任务和多个 agent。

### 关键按钮/入口

- Codex Sidebar
- Composer
- Add open file / selection / recent thread
- Work locally / Cloud
- Review diff beside code
- Threads
- Skills
- Automations
- Worktrees
- Open in editor

### 优点总结

- 同一 agent 跨 CLI / IDE / desktop / cloud
- 快任务本地做，长任务云端跑
- 多线程、多 agent、worktree 降低冲突
- diff review 把“执行结果”变成可审查对象
- skills 让能力可扩展、可复用

### ZhuaQian 可借鉴

ZhuaQian 应建立“办公任务线程”：

- 每个任务有文件、上下文、计划、执行日志、产物
- 长任务可后台运行
- 产物可打开、导出、重新生成
- 技能库从按钮升级为可安装/可编辑的技能
- 执行任务前生成一个可审查计划

## 7. Tencent WorkBuddy

### UI 理解

WorkBuddy 的定位不是代码 IDE，而是办公 AI Agent 桌面工作站。腾讯云资料描述它能用自然语言指令拆解任务、规划执行、交付可验证结果；支持多格式文件处理、授权文件夹内本地文件操作、远程控制、技能生态、沙盒执行。

其公开使用流程非常关键：

1. Brief it：用户描述任务
2. Confirm the plan：agent 展示步骤，用户批准
3. Watch it work：沙盒中显示执行进度
4. Get the result：交付文件、图表或报告

App Store 页面还展示其移动端/多端路线：微信扫码即用、12 个行业、140+ AI 顾问、2.2 万技能插件、云上/本机双模式、通过对话完成工作产物。用户评论也暴露了跨端上下文同步、记忆共享、产物类型、沙盒资源占用等体验痛点。

### 关键按钮/入口

- 新建任务
- 输入自然语言 brief
- 计划确认
- 授权文件夹
- 沙盒执行
- 查看结果 / 下载产物
- 技能 / 专家
- 远程控制绑定
- 手机端分享文件
- 任务完成提醒

### 优点总结

- 面向非程序员，目标是“交付结果”
- 计划确认让执行更可信
- 文件夹授权比全盘权限更安全
- 沙盒执行是本地操作的安全底座
- 技能/专家生态降低用户 prompt 成本
- 远程控制把桌面 agent 变成随时可调度的“工作台”

### ZhuaQian 可借鉴

ZhuaQian 的核心差异化也应该从“能聊”变成“能交付”：

- 生成 Word/PPT/Excel/TXT 必须真实落盘
- 文件整理必须先预览移动清单
- Excel 分析必须输出图表建议/公式/宏/报告文件
- 远程控制先做局域网扫码上传，再做手机端任务触发
- 文件夹授权做成独立权限，不应只有一个总 Power 开关

## 通用 UI 规律

### 1. 模式必须可见

用户必须知道当前 AI 是：

- 只回答
- 生成草稿
- 修改文件
- 执行任务
- 后台运行
- 等待确认

建议 ZhuaQian 顶部增加模式分段控件：

```text
Ask | Draft | Plan | Execute
```

### 2. 上下文必须可见

优秀产品不会让用户猜 AI 看到了什么。上下文入口通常包括：

- 当前文件
- 选中文本
- 文件夹
- 知识库
- 截图
- 剪贴板
- 终端错误
- 最近线程

建议 ZhuaQian 输入框上方显示 Context Chips：

```text
@文件: 合同.docx   @截图: 2026-07-10.png   @知识库: 项目A 8 chunks
```

### 3. 执行必须可审查

Agent 类产品最大风险是“它说做了，但没做”或“它做了，但用户没看懂”。必须拆成：

```text
计划 -> 确认 -> 执行 -> 日志 -> 结果 -> 回滚
```

建议 ZhuaQian 对本地动作使用统一执行面板：

- 将创建哪些文件
- 将移动哪些文件
- 将调用哪些插件
- 将读取哪些文件夹
- 是否上传云端
- 成功后的真实路径

### 4. 产物要成为一等对象

WorkBuddy 和 Codex 都强调结果可验收。ZhuaQian 应新增 “Outputs / 产物” 区域：

- 最近生成的 Word
- 最近生成的 PPT
- 最近生成的 Excel
- 最近导出的聊天
- 批量报告
- 插件输出

每个产物卡片按钮：

- Open
- Reveal in Folder
- Rename
- Export as
- Re-generate
- Add to Knowledge Base

### 5. 权限要细分

一个 Power 开关不够。建议拆成：

- 文件读取
- 文件写入
- 文件移动/删除
- 屏幕截图
- 剪贴板读取
- 插件执行
- 进程管理
- 网络上传
- 自动点击键盘

UI 可以是 Settings -> Permissions，也可以在第一次触发时弹权限卡。

## ZhuaQian Desktop 推荐 UI 蓝图

### 顶部栏

```text
ZhuaQian | Provider/Model | Mode: Ask/Draft/Plan/Execute | Power | Tools | Settings
```

按钮建议：

- Model 状态：绿色/黄色/红色
- Test：快速测试当前模型
- Power：总权限状态
- Permissions：细分权限入口
- Tools：工具箱
- Command：命令面板

### 左侧栏

```text
+ New Task
Search tasks

Needs input
- 项目A周报

Working
- 整理发票

Completed
- 生成PPT

Failed
- 插件运行失败
```

每个任务显示：

- 状态色
- 最近更新时间
- 产物数量
- 是否有待确认

### 主聊天区

每条消息建议带：

- 角色颜色
- Copy
- Save
- Use as context
- Regenerate
- Add to KB

Agent 执行消息不要只显示文字，应显示步骤：

```text
Plan
1. 读取文件夹
2. 分析 Excel
3. 生成报告.docx

[Approve] [Edit Plan] [Cancel]
```

### 右侧面板

可折叠 Tabs：

- Context
- Files
- Outputs
- Knowledge
- Audit

### 底部输入区

```text
[ @ Context ] [ Upload ] [ Screenshot ] [ Clipboard ] [ Voice ]
---------------------------------------------------------------
Ask ZhuaQian...
---------------------------------------------------------------
[Save File] [Plan] [Send]
```

## 按钮优先级建议

### v0.2 必做按钮

- Command Palette
- Mode: Ask / Draft / Plan / Execute
- Export Chat
- Outputs 面板
- Copy message
- Save message
- Add to KB
- Test current model
- Permission details

### v0.3 必做按钮

- Approve Plan
- Edit Plan
- Cancel Task
- Reveal Output
- Re-run with same context
- Undo last file operation
- Open audit event

### v0.4 再做

- 手机扫码上传
- 远程触发任务
- 自动化日程任务
- 技能市场
- 跨设备同步

## 对当前 ZhuaQian 的直接改造建议

### 1. 把 Tools 弹窗改成 Command Palette

当前 Tools 按钮越来越多，下一步应做搜索式命令面板：

```text
Ctrl+K / Ctrl+Shift+P
> index folder
> export chat
> screenshot ocr
> test gemini
```

### 2. 把 Power 拆成权限页

当前 Power 是总开关，保留它，但 Settings 中增加：

```text
[x] Read files
[x] Write files
[ ] Move/delete files
[x] Screenshot
[ ] Clipboard monitor
[ ] Run plugins
[ ] End processes
[ ] Network upload
```

### 3. 做 Outputs 面板

已经有真实导出能力，应继续把产物做成列表：

```text
Outputs
- 项目计划.docx
- 销售分析.xlsx
- 路演稿.pptx
- chat-export.md
```

### 4. 任务状态化

左侧任务不应只是标题列表，应变成状态列表：

```text
Draft
Needs approval
Running
Completed
Failed
```

### 5. 知识库引用卡片

搜索结果应在 UI 中显示为卡片：

```text
[chunkId] 文件名
heading
snippet
[Open file] [Use in chat]
```

## 来源

- Cursor 官方产品页：https://cursor.com/product
- Cursor 官方站点：https://cursor.com/
- Cursor 快捷键文档：https://cursor.com/docs/reference/keyboard-shortcuts
- Trae 官方站点：https://www.trae.ai/
- CodeBuddy IDE 文档：https://www.codebuddy.ai/docs/ide/User-guide/Overview
- VS Code UI 文档：https://code.visualstudio.com/docs/editing/userinterface
- VS Code Source Control 文档：https://code.visualstudio.com/docs/sourcecontrol/overview
- OpenCode 文档：https://opencode.ai/docs/
- OpenCode TUI 文档：https://opencode.ai/docs/tui/
- Claude Code CLI 文档：https://code.claude.com/docs/en/cli-reference
- Claude Code Agent View 文档：https://code.claude.com/docs/en/agent-view
- OpenAI Codex IDE 文档：https://developers.openai.com/codex/ide
- OpenAI Codex app 介绍：https://openai.com/index/introducing-the-codex-app/
- OpenAI Codex GitHub：https://github.com/openai/codex
- Tencent WorkBuddy 官方页：https://copilot.tencent.com/work/
- Tencent WorkBuddy 海外安装使用指南：https://www.tencentcloud.com/techpedia/144100?lang=en
- WorkBuddy App Store 页面：https://apps.apple.com/cn/app/workbuddy-%E4%BD%A0%E7%9A%84-ai-%E5%B7%A5%E4%BD%9C%E5%8F%B0/id6761374913

