# 执行进度：ApprovalCard 接线（替换 MessageBox 确认）

执行时间：2026-07-11
依据计划：`docs/NEXT_STEP_PLAN_2026-07-11.md` P0.5 项
工作基线：`work/zq-desktop/`

---

## 任务描述

将 `MainForm` 中仍使用 `MessageBox.Show` + `YesNo` 的高风险确认替换为 `Tools.ApprovalCard`（`ShowApprovalCard` 包装方法），使审批行为统一记录到 `actions.jsonl`。

## 改动内容

在 `work/zq-desktop/ZhuaQianDesktop.cs` 中替换了 3 处：

### 1. 回滚确认（原 line 2530）

```
Before: MessageBox.Show(this, preview + "\r\nContinue rollback?", "Confirm Rollback", YesNo)
After:  ShowApprovalCard("Rollback", "Confirm Rollback", "Execute",
          "Write/export files", preview,
          "Files will be moved back to their original locations.",
          "", "", manifestPath)
```

### 2. 结束进程确认（原 line 3020）

```
Before: MessageBox.Show(this, "End process " + proc.ProcessName + "...", "Confirm end task", YesNo)
After:  ShowApprovalCard("EndProcess", "Confirm end task", "Execute",
          "End processes",
          proc.ProcessName + " (PID " + pid + ")",
          "Unsaved data may be lost.",
          "", "", "")
```

### 3. 删除产物记录确认（原 line 2366）

```
Before: MessageBox.Show(this, Tr("Delete this output record?...") + path, "Delete record", YesNo)
After:  ShowApprovalCard("DeleteOutputRecord", "Delete record", "Execute",
          "Write/export files", path,
          "This will remove the record from the outputs panel.",
          "", "", path)
```

## 验证结果

| 项目 | 状态 |
|------|------|
| `work/zq-desktop/build.ps1` | ✅ Build OK |
| `work/zq-desktop/scripts/run-tests.ps1` | ✅ 139 passed / 0 failed |
| `work/zq-desktop/build_tests.ps1` | ✅ 50 断言通过 |
| `work/zq-desktop/build_perm_test.ps1` | ✅ 30 断言通过 |

## 前置状态

在本轮改动前，`ShowApprovalCard` 已用于：
- 云端上传确认（`ConfirmCloudUploadIfNeeded` → `ShowApprovalCard("CloudUploadConfirm", ...)`）
- 插件运行确认（`ConfirmRunPlugin` → `ShowApprovalCard("RunPlugin", ...)`）

`ShowApprovalCard` 实现（`work/zq-desktop/ZhuaQianDesktop.cs:3673`）：
```csharp
bool ShowApprovalCard(string actionType, string title, string modeName,
    string permission, string affected, string risk, string output,
    string detail, string outputPath)
{
    string editNote;
    var decision = Tools.ApprovalCard.Show(this, title, modeName,
        new List<string> { permission },
        new List<string> { affected },
        risk, output, detail, out editNote);
    bool approved = decision == Tools.ApprovalDecision.Approved
                 || decision == Tools.ApprovalDecision.Edited;
    RecordAction(actionType, approved ? "approved" : "cancelled",
                 title + "\n" + (editNote ?? ""), outputPath);
    return approved;
}
```

## 后续可继续项

剩余的 ~27 处 `MessageBox` 确认（含 Draft type 选择、Search mode 选择、Power 开关等）可按同一模式替换。优先级建议：
1. Power 开关确认（line 661）— 涉及权限总闸
2. Draft type 选择（line 2944）— 涉及文件生成
3. Search mode 选择（line 3368）— 涉及知识库检索方式
