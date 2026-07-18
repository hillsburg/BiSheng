# P2 组合根与 API 安全设计文档

## 一、Latte DI 组合根

### 1.1 目标

- 单一 `Func<LocalDbContext>` / `ILocalDbContextFactory`，App 与 ViewModel 共用
- 服务图可测试、可替换（测试注入 in-memory factory）

### 1.2 结构

```
App.OnStartup
  → LatteHost.Build()
  → ServiceCollectionExtensions.AddBiShengLatte()
  → 迁移 / 完整性 / 维护任务（同一 dbFactory）
  → MainWindow(MainViewModel from DI)
```

| 类型 | 路径 |
|------|------|
| 组合根 | `BiSheng.Latte/Composition/LatteHost.cs` |
| 注册 | `BiSheng.Latte/DependencyInjection/ServiceCollectionExtensions.cs` |
| 工厂接口 | `BiSheng.Latte/Data/ILocalDbContextFactory.cs` |

ViewModel / `ExportService` 通过构造函数注入 `Func<LocalDbContext>`，不再 `new LocalDbContext()`。

---

## 二、Server 统一错误模型

### 2.1 ProblemDetails

所有 REST 业务错误返回 RFC 7807，扩展字段：

| 字段 | 说明 |
|------|------|
| `type` | `https://bisheng.local/errors/{code}` |
| `title` | 中文标题 |
| `detail` | 可读说明 |
| `code` | 机器码（见 `BiSheng.Shared/Api/ApiErrorCodes.cs`） |
| `traceId` | 请求追踪 |

工厂：`BiSheng.Server/Api/ApiProblemResults.cs`

Mutation 控制器、`InvalidModelState`、生产 500、429 均走同一形态。

---

## 三、CORS 与 API Key

### 3.1 CORS

`appsettings.json` / `appsettings.Production.json`：

```json
"Cors": {
  "AllowAnyOrigin": false,
  "AllowedOrigins": []
}
```

- **Production 默认**：不发送 CORS 头（桌面客户端无浏览器 CORS）
- **Development**：`AllowAnyOrigin: true` 便于 Swagger
- 若将来有 Web 管理端：配置 `AllowedOrigins` 白名单

### 3.2 API Key 传递

| 路径 | 允许方式 |
|------|----------|
| `/api/*` | 仅 `X-Api-Key` Header |
| `/hubs/*` | Header **或** `access_token` query **或** Bearer（SignalR WebSocket） |

已移除 REST 上的 `?api_key=` query，防止日志/Referer 泄露。

Latte 客户端：`ApiClient` 用 Header；`SignalRService` 用 `AccessTokenProvider` → query。
