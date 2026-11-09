#Requires -Version 5.1
<#
.SYNOPSIS
  打包 Server（framework-dependent publish + zip）。
#>
param(
    [string] $Version = "",
    [string] $Configuration = "Release",
    [string] $Runtime = "linux-x64",
    [string] $OutputDir = ""
)

$ErrorActionPreference = "Stop"
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..")
Set-Location $repoRoot

if ([string]::IsNullOrWhiteSpace($Version)) {
    $Version = & (Join-Path $PSScriptRoot "Get-BiShengVersion.ps1") -RepoRoot $repoRoot
}

# AssemblyVersion 写成 $(BiShengVersion).0，只能是数字三段式
if ($Version -notmatch '^\d+\.\d+\.\d+$') {
    throw "版本号须为 x.y.z（当前: $Version）。不能带 -beta 等后缀。"
}

if ([string]::IsNullOrWhiteSpace($OutputDir)) {
    $OutputDir = Join-Path $repoRoot "artifacts\release\$Version"
}

$csproj = "src\BiSheng.Server\BiSheng.Server.csproj"
$publishDir = Join-Path $repoRoot "artifacts\server-publish\$Version\$Runtime"
$zipName = "BiSheng.Server-$Version-$Runtime.zip"
$zipPath = Join-Path $OutputDir $zipName

if (Test-Path $publishDir) {
    Remove-Item $publishDir -Recurse -Force
}

New-Item -ItemType Directory -Path $publishDir -Force | Out-Null
New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null

Write-Host "==> publish Server $Version ($Runtime)"
# 与 Latte 相同：用全局 BiShengVersion 覆盖 props，避免文件版本停在旧号
dotnet publish $csproj `
    -c $Configuration `
    -r $Runtime `
    --self-contained false `
    -p:BiShengVersion=$Version `
    -p:Version=$Version `
    -o $publishDir
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish Server 失败"
}

if (Test-Path $zipPath) {
    Remove-Item $zipPath -Force
}

Write-Host "==> zip $zipName"
Compress-Archive -Path (Join-Path $publishDir "*") -DestinationPath $zipPath -Force
Write-Host "Server 打包完成: $zipPath"
