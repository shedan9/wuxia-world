# 开发工具

| 目录 | 用途 | 建设阶段 |
|---|---|---|
| `scripts/` | 本地构建与导出脚本；CI 使用同一入口 | M0 |
| `ContentCompiler/` | 语义校验并生成 `game/generated/content`；M1 编译战斗内容，M2 起加入地图、路线、事件、任务、对白、物品目录与人物，Schema 待建 | M1 起 |
| `BattleSimulator/` | 固定种子批量战斗模拟（场景 × 流派 × 策略） | M1 |
| `SaveCrashTest/` | 人为中断写档测试：随机强杀写档进程后核对能读回最后确认的存档 | M2 |
| `VoiceBuilder/` | AI 配音批量生成、审核状态与清单 | M1–M3 |
| `ArtGen/` | 本地 SDXL 美术生成、任务文件与生成记录；环境复现见其 README | M0 起 |
| `MusicGen/` | 本地 ACE-Step 1.5 配乐生成、任务文件与试听页；环境见其 README | M1 起 |
| `AudioBuild/` | 运行时音频：BGM 母带剪循环、统一响度导出 Ogg（`bgm_runtime.py`），Freesound CC0 实录检索下载（`freesound.py`）、按配方剪辑环境声与音效（`sfx_build.py` + `sfx_recipes.json`）、CLAP 零样本听辨验收（`sfx_check.py`），BS.1770 响度测量（`loudness.py`） | M2 起 |

Windows 导出：`$env:GODOT_BIN = '<Godot .NET 编辑器路径>'; ./tools/scripts/export-windows.ps1`（先运行内容编译器，再 `dotnet build` 与 Godot 导出）

内容包：`dotnet run --project tools/ContentCompiler`（`--check` 只校验不写文件），写出 `combat.json` 与 `world.json`。内容包不入库，在编辑器里运行战斗原型或对话展示页前也要先生成一次。

战斗模拟：`dotnet run --project tools/BattleSimulator -- --runs 500 --csv build/sim/result.csv`（`--scenario <遭遇 ID>` 只跑一个场景、`--seed` 起始种子；成长档 `--hero-level N`、`--no-weapon`、`--unallocated` 把主角换成按游戏内成长规则换算的模板：指定等级、潜能按预设比例分完或不分、穿开局衣物、可去掉流派兵器；`--party linghu_chong,xiao_feng` 让经典人物按角色模板依次站前排右、前排左，取代场景里的占位同行者；`--variants variant.ch01.sluice_jammed` 开局套用遭遇变体，场景没有该变体时忽略）。场景与挑战定义在 `BattleSimulator/Scenarios.cs`，流派策略在 `Policies.cs`；结果只说明趋势，弱策略失败不等于流派无用（开发计划 6.2）。

页面截图（交付截图与布局自查）：`& $env:GODOT_BIN --path game -- --scene=res://scenes/preview/Inventory.tscn --tab=2 --capture=shop.png --size=1920x1080`（游戏默认全屏，截图时切回窗口并按 `--size` 设尺寸，缺省 1920x1080；引擎参数 `--resolution` 会被全屏设置盖过）；导出包同样支持，把 `--path game` 换成直接运行 `build/windows/WuxiaWorld.exe`。

游戏流程走查（M2，开发用，规格见架构文档 9.4）：`& $env:GODOT_BIN --path game -- --newgame --autoplay=40 --capture=end.png`（看标准输出建议用同目录的 `_console.exe`）。`--newgame` 启动即开新游戏；`--continue` 读最近一份存档；`--autoplay=N` 让探索页按主线目标指向自动交互 N 步（对话取第一个可选项、剧情战按贪心评分打完并确认结算，主线完成即停），逐步打印 `[autoplay]` 记录与终局摘要（地图、任务、队伍、银两、经验、线索、关键事实）；`--side` 先做失踪渡工支线；`--companion=linghu|huang|xiao` 在讨教与同行选择中选那位侠客（缺省取第一个选项，即令狐冲），剧情战开打时打印上场模板与生效的遭遇变体；`--lose-first` 第一场剧情战按战败暂退处理；`--hold=battle|choice|journal|menu|saves|travel|worldmap|character|martial|equipment|inventory|party|shop` 走满 N 步后停在下一场剧情战第 2 轮、下一个对话选项、札记、菜单、存档槽、选中路线目的地的大地图（`travel`，路线未开放时不打开）、所在之处的大地图（`worldmap`）、人物页三栏（属性 / 武学 / 装备）、行囊页、队伍页或店铺面板上截图（停在 `character` 时不自动分配潜能，好看到加点界面）。自动走查有未分配潜能时按流派推荐分配。`--click=x,y;x,y` 在截图前依次注入鼠标左键点击（逻辑画布坐标，按 1920×1080），核对点击交互。`--keys=Escape,Down,Enter` 在截图前依次注入按键（Godot 键名，每键后等 20 帧），核对按键路径，例如 `--autoplay=3 --hold=choice --keys=Escape` 截对话中按 Esc 打开的暂停菜单。`--click` 与 `--keys` 可各给多次，按命令行先后交替执行，例如 `--autoplay=6 --hold=shop --click=490,668;970,733 --keys=Escape,C --click=1700,262` 买下铁剑、关店铺、开人物页并切到装备栏。`--walk` 配合 `--autoplay` 改为真实行走：用与鼠标点地同一套寻路走到目标、注入真实 E / Enter / 数字键（码头打开大地图后注入 Enter 启程），走不到、卡住或被别的交互点抢先高亮都打印 `[walk] 问题`（有问题时退出码 4）。`--dev` 打开开发信息（台词编号、借景说明、战斗种子；游戏中 F12 切换）。`--audio-meter` 每 3 秒打印各音频总线峰值。`--check-staging` 逐张地图核对摆放（落点能站人、每个交互点在交互距离内走得到、摆放表不缺位置），有问题退出码为 3。`--soak=N` 耐久走查：在各地图间换图 N 次（至少 32 次才能比较，验收用 100），每到一图核对交互点、站位人物与世界状态和上次一致（一次性交互不刷新），注入按键开关札记、人物、行囊、队伍、大地图、暂停菜单并快速存档，打印节点数、内存、工作集与常驻信号连接，结尾判断有无持续增长，有问题退出码为 5；接在 `--autoplay=8 --walk --side` 之后可从章中状态开始。`--saves=目录` 把存档读写改到指定目录（走查会写自动存档，测存档界面会删档，都应指向临时目录，不碰玩家真实存档 `%APPDATA%\Godot\app_userdata\武侠世界\saves`）。走查截图放 `build/review/m2/`（不入库）。

