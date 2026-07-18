# ZhuaQian Desktop 独立编译验证报告

更新时间：2026-07-11
验证方式：在沙箱环境安装 Mono C# 编译器（`mcs` 6.8.0.105），
按各 `build_*.ps1` / `scripts/run-tests.ps1` 中声明的源文件清单与引用列表，
逐一实际编译并运行，而非依赖仓库文档的既有结论。

本报告用于补充、核实 `CODE_COMPLETION_ALIGNMENT.md` 与 `CURRENT_REALITY_2026-07-11.md`
里"已验证通过"的具体条目，标注哪些是**独立复现确认**，哪些仍需**在真实 Windows + 官方
`csc.exe` 环境二次确认**。

---

## 1. 验证结论总表

| 测试 / 构建脚本 | 文档记录 | 本次独立验证结果 | 状态 |
|---|---|---|---|
| `work/zq-desktop/build_fallback_test.ps1`（`ProviderFallbackTest`） | 9 passed / 0 failed | **9 passed / 0 failed** | ✅ 完全复现 |
| `work/zq-desktop/build_failover_test.ps1`（`FailoverTest`） | 7 passed / 0 failed | **7 passed / 0 failed** | ✅ 完全复现 |
| `src/scripts/run-tests.ps1`（`TestRunner`） | 92 passed / 0 failed | **92 passed / 0 failed** | ✅ 完全复现 |
| `work/zq-desktop/scripts/run-tests.ps1`（`TestRunner`） | 139 passed / 0 failed | **138 passed / 1 failed** | ⚠️ 有出入，根因已定位（见 §2） |
| `work/zq-desktop/build_tests.ps1`（`SelfTest`） | 50 断言通过 | **50 passed / 0 failed** | ✅ 完全复现 |
| `work/zq-desktop/build_perm_test.ps1`（`PermissionEngineTest`） | 30 断言通过 | **30 passed / 0 failed** | ✅ 完全复现 |
| `work/zq-desktop/build.ps1`（主程序） | 通过 | **需补充 `System.Security.dll` 引用才能编译通过** | ⚠️ 需在真实 Windows 复核（见 §3） |
| smoke test 空 `catch` 计数 | 3 处 | **实测 2 处**（`ZhuaQianDesktop.cs:2236`、`:3815`） | ⚠️ 轻微出入，不影响功能判断 |

**总体判断**：6 项数字型测试结论里 5 项精确吻合，说明现有完成度文档的测试记录方法是可信的，
不是凭空写"已完成"。剩下的出入不是随机噪声，每一处都能定位到具体根因，详见下文。

---

## 2. `work/` TestRunner 出入根因：`Chunker.Split` 隐式依赖 `Environment.NewLine`

### 现象

`work/zq-desktop/tests/TestRunner.cs` 中 `TestChunkerEdge` 的这条断言在 Linux/mono 上失败：

```csharp
Assert(c.Split("a\nb\nc", 3).Count >= 3, "small max splits per line");
```

### 根因

`Knowledge/Chunker.cs` 的 `Split` 方法用 `StringBuilder.AppendLine(line)` 拼接每一行：

```csharp
current.AppendLine(line);
```

`AppendLine` 内部追加的换行符是 `Environment.NewLine`：

- Windows / .NET Framework：`"\r\n"`（2 字符）
- Linux / mono：`"\n"`（1 字符）

在 `maxChars=3` 这种极端小的边界值下，`current.Length` 会因为换行符长度不同而产生不同的
切分点。按 Windows 语义手工推算：三行各触发一次 `current.Length + line.Length > maxChars`，
应切成 3 段，`139 passed` 与文档记录一致；按 Linux 语义推算，前两行会被合并进同一段，
只切成 2 段，导致该断言失败，这与我在此沙箱里的实测结果一致。

**结论**：这不代表 Windows 上的 `139 passed / 0 failed` 记录是假的——按平台语义推算是自洽的。
但它暴露了一个真实、独立于此次测试结果对错的风险：**`Chunker.Split` 的切分行为隐式绑定了
运行平台的换行符定义，不是显式可控的。** 一旦仓库开源后引入基于 Linux 容器的 CI
（GitHub Actions 常见做法），这条测试会在无任何代码改动的情况下变红。

### 建议修复（低成本）

```csharp
// 之前
current.AppendLine(line);

// 建议改为，显式控制换行符，不再依赖 Environment.NewLine
current.Append(line).Append('\n');
```

