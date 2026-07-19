# ZhuaQian 每日健康度验证 - 执行历史

## 2026-07-17
- 整体判定：PASS（只读分析，未改动文件）
- check-architecture.ps1：exit 0 通过。
  - 关键：脚本用 `Measure-Object -Line`（忽略空行）计数=3624，恰好等于 maxMainLines=3624 → 位于预算天花板、零余量。
  - 原始行数：wc -l / Get-Content.Count / 换行数均为 3895（含 271 空行）。差异源于计数口径。
  - allowedToolNews=4 == 硬上限 4，已到顶。
- check-package.ps1：exit 0 通过。outputs/ 无自嵌套，无 zip 嵌套问题。
- git：已初始化，分支 main，工作树干净（任务假设的"未初始化"不成立）。
- 无 work/zq-desktop/ 目录 → 无法做树漂移对比（本工作区即规范树）。
- 编译测试：因沙箱限制跳过 run-tests.ps1（未强跑 csc.exe）。
- 人工介入建议：主文件已顶预算+白名单已顶 4，下次新增逻辑前需先抽取到 modules/executors。

## 2026-07-19
- 整体判定：PASS（两道门禁脚本均 exit 0；只读、未改动项目文件）。但工作树脏、主文件触顶，需人工跟进。
- check-architecture.ps1：exit 0 通过。
  - 主文件：Measure-Object -Line（非空行）= 3610，恰好 = maxMainLines=3610 → 零余量、触顶。原始行(wc -l/Get-Content.Count)=3874（含约 264 空行）。
  - 预算较上次(3624)被 ratchet 下调到 3610（lastUpdated 2026-07-18，含新增 BrowserRenderClient 白名单）。
  - allowedToolNews=5，maxAllowedToolNewExceptions=5 → 触顶但未超。注意：本次任务假设的"硬上限 4"已不再成立——预算把上限放宽为 5，与 5 条白名单一致，合规但需留意是否故意放宽。
  - Select-String 无未授权 `new Tools.`，通过。
- check-package.ps1：exit 0 通过。outputs/ 无自嵌套、无 zip 嵌套。
- git：已初始化（main 分支），但**工作树脏**——21 个未提交改动（含 2026-07-18 的 BrowserRender 等改动、_line_budget.json、ZhuaQianDesktop.cs、tests/* 等），本次为首次出现脏树（上次干净）。
- work/zq-desktop/ 仍不存在 → 无树漂移基线可比（本工作区即规范树）。
- 编译测试：因沙箱无 csc.exe，跳过 run-tests.ps1 重编译，明确标注「编译测试因沙箱限制跳过」，不计入失败。
- 人工介入建议：① 主文件已触顶 3610，下次新增逻辑前必须先抽取到 executors；② 21 个未提交改动应提交（或接 CI），否则验证基准与 HEAD 不一致。