人为中断写档：`dotnet run --project tools/SaveCrashTest -- [--rounds 200] [--size 16] [--min-ms 40] [--max-ms 600] [--seed N] [--dir build/savecrash] [--verbose]`，子进程用游戏的存档代码对同一槽位连续写档，父进程随机时刻强杀后读回，要求读得出、校验通过、序号不小于最后确认写成的一份；`--size` 为填充事实的大致千字节数，加大可放宽写入窗口。任一轮失败退出码为 1。只测进程中断，不代替断电测试。

AI 配音试听：`python tools/VoiceBuilder/sample.py [--per-speaker N | --lines <line_id>...] [--dry-run]`，按 `voice_source/profiles/minimax_trial.json` 调 MiniMax 国内站生成小样到 `build/voice/samples/<档案名>/`（含 `index.html` 试听页）；密钥放在 `voice_source/secrets/minimax.env`，不入库。`--dry-run` 只列句子与字数，不计费。音色设计试听：`python tools/VoiceBuilder/design.py [--profile voice_source/profiles/minimax_design_v1.json]`（只取试听，不合成，不扣音色费；装了 `tools/VoiceBuilder/requirements.txt` 时自动检查试听音高与档案性别，不符重设）；多轮对比页：`python tools/VoiceBuilder/compare.py samples/<档案> design/<档案> ... [--speakers <人物ID>...] [-o 文件名]` → `build/voice/compare.html`。档案 `voices` 的键写成 `人物ID@候选名` 时，同一人物的多个候选会并排生成。

配音装入游戏：`python tools/VoiceBuilder/install.py [--samples build/voice/samples/minimax_cast] [--status trial|final]`，把一组已生成配音中文字与当前台词一致的句子复制到 `game/assets/audio/voice/<line_id>.mp3`（Git LFS）并写 `voice_manifest.json`；文字已改的句子列为过期不安装，不再需要的旧文件删除。装完用 Godot 编辑器打开工程或运行 `& $env:GODOT_BIN --headless --path game --import` 生成导入文件。不调用接口、不计费。

对白阅读版：`python tools/scripts/export-dialogue.py [content/dialogue/arcXX/chapterXX.json]`，把一章对白导出为 `build/review/dialogue/arcXX-chapterXX.md`，供审阅（生成物，不入库；意见按 `line_id` 落回 JSON）。

M0 交付截图：`./tools/scripts/capture-review.ps1 -Resolution 1920x1080,2560x1440`（先导出；`-Only 2` 只截编号以 2 开头的镜头），用导出包逐页截取 48 个页面状态到 `build/review/m0/<分辨率>/`；镜头清单与 [M0 交付核对](../docs/playtests/M0_REVIEW.md) 对应。

运行时音频（在仓库根目录，用 ACE-Step 的虚拟环境，需 numpy / scipy / soundfile）：`C:/code/ACE-Step-1.5/.venv/Scripts/python tools/AudioBuild/bgm_runtime.py [--only <曲目>] [--target -18]` 把 `audio_source/music/*.wav` 做成循环 Ogg 写到 `game/assets/audio/music/`（循环起点与接缝相似度写 `music_manifest.json`）；环境声与音效：`… tools/AudioBuild/freesound.py search "<英文关键词>" [--max-dur 秒]` 只列 CC0 结果（ID、时长、下载数、评分、作者），`… freesound.py fetch <ID> …` 到详情页复核 CC0 后下载高质量试听版到 `audio_source/sfx/freesound/`、来源写 `sources.json`（不需要账号）；改 `tools/AudioBuild/sfx_recipes.json` 后 `… sfx_build.py [--only <前缀>]` 剪辑到 `game/assets/audio/amb/` 与 `game/assets/audio/sfx/`（写 `sfx_manifest.json`，旧变体文件自动清掉，重跑逐采样一致）；`… sfx_check.py built` 用 CLAP 逐个听辨成品（期望描述排第一为符合，脚步连走判、环境声逐窗扫人声 / 音乐 / 交通），结果写回清单，`… sfx_check.py label|scan <文件>` 用于挑候选。CLAP 模型 `laion/larger_clap_general`（Apache-2.0）放在 `C:/code/models/larger_clap_general`（环境变量 `CLAP_MODEL` 可改），HF 直连下载易断，用 `curl -C -` 续传 `pytorch_model.bin`。`sfx_build.py` 的 `level` 只定成品峰值，游戏内音量在调用处另设。libsndfile 一次写入几百万帧 Ogg 会直接崩溃退出（退出码 127、无报错），脚本已改为分块写。生成后用 `& $env:GODOT_BIN --headless --path game --import` 导入。
