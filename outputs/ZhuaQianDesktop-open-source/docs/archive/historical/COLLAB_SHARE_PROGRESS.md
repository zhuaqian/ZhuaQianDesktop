# 异地协同 · 分享 · 密码分享 — 进度跟踪

> 本文件跟踪 `docs/COLLAB_SHARE_DESIGN.md` 三套能力的落地进度。所有代码落地于 `work/zq-desktop/`（仓库内可编译运行版本），`src/` 为重构中快照。

## 总体进度

| 阶段 | 能力 | 状态 | 入口 / 验证 |
|------|------|:----:|--------------|
| Phase 1 | 打包 `.zqp` + 密码分享 | ✅ 完成 | 顶部「Share」按钮 / 命令面板 · 编译通过 |
| Phase 2 | 局域网协同（LAN 直连） | ✅ 完成 | 命令面板 `Share over LAN` / `Import from URL` |
| Phase 3 | 自托管中继 + 实时会话 | ✅ 完成 | `providers/ShareClient.cs` + `relay/relay.ps1`（零知识）+ `Start/Join Live Session`（轮询同步） |

---

## Phase 1 — 打包 + 密码分享 ✅

- [x] `Core/ShareCrypto.cs`：AES-256-CBC + HMAC-SHA256（encrypt-then-MAC）+ PBKDF2（10万次）。
- [x] `Core/PackageBuilder.cs`：导出/导入 `.zqp`（manifest + project + knowledge），`sha256` 完整性校验。
- [x] UI：顶部「Share」按钮、`Share project (package)`、`Import package`（自动检测加密→弹密码）。
- [x] 编译通过（`work/zq-desktop/build.ps1`，Exit 0）。

**验证要点**：导出明文/加密包 → 另一实例导入 → 任务以 `(shared)` 标题出现；加密包错误密码会被 HMAC 拒绝；`manifest.sha256` 防篡改。

---

## Phase 2 — 局域网协同（LAN 直连） ✅

目标：零基础设施，同一局域网内两台机器直接传 `.zqp`。

- [x] `Core/LanShareServer.cs`：基于 `TcpListener` 的最小 HTTP 服务（避免 `HttpListener` 的 URL ACL / 管理员权限问题），随机 token 路径，仅服务当前包。
- [x] UI：`Share over LAN` 命令 → 构建包 → 启动服务 → 弹窗显示 `http://<LAN-IP>:8801/<token>` + 复制按钮 + 停止。
- [x] UI：`Import from URL` 命令 → 输入 URL → 下载 → `PackageBuilder.ImportBytes`。
- [ ] （可选）二维码：本机无 QR 库依赖，暂缓；URL 复制即可被对方粘贴导入。如需手机扫码，后续引入单文件 QR 编码器或自托管中继的 Web 页。

**设计要点**：
- 服务端只转发 `.zqp` 字节（若已加密则服务器只见密文，零知识）。
- token 为 8 位十六进制随机串，避免被局域网内随意枚举。
- 分享结束后 `Stop()` 关闭监听；UI 关闭即停止。

---

## Phase 3 — 自托管中继（零知识跨互联网） + 实时会话 ✅

目标：用户自己部署中继，跨互联网传 `.zqp`；服务器只转发密文（零知识）；并支持近实时协同会话。

### 3.1 文件转发 / 中继
- [x] `providers/ShareClient.cs`：POST 原始字节到 `{relay}/upload` → 返回分享链接 `{relay}/{id}`。
- [x] `relay/relay.ps1`：最小自托管中继参考实现（`HttpListener`，存 `blobs/`，`POST /upload` / `GET /{id}` / `GET /health`）。含 `netsh urlacl` 提示。
- [x] 配置：新增 `relayUrl` 设置（LoadConfig/SaveConfig 持久化）。
- [x] UI：命令面板 `Share via Relay` → 弹窗填中继地址 + 可选密码 → 上传 → 生成链接 + 复制；对方用 `Import from URL` 直接拉取（同一 GET 协议）。
- [x] 编译通过（Exit 0）。

### 3.2 实时协同会话（Live Session，轮询同步）
- [x] `ShareClient.cs`：新增 `PublishSession` / `FetchSession` / `BuildSessionUrl` / `TryParseSessionUrl`（含 out 参数）。
- [x] `relay/relay.ps1`：新增 `POST /session/{id}` 与 `GET /session/{id}` 端点（存 `blobs/sessions/{id}.bin`，复用 `InputSteam.CopyTo` / `File.ReadAllBytes` 模式）。
- [x] `ZhuaQianDesktop.cs`：命令面板 `Start Live Session` / `Join Live Session`；`Timer liveTimer`（4s 轮询）；`SnapshotTaskJson` / `SnapshotHash`（仅 hash `messages`，避免 `updatedAt` 每次保存触发误重发）；`ApplyRemoteSnapshot`（join 建新任务 / 同步覆盖当前任务）；last-write-wins 冲突处理；`ShowLiveSessionDialog`（链接 + 复制 + 停止）。
- [x] 编译通过（Exit 0）；`src/` 已同步。

**验证路径**：两端都填上同一 `relayUrl` → 一端 `Start Live Session` 复制链接 → 另一端 `Join Live Session` 粘贴 → 任意一方改动聊天/任务，≈4s 内对端同步（last-write-wins）。点「Stop Session」退出。

**仍规划（后续，非必需）**：
- [ ] WebSocket 增量同步取代轮询（降低延迟 / 流量）。
- [ ] `ApprovalCard` 冲突确认弹窗（当前为 last-write-wins 自动覆盖）。
- [ ] （可选）二维码：手机扫码导入 URL（当前手动粘贴即可）。

---

## 安全与隐私（持续约束）

1. API Key 永不进包（DPAPI 仅本机）。
2. 密码不落盘、不写明文审计。
3. 中继零知识：服务器只转发密文。
4. `manifest.sha256` + HMAC 双重防篡改。
5. 分享/导入受权限门（`permFileWrite` / `permNetworkUpload`）约束。
