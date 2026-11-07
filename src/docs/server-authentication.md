# Server 认证：阶段 1

后台继续使用用户名、密码、TOTP 和 Cookie；客户端继续使用原有 API Key。此阶段不引入 Identity 或 WebAuthn。

## 职责

- `PasswordCredentialService`：标准密码哈希及验证，不依赖 Razor 页面。
- `AdminCredentialService`：账号失败控制、原子 TOTP 消费、敏感操作重新验证。
- `AdminPendingSessionService`：登录、初始化和重绑的短时授权状态。Cookie 只保存受保护的不透明标识。
- `AdminSessionService`：统一 Cookie 签发、注销、认证版本校验和近期验证时间。
- 页面：收集输入、调用服务、显示结果及处理跳转。

## 行为

每个后台请求检查 Cookie 中的 `AuthVersion`。修改密码、完成 TOTP 重绑和“退出所有设备”都会增加数据库中的认证版本，从下一次请求起拒绝旧 Cookie 和旧待验证授权。已经执行中的请求不会被中断。API Key 不依赖此版本。

登录成功记录固定验证时间；滑动续期不延长该时间。敏感操作始终验证当前密码，距登录验证超过 5 分钟时还需未使用过的 TOTP。开发环境保留现有跳过 TOTP 的行为。TOTP 重绑授权有效期为 5 分钟。

TOTP 保留前后各一个时间步容差，通过数据库条件更新记录已接受时间步；同一步和更早时间步不能再次用于验证。重绑完成时记录新密钥的确认时间步，因此重新登录可能需要等待下一枚验证码。

账号累计 10 次凭据验证失败后锁定 5 分钟，完整登录成功清零。原有按 IP 的限流继续生效。每份待验证授权最多允许 5 次验证请求，成功后原子消费；重启会清除全部待验证状态。内存最多存储 1024 份待验证状态，容量满时淘汰最早到期项。目前适用于单实例部署，多实例部署前需将状态改为支持原子操作的共享存储。

## 部署

1. 停止旧服务，备份 SQLite 数据库和完整 Data Protection 密钥目录。若使用 WAL，确保备份涵盖未合并的数据，或使用 SQLite 在线备份工具。
2. 发布新程序，沿用数据库连接、Data Protection 密钥路径和应用名称 `BiSheng.Server`。
3. 启动服务。现有启动逻辑自动执行 EF Core 迁移，包括 `20261101060531_HardenAdminAuthentication`。
4. 重新登录后台。旧 Cookie 和待完成流程不提供兼容路径。

迁移只为 `Users` 增加认证版本（已有用户默认为 1）、已消费 TOTP 时间步、失败计数和锁定截止时间。密码哈希、TOTP 密钥、用户 ID、API Key 和业务数据不改写。不要删除数据库或重新初始化，也不要删除 Data Protection 密钥。回退部署应恢复成套备份。

## 验证

`dotnet test tests/BiSheng.Server.Tests` 包含旧库升级及数据保留、两步登录、pending 消费和有效期、账号锁定、TOTP 重放、敏感操作验证、密码修改和重绑后的会话失效，以及 API Key 不受认证版本变化影响的测试。

后续 WebAuthn 可以复用会话签发、认证版本与敏感操作授权边界，但应使用独立的凭据表及一次性 challenge 存储。恢复码和 WebAuthn 尚未在此阶段实现。


## 阶段 2：WebAuthn / Passkey

通过 Fido2 4.0.0 库验证注册证明和登录签名。登录要求用户验证（UV），注册还要求可发现凭据；不需要输入用户名。现有密码 + TOTP 和客户端 API Key 继续可用。

默认关闭功能。部署时在 appsettings.Production.json 或环境变量中配置：

```json
{
  "WebAuthn": {
    "Enabled": true,
    "RpId": "notes.example.com",
    "RpName": "Bisheng",
    "Origins": ["https://notes.example.com"]
  }
}
```

示例域名必须替换为实际域名。RP ID 不含协议、端口和路径，Origin 含协议及必要端口、不含路径和末尾斜杠。服务器不自动信任请求 Host、不接受通配来源。仅 localhost 允许 HTTP 本机调试。启用且配置错误时启动失败。

环境变量示例：

```text
WebAuthn__Enabled=true
WebAuthn__RpId=notes.example.com
WebAuthn__RpName=Bisheng
WebAuthn__Origins__0=https://notes.example.com
```

域名变更会影响已有凭据。数据库保存注册时的 RP ID，不将旧域名凭据自动改绑到新域名。更换 RP ID 前，应先确认密码 + TOTP 备用登录可用，再在新域名重新绑定。

服务启动自动执行 AddPasskeyCredentials 迁移，只新增凭据表、唯一索引和用户外键。公钥、凭据 ID、计数器、备份状态、名称及使用时间落库，私钥不会传到服务器。每个账号在当前 RP 下最多注册 20 把凭据。

首次按原方式登录后，在账号安全页添加通行密钥。添加和删除需输入当前密码；近期五分钟没有验证时还需新的 TOTP。重命名只改显示名称，无需重新授权。添加和删除更新认证版本，其他旧会话和待完成授权失效；当前管理会话换发 Cookie 但沿用原验证时间，不延长免 TOTP 窗口。删除最后一把仍可通过密码 + TOTP 登录。

challenge 保存在有容量限制的单实例内存中，按登录/注册用途及浏览器 Cookie 绑定，有效期三分钟，验证前原子消费，失败也必须重新开始；注册同时绑定用户和认证版本。所有写接口校验防伪令牌并限流。重启清空待完成 challenge，多实例需要共享的原子 challenge 存储。

使用方式：账号页添加、重命名和删除；登录页点击“使用通行密钥登录”，按 Windows Hello、手机或密码管理器的提示完成操作。取消或超时可重新尝试。浏览器不支持时使用原登录方式。

集成测试使用软件认证器和真实 P-256 签名，覆盖完整注册登录、零计数器凭据、重放、错误来源、缺少 UV、错误签名及用户句柄、管理权限、防伪和限流。真实 Windows Hello、手机跨设备认证及密码管理器交互仍需在启用后的 HTTPS 站点验收。


### 选择添加设备

账号页提供“在此设备上添加”和“使用其他设备添加”。前者请求 platform 内置认证器，在 Windows 上通常使用 Windows Hello，在其他系统上可能使用 Touch ID 或设备 PIN；后者请求 cross-platform 认证器，用于手机或安全密钥。按 Enter 提交默认使用本机。两种方式都要求可发现凭据和用户验证，不改变登录与重新授权策略。弹窗外观及可用认证器由浏览器和系统决定，服务端无法强制指定 Windows 品牌界面。
