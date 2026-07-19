# BiSheng 统一发版（Latte + Server）

Latte 与 Server **共用** `Directory.Build.props` 中的 `BiShengVersion`。一次 GitHub Release（tag `v*`）包含两端产物。

## 发版检查清单（必读）

推 tag 发版时，**两处版本号必须一致**，否则容易半年后对不上：

| # | 改什么 | 示例 |
|---|--------|------|
| 1 | 根目录 [`Directory.Build.props`](../../Directory.Build.props) 里的 **`BiShengVersion`**，并提交到要发版的 commit（通常先合入 `main`） | `0.1.2` |
| 2 | 在该 commit 上打 **同号** tag 并推送 | `v0.1.2`（必须带 `v` 前缀） |

```text
Directory.Build.props  BiShengVersion = 0.1.2
git tag / GitHub Release              = v0.1.2
```

推荐流程：

```bash
# 1. 改版本并合入 main（PR 或直接提交）
#    编辑 Directory.Build.props → <BiShengVersion>0.1.2</BiShengVersion>

# 2. 在已包含该改动的 commit 上打 tag
git checkout main && git pull
git tag v0.1.2
git push origin v0.1.2
```

说明：

- CI（`release.yml`）会用 **tag 去掉 `v` 后的版本** 调用 `pack-all.ps1 -Version …`，安装包/程序集版本以 tag 为准。
- **`BiShengVersion` 仍必须改**：本地默认打包、未传 `-Version` 时读 props；仓库里也应留下「当前版本」的单一真相，避免下次发版搞混。
- **不要**只推 tag 不改 props，也不要 props 已是 `0.1.2` 却打成 `v0.1.1`。
- 若 tag 打错：删远程 tag 后改对再推（注意已产生的 Release / 用户是否已装该版）。

`workflow_dispatch` 手工填版本时同样建议先改 props 再跑，避免仓库与产物长期不一致。

## 本地打包

前置：.NET 8 SDK；`dotnet tool install -g vpk`（与 NuGet `Velopack` 1.2.x 对齐）。

```powershell
# 默认读取 Directory.Build.props 的 BiShengVersion
.\scripts\release\pack-all.ps1

# 或显式指定（仍建议先改 props 再打包，避免仓库漂移）
.\scripts\release\pack-all.ps1 -Version 0.1.2
```

产物目录：`artifacts/release/<version>/`

典型文件：

- Velopack Setup / nupkg / releases.*.json（Latte）
- `BiSheng.Server-<version>-linux-x64.zip`
- `SHA256SUMS.txt`

## CI 发版

见上文检查清单。工作流：`.github/workflows/release.yml`（先测试，再打包并 `gh release create --generate-notes`）。  
Release 说明可事后在 GitHub 上 Edit。

也可在 Actions 里用 `workflow_dispatch` 填入版本号（不带 `v`）。

## 客户端 / 服务端更新

- Latte：已安装版在「关于」中检查更新（GitHub Releases / Velopack）。
- Server：Release 中的 zip + [`../server-upgrade/README.md`](../server-upgrade/README.md) / 部署文档 §9.2。
