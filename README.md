# BiSheng (Latte)

[![CI](https://github.com/hillsburg/BiSheng/actions/workflows/ci.yml/badge.svg)](https://github.com/hillsburg/BiSheng/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-8.0-512BD4)](https://dotnet.microsoft.com/)
[![Platform](https://img.shields.io/badge/Platform-Windows-0078D6)](https://www.microsoft.com/windows)

**BiSheng** (desktop app codename **Latte**) is a Windows note-taking application with an instant Markdown rendering editor and optional self-hosted sync.

> **Public source:** This repository is the public open-source tree. Runtime SQLite databases, `uploads/`, and Data Protection keys are never shipped; they are created locally when you run the app or server.

> **UI language:** Chinese (Simplified) today. English UI is planned — see [Internationalization](#internationalization).
>
> **中文说明：** 笔生 / Latte 是一款 Windows 笔记应用，支持 Markdown 即时渲染与可选的自托管同步。详细中文文档见 [`src/docs/`](src/docs/)。

---

## Features

### Desktop client (`BiSheng.Latte`)

- **Instant Markdown editor** — WYSIWYG-style editing powered by a custom `BiSheng.Editor` control (Markdig + AvalonEdit)
- **Folder tree navigation** — tree or side-by-side layout, favorites, pins, rename, drag-and-drop
- **Full-text search** — search across note titles and content (`Ctrl+Shift+F`)
- **Themes** — built-in presets (Light, Dark, Latte, 翰墨书香) plus user-defined themes for UI and editor colors
- **Export** — Markdown, Word (OpenXML), PDF
- **Note history & trash** — local revision history and soft-delete with purge
- **Offline-first** — works without a server; data stored in local SQLite (`local.db`)
- **Local backups** — scheduled SQLite backups and integrity checks

### Sync server (`BiSheng.Server`) — optional

- **Self-hosted** ASP.NET Core API + admin web panel
- **Incremental sync** — push/pull with version tracking and conflict detection
- **SignalR** — real-time change notifications to connected clients
- **Image uploads** — note images stored server-side and synced to clients
- **API key auth** — per-device keys; TOTP support for admin login
- **SQLite storage** — single-file `bisheng.db` plus `uploads/` directory

---

## Screenshots

<!-- TODO: add screenshots before first public release -->
<!-- Suggested paths: docs/images/main-window.png, editor.png, sync-settings.png -->

_Screenshots coming soon._

---

## Requirements

| Component | Requirement |
|-----------|-------------|
| **OS** | Windows 10 or later (x64) |
| **SDK** | [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) |
| **Runtime** | [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) (for published builds) |
| **WebView2** | [Microsoft Edge WebView2 Runtime](https://developer.microsoft.com/microsoft-edge/webview2/) (usually preinstalled on Windows 11) |
| **Server** (optional) | .NET 8 ASP.NET Core Runtime on Windows or Linux |

---

## Quick start

### Clone and build

```bash
git clone https://github.com/hillsburg/BiSheng.git
cd BiSheng

dotnet build Bisheng.slnx -c Release
```

### Run the desktop client

```bash
dotnet run --project src/BiSheng.Latte/BiSheng.Latte.csproj -c Release
```

On first launch the app creates data under `%LocalAppData%\BiSheng\Latte\` (`local.db`, config, images). No server configuration is required.

### Run the sync server (optional)

```bash
dotnet run --project src/BiSheng.Server/BiSheng.Server.csproj -c Release
```

By default the server listens on **http://localhost:8090** (see `src/BiSheng.Server/appsettings.json`). Open the URL in a browser to complete first-time setup (admin account, API keys).

Then in the Latte client: **Toolbar → 同步与安全** — enter server URL and API key (from `/admin/keys`). User guide (Chinese): [`src/docs/同步与安全设置使用指南.md`](src/docs/同步与安全设置使用指南.md).

### Publish (Windows x64)

Framework-dependent publish:

```bash
dotnet publish src/BiSheng.Latte/BiSheng.Latte.csproj -c Release -r win-x64 --self-contained false
```

Latte and Server share one product version (`BiShengVersion` in `Directory.Build.props`). One GitHub Release ships both:

```powershell
.\scripts\release\pack-all.ps1
```

See [`scripts/release/README.md`](scripts/release/README.md). Tag `v0.1.0` (or Actions → Release) publishes Latte Velopack packages + Server zip. In the app: **Toolbar → 关于** → 检查更新.

---

## Repository layout

| Path | Description |
|------|-------------|
| `src/BiSheng.Latte/` | WPF desktop client (main application) |
| `src/BiSheng.Server/` | ASP.NET Core sync server + admin panel |
| `src/BiSheng.Editor/` | Reusable Markdown instant-rendering editor control |
| `src/BiSheng.Shared/` | Shared sync protocol DTOs and helpers |
| `src/ICSharpCode.AvalonEdit/` | Vendored/forked AvalonEdit (text editing core) |
| `src/docs/` | Design documents (Chinese) |
| `tests/` | xUnit tests for Server, Latte, and Editor |

---

## Development

### Run tests

```bash
dotnet test tests/BiSheng.Server.Tests/BiSheng.Server.Tests.csproj -c Release
dotnet test tests/BiSheng.Latte.Tests/BiSheng.Latte.Tests.csproj -c Release
dotnet test tests/BiSheng.Editor.Tests/BiSheng.Editor.Tests.csproj -c Release
```

CI runs Server and Latte tests on every push (see [`.github/workflows/ci.yml`](.github/workflows/ci.yml)).

### Architecture highlights

- **Client:** WPF + MVVM (`CommunityToolkit.Mvvm`), DI via `Microsoft.Extensions.DependencyInjection`
- **Navigation:** incremental read-model updates (see `src/docs/导航栏架构设计文档.md`)
- **Sync:** shared payload format in `BiSheng.Shared`; client `SyncService` + server `SyncService` + SignalR hub
- **Theming:** `ThemeDefinition` / `ThemeManager` — JSON presets applied to WPF `DynamicResource` brushes
- **Dialogs:** centralized `AppDialog` service (themed replacements for `MessageBox`)

More design docs (Chinese): [`src/docs/`](src/docs/). An English architecture overview is planned.

### Code conventions

See [CONTRIBUTING.md](CONTRIBUTING.md). Summary:

- Comments in **Simplified Chinese** (project convention)
- Curly braces on their own lines; blank line after closing `}`
- XML doc comments on public APIs, enums, properties, and methods

---

## Deployment (server)

Production deployment guide (Chinese, Apache reverse proxy + HTTPS):

- [`src/docs/部署文档.md`](src/docs/部署文档.md)
- Server backup / restore (ops scripts, optional Litestream): [`src/docs/服务端备份与恢复运维指南.md`](src/docs/服务端备份与恢复运维指南.md), [`scripts/server-backup/`](scripts/server-backup/)
- Client + server update design (Velopack / upgrade script, Chinese): [`src/docs/客户端与服务端更新机制设计文档.md`](src/docs/客户端与服务端更新机制设计文档.md)

Typical production setup: Kestrel behind Apache/Nginx on port 443, with `X-Forwarded-*` headers. Security notes: [`src/docs/认证与安全设计文档.md`](src/docs/认证与安全设计文档.md).

---

## Internationalization

The UI is currently **Chinese (Simplified)**. For open source, we plan:

1. English `README` and contributor docs (this file)
2. English UI for shell controls (toolbar, navigation, dialogs) via `.resx` resources
3. Gradual translation of settings and sync messages

Contributions for `en-US` strings are welcome even before the infrastructure lands — open an issue to coordinate.

---

## Third-party components

BiSheng depends on several open-source libraries. See [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) (to be added) for the full list.

Notable vendored code:

- **AvalonEdit** (`src/ICSharpCode.AvalonEdit/`) — MIT License, ic#code / SharpDevelop team

---

## Contributing

We welcome issues and pull requests! Please read [CONTRIBUTING.md](CONTRIBUTING.md) before opening a PR.

**Good first issues** (planned labels): documentation, English UI strings, tests, bug fixes in sync/navigation.

---

## Security

If you discover a security vulnerability, please **do not** open a public issue. See [SECURITY.md](SECURITY.md) (to be added) or email the maintainers privately.

---

## License

This project is licensed under the [MIT License](LICENSE).

Copyright (c) 2026 BiSheng contributors.
