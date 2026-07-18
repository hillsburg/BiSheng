#Requires -Version 5.1
<#
.SYNOPSIS
  一次打出 Latte（Velopack）+ Server（zip），并生成 SHA256SUMS.txt。

.EXAMPLE
  .\scripts\release\pack-all.ps1
  .\scripts\release\pack-all.ps1 -Version 0.1.1
#>
param(
    [string] $Version = "",
    [string] $Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..")
Set-Location $repoRoot

if ([string]::IsNullOrWhiteSpace($Version)) {
    $Version = & (Join-Path $PSScriptRoot "Get-BiShengVersion.ps1") -RepoRoot $repoRoot
}

$outputDir = Join-Path $repoRoot "artifacts\release\$Version"
if (Test-Path $outputDir) {
    Remove-Item $outputDir -Recurse -Force
}

New-Item -ItemType Directory -Path $outputDir -Force | Out-Null

& (Join-Path $PSScriptRoot "pack-latte.ps1") -Version $Version -Configuration $Configuration -OutputDir $outputDir
& (Join-Path $PSScriptRoot "pack-server.ps1") -Version $Version -Configuration $Configuration -OutputDir $outputDir

$sumsPath = Join-Path $outputDir "SHA256SUMS.txt"
$lines = @()
Get-ChildItem -Path $outputDir -File |
    Where-Object { $_.Name -ne "SHA256SUMS.txt" } |
    Sort-Object Name |
    ForEach-Object {
        $hash = (Get-FileHash -Path $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        $lines += "$hash  $($_.Name)"
    }

$lines | Set-Content -Path $sumsPath -Encoding utf8
Write-Host ""
Write-Host "全部完成 → $outputDir"
Write-Host "上传到 GitHub Release tag v$Version 即可（含 Latte Setup / nupkg 与 Server zip）。"
