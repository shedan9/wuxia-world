<#
.SYNOPSIS
  用导出包按多种输出分辨率与画面比例截取游戏内画面，并在每种尺寸下跑焦点与版式走查，写到 build/review/m3/<分辨率>/。
.DESCRIPTION
  M3 验收“同一场景在 1080p、1440p、4K 输出下检查清晰度、文字、遮挡和安全区，记录截图”与开发计划 6.1 T10“窗口比例变化”。
  先运行 export-windows.ps1 生成 build/windows/WuxiaWorld.exe；本脚本逐条启动导出包，以 --newgame --autoplay=N 走到指定位置，
  可再注入按键（--keys）打开菜单，截图后退出（见 game/scripts/presentation/App/DevCapture.cs）。存档写到临时目录，不碰玩家存档。
  缺省尺寸：1920x1080、2560x1440、3840x2160（16:9）与 1920x1200（16:10）、2560x1080（21:9）。
  每种尺寸另跑一次 --hold=focus（暂停页与分区菜单 17 页：焦点可达、无死路、控件不越出画面），结果写 focus.txt。
  镜头清单与 docs/playtests/M3_DISPLAY.md 第 2 节一一对应，增删时两处同步。
.EXAMPLE
  ./tools/scripts/capture-play.ps1 -Resolution 2560x1080 -Only 30
  ./tools/scripts/capture-play.ps1 -TextSize 32
#>
param(
    [string[]]$Resolution = @('1920x1080', '2560x1440', '3840x2160', '1920x1200', '2560x1080'),
    [string]$Exe,
    [string]$Only,
    [int]$TextSize = 0,
    [switch]$NoFocus
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
if (-not $Exe) { $Exe = Join-Path $root 'build/windows/WuxiaWorld.exe' }
if (-not (Test-Path $Exe)) { throw "未找到导出包：$Exe（先运行 tools/scripts/export-windows.ps1）" }

# 编号_名称, 参数。探索画面用真实行走（--walk）：瞬移走查把主角与同行者放在同一点，主角会被同行者挡住。
# 自动走查第 N 步之后停在：1 芦湾河滩、2 芦湾街、3 江南客栈、7 芦湾旧渡、8 主线完成（章终回顾）。
$shots = @(
    @('01_title', @()),
    @('10_shore', @('--newgame', '--walk', '--autoplay=1')),
    @('11_street', @('--newgame', '--walk', '--autoplay=2')),
    @('12_inn', @('--newgame', '--walk', '--autoplay=3')),
    @('13_old_ferry', @('--newgame', '--walk', '--autoplay=7')),
    @('20_dialogue_choice', @('--newgame', '--autoplay=2', '--hold=choice')),
    @('30_battle', @('--newgame', '--autoplay=8', '--hold=battle')),
    @('40_character', @('--newgame', '--autoplay=3', '--keys=C')),
    @('41_party', @('--newgame', '--autoplay=3', '--keys=P')),
    @('42_inventory', @('--newgame', '--autoplay=3', '--keys=I')),
    @('43_journal', @('--newgame', '--autoplay=3', '--keys=J')),
    @('44_pause', @('--newgame', '--autoplay=3', '--keys=Escape')),
    @('45_world_map', @('--newgame', '--autoplay=3', '--keys=M')),
    @('50_chapter_end', @('--newgame', '--autoplay=8'))
)

$suffix = if ($TextSize -gt 0) { "-text$TextSize" } else { '' }
$common = if ($TextSize -gt 0) { @("--text-size=$TextSize") } else { @() }
$saves = Join-Path ([IO.Path]::GetTempPath()) "wuxia-capture-play-$PID"
$failed = @()
foreach ($res in $Resolution) {
    $dir = Join-Path $root "build/review/m3/$res$suffix"
    New-Item -ItemType Directory -Force $dir | Out-Null
    foreach ($shot in $shots) {
        $name, $extra = $shot
        if ($Only -and $name -notlike "$Only*") { continue }
        $out = Join-Path $dir "$name.png"
        Remove-Item $out -ErrorAction SilentlyContinue
        $slot = Join-Path $saves "$res-$name"
        $argList = @('--', "--saves=`"$slot`"", "--capture=`"$out`"", "--size=$res") + $common + $extra
        $p = Start-Process -FilePath $Exe -ArgumentList $argList -Wait -PassThru -WindowStyle Hidden
        if ($p.ExitCode -ne 0 -or -not (Test-Path $out)) {
            $failed += "$res/$name"
            Write-Warning "截图失败：$res/$name（退出码 $($p.ExitCode)）"
        } else {
            Write-Host "$res$suffix/$name"
        }
    }

    if (-not $NoFocus -and -not $Only) {
        $log = Join-Path $dir 'focus.txt'
        $slot = Join-Path $saves "$res-focus"
        $argList = @('--', "--saves=`"$slot`"", '--newgame', '--autoplay=3', '--hold=focus', "--capture=`"$(Join-Path $slot 'unused.png')`"", "--size=$res") + $common
        $p = Start-Process -FilePath $Exe -ArgumentList $argList -Wait -PassThru -WindowStyle Hidden -RedirectStandardOutput $log
        $summary = Select-String -Path $log -Pattern '\[focus\] (完成|问题)' | ForEach-Object { $_.Line }
        $summary | ForEach-Object { Write-Host "  $res$suffix $_" }
        if ($p.ExitCode -ne 0) { $failed += "$res/focus" }
    }
}

Remove-Item $saves -Recurse -Force -ErrorAction SilentlyContinue
if ($failed.Count -gt 0) { throw "共 $($failed.Count) 项失败：$($failed -join ', ')" }
Write-Host "截图完成：$(Join-Path $root 'build/review/m3')"
