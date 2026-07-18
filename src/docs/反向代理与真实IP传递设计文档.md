# 反向代理与真实 IP 传递设计文档

## 概述

BiSheng Server 生产部署采用 **Apache 对外 + Kestrel 本机回环** 的双层架构：

- **Apache**：SSL 终结、HTTP→HTTPS、静态安全 Header、反向代理
- **Kestrel**：仅监听 `127.0.0.1:8090`，不直接暴露公网

客户端真实 IP 与外部协议（http/https）通过 **Forwarded Headers** 从 Apache 传给 Kestrel，供以下功能使用：

- 登录 POST 限流（按 IP）
- 登录失败 / API Key 失败 / 429 限流日志
- Cookie `SecurePolicy` 与「当前是否 HTTPS」的感知（依赖 `X-Forwarded-Proto`）

> **与 `Cookies:SecureAlways` 的关系：** Production 下主会话 Cookie 与两步登录 / 初始化的 pending Cookie 均由配置强制 `Secure`（见 `appsettings.Production.json`），不依赖 `Request.IsHttps` 临时判断。即便如此，**仍须**在 HTTPS 站点正确设置 `X-Forwarded-Proto: https`，否则应用对外部协议的判断、以及限流/日志真实 IP 仍可能错误。部署检查清单见 [部署文档 §4 / §10](./部署文档.md)。

完整部署步骤与配置文件见 [部署文档.md](./部署文档.md)。

---

## 请求链路

```
客户端 (203.0.113.10, HTTPS)
        │
        ▼
┌───────────────────────────────────────┐
│  Apache :443                          │
│  REMOTE_ADDR = 203.0.113.10           │
│  写入 Header:                         │
│    X-Forwarded-For: 203.0.113.10      │
│    X-Forwarded-Proto: https           │
└───────────────┬───────────────────────┘
                │ TCP 来源 127.0.0.1
                ▼
┌───────────────────────────────────────┐
│  Kestrel 127.0.0.1:8090               │
│  UseForwardedHeaders() 解析后:        │
│  RemoteIpAddress = 203.0.113.10       │
│  Request.Scheme = https               │
└───────────────────────────────────────┘
```

**注意**：`ProxyPreserveHost On` 只传递 Host 头，**不会**传递客户端 IP；IP 必须显式设置 `X-Forwarded-For`。

---

## Apache 侧职责

### 必须启用的模块

| 模块 | 用途 |
|------|------|
| `proxy` / `proxy_http` | HTTP 反向代理 |
| `proxy_wstunnel` | SignalR WebSocket |
| `rewrite` | WebSocket Upgrade 规则 |
| `headers` | 设置 `X-Forwarded-*`（**必需**） |
| `ssl` | HTTPS |

### 必须写入的 Header

在每个 VirtualHost 的反向代理块中配置：

```apache
# 反向代理
ProxyPreserveHost On
ProxyPass / http://127.0.0.1:8090/
ProxyPassReverse / http://127.0.0.1:8090/

# 传递真实客户端信息（Kestrel ForwardedHeaders 依赖）
RequestHeader set X-Forwarded-Proto "https"    # HTTP 站点改为 "http"
RequestHeader set X-Forwarded-For "%{REMOTE_ADDR}s"
```

| Header | 值 | 说明 |
|--------|-----|------|
| `X-Forwarded-For` | `%{REMOTE_ADDR}s` | Apache 看到的直连客户端 IP |
| `X-Forwarded-Proto` | `http` / `https` | 外部访问协议 |

### 配置要点

1. 使用 **`RequestHeader set`**（覆盖），单层 Apache 场景不要用 `append` 叠加未知上游链。
2. `RequestHeader` 依赖 `mod_headers`；未启用时 Header 静默不生效，Kestrel 侧 IP 会一直是 `127.0.0.1`。
3. Kestrel 绑定地址必须与 `ProxyPass` 目标一致（默认 `127.0.0.1:8090`）。

### Apache 前面还有 CDN / 二级反代

默认配置假设 **仅一层 Apache**。若 Apache 前还有 Cloudflare 等：

- Apache 的 `REMOTE_ADDR` 可能是 CDN 节点而非用户 IP；
- 需先用 `mod_remoteip` 等从 CDN 提供的头中还原客户端 IP，再写入 `X-Forwarded-For`；
- 或扩展 Kestrel `KnownNetworks` 信任 CDN 网段（需单独评估，不在默认方案内）。

