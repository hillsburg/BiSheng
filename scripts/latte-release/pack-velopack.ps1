#Requires -Version 5.1
<#
.SYNOPSIS
  发布 BiSheng Latte：dotnet publish + vpk pack，产出可上传 GitHub Releases 的安装包。

.DESCRIPTION
  版本号读取 Directory.Build.props 中的 BiShengLatteVersion（可用 -Version 覆盖）。
  需已安装 .NET 8 SDK，并可用：dotnet tool install -g vpk（建议 vpk 与 Velopack NuGet 同主版本）。

.EXAMPLE
  .\scripts\latte-release\pack-velopack.ps1
  .\scripts\latte-release\pack-velopack.ps1 -Version 0.1.1
#>
param(
    [string] $Version = "",
    [string] $Configuration = "Release",
    [string] $Runtime = "win-x64",
    [switch] $SelfContained
)

$ErrorActionPreference = "Stop"
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..")
Set-Location $repoRoot

function Get-LatteVersionFromProps {
    $propsPath = Join-Path $repoRoot "Directory.Build.props"
    if (-not (Test-Path $propsPath)) {
        throw "未找到 Directory.Build.props"
    }

    [xml] $xml = Get-Content -Raw $propsPath
    $node = $xml.Project.PropertyGroup.BiShengLatteVersion | Select-Object -First 1
    if ([string]::IsNullOrWhiteSpace($node)) {
        throw "Directory.Build.props 中缺少 BiShengLatteVersion"
    }

    return $node.Trim()
}

if ([string]::IsNullOrWhiteSpace($Version)) {
    $Version = Get-LatteVersionFromProps
}

$packId = "BiSheng.Latte"
$csproj = "src\BiSheng.Latte\BiSheng.Latte.csproj"
$publishDir = Join-Path $repoRoot "artifacts\latte-publish\$Version"
$releaseDir = Join-Path $repoRoot "artifacts\latte-releases\$Version"

if (Test-Path $publishDir) {
    Remove-Item $publishDir -Recurse -Force
}

if (Test-Path $releaseDir) {
    Remove-Item $releaseDir -Recurse -Force
}

New-Item -ItemType Directory -Path $publishDir -Force | Out-Null
New-Item -ItemType Directory -Path $releaseDir -Force | Out-Null

$selfContainedArgs = @("--self-contained", "false")
if ($SelfContained) {
    $selfContainedArgs = @("--self-contained", "true")
}

Write-Host "==> publish Latte $Version ($Runtime)"
dotnet publish $csproj `
    -c $Configuration `
    -r $Runtime `
    @selfContainedArgs `
    -p:Version=$Version `
    -o $publishDir

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish 失败"
}

$mainExe = Join-Path $publishDir "BiSheng.Latte.exe"
if (-not (Test-Path $mainExe)) {
    throw "未找到主程序: $mainExe"
}

Write-Host "==> vpk pack"
$vpk = Get-Command vpk -ErrorAction SilentlyContinue
if (-not $vpk) {
    throw "未找到 vpk。请先执行: dotnet tool install -g vpk"
}

Push-Location $releaseDir
try {
    & vpk pack `
        --packId $packId `
        --packVersion $Version `
        --packDir $publishDir `
        --mainExe "BiSheng.Latte.exe" `
        --outputDir $releaseDir
    if ($LASTEXITCODE -ne 0) {
        throw "vpk pack 失败"
    }
}
finally {
    Pop-Location
}

Write-Host ""
Write-Host "完成。请将 $releaseDir 中的产物上传到 GitHub Release（tag 建议 v$Version 或 latte-v$Version）。"
Write-Host "客户端更新源: https://github.com/hillsburg/BiSheng/releases"
