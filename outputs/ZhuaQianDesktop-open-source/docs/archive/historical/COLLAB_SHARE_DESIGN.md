# 异地协同 · 项目功能分享 · 密码分享 — 设计与实施方案

> 文档目标：在理解 ZhuaQian Desktop 现有架构的基础上，给出「异地协同工作」「分享项目功能」「密码保护分享」三套能力的可行方案，并明确落地点、数据格式与实施阶段。

---

## 0. 项目理解（一句话 + 能力边界）

**ZhuaQian Desktop = 本地优先 + 完全开源 + 插件生态 的 Windows 桌面 AI 工作台。**

当前已具备的核心能力（来自源码 `src/`）：

| 能力 | 实现位置 | 说明 |
|------|----------|------|
| 多模型接入 | `providers/ProviderManager.cs`、`GeminiClient.cs`、`OpenRouterClient.cs`、`LocalClient.cs` | Gemini / OpenRouter / 本地 Ollama / OpenAI 兼容 |
| 任务状态机 | `ZhuaQianDesktop.cs` `TaskInfo` / `NormalizeTaskStatus` | Draft→NeedsInput→Running→Ready→Done |
| 本地知识库 RAG | `Knowledge/Chunker.cs`、`Documents/*`、`knowledge-index.json` | 离线分块、检索、脱敏 |
| 真实文档导出 | `Documents/OfficeExporter.cs` | 无依赖生成 docx / pptx / xlsx |
| 插件 / 命令 | `Tools/PluginRunner.cs`、`CommandParser.cs` | Python / PS1 / EXE manifest + 权限 |
| 权限与审计 | `Core/PermissionGate.cs`、`Core/AuditLog.cs` | 三层权限 + actions.jsonl |
| 密钥保护 | `Core/ConfigStore.cs` `Protect/Unprotect` | **DPAPI（仅本机、仅当前用户）** |
| 产物中心 | Outputs 面板 | 可追溯、可回滚 |

**关键约束**：`ConfigStore` 用的是 **DPAPI（`DataProtectionScope.CurrentUser`）**，密钥只能在本机当前用户解密。这意味着**跨机器分享不能直接复用 DPAPI**，必须引入基于密码的对称加密（见 §3）。

---

## 1. 总体方案概览

三套能力共用一套底层：**「可移植项目包（`.zqp`）」** + **「传输通道」** + **「加密层」**。

```
┌──────────────────────────────────────────────────────────────┐
│                      分享 / 协同 数据流                          │
├──────────────────────────────────────────────────────────────┤
│  本地项目 (任务 + 附件 + 知识 + 技能)                            │
│        │  ① 打包 PackageBuilder                                  │
│        ▼                                                        │
│  *.zqp 包 (zip 容器: manifest.json + task/*.json + files/ +     │
│           skills/ + knowledge/)                                 │
│        │  ② 加密层 (可选密码)                                    │
│        ▼                                                        │
│  加密载荷 (AES-256-GCM, PBKDF2 派生密钥)                         │
│        │  ③ 传输通道                                             │
│        ├──────────────┬───────────────────────┬───────────────┐
│        │  LAN 直连     │  自托管 Relay 中继      │  云盘/邮件 传文件│
│        │ (HttpListener │  (signaling + blob)    │  (仅传 .zqp)   │
│        │  + QR 配对)   │                        │               │
│        └──────────────┴───────────────────────┴───────────────┘
│        │                                                        │
│        ▼                                                        │
│  对方 ZhuaQian: 解密 → 校验 → 导入 (新任务 / 并入现有项目)        │
└──────────────────────────────────────────────────────────────┘
```

- **分享项目功能** = ① 打包 + ③ 任意通道（不含实时协同，单向）。
- **密码分享** = ② 加密层（可选，分享时勾选）。
- **异地协同** = ③ 的中继通道 + 可选的实时会话（双向、增量同步）。

---

## 2. 分享项目功能（Project / Capability Sharing）

