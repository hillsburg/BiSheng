#Requires -Version 5.1
# 兼容入口：转发到 scripts/release/pack-latte.ps1
$ErrorActionPreference = "Stop"
& (Join-Path $PSScriptRoot "..\release\pack-latte.ps1") @args
