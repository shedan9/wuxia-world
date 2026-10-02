# 开发工具

| 目录 | 用途 | 建设阶段 |
|---|---|---|
| `scripts/` | 本地构建与导出脚本；CI 使用同一入口 | M0 |
| `ContentCompiler/` | 语义校验并生成 `game/generated/content`；M1 编译战斗内容，M2 起加入地图、路线、事件、任务、对白、物品目录与人物，Schema 待建 | M1 起 |
| `BattleSimulator/` | 固定种子批量战斗模拟（场景 × 流派 × 策略） | M1 |
| `VoiceBuilder/` | AI 配音批量生成、审核状态与清单 | M1–M3 |
| `ArtGen/` | 本地 SDXL 美术生成、任务文件与生成记录；环境复现见其 README | M0 起 |
| `MusicGen/` | 本地 ACE-Step 1.5 配乐生成、任务文件与试听页；环境见其 README | M1 起 |

Windows 导出：`$env:GODOT_BIN = '<Godot .NET 编辑器路径>'; ./tools/scripts/export-windows.ps1`（先运行内容编译器，再 `dotnet build` 与 Godot 导出）

内容包：`dotnet run --project tools/ContentCompiler`（`--check` 只校验不写文件），写出 `combat.json` 与 `world.json`。内容包不入库，在编辑器里运行战斗原型或对话展示页前也要先生成一次。

战斗模拟：`dotnet run --project tools/BattleSimulator -- --runs 500 --csv build/sim/result.csv`（`--scenario <遭遇 ID>` 只跑一个场景、`--seed` 起始种子）。场景与挑战定义在 `BattleSimulator/Scenarios.cs`，流派策略在 `Policies.cs`；结果只说明趋势，弱策略失败不等于流派无用（开发计划 6.2）。

页面截图（交付截图与布局自查）：`& $env:GODOT_BIN --path game -- --scene=res://scenes/preview/Inventory.tscn --tab=2 --capture=shop.png --size=1920x1080`（游戏默认全屏，截图时切回窗口并按 `--size` 设尺寸，缺省 1920x1080；引擎参数 `--resolution` 会被全屏设置盖过）；导出包同样支持，把 `--path game` 换成直接运行 `build/windows/WuxiaWorld.exe`。

游戏流程走查（M2，开发用，规格见架构文档 9.4）：`& $env:GODOT_BIN --path game -- --newgame --autoplay=40 --capture=end.png`（看标准输出建议用同目录的 `_console.exe`）。`--newgame` 启动即开新游戏；`--continue` 读最近一份存档；`--autoplay=N` 让探索页按主线目标指向自动交互 N 步（对话取第一个可选项、剧情战按贪心评分打完并确认结算，主线完成即停），逐步打印 `[autoplay]` 记录与终局摘要（地图、任务、队伍、银两、经验、线索、关键事实）；`--side` 先做失踪渡工支线；`--lose-first` 第一场剧情战按战败暂退处理；`--hold=battle|choice|journal|menu|saves|travel` 走满 N 步后停在下一场剧情战第 2 轮、下一个对话选项、札记、菜单、存档槽或乘船面板上截图。`--check-staging` 逐张地图核对摆放（落点能站人、每个交互点在交互距离内走得到、摆放表不缺位置），有问题退出码为 3。走查截图放 `build/review/m2/`（不入库）。

AI 配音试听：`python tools/VoiceBuilder/sample.py [--per-speaker N | --lines <line_id>...] [--dry-run]`，按 `voice_source/profiles/minimax_trial.json` 调 MiniMax 国内站生成小样到 `build/voice/samples/<档案名>/`（含 `index.html` 试听页）；密钥放在 `voice_source/secrets/minimax.env`，不入库。`--dry-run` 只列句子与字数，不计费。音色设计试听：`python tools/VoiceBuilder/design.py [--profile voice_source/profiles/minimax_design_v1.json]`（只取试听，不合成，不扣音色费；装了 `tools/VoiceBuilder/requirements.txt` 时自动检查试听音高与档案性别，不符重设）；多轮对比页：`python tools/VoiceBuilder/compare.py samples/<档案> design/<档案> ... [--speakers <人物ID>...] [-o 文件名]` → `build/voice/compare.html`。档案 `voices` 的键写成 `人物ID@候选名` 时，同一人物的多个候选会并排生成。

对白阅读版：`python tools/scripts/export-dialogue.py [content/dialogue/arcXX/chapterXX.json]`，把一章对白导出为 `build/review/dialogue/arcXX-chapterXX.md`，供审阅（生成物，不入库；意见按 `line_id` 落回 JSON）。

M0 交付截图：`./tools/scripts/capture-review.ps1 -Resolution 1920x1080,2560x1440`（先导出；`-Only 2` 只截编号以 2 开头的镜头），用导出包逐页截取 48 个页面状态到 `build/review/m0/<分辨率>/`；镜头清单与 [M0 交付核对](../docs/playtests/M0_REVIEW.md) 对应。