同时检查 `Split` 内其余读取 `current.ToString()` 之后 `Trim()` 的地方是否依赖了具体的
换行符长度假设；从我看到的实现看，改成显式 `'\n'` 后不需要联动修改其他逻辑。

---

## 3. `build.ps1` 需要复核：`$refs` 缺少 `System.Security.dll`

### 现象

按 `work/zq-desktop/build.ps1` 里声明的 `$src` 全部源文件清单和 `$refs` 引用列表编译主程序，
在 mono 上报错：

```text
ZhuaQianDesktop.cs(1239,41): error CS0103: The name `ProtectedData' does not exist in the current context
ZhuaQianDesktop.cs(1255,30): error CS0103: The name `ProtectedData' does not exist in the current context
```

### 根因

`ZhuaQianDesktop.cs` 顶部有 `using System.Security.Cryptography;`，并在 `ProtectSecret` /
`UnprotectSecret`（DPAPI 密钥保护，对应 PRD 里的 P0 安全需求）中使用了
`System.Security.Cryptography.ProtectedData`。在 .NET Framework 中，这个类型定义在
`System.Security.dll` 里，不在 `mscorlib.dll` 或默认隐式引用集合中。

但 `build.ps1` 当前的引用列表是：

```powershell
$refs = @(
    "System.Windows.Forms.dll"
    "System.Drawing.dll"
    "System.Web.Extensions.dll"
    "System.IO.Compression.dll"
    "System.IO.Compression.FileSystem.dll"
)
```

没有 `System.Security.dll`。加上这一项后，本地 mono 编译可以完整通过
（14 个无害的未使用变量/字段警告，0 个错误）。

### 需要你们做的事

这一条我在 mono 上验证到的是"缺引用导致编译失败"，但**不能排除 mono 和真实
`csc.exe`（`C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe`）在隐式引用集合上
存在差异**，所以这一条我标记为"需要复核"而不是"确认为 bug"。

请在真实 Windows 环境重新跑一次 `work/zq-desktop/build.ps1`，并明确记录：

- 如果确实编译失败：说明 `README.md` / `CODE_COMPLETION_ALIGNMENT.md` 里
  "`work/zq-desktop/build.ps1`：已验证 / 通过" 这条记录需要撤回并修正，
  同时把 `System.Security.dll` 加入 `$refs`。
- 如果编译成功：说明真实 `csc.exe` 对该类型有额外的隐式解析路径，
  这条发现可以作废，但建议仍然显式加上 `System.Security.dll` 引用——
  显式引用不依赖编译器版本差异，是更稳妥的写法，成本几乎为零。

**这一项建议列为最高优先级复核项**，因为它涉及的是主程序本身能否编译，
优先级高于任何架构层面的改动。

---

## 4. 空 `catch` 计数出入（次要）

`CURRENT_REALITY_2026-07-11.md` 记录 smoke test 提示 3 个空 `catch` 块，
本次用正则 `catch[^{]*\{\s*\}` 扫描 `ZhuaQianDesktop.cs` 只匹配到 2 处：

- `ZhuaQianDesktop.cs:2236` — `catch { }`
- `ZhuaQianDesktop.cs:3815` — `try { SaveConfig(); } catch { }`

不影响整体判断，但如果 smoke test 脚本统计口径和本报告不同（例如把某处
`catch (Exception) { /* 注释 */ }` 也计入），建议核对一下 smoke-test.ps1
的具体扫描规则，确保这个数字后续被当成"待清理清单"使用时是准确的。

---

## 5. 总体结论与建议顺序

1. 现有测试记录的可信度总体是高的（6 项里 5 项精确复现），
   `Epic A：统一代码事实源` 中"测试链路收绿"这部分工作可以认为是真实完成的。
2. 在推进 `Epic 0`（Command / Gate / Executor 管道）之前，先做两件成本很低的事：
   - 在真实 Windows 上复核 `build.ps1` 是否真的编译通过（§3），需要的话补上
     `System.Security.dll` 引用；
   - 顺手把 `Chunker.Split` 的 `AppendLine` 改为显式 `'\n'`（§2），
     为将来可能的 Linux CI 铺路。
3. 这两项都不影响你们已经定好的架构方向（`ARCHITECTURE_CHARTER.md`），
   只是在"继续往前走"之前，把地基上两个可复核的裂缝先确认、补上。
