# 执行进度：代码诊断优化

更新时间：2026-07-14

## 本轮目标

继续查看代码并开始做低风险优化，优先处理开源发布前最容易影响排障体验的问题。

## 已完成

### 1. 清理 `src/` 生产路径裸 `catch`

不改变业务行为，只把原本静默吞掉的异常写入 Debug 或本地日志。

涉及文件：

- `src/Core/PackageBuilder.cs`
- `src/Core/LanShareServer.cs`
- `src/Core/ConfigStore.cs`
- `src/ZhuaQianDesktop.cs`

改进点：

- `.zqp` zip 探测失败会写 Debug。
- LAN share accept loop 异常会写 Debug。
- 配置类型转换失败会写 Debug。
- DPAPI protect/unprotect 失败会写本地 warning。
- 剪贴板读取失败会写本地 warning。
- streaming fallback 失败会写本地 warning。

### 2. 清理 `work/zq-desktop` smoke test 点名的空 `catch`

涉及文件：

- `work/zq-desktop/ZhuaQianDesktop.cs`
- `work/zq-desktop/Tools/FolderOrganizer.cs`
- `work/zq-desktop/Agent/ExportFileExecutor.cs`
- `work/zq-desktop/Agent/OrganizeFolderExecutor.cs`

结果：

- `work/zq-desktop/scripts/smoke-test.ps1` 不再报告 MainForm/Tools 空 `catch` 警告。
- 运行基线的输出大小读取、快照 hash fallback、模型切换保存配置、整理回滚都保留原行为，但异常可诊断。

## 验证结果

| 命令 | 结果 |
|---|---|
| 根目录 `build.ps1 -Output dist/ZhuaQianDesktop.optimized2.exe` | 通过 |
| `src/scripts/run-tests.ps1` | `92` passed / `0` failed |
| `work/zq-desktop/build.ps1 -Output ZhuaQianDesktop.optimized2.exe` | 通过 |
| `work/zq-desktop/scripts/run-tests.ps1` | `157` passed / `0` failed |
| `work/zq-desktop/scripts/smoke-test.ps1` | 通过，空 `catch` 警告清零 |

## 仍需优化

- `work/zq-desktop` 的旧基线模块里仍有若干容错型空 `catch`，主要集中在 `Core/`、`Knowledge/`、`Tools/ProcessSnapshotCollector.cs` 和测试清理代码。
- 下一轮建议只处理生产路径，测试清理代码可以保留或集中封装成 `TryDelete` helper。
- 主窗体仍然过大，下一步应继续把 `src/ZhuaQianDesktop.cs` 的可测试逻辑迁出。