### 2.1 可移植包格式 `.zqp`

复用现有 `AddZipText`（`ZhuaQianDesktop.cs:890`）已有的 zip 能力，定义统一容器：

```
xxx.zqp (zip)
├── manifest.json        # 包元数据 + 完整性哈希
├── project/
│   ├── task.json        # 当前任务 (复用 TaskFile 结构)
│   ├── tasks/           # 其他关联任务
│   └── settings.json    # 非敏感设置 (模式/语言/权限级别, 不含 API Key)
├── files/               # 任务内上传的附件 (原样拷贝)
├── knowledge/           # 知识库索引片段 + chunk 文本 (可选)
└── skills/              # 本任务用到的 SKILL.md / 插件 manifest (能力分享)
```

`manifest.json` 结构：

```json
{
  "schema": "zqp/1",
  "title": "2026-Q3 周报生成器",
  "createdBy": "<displayName>",
  "createdAt": "2026-07-11T10:00:00Z",
  "kind": "project | capability | session",
  "encrypted": false,
  "sha256": "<整包内容哈希，用于校验损坏/篡改>",
  "includes": ["task", "files", "knowledge", "skills"]
}
```

### 2.2 「分享功能」的两种语义

| 语义 | 含义 | 实现 |
|------|------|------|
| **分享一个项目** | 把整套任务+附件+知识打包发给同事 | `PackageBuilder.Build(projectDir) → .zqp` |
| **分享一项能力** | 把某个技能/插件/模板单独发出，对方装上即可用 | `skills/` 目录 + `SKILL.md`，复用 `PluginRunner` 的发现机制 |

> 与现有架构衔接：技能分享直接复用 `PluginRunner.cs` 的 manifest + 权限声明；模板复用 `OfficeExporter` 的模板引擎（愿景 §差异化2）。

### 2.3 导入与「并入」

- 导入为**新任务**（默认）：解码后落盘到 `tasksDir`，出现在左侧任务列表。
- 导入为**并入现有项目**：把 `files/` 与 `knowledge/` 合并进当前任务目录。
- 导入时**永远不携带 API Key**（settings.json 排除密钥，沿用 `ConfigStore` 的脱敏思路）。

---

## 3. 密码保护分享（Encrypted Sharing）— 核心加密设计

> 关键决策：**不使用 DPAPI**（它无法跨机器）。改用 **密码派生密钥 + AES-GCM** 的标准做法，保证分享出去的 `.zqp` 在任意机器上都能用密码解开。

### 3.1 算法

```
KDF:   PBKDF2-SHA256
       password + 16-byte random salt → 32-byte key (迭代 ≥ 100_000)
Cipher:AES-256-GCM
       12-byte random nonce
       输出 = salt(16) + nonce(12) + ciphertext + tag(16)
```

AES-GCM 同时提供**机密性 + 完整性**：密码错误会直接验证失败，不会泄露明文。

### 3.2 落地点：`Core/ShareCrypto.cs`（新增）

```csharp
namespace ZhuaQianDesktopApp.Core
{
    public static class ShareCrypto
    {
        // 加密整个 .zqp 字节流，返回带 salt/nonce/tag 的封装
        public static byte[] Encrypt(byte[] payload, string password)
        {
            var salt = RandomBytes(16);
            var key  = DeriveKey(password, salt);          // PBKDF2
            using (var aes = new AesGcm(key))
            {
                var nonce = RandomBytes(12);
                var ct = new byte[payload.Length];
                var tag = new byte[16];
                aes.Encrypt(nonce, payload, ct, tag);
                return Concat(salt, nonce, ct, tag);
            }
        }

        public static byte[] Decrypt(byte[] blob, string password)
        {
            // 拆 salt/nonce/ct/tag → 派生 key → 校验 tag → 返回明文
            // 密码错误 → CryptographicException，UI 提示「密码错误」
        }

        static byte[] DeriveKey(string pw, byte[] salt) =>
            new Rfc2898DeriveBytes(pw, salt, 100000, HashAlgorithmName.SHA256)
                .GetBytes(32);
    }
}
```

