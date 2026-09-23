<#
.SYNOPSIS
  构建 C# 并导出 Windows x64 展示包到 build/windows/。
.DESCRIPTION
  需要 Godot 4.7.2 .NET 编辑器（同版导出模板已安装）与 .NET 10 SDK。
  通过 GODOT_BIN 环境变量或 -Godot 参数指定编辑器可执行文件。
#>
param(
    [string]$Godot = $env:GODOT_BIN,
    [ValidateSet('release', 'debug')][string]$Mode = 'release'
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)

if (-not $Godot -or -not (Test-Path $Godot)) {
    throw '未找到 Godot 编辑器：请设置 GODOT_BIN 或传入 -Godot。'
}

dotnet build (Join-Path $root 'WuxiaWorld.sln') -c Debug
if ($LASTEXITCODE -ne 0) { throw 'dotnet build 失败' }

New-Item -ItemType Directory -Force (Join-Path $root 'build/windows') | Out-Null
& $Godot --headless --path (Join-Path $root 'game') "--export-$Mode" 'Windows Desktop'
if ($LASTEXITCODE -ne 0) { throw 'Godot 导出失败' }

# OFL 等许可要求随发行包附带许可文本。
$licenses = Join-Path $root 'build/windows/licenses'
New-Item -ItemType Directory -Force $licenses | Out-Null
Copy-Item (Join-Path $root 'game/assets/fonts/*.txt') $licenses -Force
Copy-Item (Join-Path $root 'docs/art/ASSET_LEDGER.md') $licenses -Force

Write-Host "已导出：$(Join-Path $root 'build/windows/WuxiaWorld.exe')"
