#Requires -Version 5.1
<#
.SYNOPSIS
  打包 Latte（dotnet publish + vpk pack）。
#>
param(
    [string] $Version = "",
    [string] $Configuration = "Release",
    [string] $Runtime = "win-x64",
    [switch] $SelfContained,
    [string] $OutputDir = ""
)

$ErrorActionPreference = "Stop"
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..")
Set-Location $repoRoot

if ([string]::IsNullOrWhiteSpace($Version)) {
    $Version = & (Join-Path $PSScriptRoot "Get-BiShengVersion.ps1") -RepoRoot $repoRoot
}

if ([string]::IsNullOrWhiteSpace($OutputDir)) {
    $OutputDir = Join-Path $repoRoot "artifacts\release\$Version"
}

$packId = "BiSheng.Latte"
$csproj = "src\BiSheng.Latte\BiSheng.Latte.csproj"
$publishDir = Join-Path $repoRoot "artifacts\latte-publish\$Version"

if (Test-Path $publishDir) {
    Remove-Item $publishDir -Recurse -Force
}

New-Item -ItemType Directory -Path $publishDir -Force | Out-Null
New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null

# Windows 下目录名 Releases 与文件名 RELEASES 冲突；清理误建的嵌套目录
Get-ChildItem -Path $OutputDir -Directory -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -ieq "Releases" -or $_.Name -ieq "RELEASES" } |
    ForEach-Object {
        Write-Host "移除会与 RELEASES 文件冲突的目录: $($_.FullName)"
        Remove-Item $_.FullName -Recurse -Force
    }

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
    throw "dotnet publish Latte 失败"
}

$mainExe = Join-Path $publishDir "BiSheng.Latte.exe"
if (-not (Test-Path $mainExe)) {
    throw "未找到主程序: $mainExe"
}

$vpk = Get-Command vpk -ErrorAction SilentlyContinue
if (-not $vpk) {
    throw "未找到 vpk。请先执行: dotnet tool install -g vpk"
}

Write-Host "==> vpk pack → $OutputDir"
# 必须显式 --outputDir，且不要 Push-Location 到产物目录：
# vpk download 默认 -o Releases，在 cwd 下建 Releases\ 后，pack 写 RELEASES 文件会 Access Denied。
$repoUrl = "https://github.com/hillsburg/BiSheng"
& vpk download github --repoUrl $repoUrl --outputDir $OutputDir
if ($LASTEXITCODE -ne 0) {
    Write-Host "（无历史 Velopack 包或下载失败，将打全量包）"
}

& vpk pack `
    --packId $packId `
    --packVersion $Version `
    --packDir $publishDir `
    --mainExe "BiSheng.Latte.exe" `
    --outputDir $OutputDir
if ($LASTEXITCODE -ne 0) {
    throw "vpk pack 失败"
}

Write-Host "Latte 打包完成: $OutputDir"
