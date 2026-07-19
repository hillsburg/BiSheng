# BiSheng 统一发版（Latte + Server）

Latte 与 Server **共用** `Directory.Build.props` 中的 `BiShengVersion`。一次 GitHub Release（tag `v*`）包含两端产物。

## 本地打包

前置：.NET 8 SDK；`dotnet tool install -g vpk`（与 NuGet `Velopack` 1.2.x 对齐）。

```powershell
# 改版本：编辑 Directory.Build.props 的 BiShengVersion，或：
.\scripts\release\pack-all.ps1 -Version 0.1.1
```

产物目录：`artifacts/release/<version>/`

典型文件：

- Velopack Setup / nupkg / releases.*.json（Latte）
- `BiSheng.Server-<version>-linux-x64.zip`
- `SHA256SUMS.txt`

## CI 发版

推送 tag：

```bash
git tag v0.1.0
git push origin v0.1.0
```

工作流：`.github/workflows/release.yml`（先跑测试，再打包并创建 GitHub Release）。  
Release 说明由 `gh --generate-notes` 根据上一 tag 以来的 PR/commit 自动生成，可事后在 GitHub 上 Edit。

也可在 Actions 里用 `workflow_dispatch` 填入版本号（不带 `v`）。

## 客户端更新

已安装的 Latte 从同一仓库 Releases 检查更新（见关于页）。服务端用 zip + 部署文档 / 升级脚本。
