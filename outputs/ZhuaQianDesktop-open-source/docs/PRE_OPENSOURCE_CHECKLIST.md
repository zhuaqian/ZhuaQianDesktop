# 开源前收尾清单

更新时间：2026-07-11

---

## 1. LICENSE 选型建议

### 推荐：MIT License

理由：
- 与竞品 OpenCode 一致（MIT）
- 允许商业使用、修改、再分发
- 低摩擦吸引贡献者
- 无 Copyleft 顾虑，企业可以采用
- 当前 `outputs/ZhuaQianDesktop-open-source/LICENSE` 已是 MIT

### 不推荐的理由

| 许可证 | 不推荐原因 |
|--------|-----------|
| GPL v3 | 可能阻止企业采用，与"开源生态"目标冲突 |
| Apache 2.0 | 专利声明对单体 WinForms 项目意义不大 |
| AGPL v3 | 网络分发条款与本项目桌面端定位不匹配 |
| BSL / 商业 | 与"完全开源"定位矛盾 |

### 建议的 LICENSE 文件内容

保持 `outputs/ZhuaQianDesktop-open-source/LICENSE` 不变（标准 MIT 模板）。

---

## 2. CONTRIBUTING.md

文件位于仓库根目录 `CONTRIBUTING.md`，当前包含通用模板。建议精简为以下内容：

### 必须包含

1. **构建方式**：`build.ps1` + csc.exe / 或 `dotnet build`（如已迁移）
2. **测试方式**：`scripts/run-tests.ps1`
3. **开发基线**：`src/` 是当前公开贡献主线；`work/zq-desktop/` 只是过渡镜像
4. **PR 前必读**：`docs/ARCHITECTURE_CHARTER.md`
5. **PR 检查清单**（直接从 ARCHITECTURE_CHARTER.md §7 复制）
6. **提交规范**：简洁，一行标题 + 可选详情

### 不应包含

- 详细的 IDE 配置指南（各人偏好不同）
- 历史分析/评估文档链接（应指向 `docs/` 入口）
- 非核心功能的长篇 roadmap

---

## 3. docs/ 精简方案

### 保留在根 docs/（核心文档）

| 文件 | 保留理由 |
|------|---------|
| `ARCHITECTURE_CHARTER.md` | 架构约束，贡献者必读 |
| `PRODUCT_REQUIREMENTS.md` | 产品定位与范围 |
| `PRODUCT_ARCHITECTURE.md` | 架构详细说明 |
| `EXECUTION_BACKLOG.md` | 执行待办 |
| `CODE_COMPLETION_ALIGNMENT.md` | 完成度事实基准 |
| `CURRENT_REALITY_2026-07-11.md` | 当前状态快照 |
| `SLN_MIGRATION_PLAN.md` | 迁移方案（新增） |
| `PERMISSION_UPGRADE_SCHEMA.md` | 权限升级方案（新增） |
| `PRE_OPENSOURCE_CHECKLIST.md` | 本文件 |

### 移入 docs/archive/

| 文件 | 理由 |
|------|------|
| `DEEP_EVALUATION.md` | 一次性评估，历史参考 |
| `IMPLEMENTATION_UPDATE_2026-07-11.md` | 过程记录 |
| `NEXT_STEP_EXECUTION_PLAN.md` | 被 NEXT_STEP_PLAN_2026-07-11.md 取代 |
| `NEXT_SUPER_AGENT_BRIEF.md` | 一次性简报 |
| `OPENCODE_ACTION_PLAN.md` | 历史计划 |
| `UI_COMPETITOR_RESEARCH.md` | 竞品研究，参考价值但不持续 |
| `WINDOWS_AGENT_MONITORING_FEASIBILITY.md` | 可行性研究 |
| `ZHUQIAN_VISION.md` | 愿景文档，保留参考 |
| `COMPETITIVE_GAP_ANALYSIS.md` | 竞品差距分析 |
| `COLLAB_SHARE_DESIGN.md` | 功能设计文档 |
| `COLLAB_SHARE_PROGRESS.md` | 过程记录 |

### 删除（已过时/重复）

| 文件 | 理由 |
|------|------|
| `NEXT_STEP_PLAN_2026-07-11.md` | 已被 EXECUTION_BACKLOG.md 承接 |

### 进度文档（保留不移）

`PROGRESS_*` 文件记录每次改动的验证结果，保持可追溯。可移入 `docs/progress-notes/`。

---

## 4. 执行步骤

1. 决定 LICENSE（推荐 MIT，已就绪）
2. 精简 CONTRIBUTING.md（见上方内容）
3. 执行 docs/ 精简：创建 `archive/` → 移入历史文件 → 删除过时文件
4. 更新 README.md 的 docs 引用路径
5. 确认 `docs/README.md` 指向正确的入口文档

---

## 5. 开源前必须修复的项（从 EXECUTION_BACKLOG.md）

- [x] `work/zq-desktop/build.ps1` 补充 `System.Security.dll`（已修）
- [x] `Chunker.Split` 平台耦合（已修）
- [ ] `build_fallback_test.ps1` 回归绿（当前 9 passed）
- [ ] `build_failover_test.ps1` 回归绿（当前 7 passed）
- [ ] 三棵源码树收敛决策（当前以 `src/` 为公开贡献主线，`work/zq-desktop` 仍需同步到退休为止）
