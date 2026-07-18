# SignalR 实时同步设计文档

## 一、SignalR 工作原理

### 1.1 什么是 SignalR

SignalR 是 ASP.NET Core 提供的实时通信库，封装了 WebSocket、Server-Sent Events (SSE) 和长轮询三种传输协议，自动根据客户端和服务端的能力选择最优协议。其核心抽象是 **Hub（集线器）**——一个服务端和客户端共享的虚拟通信端点。

### 1.2 通信模型

```
客户端 ──HubConnection──→ Hub (服务端)
         ←──推送──         │
                           ├──广播给 Group
                           ├──定向推送 Client
                           └──全局广播 All
```

| 概念 | 说明 |
|------|------|
| Hub | 服务端定义的通信类，客户端连接/断开时触发回调 |
| HubConnection | 客户端持有的连接对象，支持自动重连 |
| Group | 将多个连接分组，按组广播（本项目按 UserId 分组） |
| Method | 双方约定的方法名，服务端调客户端方法 = 推送，反之亦然 |

### 1.3 传输协议协商顺序

1. **WebSocket**（最优）——双向全双工，延迟最低
2. **Server-Sent Events** ——服务端单向推送，客户端 HTTP 长连接
3. **长轮询** ——最兼容，延迟较高

### 1.4 连接生命周期

```
StartAsync() → Connected → [收发数据] → Reconnecting → Reconnected
                                          ↓ (超过重试次数)
                                        Closed
```

---

## 二、本项目的 SignalR 架构

### 2.1 整体定位

本项目采用 **本地优先（Local-first）+ SignalR 实时推送** 的混合同步架构：

```
┌─────────────────────────────────────────────────────────────────┐
│  客户端 A (WPF)                                                 │
│  ┌──────────────┐   ┌──────────────┐   ┌────────────────────┐  │
│  │ ViewModel    │──→│ ChangeTracker│──→│ SyncService        │  │
│  │ (写本地 DB)  │   │ (记录 pending)│   │ (Push/Pull 引擎)   │  │
│  └──────────────┘   └──────────────┘   └────────┬───────────┘  │
│                                                  │              │
│                                    ┌─────────────┴───────────┐  │
│                                    │ SignalRService           │  │
│                                    │ (接收轻量通知，无 Payload) │  │
│                                    └─────────────┬───────────┘  │
└──────────────────────────────────────────────────┼──────────────┘
                                                   │ WebSocket
                                          ┌────────┴────────┐
                                          │ SyncHub (服务端) │
                                          │ Group = UserId   │
                                          └────────┬────────┘
                                                   │
┌──────────────────────────────────────────────────┼──────────────┐
│  客户端 B (WPF)                                  │              │
│  （同 A 结构）←── SignalR 轻量通知 ──────────────┘              │
└─────────────────────────────────────────────────────────────────┘
```


### 2.2 服务端设计

#### SyncHub.cs

```csharp
[Authorize(AuthenticationSchemes = ApiKeyAuthHandler.SchemeName)]
public class SyncHub : Hub
```

**职责**：
- 客户端连接时自动加入以 `UserId` 命名的 Group
- 断开时自动离开 Group
- 本身不定义业务方法（仅做连接管理）

**推送方式**：事务提交后由 `ISyncChangeNotifier`（`SyncChangeNotifier`）经 `IHubContext<SyncHub>` 广播**无 Payload 元数据**（Push 与 REST 写路径共用）。

```csharp
await hubContext.Clients.Group(userId).SendAsync("OnChange", notifyDto);
// notifyDto: EntityType / EntityId / Action / Version / Timestamp，Payload = null
```

#### Program.cs 与 DI 注册

核心服务通过 `AddBiShengServerCore()` 注册（含 SignalR）；Hub 映射仍在 `Program.cs`：

```csharp
builder.Services.AddBiShengServerCore(builder.Configuration);
// ...
app.MapHub<SyncHub>("/hubs/sync");
```

