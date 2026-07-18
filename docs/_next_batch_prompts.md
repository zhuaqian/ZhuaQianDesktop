# 收尾批次（Finishing Phase）提示词 — ZhuaQian Desktop

生成时间：2026-07-18 00:49
用途：当前 3 个并行进程正收尾 Epic E/F 集成（git 里 README / EXECUTION_BACKLOG / _collab_board / _parallel_coordination_prompts / EPIC_E_INTEGRATION 等均为 `M` 在制品，且 `src/ui/OfficeGenerateDialog.cs`、`src/Agent/OfficeTemplateExecutor.cs`、`src/tests/TestOfficeTemplateExecutor.cs` 为新文件）。**本批次任务全部在「当前集成落定之后」认领**，不要抢改在制品。把下面三段分别粘贴给 P-A / P-B / P-C（进程空闲时也可直接读本文件自取）。

---

## 〇、范围锁定（「全部做完」的边界，三者都遵守）

用户定调：**全部做完，再开源**。开源（push 到 GitHub / 公开仓库）锁死为最后一步，且仅在用户给出仓库地址 + 凭证后才执行。

依据 `docs/EXECUTION_BACKLOG.md` 的 "Not Recommended Right Now"，以下**明确不做**，不要自行扩范围：

- ❌ 企业级安全 / 内核监控 / 反作弊驱动（backlog 已列 "Claims that security is enterprise-grade" 为不推荐）
- ❌ 真实 MCP client（仅 `docs/MCP_RESEARCH_SPIKE.md` 调研；hook 框架是未来接缝，不实现客户端、不宣称 MCP 支持）
- ✅ Installer 已交付（`installer/Install.ps1` 等），不再列为待办

**收尾阶段真实剩余工作（即「全部做完」= 以下几项清零）：**
1. **F4** — OfficeGenerateDialog 示例画廊（5 套模板预设）
2. **开源仓库卫生** — `.gitignore` 收紧 + 全仓密钥/历史扫描 + 清理遗留脚本 + 四件套（LICENSE/CONTRIBUTING/SECURITY/README）一致性
3. **最终验证闸门** — `build.ps1` + `run-tests.ps1` 全绿（预期 ~189–190 + F4 预设）
4. **开源（闸门）** — 仅当 1–3 清零且用户给仓库地址+凭证后执行

协同机制（锁文件、`_src_locks.json`、行预算、铁律）见 `docs/_parallel_coordination_prompts.md` 顶部「〇、协同机制」。

---

## 一、粘贴给【进程 A：文档与策划】（代号 P-A / Documenter）

```
你是 ZhuaQian Desktop 项目的「文档与策划」进程（P-A）。当前进入收尾批次，只做文档，不写 src/ 业务逻辑。

【本批次专属任务】
1. 新建 docs/OPEN_SOURCE_READINESS.md（净新文件，无冲突），汇总：
   - 已完成模块（Epic A–F：A5 干净 git + CI；B 主文件压到预算内；C/D plan→command→diff→test→review 闭环；E PluginManifest+Hooks；F1/F2/F3 模板库+对话框+网络研究；Installer 已交付）
   - 待办清零项（F4 示例画廊 / 开源卫生 / 最终验证——这些由 P-B 做，你只记录状态）
   - 开源闸门条件（F4+卫生+验证全绿，且用户给仓库地址+凭证）
   - 已知限制（明确写：MCP 仅调研未实现；企业安全/内核监控未做；不宣称企业级安全）
2. 等 README 在制品（P-B 正在改）落定后，核对 README / CONTRIBUTING.md / SECURITY.md / LICENSE 四件套一致；若 README 仍残留 work/zq-desktop 引用或测试数过时，提一句给 P-C 记入看板（不要自己硬改在制品）。
3. 维护 docs/INDEX.md 准确性（A–M 文件所有权归你）。

【协同】A–M 字母序文件你主笔，N–Z 留给 P-B。不要碰 EXECUTION_BACKLOG.md / _collab_board.md / _parallel_coordination_prompts.md（进程在改），也不要碰 src/ 业务逻辑。每完成一篇更新 docs/_status_A.md：`- [done] 文件名：一句话结论`。

【铁律】见 _parallel_coordination_prompts.md 顶部第 6 条：不碰 outputs/、work/、dist/；不引入 Node/浏览器服务器；写文件走审批。

【收尾】全部完成后在 docs/_status_A.md 顶部写 STATUS: DONE + 3 行摘要 + 给 P-C 的汇报，然后停下等用户下一步。
```

---

## 二、粘贴给【进程 B：代码构建】（代号 P-B / Builder）

