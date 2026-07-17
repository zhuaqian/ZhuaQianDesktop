# 执行进度：独立编译验证报告对应修复

执行时间：2026-07-11
依据：独立编译验证报告（沙箱 mono 环境）
工作基线：`work/zq-desktop/` + `src/`

---

## 验证报告摘要

外部验证者在 mono 沙箱中对所有构建/测试脚本逐一编译运行，
结论：6 项数字型测试结论里 5 项精确吻合，完成度文档可信度高。
报告指出了两个需修复的问题和一个待复核项。

---

## 修复项

### 修复 1：`work/zq-desktop/build.ps1` 补充 `System.Security.dll` 引用（§3）

**根因**：`ZhuaQianDesktop.cs` 使用 `System.Security.Cryptography.ProtectedData`
（DPAPI 密钥保护），该类型定义在 `System.Security.dll`。Windows `csc.exe` 有额外隐式解析路径，
在 Windows 上编译通过，但 Mono/Linux 上因缺少显式引用而编译失败。

**改动**：在 `work/zq-desktop/build.ps1` 的 `$refs` 中添加 `"System.Security.dll"`。

**状态**：已在真实 Windows `csc.exe` 验证编译通过。

**影响范围**：
- `src/build.ps1`：✅ 已有（此前已加）
- `work/zq-desktop/build.ps1`：✅ 已补
- `outputs/.../build.ps1`：✅ 已有（此前已加）

### 修复 2：`Chunker.Split` 跨平台换行符兼容（§2）

**根因**：`current.AppendLine(line)` 依赖 `Environment.NewLine`：
- Windows：`"\r\n"`（2 字符）
- Linux/Mono：`"\n"`（1 字符）

极端小 `maxChars` 下产生不同分块数，导致测试在 Linux 上失败。

**改动**：
- `work/zq-desktop/Knowledge/Chunker.cs:52`：`current.AppendLine(line)` → `current.Append(line).Append('\n')`
- `src/Knowledge/Chunker.cs:52`：同上
- `work/zq-desktop/tests/TestRunner.cs:304`：断言 `>= 3` → `>= 2`（对齐新行为）

---

## 验证结果

| 测试脚本 | 结果 |
|----------|------|
| `work/zq-desktop/build.ps1` | ✅ Build OK |
| `work/zq-desktop/scripts/run-tests.ps1` | ✅ 139 passed / 0 failed |
| `work/zq-desktop/build_tests.ps1` | ✅ 50 passed / 0 failed |
| `work/zq-desktop/build_perm_test.ps1` | ✅ 30 passed / 0 failed |
| `work/zq-desktop/build_fallback_test.ps1` | ✅ 9 passed / 0 failed |
| `work/zq-desktop/build_failover_test.ps1` | ✅ 7 passed / 0 failed |

---

## 待复核项（验证报告 §3）

报告指出 `work/zq-desktop/build.ps1` 缺 `System.Security.dll` 导致 mono 编译失败，
已在真实 Windows 确认：Windows `csc.exe` 编译通过。已补上显式引用以防未来 Linux CI 问题。
