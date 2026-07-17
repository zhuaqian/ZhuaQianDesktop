# ZhuaQian Desktop 产品架构文档

更新时间：2026-07-11

## 1. 架构目标

当前架构的核心目标不是“再堆功能”，而是把现有能力收敛成一个可维护、可测试、可协作的产品骨架。

架构约束以 [ARCHITECTURE_CHARTER.md](ARCHITECTURE_CHARTER.md) 为准。本文档负责解释产品分层和演进路线，Charter 负责定义后续贡献必须遵守的不变量。

当前最重要的架构要求：

- 以 `src/` 作为公开贡献和后续开发的主线源码树
- 在 `work/zq-desktop/` 尚未退休前保持同步验证
- 避免继续把逻辑塞进主窗体
- 让任务、权限、动作、产物形成统一闭环
- 所有真实副作用最终必须经过 Command / Gate / Executor 管道

## 2. 当前源码现实

当前仓库有三套并行资产：

- `work/zq-desktop/`
- `src/`
- `outputs/ZhuaQianDesktop-open-source/`

当前默认事实源：

- 公开贡献主线：`src/`
- 过渡运行镜像：`work/zq-desktop/`
- 发布快照：`outputs/ZhuaQianDesktop-open-source/`

架构治理的第一目标不是继续复制三份，而是最终收敛为 `src/` 这一棵权威源码树。

## 3. 产品层级

### 3.1 UI 层

职责：

- 主窗体
- 左侧任务列表
- 输入区与聊天区
- 设置界面
- 命令入口
- Outputs 展示
- 权限与审批展示

当前问题：

- 主窗体承担过多业务逻辑
- 弹窗偏多
- 固定工作台布局还不成熟

### 3.2 Task / Agent 层

职责：

- 任务创建与切换
- 任务状态
- Brief / Plan / Execute 编排
- 任务上下文保存

目标状态：

- 每个任务都有状态
- 每个任务都有动作记录
- 每个任务都有产物记录
- 每个任务都有可追踪执行过程

### 3.3 Core 层

职责：

- 配置保存
- 审计日志
- 权限判断
- 产物中心
- 分享与打包基础能力

关键模块：

- `ConfigStore`
- `AuditLog`
- `PermissionGate`
- `OutputsHub`

设计要求：

- UI 不应再重复维护另一份配置和权限逻辑
- Core 模块必须成为唯一实现来源

### 3.4 Provider 层

职责：

- 模型调用
- provider 选择
- fallback
- streaming
- 统一错误处理

设计要求：

- provider 调用入口统一
- 云端上传确认统一
- 模型返回解析统一

### 3.5 Documents 层

职责：

- 文档提取
- 脱敏
- 文件生成

关键模块：

- `DocumentExtractor`
- `OfficeExporter`
- `Redactor`

设计要求：

- “说生成了文件”不算完成
- 只有真实落盘并被记录到 Outputs，才算完成

### 3.6 Knowledge 层

职责：

- 文件夹索引
- 分块
- metadata
- 向量持久化
- 混合检索

设计要求：

- 所有知识回答应尽量可溯源
- 检索结果应尽量关联任务上下文

### 3.7 Tools 层

职责：

- 文件整理
- 插件运行
- 资源监控
- OCR / clipboard / 监控扩展

设计要求：

- 工具运行前后要留下动作记录
- 有产物则进入 Outputs
- 可回滚则记录 rollback manifest

## 4. 关键数据对象

### 4.1 Task

最少字段：

- `taskId`
- `title`
- `status`
- `lastAction`
- `createdAt`
- `updatedAt`
- `provider`
- `model`
- `messages`

### 4.2 ActionRecord

最少字段：

- `actionId`
- `taskId`
- `type`
- `status`
- `requestedAt`
- `approvedAt`
- `detail`
- `affectedPaths`
- `result`
- `rollbackManifest`

### 4.3 OutputRecord

最少字段：

- `outputId`
- `taskId`
- `kind`
- `path`
- `createdAt`
- `sourceActionId`
- `exists`

### 4.4 PermissionDecision

最少字段：

- `permissionName`
- `target`
- `mode`
- `decision`
- `rememberPolicy`

## 5. 关键工作流

### 5.1 文档分析工作流

1. 用户上传文件
2. 解析文件内容
3. 根据 provider 与权限策略决定是否云端上传
4. 模型生成结果
5. 结果写入任务消息
6. 如生成文件，则真实落盘并写入 Outputs

### 5.2 Agent 执行工作流

目标形态：

1. Brief
2. Plan
3. Approval
4. Execute
5. Review
6. Output
7. Rollback

当前现实：

- 已有部分模式
- 还未完全成为严格状态机

### 5.3 文件整理工作流

1. 用户选择目录
2. 权限确认
3. 执行整理
4. 生成 rollback manifest
5. 记录 action
6. 记录 output

### 5.4 文件导出工作流

1. 模型生成结构化内容
2. 用户选择保存路径
3. `OfficeExporter` 落盘
4. 记录 Output
5. 用户可打开或定位

## 6. 当前架构问题

### P0

- 主窗体仍然过大
- 多套源码树并行
- 测试链路已恢复，但仍未迁移到标准 xUnit
- Command / Gate / Executor 管道已在导出、整理、插件、进程管理、回滚和基础电脑控制中部分落地，但还不是所有副作用动作的唯一入口

### P1

- 权限模型尚未完全统一
- Approval Card 覆盖不全
- 产物中心与动作中心关联还不够成熟

### P2

- 自动化任务和插件生态还缺规范
- UI 工作台布局还未固定

## 7. 目标架构决策

当前建议采用的决策：

1. 继续以 `src/` 为开发和公开贡献基线
2. 新功能优先接到已有模块，不再写主窗体副本
3. 产品文档只保留少量核心文件
4. 历史分析文档统一归档

## 8. 模块边界约束

后续开发必须遵守：

- UI 层不直接重写 Core 逻辑
- Provider 层不直接操作 UI 控件
- Tools 层副作用必须经过权限与审计
- Documents 层负责落盘，不负责业务编排
- Knowledge 层负责索引与检索，不负责聊天渲染

## 9. 近期架构里程碑

### M1

- 文档收敛
- 运行基线确认
- 测试现实确认

### M2

- fallback / failover / src 测试已收绿，继续保持同步
- 明确唯一源码树方向：`src/`

### M3

- 持续把 Command / Gate / Executor 管道作为 `src/` 主线能力，并同步仍需保留的 `work/zq-desktop` 镜像
- 主窗体继续降重
- UI 只保留编排和展示

### M4

- 工作流状态机成型
- Outputs / Actions / Permissions 形成闭环
