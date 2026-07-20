# 任务闭环（浏览器 / 桌面控制）生产化说明

模块位置：`src/Agent/TaskAgentRunner.cs`。闭环：感知 → 决策 → 行动 → 验证，按步预算重复。

## 1. 三个角色

- `IEnvironment` — 智能体运行所在环境（浏览器标签页 / 桌面）。负责 `ObserveAsync`（拿到当前状态）与 `ActuateAsync`（执行一个动作）。
- `ITaskPolicy` — 大脑：根据「最新观察 + 历史」决定下一个动作，或宣布任务完成。
- `TaskAgentRunner` — 把三者串成闭环；`MaxSteps` 步预算耗尽仍未完成则报告失败。

## 2. `ITaskPolicy` 的三种实现

| 实现 | 用途 |
| --- | --- |
| `ScriptedTaskPolicy` | 确定性脚本（固定步骤），无需模型；测试与兜底用。 |
| `DelegateTaskPolicy` | 把决策逻辑以 `Func<Observation, List<StepRecord>, PolicyDecision>` 注入，任意外部大脑。 |
| `LlmTaskPolicy` | **接 LLM 的生产实现**：把观察 + 历史拼成提示，问模型返回结构化 JSON 决策。 |

## 3. `LlmTaskPolicy`（LLM 大脑）

完全解耦：只依赖一个聊天函数 `Func<string, CancellationToken, Task<string>> chat`（提示 → 回复文本）。
宿主注入自己的聊天后端（如 `providerManager.SendAsync`、流式桥接等），不耦合具体 provider / WinForms。

构造：

```csharp
var policy = new LlmTaskPolicy(
    chat: (prompt, ct) => providerManager.SendAsync(MessagesForProvider(prompt, ct)),
    goal: "登录 example.com 并把首页标题截图",
    strict: false); // strict=true 时解析失败直接抛 FormatException
```

模型返回约束（提示里已写明）：单个 JSON 对象

```json
{ "done": false, "reasoning": "...", "action": { "commandType": "click", "target": "#id", "parameters": { "selector": "#id" } } }
```

允许的 `commandType`：`navigate, click, clicktext, fill, type, press, submit, screenshot, wait, dom, text, title, url, start, stop`。

**健壮性**：模型可能用 ```json 围栏或夹带散文。`ParseDecision` 会自动抽取第一个 `{...}` 块并用
`JavaScriptSerializer` 解析。解析失败时（非 `strict` 模式）回退为 `wait 1s` 动作，闭环不会崩溃，
受 `MaxSteps` 预算约束。

## 4. 生产化：把动作走单一审计管道

设计铁律：所有真实副作用必须过 `Command → PermissionGate → Executor → AuditLog`。
此前 `RunAsync` 把 `env.ActuateAsync` 直接调客户端、绕过管道；现在 `RunAsync` 接受可选
`actuateOverride`，生产环境用它把动作改走管道。

`TaskAgentGating.GatedActuator` 把 `AgentPipeline` 包成 `RunAsync` 需要的 actuate 函数：

```csharp
var actuate = TaskAgentGating.GatedActuator(pipeline, taskId, sharedSession: browserClient);
var report = await runner.RunAsync(goal, env, policy, ct, actuate);
```

- 浏览器动词（`navigate/click/...`）映射到已注册的 `BrowserControl` 命令（`permNetworkUpload`），
  参数原样携带（navigate 的 `url`、press 的 `key` 也会填进 `Target`）。
- 桌面 / 其它动词（如 `ComputerControl`）按 `action.CommandType` 透传（`permAutomationInput`）。
- 若该动词没有注册执行器，管道返回非致命失败，闭环记录并继续（受 `MaxSteps` 约束）。

### ⚠️ 接线注意：共享浏览器会话

`BrowserEnvironment` 持有自己的 `BrowserAgentClient` 用于**观察**；若把**行动**改走管道里另一个
`BrowserControlExecutor`，而它内部是**另一个** `BrowserAgentClient` 实例，则「观察」与「行动」会落在
两个不同会话，状态不一致。

正确做法：把环境用的同一个 `BrowserAgentClient` 实例传给 `GatedActuator`（上面的 `sharedSession` 参数），
它会 `pipeline.Register(new BrowserControlExecutor(sharedSession, null))`，保证同一会话。
这一步接线发生在组合根（如 `MainForm`），`TaskAgentRunner` 本身保持环境/策略无关。

## 5. 测试

`src/tests/TestLlmTaskPolicy.cs` 用**假聊天函数**返回固定 JSON（或脏文本）验证解析 / 回退逻辑，
不依赖真实模型，可在原生 `csc` 测试构建里跑：

```
failures += TestLlmTaskPolicy.RunAll();   // 已注册到 TestRunner.Main()
```

## 6. Demo 路径保留

不传 `actuateOverride` 时，`RunAsync` 仍直接调 `env.ActuateAsync`（现有闭环 demo 行为不变）。
`BrowserEnvironment` / `DesktopEnvironment` 适配器保持不变。

## 7. 下一步（待拍板）

- 在组合根把 `LlmTaskPolicy` + `GatedActuator` 接进「Diagnose & Fix / 浏览器任务」UI 入口，
  完成 H 模块 100% 生产化。
- 给 `LlmTaskPolicy` 增加工具调用 / 多轮纠错（当前为单步决策）。
