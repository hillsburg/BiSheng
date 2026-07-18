# SyncLog 裁剪设计文档

## 一、背景

BiSheng 同步系统为每次变更在 `SyncLogs` 表写入一条记录，版本号 `Version`（`long`）单调递增。版本号本身不会溢出，但 **日志行数** 会随使用时间线性增长。

Pull 接口已采用**终态折叠**（同一实体多次编辑只返回最新状态），因此裁剪历史 SyncLog **不会影响**已在线设备的增量同步语义——前提是：**不能删除任何仍在线设备尚未同步到的版本之前的日志**。

---

## 二、核心原则

> **裁剪必须以版本号为依据，绝不能以时间为依据。**

按「删除 N 天前的日志」会在离线设备重新上线时造成 `LastSyncVersion` 断层，导致增量 Pull 丢失变更。

安全裁剪线定义：

```
安全裁剪线(userId) = min(该用户所有「活跃客户端」的 LastSyncVersion)
可删除: SyncLogs WHERE UserId = ? AND Version < 安全裁剪线
```

---

## 三、数据模型

### 3.1 ClientSyncStates（客户端同步状态）

每个 **API Key（设备）** 一行，记录该设备最后成功同步到的服务端版本。

| 字段 | 类型 | 说明 |
|------|------|------|
| ApiKeyId | Guid PK, FK | 对应 `ApiKeys.Id` |
| UserId | Guid | 所属用户 |
| LastSyncVersion | long | 该设备最后确认的 ServerVersion |
| LastSeenAt | DateTime | 最后一次 Push/Pull/version 请求时间 |
| IsStaleExcluded | bool | 是否因长期离线而从裁剪基线中排除 |

Push / Pull 成功后更新 `LastSyncVersion = max(旧值, 新版本)`，`LastSeenAt = UtcNow`，`IsStaleExcluded = false`。

### 3.2 UserSyncMetas（用户同步元数据）

| 字段 | 类型 | 说明 |
|------|------|------|
| UserId | Guid PK | 用户 ID |
| LogRetentionFloor | long | 已裁剪掉的最高版本上界（Version < Floor 的日志已被删除） |
| CurrentVersion | long | 版本计数器：当前已分配的最高 SyncLog 版本号（见 [版本管理与增量同步设计文档](./版本管理与增量同步设计文档.md) §3） |

每次裁剪成功后：`LogRetentionFloor = max(旧值, 本次裁剪线)`。版本分配由 `CurrentVersion` 原子递增，裁剪**不重置**计数器。

### 3.3 ServerConfig 扩展

| 字段 | 默认值 | 说明 |
|------|--------|------|
| SyncLogStaleClientDays | 90 | 超过此天数未同步的设备标记为 stale |
| SyncLogMinEntriesForCompaction | 1000 | 用户 SyncLog 低于此行数时不裁剪 |
| SyncLogCompactionIntervalHours | 24 | 后台裁剪任务间隔 |

---

## 四、僵尸客户端（Stale Client）

若某 API Key 超过 `SyncLogStaleClientDays` 未上报同步：

1. 设置 `IsStaleExcluded = true`（**不再参与** `min(LastSyncVersion)` 计算）
2. 允许其他在线设备推进裁剪线，删除更旧的 SyncLog

当 stale 设备再次连接：

- 若其 `clientVersion`（或 Pull 的 `since`）**小于** `UserSyncMeta.LogRetentionFloor`
- 服务端在 Push/Pull 响应中返回 `RequiresFullSync: true`
- 客户端清空本地业务数据，以 `since=0` 全量 Pull 重建

---

## 五、流程

### 5.1 同步时上报（ISyncService）

```
Push 成功 / Pull 完成
  → SyncService 调用 ClientSyncStateService.UpsertAsync（推进 LastSyncVersion）
  → 若 clientVersion / since < LogRetentionFloor → RequiresFullSync = true

GET /api/sync/version（版本探测）
  → 仅 ClientSyncStateService.TouchAsync（刷新 LastSeenAt）
  → 不推进 LastSyncVersion（避免误抬裁剪线）
```

HTTP 入口为 `SyncController`，业务逻辑在 `Services/SyncService.cs`。

**已知限制（无 ACK）：** Pull 在写出 HTTP 响应之前即 `Upsert` 设备游标。客户端崩溃或响应丢失时，一般可用旧 `since` 重拉；若期间裁剪已按抬高后的 floor 删除日志，则返回 `RequiresFullSync`（显式全量，非静默缺口）。将来若改为「客户端 ACK 后再抬游标」属协议变更，见 [数据安全策略设计文档](./数据安全策略设计文档.md) §六。

### 5.2 后台裁剪（SyncLogCompactionService）

```
每 SyncLogCompactionIntervalHours 小时:
  1. 标记 stale 客户端 (LastSeenAt 超时)
  2. 对每个 UserId:
     a. 若 SyncLog 行数 < MinEntries → 跳过
     b. cutoff = min(活跃 ClientSyncState.LastSyncVersion)
     c. 若 cutoff 为空或 0 → 跳过
     d. 在同一 SQLite 事务中：
        DELETE SyncLogs WHERE UserId AND Version < cutoff
        UserSyncMeta.LogRetentionFloor = max(旧值, cutoff)
        任一步失败则整批回滚（避免「日志已删但 floor 未抬」导致静默丢变更）
```

实现：`SyncLogCompactor.CompactUserAsync`（由 `SyncLogCompactionService` 调用）。

### 5.6 客户端全量重建（Latte）

收到 `RequiresFullSync`：

1. 清空本地 Folders / Notes / PendingChanges
2. `LastSyncVersion = 0`
3. 执行 Pull(since=0)
4. 刷新 UI

---

## 六、与版本号类型的关系

- `Version` 仍为 `long`，**裁剪后不重置计数器**，新变更继续递增
- 裁剪仅减少 `SyncLogs` 表体积与 Pull 扫描范围
- `LogRetentionFloor` 告知离线过久的设备「你的 since 已失效，需全量同步」

> **注意**：笔记用户历史（`NoteRevisions`）与 SyncLog 裁剪无关，保留策略见 [笔记历史版本设计文档](./笔记历史版本设计文档.md)。

---

## 七、相关代码

| 组件 | 路径 |
|------|------|
| 实体 | `BiSheng.Server/Data/Entities/ClientSyncState.cs`, `UserSyncMeta.cs` |
| 客户端状态服务 | `BiSheng.Server/Services/ClientSyncStateService.cs` |
| 版本计数器 | `BiSheng.Server/Services/UserSyncVersionService.cs` |
| 裁剪后台任务 | `BiSheng.Server/Services/SyncLogCompactionService.cs` |
| 单用户原子裁剪 | `BiSheng.Server/Services/SyncLogCompactor.cs` |
| 同步业务 | `BiSheng.Server/Services/SyncService.cs`（`ISyncService`） |
| HTTP 适配 | `BiSheng.Server/Controllers/SyncController.cs` |
| 客户端处理 | `BiSheng.Latte/Services/SyncService.cs` |
| DTO | `BiSheng.Shared/Sync/SyncDtos.cs` → `RequiresFullSync` |

---

## 八、运维日志

裁剪任务以 `Information` 级别记录：

```
用户 {UserId} SyncLog 裁剪: 删除 {Count} 条, 保留线 Version >= {Cutoff}
```

Stale 标记以 `Debug` 级别记录。