> `AesGcm` 在 .NET Framework 4.8 需 `System.Security.Cryptography` 直接可用（4.6.2+ 已内置 `AesGcm`）。如目标机缺失，回退到 `AesCryptoServiceProvider` (CBC + HMAC-SHA256) 做 encrypt-then-MAC。

### 3.3 UI 交互（在现有导出/分享流程上挂接）

- 分享对话框新增 **「设置密码（可选）」** 勾选框 + 密码输入。
- 留空 = 明文 `.zqp`（方便内网快速传）；填写 = 加密 `.zqp`。
- `manifest.json.encrypted = true` 标记，导入时自动弹密码框。
- 密码**绝不落盘**、**绝不写审计明文**（审计只记「已加密分享 / 已解密导入」）。

---

## 4. 异地协同工作（Remote Collaboration）

分两层，按需落地，避免一上来就做复杂实时协同。

### 4.1 通道 A：局域网直连（零基础设施，优先做）

- 新增 `Core/LanShareServer.cs`：用 `HttpListener` 起一个本地 HTTP 服务（如 `http://127.0.0.1:8801` 或 `http://+:8801`）。
- 分享方生成 `.zqp` 后选择「LAN 分享」→ 服务把包挂到 `/pkg/{token}`。
- 对方在同一局域网：扫码（复用愿景 §Phase3「LAN 手机扫码上传」）或输入 IP → 下载导入。
- **二维码**：用轻量库或 `QRCoder` 风格实现（无外部依赖可用 `System.Drawing` 自绘，或内置一个最小 QR 生成器）。

### 4.2 通道 B：自托管中继（跨互联网协同，第二阶段）

可选、用户自己部署，不绑定任何商业云服务，符合「隐私不妥协」定位：

```
Relay (最小实现, 任意语言/可 PS1)
├── POST /upload   → 存 blob，返回 shareId + 一次性 pull token
├── GET  /pull/{id} → 校验 token 返回 blob
└── WS   /session  → 实时协同信令 (可选)
```

- 客户端 `providers/` 下新增 `ShareClient.cs` 负责上传/拉取，复用 `ProviderManager` 的网络封装风格。
- 链接形态：`https://your-relay.example.com/s/{shareId}#<可选客户端盐>`（密码仍存在用户脑中，服务器只看到密文）。
- **服务器零知识**：因为 §3 已加密，中继只转发密文，无法读取内容。

### 4.3 实时协同（第三阶段，锦上添花）

- 在「会话 (.zqp kind=session)」基础上，用 Relay 的 WebSocket 做**增量消息同步**。
- 复用现有 `messages` (`ArrayList`) 结构，增量 = `{taskId, patch}`；冲突以「最后写入 + 用户确认」解决（沿用 `ApprovalCard` 审批模式）。
- 协同成员以显示名标识，写入 `AuditLog`，满足「谁改了什么」可追溯。

---

## 5. 与现有架构的集成清单

| 新模块 | 复用现有 | 说明 |
|--------|----------|------|
| `Core/ShareCrypto.cs` | `ConfigStore.Protect` 思路（但改为跨机密码派生） | 加密层 |
| `Core/PackageBuilder.cs` | `ZhuaQianDesktop.cs:AddZipText` / `TaskFile` / `SaveCurrentTask` | 打包 |
| `Core/LanShareServer.cs` | `ServicePointManager.Tls12` 已在 `MainForm` 设置 | LAN 服务 |
| `providers/ShareClient.cs` | `ProviderManager` 网络风格 | Relay 上传/拉取 |
| 分享对话框 | `SettingsDialog.cs`、`PromptExportFormat` (`ZhuaQianDesktop.cs:794`) | UI 挂接 |
| 导入流程 | `LoadTask` / `LoadTasks` | 解包后落盘 |
| 审计 | `AuditLog.LogAction` | 记「分享/导入/解密」事件，不含明文密码 |

