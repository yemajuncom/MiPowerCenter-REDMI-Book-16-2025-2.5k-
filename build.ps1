# MiPowerCenter 一键编译发布脚本
# 用法:  powershell -ExecutionPolicy Bypass -File build.ps1
# 产物:  release\MiPowerCenter\  (自包含、免安装，直接运行 MiPowerCenter.exe)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot

# 1. 优先使用本包自带的 Xiaomi 原生组件 (Components)，保证无需安装小米电脑管家
$xiaomiDir = Join-Path $root "MiPowerCenter\Components"

# 2. 发布
$out = Join-Path $root "release\MiPowerCenter"
Write-Host "Publishing MiPowerCenter -> $out"
dotnet publish (Join-Path $root "MiPowerCenter\MiPowerCenter.csproj") `
    -c Release `
    -p:XiaomiDir="$xiaomiDir" `
    -o $out

Write-Host ""
Write-Host "Done. Run: $out\MiPowerCenter.exe"
