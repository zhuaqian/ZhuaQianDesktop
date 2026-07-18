# ZhuaQian Desktop — 文档导航

本文件是 `docs/` 的总索引。按"想找什么"挑分类即可。

> 约定：以 `_` 开头的文件是**实时协作/构建产物**（看板、并行协调提示、行数预算、验证记录），会被自动化或并行会话持续改写，不要手改其结论。
> 早期（2026-07 上旬）的协调、进度、评估碎片已全部归入 `docs/archive/coordination-2026-07/`，仅供追溯。

## 一、规范文档（产品与架构真相源）

- [ARCHITECTURE_CHARTER.md](ARCHITECTURE_CHARTER.md) — 架构宪章：边界、预算、模块契约的硬约束。
- [PRODUCT_REQUIREMENTS.md](PRODUCT_REQUIREMENTS.md) — 产品需求。
- [PRODUCT_ARCHITECTURE.md](PRODUCT_ARCHITECTURE.md) — 产品架构总览。
- [PRODUCT_IMPLEMENTATION_DIRECTION_2026-07-16.md](PRODUCT_IMPLEMENTATION_DIRECTION_2026-07-16.md) — 实现方向与取舍（2026-07-16）。
- [DESKTOP_PROMPT_COLLABORATION_2026-07-17.md](DESKTOP_PROMPT_COLLABORATION_2026-07-17.md) — 桌面 Prompt 工作台协作设计。

## 二、插件与扩展

- [PLUGIN_ECOSYSTEM.md](PLUGIN_ECOSYSTEM.md) — 插件清单契约、可信文件夹、权限与信任徽章指引。
- [MCP_RESEARCH_SPIKE.md](MCP_RESEARCH_SPIKE.md) — MCP 兼容性调研：**当前未实现 MCP**，钩子框架是未来接缝。

## 三、开源相关

- [FREE_OPEN_SOURCE_RELEASE_PLAN.md](FREE_OPEN_SOURCE_RELEASE_PLAN.md) — 免费开源发布计划。
- [PRE_OPENSOURCE_CHECKLIST.md](PRE_OPENSOURCE_CHECKLIST.md) — 开源前检查清单。
- [OPEN_SOURCE_MONITORING_BOUNDARY.md](OPEN_SOURCE_MONITORING_BOUNDARY.md) — 开源监控边界（本地活动监控能/不能做什么）。

## 四、交接与进度

- [PROJECT_HANDOFF_2026-07-16.md](PROJECT_HANDOFF_2026-07-16.md) — 项目交接说明（含 `work/zq-desktop/` 镜像退役记录）。
- [EXECUTION_BACKLOG.md](EXECUTION_BACKLOG.md) — **实时** Epic 待办与完成度（A–F）。

## 五、实时协作 / 构建（被自动化与并行会话改写，慎手改）

- [_collab_board.md](_collab_board.md) — 并行协作看板：当前执行者、已落地、风险、用户指令。
- [_parallel_coordination_prompts.md](_parallel_coordination_prompts.md) — 跨会话协调提示协议（文件系统传纸条）。
- [patches/](patches/) — 构建登记补丁（部分已随动态构建而过时，见看板说明）。
- [_line_budget.json](_line_budget.json) — 主文件行数预算配置。
- [_last_verification.txt](_last_verification.txt) — 最近一次验证记录。

## 六、归档（历史追溯，非当前真相）

- `docs/archive/coordination-2026-07/` — 2026-07 上旬协调/进度/评估碎片（19 篇）。
- `docs/archive/historical/` — 早期愿景、竞品分析、可行性研究等。
- `docs/archive/root-notes/` — 早期根目录笔记（Bug 分析、重构、安装、维护指南）。
- `docs/archive/progress-notes/` — 早期进度笔记。

## 七、根目录其他文件

- `README.md`（仓库根）、`LICENSE`、`SECURITY.md`、`CONTRIBUTING.md`、`CONTRIBUTING_zh.md` 见仓库根。
- `.github/`：Issue/PR 模板 + `tests.yml` CI。
- `installer/`：安装/卸载/打包脚本。

## Release Trust

- [RELEASE_TRUST_PIPELINE.md](RELEASE_TRUST_PIPELINE.md) - review and release packages must come from committed git/CI artifacts, not manual workspace zips.
