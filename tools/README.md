# 开发工具

| 目录 | 用途 | 建设阶段 |
|---|---|---|
| `scripts/` | 本地构建与导出脚本；CI 使用同一入口 | M0 |
| `ContentCompiler/` | 语义校验并生成 `game/generated/content`；M1 编译战斗内容，M2–M4 扩到地图、任务、对白与 Schema | M1 起 |
| `BattleSimulator/` | 固定种子批量战斗模拟（场景 × 流派 × 策略） | M1 |
| `VoiceBuilder/` | AI 配音批量生成、审核状态与清单 | M1–M3 |
| `ArtGen/` | 本地 SDXL 美术生成、任务文件与生成记录；环境复现见其 README | M0 起 |

Windows 导出：`$env:GODOT_BIN = '<Godot .NET 编辑器路径>'; ./tools/scripts/export-windows.ps1`（先运行内容编译器，再 `dotnet build` 与 Godot 导出）

内容包：`dotnet run --project tools/ContentCompiler`（`--check` 只校验不写文件）。内容包不入库，在编辑器里运行战斗原型前也要先生成一次。

战斗模拟：`dotnet run --project tools/BattleSimulator -- --runs 500 --csv build/sim/result.csv`（`--scenario <遭遇 ID>` 只跑一个场景、`--seed` 起始种子）。场景与挑战定义在 `BattleSimulator/Scenarios.cs`，流派策略在 `Policies.cs`；结果只说明趋势，弱策略失败不等于流派无用（开发计划 6.2）。

页面截图（交付截图与布局自查）：`& $env:GODOT_BIN --path game -- --scene=res://scenes/preview/Inventory.tscn --tab=2 --capture=shop.png --size=1920x1080`（游戏默认全屏，截图时切回窗口并按 `--size` 设尺寸，缺省 1920x1080；引擎参数 `--resolution` 会被全屏设置盖过）；导出包同样支持，把 `--path game` 换成直接运行 `build/windows/WuxiaWorld.exe`。

M0 交付截图：`./tools/scripts/capture-review.ps1 -Resolution 1920x1080,2560x1440`（先导出；`-Only 2` 只截编号以 2 开头的镜头），用导出包逐页截取 48 个页面状态到 `build/review/m0/<分辨率>/`；镜头清单与 [M0 交付核对](../docs/playtests/M0_REVIEW.md) 对应。
