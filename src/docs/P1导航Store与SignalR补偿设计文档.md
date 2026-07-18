# P1 导航 Store、SignalR 补偿与附件 API 设计文档

## 一、概述

P1 在 P0 统一写路径之上，解决三类客户端/服务端可靠性问题：

| 子项 | 目标 | 状态 |
|------|------|------|
| **P1-A** | Latte `INavigationStore` + `LocalNoteMerger` 单一读模型 | ✅ |
| **P1-B** | SignalR 不可靠补偿：重连 Pull + 连接态版本探测 | ✅ |
| **P1-C** | 废弃 legacy `FilesController`；Images 为唯一附件 API | ✅ |

---

## 二、P1-A：导航 Store 与单一读模型

### 2.1 问题

- 笔记数据三处缓存：SQLite、`NoteList`/`FolderTree` 的 `LocalNote`、编辑器 `EditorContent`
- `SyncNoteFields` 在 `NoteListViewModel` 与 `EditorViewModel` 重复实现且规则不一致
- 树模式 `SelectNote` 可能把**树节点** `NoteItemViewModel` 直接赋给 `NoteList.SelectedNote`，该引用不在 `Notes` 集合中
- 同步完成后的 `Refresh` 编排散落在 `MainViewModel`

### 2.2 方案（P1 初版；现行架构见 [导航栏架构设计文档](./导航栏架构设计文档.md)）

P1 建立 Store 与合并规则；同步后刷新在 **P4/P5** 已改为读模型 + Coordinator，不再使用 `OnSyncComplete`。

```
SelectNote（树/列表）
  → NavigationStore.SelectNoteById(noteId, folderId)
       → NoteList.LoadNotes + 从 Notes 集合解析 SelectedNote

同步 / 本地 / 搜索 / 布局（P4/P5）
  → INavigationReadModel → NavigationPresentationCoordinator
       → NavigationStore.ApplyRemoteDelta / ApplyFilterProjection / ApplyLayoutRebuild
       → IEditorSessionService（版本感知，仅 DataChange）
```

**`LocalNoteMerger.MergeFields`**：DB → 内存的唯一合并规则（含「DB 空不覆盖非空正文」守卫）。

### 2.3 关键类型

| 类型 | 路径 |
|------|------|
| `LocalNoteMerger` | `BiSheng.Latte/Services/Navigation/LocalNoteMerger.cs` |
| `INavigationStore` | `BiSheng.Latte/Services/Navigation/INavigationStore.cs` |
| `NavigationStore` | `BiSheng.Latte/Services/Navigation/NavigationStore.cs` |

`MainViewModel` 持有 `NavigationStore`；`NoteSwitching` / `NoteClosed` 事件转发至 Store。

### 2.4 测试

- `NavigationStoreTests`：`MergeFields` 空 DB 守卫、`SelectNoteById` 集合解析
- 保留 `FolderTreeRefreshTests`：同 Id 不替换 `SelectedFolder` 引用

---

## 三、P1-B：SignalR 补偿（Pull-on-reconnect，非 Server outbox）

### 3.1 原则

`SyncLog` 已是持久事件日志；**不**在服务端 duplicate SignalR outbox。客户端假设 **SignalR 可丢消息**，用轻量版本探测补偿。

### 3.2 触发点

| 场景 | 行为 |
|------|------|
| SignalR **自动重连**（`OnReconnected`） | 无条件 `PushAndPullAsync("SignalR 重连")` |
| **周期 tick**（默认 30s）且 `PeriodicVersionProbeWhenConnected=true` | 即使已连接、无 pending，仍 `GET /api/sync/version` 比对并 Pull |
| SignalR **反序列化失败** | `PushAndPullAsync("SignalR 消息异常补偿")` |
| 系统网络恢复 | 原有 `OnNetworkRecovered` 不变 |

首次 `StartAsync` 连接**不**经重连路径，避免与启动同步重复。

### 3.3 配置

`SyncSettings.PeriodicVersionProbeWhenConnected`（默认 `true`）。

### 3.4 图片管道

`ImageSyncService.OnNetworkRecoveredAsync()` 在 SignalR 重连时上传 + 增量拉取；`GetLocalImagePath` 支持 `{id}.*` 扩展名。

---

## 四、P1-C：废弃 Files API

### 4.1 结论

- **`ImagesController`**（`/api/images`）为唯一附件 API：`bisheng://img/{uuid}` + `LocalImage`/`ServerImage`
- **`FilesController`**（`/api/files`）无客户端调用、无 DB 元数据、与 Images 重叠 → **已删除**

### 4.2 遗留风险（P2）

- ~~服务端 orphan 图片（无 ref 扫描 GC）~~ ✅（PR5：`NoteImageReferenceScanner` + `ImageCleanupService`）
- ~~客户端删除 markdown 内图片未 propagate `DELETE`~~ → **设计如此：仅服务端 GC**（文档已对齐；非同步正确性缺口）
- 导出未解析 `bisheng://`

---

## 五、相关代码索引

| 组件 | 路径 |
|------|------|
| 导航 Store | `BiSheng.Latte/Services/Navigation/` |
| SignalR 客户端 | `BiSheng.Latte/Services/SignalRService.cs` |
| 同步引擎 | `BiSheng.Latte/Services/SyncService.cs` |
| 图片同步 | `BiSheng.Latte/Services/ImageSyncService.cs` |
| 同步配置 | `BiSheng.Latte/Models/SyncSettings.cs` |
| 图片 API | `BiSheng.Server/Controllers/ImagesController.cs` |

---

## 六、后续（P2）

- 导航 **增量 Refresh**（按变更 entityId 局部更新，替代全树重建）
- ~~图片 ref 扫描 GC~~ ✅（PR5）；~~客户端删除 propagate~~ → 仅服务端 GC（文档对齐）
- `ExportService` 解析 `bisheng://img/`