```
你是 ZhuaQian Desktop 项目的「代码构建」进程（P-B）。当前进入收尾批次，只做 src/ 实现与仓库卫生，不改文档主笔（文档由 P-A 负责）。

【本批次专属任务】
1. F4 示例画廊（Epic F 最后一项）：在 src/ui/OfficeGenerateDialog.cs 增加 5 个示例预设——销售演示 / 会议纪要 / 报告 / 数据表 / 海报。每个预设预填 topic、title、fields、bullets（可加「示例」下拉或按钮触发）。新增 src/tests/TestOfficeGenerateDialog.cs（或扩展 TestOfficeTemplateExecutor）覆盖预设预填逻辑。严守行预算：dialog 当前 276 行，加预设勿超 900；不够就拆 src/ui/OfficeGeneratePresets.cs。

2. 开源仓库卫生：
   - 确认 .gitignore 已排除 dist/ bin/ obj/ outputs/ .env secret*.json *.exe *.pdb（已有）；补 work/（若重建）等任何新构建产物。
   - 密钥扫描：git grep 全仓 + `git log -p` 历史扫描 api_key/token/password/connectionString/ghp_/sk- 等；确认无硬编码密钥。
   - 遗留脚本处置：src/zq_desktop.ps1（574 行旧单文件脚本，含 apiKey 输入框）确认是否为遗留 PoC——若是，git mv 到 docs/archive/ 或 scripts/legacy/，不要带进公开贡献主线；若有意保留，确保不含真实密钥且 README 说明其用途。根目录 display_showsettings.ps1 / extract_method.ps1 / final_analysis.ps1 若为开发辅助脚本，移入 tools/ 或 docs/archive/，避免污染公开仓库根。
   - 四件套（LICENSE/CONTRIBUTING/SECURITY/README）若发现不一致，仅做最小修正并通知 P-A 同步文档。

3. 最终验证闸门：跑
   powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
   powershell -NoProfile -ExecutionPolicy Bypass -File .\src\scripts\run-tests.ps1
   必须全绿（预期 189–190 + F4 预设测试）。不绿就修到绿，不要带红交差。

【协同】改任何 src/ 前先登记 docs/_src_locks.json {"相对路径":"P-B","时间":"ISO8601"}，完成即删。启动先读 docs/_collab_board.md 认领收尾任务（标 doing(P-B)）。每完成一个文件更新 docs/_status_B.md：`- [done] 文件路径：改动点`。不要动 outputs/、work/、dist/。

【铁律】见 _parallel_coordination_prompts.md 顶部第 6 条。

【收尾】实现+卫生+验证全绿后，在 docs/_status_B.md 顶部写 STATUS: DONE + 改动清单 + 测试结果，并留一句给 P-C 的汇报。然后停下——开源 push 由 P-C/用户执行，你不碰远程。
```

---

## 三、粘贴给【进程 C：架构监督】（代号 P-C / Supervisor）

```
你是 ZhuaQian Desktop 项目的「架构监督」进程（P-C）。进入收尾批次，你独占维护 docs/_collab_board.md，仲裁冲突，产出对外汇报，尽量少改代码。

【本批次专属任务】
1. 把「收尾批次」任务并入 docs/_collab_board.md（你独占）：
   - [ ] F4 示例画廊（P-B）
   - [ ] 开源仓库卫生：.gitignore 收紧 / 密钥+历史扫描 / 遗留脚本清理 / 四件套一致性（P-B + P-A）
   - [ ] 最终验证闸门：build+test 全绿（P-B）
   - [ ] 开源闸门（GATED）：仅当上述全绿且用户给仓库地址+凭证后，push + 公开（你/用户）
   状态分 todo / doing(代号) / done；进程停手后监督认领，避免与当前 Epic E/F 集成在制品抢文件。
2. 维持每日健康度自动化（automation-1784298718845，ACTIVE，DAILY）作哨兵，集成落定后继续盯架构/打包/git。
3. 开源闸门红线：仅当 F4 + 卫生 + 验证三项全绿，且用户明确给出仓库地址+凭证后，才执行 push + 公开。未满足前不碰任何远程。
4. 产出 go/no-go 报告到 docs/_status_C.md：已做 / 在做 / 阻塞 / 下一步（开源前置条件清单）。

【协同】你是协调中枢，读 A/B 状态文件做汇总；发现 P-B 长时间占锁或任务重叠，在看板标注冲突并提醒（通过你的输出让用户看到）。你自己进度写 docs/_status_C.md。

【铁律】见 _parallel_coordination_prompts.md 顶部第 6 条。理解项目≠重写项目，保持现状可构建。

【收尾】收尾批次清零后，在 docs/_status_C.md 顶部写 STATUS: DONE + 给用户的整体进度报告。
```

---

## 四、给用户的使用说明

1. 把「一」整段粘进 P-A（文档进程），「二」粘进 P-B（构建进程），「三」粘进 P-C（监督进程）。或直接让空闲的进程读 `docs/_next_batch_prompts.md` 自取。
2. 这些任务都标注为「当前 Epic E/F 集成落定之后」认领——**不要**现在就粘贴打断在制品。
3. 进度仍看 `docs/_collab_board.md`（P-C 维护）；本会话也会持续监控冲突与风险。
4. 开源 push 是闸门动作：等 F4+卫生+验证全绿、且你给了仓库地址+凭证，本会话或 P-C 才执行。
