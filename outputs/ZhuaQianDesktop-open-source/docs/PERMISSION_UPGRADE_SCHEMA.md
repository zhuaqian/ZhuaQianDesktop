# 权限模型升级方案：布尔 → allow/ask/deny + pattern matching

更新时间：2026-07-11
依据：`docs/ARCHITECTURE_CHARTER.md` §1、`docs/NEXT_STEP_PLAN_2026-07-11.md` P1

---

## 1. 现状

当前代码中存在**两套并行的权限模型**：

### 模型 A：`MainForm` 的布尔开关
文件：`work/zq-desktop/ZhuaQianDesktop.cs`
```csharp
bool permFileWrite = true;
bool permFileRead = true;
bool permNetworkUpload = false;
bool permFileMoveDelete = false;
bool permProcessManage = false;
bool permPluginRun = false;
bool permScreenshot = false;
bool permClipboard = false;
```
这些布尔字段被 `EnsurePermission` 方法检查，`SaveConfig/LoadConfig` 直接序列化为 JSON 键值对。

### 模型 B：`Core/PermissionGate.cs` 的三层模型
```csharp
enum PermissionLevel { Allow, Ask, Deny }
enum PermissionDecision { Allow, Ask, Deny }
class PermissionGate {
    PermissionDecision Check(string action, string target);
    void Set(string action, PermissionLevel level);
    // + glob patterns, session cache, auto mode, external directory scope
}
```
`PermissionGate` 已支持三态+pattern+remember+auto+scope，但**MainForm 没有真正消费它**。

---

## 2. 目标：单一事实源

所有权限检查只走 `PermissionGate.Check(action, target)`。MainForm 不再维护独立的布尔字段。

### 2.1 动作名映射表

| 旧布尔字段 | 新 action 名 | 默认值 | 说明 |
|---|---|---|---|
| `permFileRead` | `permFileRead` | Allow | 读本地文件 |
| `permFileWrite` | `permFileWrite` | Allow | 写/导出文件 |
| `permNetworkUpload` | `permNetworkUpload` | Ask | 云端上传 |
| `permFileMoveDelete` | `permFileMoveDelete` | Ask | 移动/删除文件 |
| `permProcessManage` | `permProcessManage` | Ask | 结束进程 |
| `permPluginRun` | `permPluginRun` | Ask | 运行插件 |
| `permScreenshot` | `permScreenshot` | Ask | 截图 |
| `permClipboard` | `permClipboard` | Ask | 剪贴板监控 |

### 2.2 `PermissionGate.Check` 的行为链

```
1. session deny → return Deny
2. session allow → return Allow
3. persistent remember-always patterns → return match level
4. base level for action → Allow / Ask / Deny
5. auto mode: Ask → Allow
6. external directory: allow + outside scope → Ask
```

### 2.3 配置序列化格式（向后兼容）

旧格式：
```json
{ "permFileWrite": true, "permNetworkUpload": false }
```

新格式（`PermissionGate.FromJson/ToJson` 已在 Core 中实现）：
```json
{
  "permissions": {
    "permFileWrite": "Allow",
    "permNetworkUpload": "Ask"
  },
  "patterns": [
    { "action": "permPluginRun", "glob": "trusted/*.ps1", "level": "Allow" }
  ],
  "autoMode": false,
  "allowedDirectories": []
}
```

迁移脚本同时读旧格式并按映射表转换。

---

## 3. 迁移代码

在 `ZhuaQianDesktop.cs` 的 `LoadConfig` 中，加载完 `config.json` 后调用此迁移：

```csharp
static void MigrateLegacyPermissions(Dictionary<string, object> config, PermissionGate gate)
{
    // 旧布尔字段列表：字段名 → action 名
    var legacyMap = new Dictionary<string, string>
    {
        { "permFileRead", "permFileRead" },
        { "permFileWrite", "permFileWrite" },
        { "permNetworkUpload", "permNetworkUpload" },
        { "permFileMoveDelete", "permFileMoveDelete" },
        { "permProcessManage", "permProcessManage" },
        { "permPluginRun", "permPluginRun" },
        { "permScreenshot", "permScreenshot" },
        { "permClipboard", "permClipboard" }
    };

    // 默认值（与新权限模型对齐）
    var legacyDefaults = new Dictionary<string, bool>
    {
        { "permFileRead", true },
        { "permFileWrite", true },
        { "permNetworkUpload", false },
        { "permFileMoveDelete", false },
        { "permProcessManage", false },
        { "permPluginRun", false },
        { "permScreenshot", false },
        { "permClipboard", false }
    };

    // 检查是否已有新格式的 permissions 节
    if (config.ContainsKey("permissions")) return; // 已迁移，跳过

    foreach (var kv in legacyMap)
    {
        string key = kv.Key;
        string action = kv.Value;
        bool defaultValue = legacyDefaults.ContainsKey(key) ? legacyDefaults[key] : false;

        bool value = defaultValue;
        if (config.ContainsKey(key))
        {
            try { value = Convert.ToBoolean(config[key]); }
            catch { value = defaultValue; }
        }

        gate.Set(action, value ? PermissionLevel.Allow : PermissionLevel.Ask);
    }
}
```

### 3.1 调用时机

在 `LoadConfig()` 中 `json.DeserializeObject` 之后、使用任何权限字段之前调用：

```csharp
var config = json.DeserializeObject(File.ReadAllText(configPath)) as Dictionary<string, object>;
if (config != null)
{
    MigrateLegacyPermissions(config, permGate);
    // 后续改用 permGate.Check(...) 而非直接读布尔字段
}
```

---

## 4. MainForm 中替换步骤

### 4.1 删除或废弃的字段
```
permFileWrite, permFileRead, permNetworkUpload, permFileMoveDelete,
permProcessManage, permPluginRun, permScreenshot, permClipboard
```

### 4.2 EnsurePermission 替换

当前：
```csharp
if (!EnsurePermission("Write/export files", permFileWrite, false, "Share")) return;
```

改为：
```csharp
if (permGate.Check("permFileWrite", targetPath) != PermissionDecision.Allow)
{
    if (permGate.Check("permFileWrite", targetPath) == PermissionDecision.Deny)
    { /* 拒绝 */ return; }
    // Ask → 走 ApprovalCard
    if (!ShowApprovalCard(...)) return;
}
```

或通过 `AgentPipeline.Run` 自动处理（推荐，见 `ARCHITECTURE_CHARTER.md`）。

### 4.3 SaveConfig/LoadConfig 适配

保存时：`config["permissions"] = permGate.ToJson()`
加载时：`permGate = PermissionGate.FromJson(config["permissions"])`

旧布尔字段在 MigrateLegacyPermissions 中被读取后不再写入。

---

## 5. 验证清单

- [ ] 含旧布尔字段的 `config.json` 被加载后，`PermissionGate.Check("permFileRead", "")` 返回 Allow
- [ ] `PermissionGate.Check("permNetworkUpload", "")` 对应旧 `permNetworkUpload = false` → Ask
- [ ] 迁移后新保存的 config.json 使用新格式 
- [ ] 再加载新格式 config.json 时，`MigrateLegacyPermissions` 因存在 `permissions` 键而跳过
