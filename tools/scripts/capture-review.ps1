<#
.SYNOPSIS
  用导出包批量截取 M0 交付核对截图，写到 build/review/m0/<分辨率>/。
.DESCRIPTION
  先运行 export-windows.ps1 生成 build/windows/WuxiaWorld.exe；本脚本逐条启动导出包，
  以 --scene / --tab / --capture 进入指定页面状态、截图后自动退出（见 game/scripts/presentation/App/DevCapture.cs）。
  镜头清单与 docs/playtests/M0_REVIEW.md 第 3 节一一对应，增删页面状态时两处同步。
.EXAMPLE
  ./tools/scripts/capture-review.ps1 -Resolution 1920x1080,2560x1440
#>
param(
    [string[]]$Resolution = @('1920x1080'),
    [string]$Exe,
    [string]$Only
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
if (-not $Exe) { $Exe = Join-Path $root 'build/windows/WuxiaWorld.exe' }
if (-not (Test-Path $Exe)) { throw "未找到导出包：$Exe（先运行 tools/scripts/export-windows.ps1）" }

# 编号_名称, 场景, tab, 附加参数
$shots = @(
    @('00_catalog', 'PreviewCatalog', 0, @()),
    @('01_title_press', 'MainMenu', 1, @()),
    @('02_title_menu', 'MainMenu', 0, @()),
    @('03_title_saves', 'MainMenu', 2, @()),
    @('10_map_ride', 'WorldMap', 0, @()),
    @('11_map_locked', 'WorldMap', 1, @()),
    @('12_map_ferry', 'WorldMap', 2, @()),
    @('13_map_zoom', 'WorldMap', 3, @()),
    @('14_map_travel', 'WorldMap', 4, @('--motion', '--settle=150')),
    @('15_map_overview', 'WorldMap', 5, @()),
    @('20_town_stone', 'ExploreTown', 0, @()),
    @('21_town_occlude', 'ExploreTown', 1, @()),
    @('22_town_inn_door', 'ExploreTown', 2, @()),
    @('23_town_corridor', 'ExploreTown', 3, @()),
    @('24_town_bridge', 'ExploreTown', 4, @()),
    @('25_town_west', 'ExploreTown', 5, @()),
    @('30_inn_entry', 'ExploreInn', 0, @()),
    @('31_inn_screen', 'ExploreInn', 1, @()),
    @('32_inn_counter', 'ExploreInn', 2, @()),
    @('33_inn_stairs', 'ExploreInn', 3, @()),
    @('34_inn_overview', 'ExploreInn', 4, @()),
    @('40_wild_foot', 'ExploreWild', 0, @()),
    @('41_wild_bridge', 'ExploreWild', 1, @()),
    @('42_wild_pavilion', 'ExploreWild', 2, @()),
    @('43_wild_sign', 'ExploreWild', 3, @()),
    @('44_wild_overview', 'ExploreWild', 4, @()),
    @('45_wild_steps', 'ExploreWild', 5, @()),
    @('50_hud', 'ExploreHud', 0, @()),
    @('51_hud_minimal', 'ExploreHud', 1, @()),
    @('60_dialogue_line', 'Dialogue', 0, @()),
    @('61_dialogue_choice', 'Dialogue', 1, @()),
    @('62_dialogue_log', 'Dialogue', 2, @()),
    @('70_battle_command', 'Battle', 0, @()),
    @('71_battle_result', 'Battle', 1, @()),
    @('80_character_attr', 'Character', 0, @()),
    @('81_character_skill', 'Character', 1, @()),
    @('82_character_growth', 'Character', 2, @()),
    @('83_inventory_bag', 'Inventory', 0, @()),
    @('84_inventory_equip', 'Inventory', 1, @()),
    @('85_inventory_shop', 'Inventory', 2, @()),
    @('86_journal_quest', 'Journal', 0, @()),
    @('87_journal_bond', 'Journal', 1, @()),
    @('88_journal_note', 'Journal', 2, @()),
    @('90_settings_pause', 'Settings', 0, @()),
    @('91_settings_display', 'Settings', 1, @()),
    @('92_settings_audio', 'Settings', 2, @()),
    @('93_settings_text', 'Settings', 3, @()),
    @('94_settings_assist', 'Settings', 4, @())
)

$failed = @()
foreach ($res in $Resolution) {
    $dir = Join-Path $root "build/review/m0/$res"
    New-Item -ItemType Directory -Force $dir | Out-Null
    foreach ($shot in $shots) {
        $name, $scene, $tab, $extra = $shot
        if ($Only -and $name -notlike "$Only*") { continue }
        $out = Join-Path $dir "$name.png"
        $argList = @('--', "--scene=res://scenes/preview/$scene.tscn", "--tab=$tab", "--capture=`"$out`"", "--size=$res") + $extra
        $p = Start-Process -FilePath $Exe -ArgumentList $argList -Wait -PassThru -WindowStyle Hidden
        if ($p.ExitCode -ne 0 -or -not (Test-Path $out)) {
            $failed += "$res/$name"
            Write-Warning "截图失败：$res/$name（退出码 $($p.ExitCode)）"
        } else {
            Write-Host "$res/$name"
        }
    }
}

if ($failed.Count -gt 0) { throw "共 $($failed.Count) 张截图失败：$($failed -join ', ')" }
Write-Host "截图完成：$(Join-Path $root 'build/review/m0')"
