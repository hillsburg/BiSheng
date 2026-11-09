# BiSheng 统一发版（Latte + Server）

一次 GitHub Release（tag `vX.Y.Z`）包含 Latte 与 Server。安装包版本、Velopack 版本和程序集文件版本都来自这个 tag。

[`Directory.Build.props`](../../Directory.Build.props) 里的 `BiShengVersion` 只用于本地 `dotnet run`，以及不传 `-Version` 时的本地打包。发版不用改它。

## 发版

在要发布的提交上打附注 tag 并推送。CI 去掉 `v` 后调用 `pack-all.ps1 -Version`，打包再传 `-p:BiShengVersion`，盖过 props 里的旧号。

```bash
git checkout main && git pull
git tag -a v0.1.3 -m "v0.1.3"
git push origin v0.1.3
```

约束：

- tag 必须是 `v` + `x.y.z`，例如 `v0.1.3`。`AssemblyVersion` 写成 `x.y.z.0`，不能带 `-beta` 这类后缀。
- 只在本地打 tag 不会发版，必须 `git push origin vX.Y.Z`。
- 若 tag 打错：删远程 tag 后改对再推（注意已产生的 Release，以及用户是否已装该版）。

Actions 里也可以用 `workflow_dispatch` 手工填版本号（不带 `v`），规则相同。

## 本地打包

前置：.NET 8 SDK；`dotnet tool install -g vpk`（与 NuGet `Velopack` 1.2.x 对齐）。

```powershell
# 默认读取 Directory.Build.props 的 BiShengVersion（本地默认版本）
.\scripts\release\pack-all.ps1

# 显式指定时，程序集版本与 Velopack 版本都用这个号，不必先改 props
.\scripts\release\pack-all.ps1 -Version 0.1.3
```

产物目录：`artifacts/release/<version>/`

典型文件：

- Velopack Setup / nupkg / releases.*.json（Latte）
- `BiSheng.Server-<version>-linux-x64.zip`
- `SHA256SUMS.txt`

## CI 发版

工作流：`.github/workflows/release.yml`（先测试，再打包并 `gh release create --generate-notes`）。  
Release 说明可事后在 GitHub 上 Edit。

也可在 Actions 里用 `workflow_dispatch` 填入版本号（不带 `v`）。

## 客户端 / 服务端更新

- Latte：已安装版在「关于」中检查更新（GitHub Releases / Velopack）。
- Server：Release 中的 zip + [`../server-upgrade/README.md`](../server-upgrade/README.md) / 部署文档 §9.2。
