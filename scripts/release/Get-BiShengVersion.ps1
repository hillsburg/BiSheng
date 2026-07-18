#Requires -Version 5.1
<#
.SYNOPSIS
  从 Directory.Build.props 读取 BiShengVersion。
#>
param(
    [string] $RepoRoot = ""
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($RepoRoot)) {
    $RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
}

$propsPath = Join-Path $RepoRoot "Directory.Build.props"
if (-not (Test-Path $propsPath)) {
    throw "未找到 Directory.Build.props"
}

[xml] $xml = Get-Content -Raw -Path $propsPath
$candidates = @(
    $xml.Project.PropertyGroup |
        ForEach-Object { $_.BiShengVersion } |
        Where-Object { -not [string]::IsNullOrWhiteSpace("$_") }
)

if ($candidates.Count -eq 0) {
    throw "Directory.Build.props 中缺少 BiShengVersion"
}

$raw = $candidates[0]
if ($raw -is [System.Xml.XmlElement]) {
    return $raw.InnerText.Trim()
}

return "$raw".Trim()
