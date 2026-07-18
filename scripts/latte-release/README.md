# Latte Velopack 发版

## 前置

- .NET 8 SDK
- `dotnet tool install -g vpk`（版本尽量与 `BiSheng.Latte` 引用的 Velopack NuGet 一致，当前为 1.2.x）

## 打包

在仓库根目录：

```powershell
.\scripts\latte-release\pack-velopack.ps1
```

版本默认取根目录 `Directory.Build.props` 的 `BiShengLatteVersion`。

产物目录：`artifacts/latte-releases/<version>/`。

## 发布

1. 修改 `BiShengLatteVersion`（或 `-Version`）
2. 运行打包脚本
3. 在 [hillsburg/BiSheng](https://github.com/hillsburg/BiSheng) 创建 Release，上传 Setup / nupkg 等 Velopack 产物
4. 已安装用户可在 Latte「关于」中「检查更新」

## 说明

- 应用内更新仅支持 **Velopack 安装版**；`dotnet run` / 绿色解压 publish 目录会提示不支持应用内更新。
- 用户数据在 `%LocalAppData%\BiSheng\Latte\`，与安装目录分离。