---

## 6. 实施路线图

### Phase 1 — 打包 + 密码分享（最小可用，1~2 周） ✅ 已完成
- [x] `Core/ShareCrypto.cs`（AES-256-CBC + HMAC-SHA256 + PBKDF2；因 .NET Framework 4.8 无 `AesGcm`，采用 encrypt-then-MAC 方案）
- [x] `Core/PackageBuilder.cs`（导出/导入 `.zqp`：manifest.json + project/ + knowledge/，含 `sha256` 完整性校验）
- [x] 分享对话框 `PromptShareOptions`（可选密码框 + 二次确认），复用于命令面板与顶部「Share」按钮
- [x] 导入流程：检测加密→弹密码框→校验 `sha256`→落盘为新任务（换新 id，标题加 `(shared)`）
- [ ] 技能/插件单独分享（`skills/` 目录）— 留待后续细化

> 实现落点：`work/zq-desktop/`（仓库内可编译通过的运行版本）。入口：
> - 顶部「Share」按钮 / 命令面板 `Share project (package)` → 导出 `.zqp`
> - 命令面板 `Import package` → 导入 `.zqp`
> - 加密包在导出时勾选「用密码保护此分享包」，导入时输入密码解密（密码不落盘、不写明文审计）。
>
> 注：根目录 `src/` 为重构中的快照，目前存在既有 `EnsurePermission` 签名不一致问题、无法独立编译；功能代码已同步写入 `src/Core/`，运行时以 `work/zq-desktop/` 为准。

### Phase 2 — 局域网协同（零基础设施，2~3 周）
- [x] `LanShareServer.cs` (`HttpListener` 极简 HTTP + 8 位 token) ✅（见 `work/zq-desktop/Core/LanShareServer.cs`）
- [x] 命令面板 `Share over LAN` / `Import from URL` ✅
- [ ] 二维码生成 + 扫码导入（设计愿景中的 LAN 手机扫码，待做）
- [ ] 设置里「协同 / 分享」开关，复用 `PermissionGate` 网络权限 `permNetworkUpload`（当前直接走 `permNetworkUpload` 权限，未单独开关）

### Phase 3 — 自托管中继 + 实时会话（跨互联网，按需）
- [x] `ShareClient.cs` + 最小 Relay 参考实现 `relay/relay.ps1`（零知识密文转发 + `POST/GET /session/{id}`）✅
- [x] 分享链接生成（零知识密文）✅
- [x] 实时协同会话（轮询同步，`Start/Join Live Session`，last-write-wins）✅
- [ ] WebSocket 增量同步取代轮询（降低延迟 / 流量）
- [ ] `ApprovalCard` 冲突确认弹窗（当前为 last-write-wins 自动覆盖）

---

## 7. 安全与隐私要点

1. **密钥永不跨机**：API Key 仍走 DPAPI 不进包；分享包不含任何密钥。
2. **密码不落盘、不写审计明文**：只记「已加密/已解密」事件。
3. **中继零知识**：服务器只转发 §3 密文，无密码则不可解密。
4. **完整性优先**：AES-GCM tag + `manifest.sha256` 双重防篡改。
5. **权限对齐**：分享/导入动作受 `PermissionGate` 网络与文件权限约束，危险导入（含插件）走 `PluginRunner` 审批。
6. **脱敏复用**：打包前对附件跑一遍 `Redactor`（PII 脱敏），避免把身份证/手机号误发出去。

---

## 8. 一句话结论

> 在 ZhuaQian Desktop 现有「本地优先 + 任务状态机 + 插件生态 + DPAPI 配置」之上，用 **`.zqp` 可移植包 + AES-GCM 密码加密 + LAN/Relay 双通道**，即可低成本实现「分享项目功能」「密码保护分享」「异地协同工作」三件套，且完全不牺牲隐私与开源定位。
