# ZhuaQian Desktop 推广前途评估

更新时间：2026-07-12

## 一句话判断

有推广前途，但必须走“免费开源 Windows 本地 AI 工作台”路线，而不是泛泛宣传成 AI 助手。

如果只说“AI 聊天、读文档、导出文件”，竞争会非常拥挤；如果说“本地优先、权限可见、审计可查、能真实落地文件和插件的 Windows AI 工作台”，差异化会清楚很多。

## 最强定位

推荐定位：

```text
ZhuaQian Desktop is a free, open-source, local-first AI workbench for Windows.
```

中文定位：

```text
抓钱桌面版：免费开源的 Windows 本地 AI 工作台。
```

副标题：

```text
聊天只是入口，真正重点是文件、产物、权限、审计、插件和本地工作流。
```

## 目标用户

第一批用户不应追求“大众办公人群”，而应聚焦：

1. Windows 重度用户
2. 独立开发者
3. 自动化爱好者
4. 本地部署/隐私敏感用户
5. 想研究桌面 Agent 架构的开发者
6. 需要多 provider 切换的 AI 工具玩家

这些人能容忍 v0.1 原型的不完美，也更可能贡献 issue、PR、插件和测试。

## 推广卖点

### 1. 免费开源

MIT 许可证降低采用和二次开发门槛。推广时要明确：

- 无订阅绑定
- 无内置后端锁定
- 无强制账号体系
- 用户自己配置 provider key 或使用本地模型

### 2. Windows 原生

不要把 Windows-only 当短板隐藏。它是项目短期差异化：

- 能调用 WinForms、本地文件、剪贴板、截图、进程
- 比 Web app 更接近真实桌面工作流
- 比 IDE 插件更适合普通文件/办公场景

### 3. 本地优先和权限可见

推广时强调“AI 不应该偷偷做事”：

- 云端上传前提示
- API key 本地 DPAPI 保护
- 高风险动作需要 Power / Permission / Approval
- 有本地 audit / outputs 记录

### 4. 真正产物落地

不要只演示聊天，必须演示：

- 生成 Word
- 生成 PPT
- 生成 Excel
- 整理文件夹并回滚
- 索引本地资料再问答

### 5. 面向开发者可扩展

适合开源社区贡献：

- provider
- document parser
- exporter
- plugin runner
- permission policy
- local knowledge base
- UI panels

## 推广弱点

1. UI 仍是 WinForms 原型，第一眼不够现代。
2. 三套源码树并存，会让贡献者困惑。
3. 没有安装器，普通用户上手门槛高。
4. 测试不是标准 .NET 项目结构。
5. 安全边界仍需谨慎表达，不能营销成企业安全工具。

## 首发渠道建议

### 第一阶段：开发者冷启动

目标是拿 star、issue、反馈，不是大规模普通用户。

- GitHub README + Releases
- V2EX / 开发者社区
- Reddit / Hacker News 的 Show HN 风格帖子
- Product Hunt 可等 UI 和 installer 稍微成熟后再发
- Bilibili / 小红书 / 抖音适合做演示短视频，但不要作为第一技术反馈来源

### 第二阶段：内容演示

建议做 5 个短 demo：

1. 本地 Excel -> 分析建议 -> 导出报告
2. 截图 OCR -> 总结 -> 生成 Markdown
3. 资料文件夹索引 -> 本地知识库问答
4. 混乱下载目录 -> 自动整理 -> 回滚
5. 插件运行 -> 审批 -> audit/output 记录

### 第三阶段：插件生态

等核心结构稳定后，开放：

- example plugins
- plugin manifest
- skill templates
- provider adapter guide

## Star 增长判断

保守判断：

- 只发布源码，无视频、无截图：增长慢，主要靠偶然发现。
- README + 截图 + 可执行 exe + 5 个 demo：有机会形成第一波开发者关注。
- 如果后续补 installer、标准 .sln、插件例子、本地模型教程，传播潜力明显提高。

短期目标建议：

- 第 1 周：10-50 stars，收集真实 issue
- 第 1 月：100-300 stars，形成贡献者入口
- 3 个月：如果 demo 和插件生态跟上，有机会冲 500+ stars

这不是承诺，只是基于当前产品差异化和开源完成度的推广预估。

## 最推荐的首发文案

英文：

```text
ZhuaQian Desktop is a free, open-source, local-first AI workbench for Windows.
It can chat with multiple providers, read local files, export real Office documents,
index local knowledge, run trusted plugins, and keep risky desktop actions visible
through permissions, approvals, audit logs, and output records.
```

中文：

```text
抓钱桌面版是一个免费开源的 Windows 本地 AI 工作台。
它不只是聊天框，而是把文件读取、Office 导出、本地知识库、插件、权限审批、审计日志和产物记录放在一个桌面工作流里。
```

## 推广前必须避免

- 不要说“完全替代 Cursor / Claude Code / WorkBuddy”。
- 不要说“企业级安全”。
- 不要说“全自动操作电脑”。
- 不要把未完成的 Agent / MCP / CLI 当成已完成能力。
- 不要默认鼓励用户上传隐私文件到云端 provider。

## 下一步最高杠杆

1. 截图和 60 秒 demo 视频。
2. 干净 GitHub 仓库。
3. Release 里提供 exe + SHA256。
4. README 顶部放真实 GIF/截图。
5. 把“如何新增 provider / plugin”写成贡献教程。
6. 合并源码事实源，减少 `src/` 和 `work/` 的解释成本。