---

## Kestrel 侧职责

### ForwardedHeaders 注册（`Program.cs`）

```csharp
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor
                             | ForwardedHeaders.XForwardedProto;
    options.KnownProxies.Clear();
    options.KnownNetworks.Clear();

    if (builder.Environment.IsProduction())
    {
        options.KnownProxies.Add(IPAddress.Loopback);      // 127.0.0.1
        options.KnownProxies.Add(IPAddress.IPv6Loopback); // ::1
    }
});
```

| 环境 | KnownProxy | 行为 |
|------|------------|------|
| **Production** | 仅 `127.0.0.1` / `::1` | 只接受来自本机 Apache 的 Forwarded 头，解析真实 IP |
| **Development** | 无 | 直连 Kestrel 时不信任 Forwarded 头，防止本地伪造 IP |

### 中间件顺序

```
UseForwardedHeaders()   ← 必须最先（在 RateLimiter 之前）
UseRateLimiter()
…
```

若顺序颠倒，限流与登录日志中的 `RemoteIpAddress` 仍为 `127.0.0.1`。

### 环境变量要求

Systemd 服务中必须设置：

```ini
Environment=ASPNETCORE_ENVIRONMENT=Production
Environment=ASPNETCORE_URLS=http://127.0.0.1:8090
```

未设置 `Production` 时，Kestrel **不会**添加 KnownProxy，Forwarded 头将被忽略。

---

## Systemd 与 Apache 配置对照

| 配置项 | Systemd | Apache | 必须一致 |
|--------|---------|--------|----------|
| 监听地址 | `ASPNETCORE_URLS=http://127.0.0.1:8090` | `ProxyPass … http://127.0.0.1:8090/` | ✓ |
| 运行环境 | `ASPNETCORE_ENVIRONMENT=Production` | — | ✓（启用 IP 解析） |
| 真实 IP | — | `X-Forwarded-For` / `X-Forwarded-Proto` | ✓ |

完整可复制配置见 [部署文档 §4 Systemd](./部署文档.md#4-创建-systemd-服务) 与 [§5 Apache](./部署文档.md#5-安装配置-apache)。

---

## 验证清单

部署完成后按序检查：

```bash
# 1. Kestrel 仅本机监听
ss -tlnp | grep 8090

# 2. 环境为 Production
sudo systemctl show bisheng -p Environment

# 3. Apache 已写入 Forwarded 头
grep -E 'X-Forwarded-(For|Proto)' /etc/apache2/sites-available/bisheng-le-ssl.conf

# 4. mod_headers 已启用
apache2ctl -M | grep headers

# 5. 经 Apache 访问后，日志中 IP 应为公网地址而非 127.0.0.1
sudo journalctl -u bisheng -f
# 故意登录失败一次，应看到 IP=客户端公网 IP
```

---

## 故障现象对照

| 现象 | 可能原因 |
|------|----------|
| 日志 / 限流 IP 全是 `127.0.0.1` | Apache 未设 `X-Forwarded-For`，或 `mod_headers` 未启用 |
| 同上 | `ASPNETCORE_ENVIRONMENT` 不是 `Production` |
| 同上 | `UseForwardedHeaders` 在 `UseRateLimiter` 之后 |
| 限流误伤所有用户 | 同上（所有人共用一个 127.0.0.1 配额） |
| HTTPS Cookie 异常 | 缺少 `X-Forwarded-Proto: https`；或未走 HTTPS 反代而直连本机 HTTP |
| 两步登录 pending / 主会话策略疑虑 | Production 应启用 `Cookies:SecureAlways`；pending 与主会话均强制 Secure。仍须配置 Forwarded Proto，见 [部署文档](./部署文档.md) §4 注意事项 |

---

## 相关文档

- [部署文档.md](./部署文档.md) — Apache VirtualHost、Systemd 完整示例
- [认证与安全设计文档.md](./认证与安全设计文档.md) — 登录限流、Cookie、ForwardedHeaders 在安全体系中的位置
- [反向代理与真实IP传递设计文档.md](./反向代理与真实IP传递设计文档.md) — Apache ↔ Kestrel 真实 IP 传递
- [服务器日志设计文档.md](./服务器日志设计文档.md) — 含 IP 字段的 Warning 日志说明