详见 [版本管理与增量同步设计文档 §4.2](./版本管理与增量同步设计文档.md#42-依赖注入microsoftextensionshosting)。

> **限流说明**：SignalR 连接与 `/api/sync` 均**不受**登录页 Rate Limiter 约束，客户端可高频 Push/Pull。限流范围见 [认证与安全设计文档 §3.2](./认证与安全设计文档.md#32-限流范围)。

### 2.3 客户端设计

#### SignalRService.cs

| 特性 | 实现 |
|------|------|
| 连接地址 | `{ServerUrl}/hubs/sync` |
| 认证方式 | `AccessTokenProvider` 传递 API Key |
| 自动重连 | 立即 → 5 秒 → 15 秒（三次重试） |
| 监听方法 | `OnChange`（接收 RemoteChange JSON） |
| 事件暴露 | `OnChangeReceived` / `OnConnectionStateChanged` |

#### SyncService 对 SignalR 的使用

**订阅关系**：
```csharp
_signalR.OnChangeReceived += OnRemoteChange;       // 收到通知 → 防抖后 PushAndPullAsync
_signalR.OnConnectionStateChanged += OnSignalRConnectionChanged;
_signalR.OnReconnected += OnSignalRReconnected;    // 重连 → 无条件补偿同步
```

**OnRemoteChange 流程**（notify-only，不就地应用）：
```
收到 OnChange（无 Payload）
    │
    ├── SchedulePullFromSignalRNotify（~300ms 防抖，合并批量通知）
    │
    └── DebouncedSignalRNotifyPullAsync
          └── PushAndPullAsync("SignalR 通知")
                └── HTTP Pull 取完整 Payload → 冲突检测 / 写本地 DB
```

---

## 三、同步触发机制（多触发器设计）

| 触发器 | 时机 | 延迟 |
|--------|------|------|
| 事件驱动 | `LocalChangeTracker.OnChangeRecorded` | 2 秒防抖 |
| 周期轮询 | `Timer` 定时 | 每 30 秒 |
| 网络恢复 | `NetworkChange` 事件 / SignalR Reconnect | 立即 |
| 应用唤醒 | `Window.Activated` | 立即 |
| 手动同步 | 用户点击同步按钮 | 立即 |
| 实时通知 | SignalR `OnChange` 回调 | ~300ms 防抖后 HTTP Push+Pull |

**并发控制**：`SemaphoreSlim(1, 1)` 保证同一时刻只有一个同步任务运行，多个触发器互不干扰。

---

## 四、完整同步流程

```
PushAndPullAsync(trigger)
    │
    ├── [Phase 1] Push：推送本地 PendingChanges
    │     ├── POST /api/sync/push {clientVersion, changes}
    │     ├── 服务端事务应用 + 写入 SyncLog
    │     ├── 返回 ServerVersion + ConflictingChanges
    │     └── 清空成功的 pending，保留失败的
    │
    ├── [Phase 2] 版本感知
    │     ├── 有 Push → 从 DB 读取 ServerVersion
    │     ├── 无 Push → GET /api/sync/version
    │     └── localVersion >= serverVersion → 跳过 Pull，完成
    │
    └── [Phase 3] Pull：拉取远端变更（终态折叠）
          ├── GET /api/sync/pull?since={localVersion}&limit=200  （分页循环至 HasMore=false）
          ├── 服务端按 EntityId 去重，返回每个实体的最新状态
          ├── 冲突检测：同一实体有本地 pending → 记录 SyncConflict
          ├── 无冲突 → 应用到本地 DB（upsert 语义）
          └── 更新 LastSyncVersion
```

---

## 五、冲突解决策略

**原则**：保留副本，用户手动决策（git-style）。

**触发条件**：Pull 时（含由 SignalR 通知触发的 Pull），本地 `PendingChanges` 中存在同一 `EntityType + EntityId` 的待推送变更。

**处理方式**：
| 操作 | 说明 |
|------|------|
| 保留本地 | 标记冲突已解决，将本地版本重新入队推送 |
| 保留远端 | 远端内容覆盖本地 DB，标记冲突已解决 |
| 手动合并 | 用户编辑后的内容写入本地 DB，入队推送 |

---

## 六、SignalR 避坑法则

### 6.1 法则一：SignalR 只传轻量通知，不传大数据

**问题**：直接把整篇笔记内容（几万字 + 大图）通过 SignalR 推送，会导致：
- **长连接拥堵**：WebSocket 通道被大包占满，其他实体的实时通知被阻塞
- **状态不可靠**：推送瞬间网络闪断，大包丢失后客户端无感知

**BiSheng 的实现**：SignalR 推送的 `ChangeDto` **不携带 Payload**，只包含 5 个轻量字段：

```csharp
// SyncChangeNotifier.BuildNotifyDto — Payload 恒为 null
new ChangeDto
{
    EntityType = EntityTypes.Note,
    EntityId = note.Id,
    Action = ChangeActions.Create,
    Version = nextVersion,
    Timestamp = DateTime.UtcNow
    // ← 无 Payload，通知体积极小
};
```


| 字段 | 示例值 | 说明 |
|------|--------|------|
| `EntityType` | `"Note"` | 实体类型（4~6 字节） |
| `EntityId` | `Guid` | 实体 ID（36 字节） |
| `Action` | `"Create"` | 操作类型（6~7 字节） |
| `Version` | `long` | 版本号（8 字节） |
| `Timestamp` | `DateTime` | 时间戳（8 字节） |

**单条通知总大小 ≈ 70 字节**，即使高频推送也不会拥堵 WebSocket 通道。

### 6.2 法则二：通知型同步架构

**核心理念**：SignalR 负责"吹哨"（低延迟通知），HTTP 负责"搬砖"（稳健数据传输）。

```
电脑端修改笔记 → 提交服务器 → 服务端生成 Version=120
                                    │
                    ┌──────────────┴───────────────┐
                    │  SignalR 轻量通知              │
                    │  {Action, EntityId, Version}  │
                    └──────────────┬───────────────┘
                                   ↓
                    手机端收到通知 → HTTP Pull → 应用到本地
```

**BiSheng 的实现**：客户端收到 SignalR 通知后触发 **HTTP Push+Pull**（短防抖合并批量通知），不就地应用推送体：
- 服务端在 `SaveChangesAsync()` / Commit 之后才发通知，数据已持久化
- 完整 Payload 始终走 HTTP，避免 WebSocket 大包与乱序就地写入
- 与「重连补偿 Pull」「周期版本探测」同一条搬砖路径，行为一致

**若改为就地应用 Payload**（旧变体，已弃用）：
- 优点：少一次 HTTP 往返，延迟略低
- 缺点：大笔记拥堵长连接；客户端需处理版本空洞与空 Payload
- 本仓库自 PR3 起统一为「通知 → HTTP Pull」

### 6.3 法则三：双轨制兜底

SignalR 长连接**一定会断**（手机黑屏休眠、杀后台、网络切换）。同步系统不能完全依赖 SignalR。

**双轨制设计**：

| 轨道 | 角色 | 触发条件 |
|------|------|----------|
| **实时轨（SignalR）** | App 在前台且连接正常时，靠 SignalR 触发即时同步 | `OnChange` 回调 |
| **兜底轨（HTTP）** | 即使 SignalR 无动静，客户端也必须主动走一次 HTTP 同步 | 见下表 |

**HTTP 兜底触发时机**（6 层保障）：

| 触发器 | 时机 | 延迟 |
|--------|------|------|
| 事件驱动 | 本地变更后 | 2 秒防抖 |
| 周期轮询 | 定时触发 | 每 30 秒 |
| 网络恢复 | `NetworkChange` 事件 | 立即 |
| SignalR 重连 | 长连接恢复后 | 立即（5 秒去重） |
| 应用唤醒 | `Window.Activated` | 立即 |
| 手动同步 | 用户点击按钮 | 立即 |

**并发控制**：`SemaphoreSlim(1, 1)` 保证所有触发器互斥，不会重复执行。

**核心结论**：SignalR 是**加速器**而非**必要条件**。即使完全去掉 SignalR，系统仍能通过 HTTP 兜底轨保证数据最终一致性，只是实时性从毫秒级降级到秒级。

---

## 七、关键文件索引

| 文件 | 职责 |
|------|------|
| `Server/Hubs/SyncHub.cs` | 服务端 Hub，管理连接分组 |
| `Server/Services/Mutations/SyncChangeNotifier.cs` | 事务提交后推送无 Payload 元数据 |
| `Server/Services/SyncService.cs` | Push/Pull 业务逻辑；提交后调用 `NotifyBatchAsync` |
| `Server/Controllers/SyncController.cs` | HTTP 适配层（鉴权、路由） |
| `Server/DependencyInjection/ServiceCollectionExtensions.cs` | DI 注册（`ISyncService`、SignalR 等） |
| `Latte/Services/SignalRService.cs` | 客户端连接管理，事件封装 |
| `Latte/Services/SyncService.cs` | 同步引擎门面：编排 + 锁 + 对外 API |
| `Latte/Services/Sync/` | Push/Pull/合并/排序/全量重建/SignalR 协调管道 |
| `Latte/Services/LocalChangeTracker.cs` | 本地变更记录（写 pending 队列） |
| `Latte/Data/Entities/LocalPendingChange.cs` | 待推送变更实体 |
| `Latte/Data/Entities/SyncConflict.cs` | 同步冲突记录实体 |
