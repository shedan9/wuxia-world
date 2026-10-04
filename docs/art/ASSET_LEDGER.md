# 资产台账

记录随包或进入 `art_source/` 的每项外部与生成资产的来源、许可和用途。新增资产须先登记再入库；许可不明的资产不得进入 `game/assets/`。公开发行前仍须复核本台账（AGENTS.md「修改内容时守住的边界」）。

## 取得方式（2026-09-24 用户确定）

| 方式 | 用于 | 许可要求 |
|---|---|---|
| 本地 AI 生成（SDXL 系模型，`tools/ArtGen`） | 人物、场景、道具、图标的草图与成品底稿；人物立绘用 Animagine XL 4.0（CreativeML Open RAIL++-M，模型卡写明允许商用）；布景件用 SDXL 1.0 base（CreativeML Open RAIL++-M）加 ControlNet `xinsir/controlnet-canny-sdxl-1.0`（Apache-2.0，允许商用），引导图取自本作程序化占位件（战斗道具的引导图由 `tools/ArtGen/prop_guide.py` 按任务文件里的色块坐标绘制）；全身人物形象用 Animagine XL 4.0 加 ControlNet `xinsir/controlnet-openpose-sdxl-1.0`（Apache-2.0，允许商用），骨架由 `tools/ArtGen/figure.py` 按坐标程序绘制，不取自他人图像 | 模型许可须允许商用；每张图保留生成记录（模型、种子、提示词、哈希） |
| 博物馆开放获取（CC0） | 构图参考（2026-09-24 画风改为蓝绿清新后，不再直接作远景或肌理） | 仅用 CC0 / 公有领域；记录馆藏号与原始链接 |
| 开源字体 | 正文与标题字体 | SIL OFL 1.1，许可文本随包附带 |

AI 生成图在多数司法辖区可能无法取得著作权保护，别人复制使用时难以主张权利；这不影响本作使用，但发行前须知悉。生成图须人工挑选并修整后使用，不直接把未经检查的输出放入游戏。

## 风格参照图（不入包）

| 文件 | 内容 | 用途限制 |
|---|---|---|
| `docs/art/example/1.png`–`4.png` | 用户提供的第三方国风游戏立绘截图 4 张（2026-09-24）：粗线稿、硬边阴影、多层撞色服装与金饰 | 仅作画风沟通参照；不得用作图生图输入、LoRA 训练素材或直接描摹，不进入 `game/` 与发行包 |

## 字体

| 文件 | 字体 | 版本 | 来源 | 许可 | 用途 |
|---|---|---|---|---|---|
| `game/assets/fonts/SourceHanSansCN-Regular.otf` | 思源黑体 CN Regular | 2.005R | [adobe-fonts/source-han-sans](https://github.com/adobe-fonts/source-han-sans/releases/tag/2.005R) | SIL OFL 1.1（`SourceHanSans-OFL.txt`） | 正文 |
| `game/assets/fonts/SourceHanSansCN-Medium.otf` | 思源黑体 CN Medium | 2.005R | 同上 | 同上 | 正文强调 |
| `game/assets/fonts/LXGWWenKai-Medium.ttf` | 霞鹜文楷 Medium | v1.522 | [lxgw/LxgwWenKai](https://github.com/lxgw/LxgwWenKai/releases/tag/v1.522) | SIL OFL 1.1（`LXGWWenKai-OFL.txt`） | 标题、印章、页签 |

保留字体名：“Source”（Adobe）、“霞鹜”“落霞孤鹜”“LXGW”。本作不修改字体文件；若将来子集化，须改名或符合各自许可的附加条款。

## 博物馆开放获取图像（CC0）

| 馆藏号 | 作品 | 馆藏 | 原图 | 许可 | 用途 | 状态 |
|---|---|---|---|---|---|---|
| 1953.126 | 《溪山无尽图》（Streams and Mountains without End），北宋末至金 | 克利夫兰美术馆 | [馆藏页](https://clevelandart.org/art/1953.126)，28075×4336 TIFF | CC0 | 大地图与山路远景候选 | 下载中，未裁切 |
| 1997.95 | 陈汝言《仙山图》（Mountains of the Immortals），元 | 克利夫兰美术馆 | [馆藏页](https://clevelandart.org/art/1997.95)，17085×4678 TIFF | CC0 | 远山层候选 | 下载中，未裁切 |

原始 TIFF 体积 300–700 MB，不入库；只把裁切调色后的成品放入 `art_source/cc0/`，并在此登记裁切范围。

## AI 生成资产

| 资产 | 任务文件 / 名称 | 种子 | 生成机器 | 状态 |
|---|---|---|---|---|
| 陆青禾立绘 v1（半身，832×1216）`art_source/ai/characters/lu_qinghe_portrait_v1.png` | `tools/ArtGen/jobs/m0_portrait.json` / `lu_qinghe_v3_from505`，图生图 strength 0.6，底图为下一行 | 7 | 家用台式机 RTX 4070 Ti SUPER | 2026-09-24 用户选定；未修整；SHA-256 `acbca397…d3ad` |
| 陆青禾立绘 v1 抠图版 `art_source/ai/characters/lu_qinghe_portrait_v1_cutout.png`，入包为 `game/assets/portraits/lu_qinghe_v1.png` | `tools/ArtGen/cutout.py` 由上一行去除纯色底（阈值 30、羽化 1.2，记录见同名 `.json`） | — | 家用台式机 | 2026-09-24 用于标题页、对话页与人物页；未做其他修整；SHA-256 `1b5a5449…a81e` |
| 陆青禾立绘 v1 的图生图底图 `art_source/ai/characters/lu_qinghe_portrait_v1_base.png` | 同上 / `lu_qinghe_v2_cowboy` | 505 | 同上 | 仅供复现，不进游戏包；SHA-256 `69a2b00d…4bc3` |

| 城镇民居 `town.house.north.1`（1120×1024）`art_source/ai/town/`，入包 `game/assets/art/town/` | `tools/ArtGen/jobs/m0_town_pieces.json` / `r5_house_g`，SDXL base + canny ControlNet 文生图（control 0.8、至六成步数），按引导图 alpha 抠出 | 44 | 家用台式机 RTX 4070 Ti SUPER | 2026-09-25 方案 C 试做入库，待用户核对；未修整；SHA-256 `6d3109c7…` |
| 平桥桥面与两道栏杆 `town.bridge.deck` / `.rail_w` / `.rail_e` | 同上 / `r4_bridge`（control 0.9、至七成步数），按部件遮罩拆分 | 55 | 同上 | 同上；SHA-256 `ed00e211…` / `54a92efc…` / `894d67e6…` |
| 渡口柳树 `town.tree.1` | 同上任务文件、SDXL base / `r3_willow`（control 0.35、至四成步数），`cutout.py` 按底色抠图（阈值 22），再按色差软抠图（10–50）去掉树冠里混入的雾状底色，擦除自带地面阴影 | 22 | 同上 | 同上；2026-09-25 用户认可后按其要求去除树冠灰雾；SHA-256 `632e805e…` |
| 石板街纹理 `town.ground.flagstone`（1024×1024，无缝，覆盖 480 世界单位） | 同上 / `r6_flagstone`，条石错缝格线作 ControlNet 引导，环绕填充 | 11 | 同上 | 同上；SHA-256 `2e5fd030…` |
| 地面纹理（4 张，1024×1024 无缝；任务名·种子）：客栈方砖 `inn.ground.brick` g1·11（60 见方直缝格线作 ControlNet 引导，覆盖 480 世界单位）、山道素土 `wild.ground.dirt` g5·44（覆盖 360）、山地草坡 `wild.ground.meadow` g6·44、城镇草地 `town.ground.grass` g7·44（两张草地生成时记 420，入包 json 改为每 200 世界单位循环一次，叶簇才不显粗大） | `tools/ArtGen/jobs/m0_ground_textures.json`，SDXL base 文生图、环绕填充；只有方砖带格线引导 | 见左栏 | 家用台式机 RTX 4070 Ti SUPER | 2026-09-26 入库，待用户核对；未修整，调色在着色器内（`GroundTint`、`InnTone.BrickTint`）；SHA-256 `d4319f14…` / `82df410f…` / `6616cf4a…` / `cf401162…`，完整记录见 `art_source/ai/<地区>/<id>.json` |
| 城镇房屋（9 件；任务前缀·种子）：`town.house.back.1` b1·55、`town.house.back.2` b1·44、`town.house.inn` b2·44、`town.house.north.2` b2·66、`town.house.north.3` b2·44、`town.house.north.4` b1·55、`town.house.south.1` b1·11、`town.house.south.2` b1·11、`town.house.south.3` b1·33 | `tools/ArtGen/jobs/m0_town_pieces.json`，SDXL base + canny ControlNet 文生图；按引导图轮廓抠出 | 见左栏 | 家用台式机 RTX 4070 Ti SUPER | 2026-09-25 批量出件入库，待用户核对；未修整；每件的参数与 SHA-256 见 `art_source/ai/town/<id>.json` |
| 城镇杂件（7 件；任务前缀·种子）：`town.prop.boat.2` b3·11、`town.prop.boat.ferry` b3·33、`town.prop.jars.inn` b1·11、`town.prop.jars` b1·11、`town.prop.notice` b3·33、`town.prop.stall` b1·33、`town.prop.stone_mark` b3·11 | `tools/ArtGen/jobs/m0_town_pieces.json`，SDXL base + canny ControlNet 文生图；按引导图轮廓抠出 | 见左栏 | 家用台式机 RTX 4070 Ti SUPER | 2026-09-25 批量出件入库，待用户核对；未修整；每件的参数与 SHA-256 见 `art_source/ai/town/<id>.json` |
| 城镇树（8 件；任务前缀·种子）：`town.tree.10` b1·11、`town.tree.2` b1·11、`town.tree.3` b1·11、`town.tree.5` b1·11、`town.tree.6` b1·11、`town.tree.7` b1·22、`town.tree.8` b1·11、`town.tree.9` b1·11 | `tools/ArtGen/jobs/m0_town_pieces.json`，SDXL base + canny ControlNet 文生图；`cutout.py` 按底色抠图、软抠、清底部地影（`place.py --recut/--soft/--deshadow`） | 见左栏 | 家用台式机 RTX 4070 Ti SUPER | 2026-09-25 批量出件入库，待用户核对；未修整；每件的参数与 SHA-256 见 `art_source/ai/town/<id>.json` |
| 客栈柜台（1 件；任务前缀·种子）：`inn.counter` b1·22 | `tools/ArtGen/jobs/m0_inn_pieces.json`，SDXL base + canny ControlNet 文生图；按引导图轮廓抠出 | 见左栏 | 家用台式机 RTX 4070 Ti SUPER | 2026-09-25 批量出件入库，待用户核对；未修整；每件的参数与 SHA-256 见 `art_source/ai/inn/<id>.json` |
| 客栈立柱（3 件；任务前缀·种子）：`inn.pillar.1` b1·33、`inn.pillar.2` b1·33、`inn.pillar.3` b1·33 | `tools/ArtGen/jobs/m0_inn_pieces.json`，SDXL base + canny ControlNet 文生图；按引导图轮廓抠出 | 见左栏 | 家用台式机 RTX 4070 Ti SUPER | 2026-09-25 批量出件入库，待用户核对；未修整；每件的参数与 SHA-256 见 `art_source/ai/inn/<id>.json` |
| 客栈盆栽（2 件；任务前缀·种子）：`inn.plant.1` b2·22、`inn.plant.2` b2·44 | `tools/ArtGen/jobs/m0_inn_pieces.json`，SDXL base + canny ControlNet 文生图；`cutout.py` 按底色抠图、软抠、清底部地影（`place.py --recut/--soft/--deshadow`） | 见左栏 | 家用台式机 RTX 4070 Ti SUPER | 2026-09-25 批量出件入库，待用户核对；未修整；每件的参数与 SHA-256 见 `art_source/ai/inn/<id>.json` |
| 客栈屏风（1 件；任务前缀·种子）：`inn.screen` b1·33 | `tools/ArtGen/jobs/m0_inn_pieces.json`，SDXL base + canny ControlNet 文生图；按引导图轮廓抠出 | 见左栏 | 家用台式机 RTX 4070 Ti SUPER | 2026-09-25 批量出件入库，待用户核对；未修整；每件的参数与 SHA-256 见 `art_source/ai/inn/<id>.json` |
| 客栈楼梯（1 件；任务前缀·种子）：`inn.stairs` b1·33 | `tools/ArtGen/jobs/m0_inn_pieces.json`，SDXL base + canny ControlNet 文生图；按引导图轮廓抠出 | 见左栏 | 家用台式机 RTX 4070 Ti SUPER | 2026-09-25 批量出件入库，待用户核对；未修整；每件的参数与 SHA-256 见 `art_source/ai/inn/<id>.json` |
| 客栈桌凳（4 件；任务前缀·种子）：`inn.table.1` b1·33、`inn.table.2` b1·33、`inn.table.3` b1·33、`inn.table.booth` b1·33 | `tools/ArtGen/jobs/m0_inn_pieces.json`，SDXL base + canny ControlNet 文生图；按引导图轮廓抠出 | 见左栏 | 家用台式机 RTX 4070 Ti SUPER | 2026-09-25 批量出件入库，待用户核对；未修整；每件的参数与 SHA-256 见 `art_source/ai/inn/<id>.json` |
| 山路山门（按部件拆分）（3 件；任务前缀·种子）：`wild.gate.pillar_e` b1·33、`wild.gate.pillar_w` b1·33、`wild.gate.top` b1·33 | `tools/ArtGen/jobs/m0_wild_pieces.json`，SDXL base + canny ControlNet 文生图；按引导图轮廓抠出 | 见左栏 | 家用台式机 RTX 4070 Ti SUPER | 2026-09-25 批量出件入库，待用户核对；未修整；每件的参数与 SHA-256 见 `art_source/ai/wild/<id>.json` |
| 山路茶亭（按部件拆分）（8 件；任务前缀·种子）：`wild.pavilion.bench_e` b1·11、`wild.pavilion.bench_n` b1·11、`wild.pavilion.pillar_ne` b1·11、`wild.pavilion.pillar_nw` b1·11、`wild.pavilion.pillar_se` b1·11、`wild.pavilion.pillar_sw` b1·11、`wild.pavilion.roof` b1·11、`wild.pavilion.table` b1·11 | `tools/ArtGen/jobs/m0_wild_pieces.json`，SDXL base + canny ControlNet 文生图；按引导图轮廓抠出 | 见左栏 | 家用台式机 RTX 4070 Ti SUPER | 2026-09-25 批量出件入库，待用户核对；未修整；每件的参数与 SHA-256 见 `art_source/ai/wild/<id>.json` |
| 山路山石（16 件；任务前缀·种子）：`wild.rock.1` b2·44、`wild.rock.10` b2·22、`wild.rock.11` b2·11、`wild.rock.12` b2·22、`wild.rock.13` b2·11、`wild.rock.14` b2·22、`wild.rock.15` b2·33、`wild.rock.16` b1·22、`wild.rock.2` b1·22、`wild.rock.3` b2·11、`wild.rock.4` b2·33、`wild.rock.5` b1·11、`wild.rock.6` b1·22、`wild.rock.7` b1·22、`wild.rock.8` b2·22、`wild.rock.9` b2·44 | `tools/ArtGen/jobs/m0_wild_pieces.json`，SDXL base + canny ControlNet 文生图；`wild.rock.2`、`5`、`6`、`7`、`9`、`10`、`11` 按引导图轮廓抠出，其余按底色抠图并清底部地影 | 见左栏 | 家用台式机 RTX 4070 Ti SUPER | 2026-09-25 批量出件入库，待用户核对；未修整；每件的参数与 SHA-256 见 `art_source/ai/wild/<id>.json` |
| 山路灌丛 / 蕨 / 草药（15 件；任务前缀·种子）：`wild.shrub.1` b1·11、`wild.shrub.10` b2·33、`wild.shrub.11` b1·33、`wild.shrub.12` b2·44、`wild.shrub.13` b1·33、`wild.shrub.14` b2·33、`wild.shrub.15` b1·33、`wild.shrub.2` b1·44、`wild.shrub.3` b1·33、`wild.shrub.4` b2·44、`wild.shrub.5` b1·33、`wild.shrub.6` b2·44、`wild.shrub.7` b1·33、`wild.shrub.8` b2·33、`wild.shrub.9` b1·33 | `tools/ArtGen/jobs/m0_wild_pieces.json`，SDXL base + canny ControlNet 文生图；`cutout.py` 按底色抠图、软抠、清底部地影（`place.py --recut/--soft/--deshadow`） | 见左栏 | 家用台式机 RTX 4070 Ti SUPER | 2026-09-25 批量出件入库，待用户核对；未修整；每件的参数与 SHA-256 见 `art_source/ai/wild/<id>.json` |
| 山路岔路木牌（1 件；任务前缀·种子）：`wild.signpost` b1·33 | `tools/ArtGen/jobs/m0_wild_pieces.json`，SDXL base + canny ControlNet 文生图；按引导图轮廓抠出 | 见左栏 | 家用台式机 RTX 4070 Ti SUPER | 2026-09-25 批量出件入库，待用户核对；未修整；每件的参数与 SHA-256 见 `art_source/ai/wild/<id>.json` |
| 山路路碑（1 件；任务前缀·种子）：`wild.stele` b1·11 | `tools/ArtGen/jobs/m0_wild_pieces.json`，SDXL base + canny ControlNet 文生图；按引导图轮廓抠出 | 见左栏 | 家用台式机 RTX 4070 Ti SUPER | 2026-09-25 批量出件入库，待用户核对；未修整；每件的参数与 SHA-256 见 `art_source/ai/wild/<id>.json` |
| 山路树（24 件；任务前缀·种子）：`wild.tree.1` b1·22、`wild.tree.10` b1·11、`wild.tree.11` b1·22、`wild.tree.12` b1·11、`wild.tree.13` b1·11、`wild.tree.14` b1·22、`wild.tree.15` b1·33、`wild.tree.16` b1·22、`wild.tree.17` b1·33、`wild.tree.18` b1·33、`wild.tree.19` b1·33、`wild.tree.2` b1·11、`wild.tree.20` b1·33、`wild.tree.21` b1·22、`wild.tree.22` b1·33、`wild.tree.23` b1·33、`wild.tree.24` b1·11、`wild.tree.3` b1·11、`wild.tree.4` b1·11、`wild.tree.5` b1·44、`wild.tree.6` b1·33、`wild.tree.7` b1·33、`wild.tree.8` b1·11、`wild.tree.9` b1·33 | `tools/ArtGen/jobs/m0_wild_pieces.json`，SDXL base + canny ControlNet 文生图；`cutout.py` 按底色抠图、软抠、清底部地影（`place.py --recut/--soft/--deshadow`） | 见左栏 | 家用台式机 RTX 4070 Ti SUPER | 2026-09-25 批量出件入库，待用户核对；未修整；每件的参数与 SHA-256 见 `art_source/ai/wild/<id>.json` |
| 2026-09-26 替换：山路竹丛 9 件（任务名·种子）`wild.tree.2` m·33、`.3` m·55、`.5` m·33、`.10` m·55、`.11` m·55、`.14` m·33、`.16` m·77、`.21` m·33、`.24` m·55（上一行对应的竹丛旧件作废）；城镇柳树 6 件 `town.tree.1` a1_s40·11、`.2` a2·22、`.3` a3·22、`.5` a5·22、`.6` a6·22、`.7` a7·22（前文试做与批量行中的柳树旧件作废） | 竹丛：`tools/ArtGen/jobs/m0_bamboo.json`，SDXL base + canny ControlNet 文生图（control 0.5、至五成步数），引导图取自加密后的占位竹丛；柳树：`tools/ArtGen/jobs/m0_willow_restyle.json`，以各自旧件的生成原图为底（`init_from`），Animagine XL 4.0 + canny ControlNet 图生图 strength 0.4；均 `place.py --recut 22 --soft 10,50 --deshadow 0.12` | 见左栏 | 家用台式机 RTX 4070 Ti SUPER | 用户 2026-09-26 指出竹子简陋、柳树偏写实后重做，待用户核对；未修整；参数与 SHA-256 见 `art_source/ai/<地区>/<id>.json` |
| 2026-09-26 补件：城镇井台 `town.prop.well` c1·66、廊棚屋面 `town.corridor.roof` c2·88（`jobs/m0_town_rest.json`，SDXL base + canny ControlNet 文生图，按引导图轮廓抠出）；纹理（`jobs/m0_ground_textures.json`，环绕填充）：山路崖壁 `wild.cliff` g9·66（1536×640，横向覆盖 360 单位）、城镇驳岸条石 `town.embankment` g11·22（1536×512，条石错缝格线引导，覆盖 240 单位）、木桥桥面木板 `wild.ground.planks` g10·11（板缝格线引导，覆盖 220 单位） | 见左栏 | 见左栏 | 家用台式机 RTX 4070 Ti SUPER | 待用户核对；未修整；参数与 SHA-256 见 `art_source/ai/<地区>/<id>.json` |
| 石面纹理 `wild.ground.slab`（1024×1024，无缝，覆盖 160 世界单位）g13·11，用于山路石阶与城镇渡口石阶 | `tools/ArtGen/jobs/m0_ground_textures.json` / `g13_stone_slab`，SDXL base 文生图、环绕填充 | 11 | 家用台式机 RTX 4070 Ti SUPER | 2026-09-26 入库，待用户核对；未修整；SHA-256 `70ae26c3…` |
| 草丛与矮灌精灵（5 件；任务名·种子）：`wild.tuft.grass.1` t5·33、`.grass.2` t5·44、`.grass.3` t4·33、`.grass.4` t1·33、`wild.tuft.bush.1` t3·33 | `tools/ArtGen/jobs/m0_grass_tufts.json`，SDXL base 文生图（纯色底，1024×768）；`cutout.py` 按底色抠图（grass.3 阈值 45，其余 22 + 软抠 10–50）并裁到内容边界 | 见左栏 | 家用台式机 RTX 4070 Ti SUPER | 2026-09-26 入库，待用户核对；未修整；记录见 `art_source/ai/wild/<id>.json` |
| 溪涧纹理（2 张；任务名·种子）：溪岸 `wild.bank` k5·44（长苔不规则岩土，1536×512，横向无缝，横向覆盖 200 单位、岸高 40 只取上部）、溪床 `wild.ground.streambed` k6·11（细沙，1024×1024，四向无缝，320 单位一格）；同日首版 k1·66（规整卵石岸）、k2·44（满底卵石）经用户指出过于规整后替换 | `tools/ArtGen/jobs/m0_creek.json`，SDXL base 文生图、环绕填充；溪床由 `town_ground` 着色器叠散石、水色、浪线与白沫 | 见左栏 | 家用台式机 RTX 4070 Ti SUPER | 2026-09-26 入库，待用户核对；未修整；记录见 `art_source/ai/wild/<id>.json` |
| 木纹纹理（2 张；任务名·种子）：朱漆木 `town.wood.lacquer` w1·55（竖纹，1024×1024，四向无缝，120 单位一格）、风化木 `town.wood.weathered` w2·11（横纹，160 单位一格） | `tools/ArtGen/jobs/m0_wood.json`，SDXL base 文生图、环绕填充；朱漆木只以 35% 不透明度叠在程序化朱漆底色上（城镇廊柱），风化木整贴（城镇坐栏、山路桥栏与边梁） | 见左栏 | 家用台式机 RTX 4070 Ti SUPER | 2026-09-26 入库，待用户核对；未修整；记录见 `art_source/ai/town/<id>.json` |
| 客栈内墙与吊灯（4 件；任务名·种子）：北墙正立面 `inn.wall.north` a2·11、东墙正立面 `inn.wall.east` a2·33（均经 `--regrade 0.7,1.0` 分区调色）、吊灯 `inn.lantern` a2·66、粉壁纹理 `inn.wall.plaster` a1·22（1024×1024，四向无缝，200 单位一格） | `tools/ArtGen/jobs/m0_inn_walls.json`：内墙 SDXL base + canny ControlNet 文生图（完整引导图，control 0.8、至七成步数），`regrade.py` 按引导图色块调回布局配色；吊灯 SDXL base + canny ControlNet 图生图 strength 0.7；粉壁 SDXL base 文生图、环绕填充 | 见左栏 | 家用台式机 RTX 4070 Ti SUPER | 2026-09-30 入库，待用户核对；未修整；记录见 `art_source/ai/inn/<id>.json` |
| 全身人物形象（7 件；任务名·种子）：主角 `figure.hero` hero·66、陆青禾 `figure.lu_qinghe` lu_qinghe·33、乔红绡 `figure.qiao_hongxiao` qiao_hongxiao·22、店小二 `figure.waiter` waiter·22、茶客 `figure.tea_guest` tea_guest·22（坐姿）、唐守亭 `figure.tang_shouting` tang_shouting·11、押运打手 `figure.escort` escort·44（迎敌架势）；入包为 `game/assets/art/figure/<id>.png`，头顶到脚底 800 像素 | `tools/ArtGen/jobs/m0_figures.json`：Animagine XL 4.0 + openpose ControlNet 文生图（control 0.8、至八成步数，长提示词分段编码），`cutout.py` 灰底抠图，`place.py --figure` 裁边缩放并按骨架记脚底与身高 | 见左栏 | 家用台式机 RTX 4070 Ti SUPER | 2026-09-30 入库，探索与战斗共用，待用户核对；未修整；记录见 `art_source/ai/figure/<id>.json` |
| 战斗远景“旧渡黄昏”`battle.ferry_dusk`（1920×1080），入包 `game/assets/art/battle/` | `tools/ArtGen/jobs/m0_battle_backdrop.json` / `m_far`：SDXL base 文生图 1360×768（长提示词分段编码），放大到 1920×1088 后以 strength 0.3 图生图补细节；`place.py --backdrop 578` 记远岸水线 | 99 | 家用台式机 RTX 4070 Ti SUPER | 2026-09-30 入库，水线以下由引擎画码头石板，待用户核对；未修整；SHA-256 `f7cbbefb…` |
| 战斗道具“水门机关”`battle.sluice_gate`（937×1022） | `tools/ArtGen/jobs/m0_battle_props.json` / `b_sluice_txt`：`prop_guide.py` 按任务文件色块画正立面引导图，SDXL base + canny ControlNet 文生图（control 0.75、至六成半步数），按引导图 alpha 抠出；`place.py --regrade 0.7,1.0 --prop` 调色、裁边并记底边与整高 | 66 | 同上 | 同上；SHA-256 `95b7962f…` |
| 全身人物形象 第二批（4 件；任务名·种子）：令狐冲 `figure.linghu_chong` linghu_chong·33、黄蓉 `figure.huang_rong` huang_rong·44、萧峰 `figure.xiao_feng` xiao_feng·22、杜三篙 `figure.du_sangao` du_sangao·33（持篙） | `tools/ArtGen/jobs/m2_figures.json`，同第一批管线（`figure.py` OpenPose 骨架 + Animagine XL 4.0，`cutout.py` 抠图，`place.py --figure` 入库）；源图在 `art_source/ai/figure/`，入包 `game/assets/art/figure/`（导入开 mipmap） | 见左 | 家用台式机 RTX 4070 Ti SUPER | 2026-10-02 入库，探索与战斗共用；**令狐冲、黄蓉、萧峰的外形是本作视觉草案**，只取常见的身份气质，待 M2-10 人物锚点核验与用户审看；每人 6 个种子由制作方挑选，未经用户选定。2026-10-02 用户指出萧峰全身形象的长络腮须与对话立绘对不上，定为两边统一短络腮胡：`tools/ArtGen/jobs/m2_portrait_fix.json` 的 `xiao_feng_figure_beard` 在 832×1216 原图上涂掉长须、沿下颌引导后重绘（选种子 11），`cutout.py` 抠图（阈值 26、羽化 1.0，加 `--enclosed 16` 顺带去掉原先残留在两腿之间的灰底），沿用原裁框 [248,108,603,1168] 与缩放 0.7663，脚底与身高不变；改前文件存于 `tools/ArtGen/out/m2_portrait_fix/before/`（不入库） |
| 对话立绘 第二批（6 件，832×1216 半身，抠图后入包 `game/assets/portraits/<人物>_v1.png`；源图、抠图与生成记录在 `art_source/ai/characters/`）：乔红绡 qiao_hongxiao·505、黄蓉 huang_rong·505、杜三篙 du_sangao·101、唐守亭 tang_shouting·202、萧峰 xiao_feng_v2·404（入包时缩到 0.8 倍并与陆青禾立绘头顶对齐）、令狐冲 linghu_chong_i2i_s80·202（以其全身形象上半身放大、淡青底，图生图 strength 0.8） | `tools/ArtGen/jobs/m2_portraits.json`，Animagine XL 4.0，风格词同陆青禾立绘 `portrait_final` | 见左 | 同上 | 2026-10-02 入库；经典人物外形同上为视觉草案；令狐冲文生图多张背景杂乱、出现西式酒瓶与十字形扣饰，萧峰第一轮体型过度夸张，均弃用；未经用户选定。2026-10-02 用户指出男角色立绘眼珠发白：萧峰、唐守亭、令狐冲、杜三篙四张以 `tools/ArtGen/jobs/m2_eye_fix.json` 只重绘眼部（各 4 种子，选萧峰·44、唐守亭·44、令狐冲·22、杜三篙·22），`eye_place.py` 压暗为深棕后贴回，其余像素与透明通道不变；唐守亭立绘下半截渐变天色原先没抠干净（游戏里露出浅蓝色块），用 `cutout.py --gradient` 重抠（带底色的源图仍在 `art_source/ai/characters/<人物>_portrait_v1.png`）；未经用户审看。2026-10-02 复核：萧峰上衣原为现代翻领加纽扣门襟，以 `tools/ArtGen/jobs/m2_portrait_fix.json` 三步局部重绘改为交领短打（选 xiao_feng·33、xiao_feng_b·33、xiao_feng_c·44）；黄蓉绿眼珠同一任务只重绘眼部改深棕（选 huang_rong·33），均用 `eye_place.py --set m2_portrait_fix` 套回，透明通道不变，生成参数与选定记录在 `tools/ArtGen/out/m2_portrait_fix/`；未经用户审看。同日用户审看：黄蓉眼睛认可；萧峰上身涂抹感重、胡子与全身形象对不上——第一轮结果作废，以现有立绘为底先加贴脸短络腮胡（`xiao_feng_v2_beard`，沿下颌粗涂引导，选 33），再以 strength 0.75 重绘领口到腰带的整个上身（`xiao_feng_v2_torso`，不含两臂，选 66）；整图存 `art_source/ai/characters/xiao_feng_portrait_v1_fixed.png`，`cutout.py` 重抠（阈值 30、羽化 1.2），入包图由抠图缩 0.8 倍贴到 (82,53) 整张生成（按透明通道比对求得；此前任务里记的 0.805 /(81,53) 有偏差，区域贴回在腰部附近会错开数像素，已更正）；未经用户审看 |
| 主角对话立绘 v1（832×1216 半身）`art_source/ai/characters/hero_portrait_v1.png`；右缘浅色竖条以底色覆盖后的 `hero_portrait_v1_clean.png`；抠图版 `hero_portrait_v1_cutout.png`，入包为 `game/assets/portraits/hero_v1.png`（游戏里水平翻转放右侧） | `tools/ArtGen/jobs/m2_hero_portrait.json` / `hero_i2i_s55`：以全身形象 `out/m0_figures/hero_66__raw.png` 抠图铺淡青底、裁上半身放大为底图（步骤见任务 `note`），Animagine XL 4.0 图生图 strength 0.55（另有 0.7 一组共 12 张候选）；`cutout.py` 阈值 30、羽化 1.2、`--enclosed 28`（去掉后脑发梢与腰侧夹缝里的底色） | 202 | 家用台式机 RTX 4070 Ti SUPER | 2026-10-04 入库，与全身形象同一套服装（米白交领短衣、靛蓝领缘与袖口、黑色内袖、米白腰带），眼珠深棕已放大核对，抠图深浅底各看一遍；同日先暂用 606，用户从 12 张候选中选定 s55·202 后换图（右缘 x ≥ 700 以底色 (158,200,193) 覆盖，同参数抠图，比例量点改为头顶 173、眼线 300、下巴 400、两眼中点 478 后重跑 `portrait_norm.py`）；SHA-256 源图 `fb7172b6…4d9c`、抠图 `aed3312a…c92f` |
| 对话立绘统一比例（8 件：陆青禾、主角、令狐冲、黄蓉、萧峰、乔红绡、唐守亭、杜三篙），覆盖 `game/assets/portraits/<人物>_v1.png`；入包前原图另存 `art_source/ai/characters/<人物>_portrait_v1_ingame.png`，每件变换记录在同目录 `<人物>_portrait_v1_norm.json` | `tools/ArtGen/portrait_norm.py` + `jobs/portrait_norm.json`（人工量点：头顶、眼线、下巴、两眼中点；体型与身高），只做等比缩放（LANCZOS）与平移，不改画面内容 | — | 家用台式机 | 2026-10-04 用户指出主角与陆青禾立绘“高度大小不一致”后统一：缩放 陆青禾 0.997、主角 1.108（同日换 s55·202 后为 1.119）、令狐冲 0.999、黄蓉 0.890、萧峰 1.063、乔红绡 0.890、唐守亭 0.883、杜三篙 0.867 |

生成记录由 `tools/ArtGen/generate.py` 写在每张图旁的 `.json` 中；入库时把该记录一并放入 `art_source/ai/`，并在上表登记。

## AI 配音

取得方式：MiniMax 国内站语音合成（`speech-2.8-hd`），`tools/VoiceBuilder`；工作选角见 `voice_source/profiles/minimax_cast.json`，其中主角、黄蓉为用户确认后使用的设计音色（各扣音色费 9.9 元），其余为系统音色。商用与随包分发的可用范围待按 MiniMax 用户协议与所购套餐核对，未确认前不作为发行资产。

| 资产 | 来源 | 位置 | 状态 |
|---|---|---|---|
| 第一章台词配音（99 句，心里话不配） | `minimax_cast` 工作选角，2026-10-01 生成 | `game/assets/audio/voice/*.mp3`（Git LFS）、`voice_manifest.json` | 2026-10-02 作为试听版装入游戏；台词未锁稿、逐句审核未做；锁稿后按改动增量重生成并制作正式母带 |
| 第一章战斗喊声配音（32 句） | `minimax_cast` 工作选角，2026-10-04 经用户确认主角、黄蓉设计音色后生成（计费 463 字，约 0.16 元） | 同上（`ch01.battle.*.mp3`） | 2026-10-04 作为试听版装入游戏；台词未锁稿、未经人耳审核 |

## AI 配乐

取得方式：本地 ACE-Step 1.5（代码与权重 MIT，生成物可商用；其 README 要求核对原创性并披露 AI 参与），`tools/MusicGen`，家用台式机 RTX 4070 Ti SUPER。母带放 `audio_source/music/`（Git LFS），每首旁的同名 `.json` 记录逐步生成与剪辑过程。2026-10-02 由 `tools/AudioBuild/bgm_runtime.py` 自动找循环点、统一到 −18 LUFS，运行时 Ogg 入包 `game/assets/audio/music/`（循环起点、接缝相似度与源文件哈希见 `music_manifest.json`），循环接缝待用户试听。

| 资产 | 来源过程 | 母带 | 状态 |
|---|---|---|---|
| 芦湾水镇·探索 `bgm.town.luwan` | XL turbo 文生曲种子 11 | `audio_source/music/bgm.town.luwan.wav`，48000 Hz 2 声道 FLOAT，120.00 秒 | 2026-10-02 用户选定工作稿；未剪循环；SHA-256 `b44f8bb1…0794` |
| 普通战斗 `bgm.battle.common` | 2B 种子 11 → XL cover 种子 11 / 22 → 60.02–92.69 秒换成种子 22 同段（交叉淡化拼接） | `audio_source/music/bgm.battle.common.wav`，48000 Hz 2 声道 PCM_16，100.00 秒 | 2026-10-02 用户选定工作稿；未剪循环；SHA-256 `4ec4cd5d…925e` |
| 旧渡首领战 `bgm.boss.old_ferry` | 普通战斗 2B 种子 33 → XL 重画两段旋律（种子 11）→ 0:48 借种子 44 呼吸律动做留白过渡 → 去 0:53 高音 → `note_gate.py` 删去类似二胡长音 → 0:03–0:09 重画为大提琴独奏 | `audio_source/music/bgm.boss.old_ferry.wav`，48000 Hz 2 声道 FLOAT，100.00 秒 | 2026-10-02 用户选定工作稿；未剪循环；SHA-256 `15d9358f…b0a0` |

## 实录音效与环境声（Freesound CC0）

2026-10-02 用户试听后认为程序合成的音效“太烂，只有一下钢琴声”，原 `sfx_synth.py` 合成版（41 个音效、4 段环境声）全部作废，改用实录素材剪辑。

取得方式：`tools/AudioBuild/freesound.py` 只检索、下载 Freesound 上 **Creative Commons 0** 授权的声音（检索页按 CC0 过滤，下载前到每个声音详情页再核一次授权，不是 CC0 的拒收）；当前下载的是官方高质量试听版（Ogg，约 192 kbps），原始文件需登录，发行前可换原档（同一 ID，剪辑流程不变）。素材缓存与来源记录在 `audio_source/sfx/freesound/`（`<ID>.ogg` + `sources.json`：标题、作者、链接、授权、标签），共下载候选 93 个，成品用到其中 47 个。`tools/AudioBuild/sfx_build.py` 按 `sfx_recipes.json` 剪辑（截取、按起音切出单步 / 单击、高低通、变调、叠层、首尾淡化、统一峰值；环境声取段混合、首尾交叉淡化成循环并按 BS.1770 定响度），处理不含随机数，重跑逐采样一致。CC0 不要求署名，下表仍逐条记录以备核查。

挑选与验收：制作方不能直接听声音，用 CLAP 零样本听辨（`tools/AudioBuild/sfx_check.py`，模型 `laion/larger_clap_general`，Apache-2.0，只在开发机上用，不入包）初筛候选、验收成品——每个音效对 40 余条描述（含“钢琴、电子音、人声、汽车、飞机”等错类）打分，期望描述须排第一；脚步按游戏步频把变体连成一串再听辨；环境声另按 5 秒窗口扫描人声、音乐、交通、警笛、狗叫。旧合成版的听辨结果是：通知声被听成“钢琴 / 警笛”，脚步与击打被听成“鼓”，四段环境声都被听成“静音”，与用户反馈一致。

| 资产 | 内容 | 位置 | 状态 |
|---|---|---|---|
| 环境声 4 段 | 河水 `amb.river`（两段溪流叠加，−24 LUFS）、风吹芦苇 `amb.reeds`（−27）、水镇街巷 `amb.town`（清晨鸟鸣 + 远处低通河水，−27）、客栈大堂 `amb.inn`（低通人声嘈杂 + 炉火 + 收拾碗盏，−28）；立体声 60 秒无缝循环 | `game/assets/audio/amb/` | 2026-10-02 入库；CLAP 听辨全部符合，窗口扫描未见人声、音乐、交通；**未经人耳试听** |
| 音效 51 个 | 界面（木键轻响、木鱼式确认、纸页、翻页、卷轴）、通知（铜钱入袋、木鱼一叩、小锣、铃）、脚步（泥石路 / 木地板 / 石板各 6 变体）、交互（衣料）、战斗（拔刀、兵刃破空 4、劈砍入肉 3、拳掌击中 3、兵刃相格 3、倒地、运气呼吸、衣料、蓄力、胜锣、败鼓）、旅行（摇橹入水、衣袂） | `game/assets/audio/sfx/`、`sfx_manifest.json`（每条记来源 ID 与 CLAP 结果） | 2026-10-02 入库；CLAP 听辨 0 个不符（三种路面连走均符合）；**未经人耳试听**，正式验收在 M3-06 |

| Freesound ID | 标题 | 作者 | 用于 |
|---|---|---|---|
| [19291](https://freesound.org/people/martian/sounds/19291/) | foley cloth rustle.wav | martian | `interact`、`travel.whoosh` |
| [59988](https://freesound.org/people/qubodup/sounds/59988/) | SWOSH-01 44.1kHz | qubodup | `battle.swing` |
| [71507](https://freesound.org/people/pfeifferc/sounds/71507/) | Gong1.wav | pfeifferc | `battle.victory` |
| [119914](https://freesound.org/people/ftpalad/sounds/119914/) | Footsteps on Wooden Floor.aif | ftpalad | `step.wood` |
| [139507](https://freesound.org/people/robertmcdonald/sounds/139507/) | Drum Hit 3.wav | robertmcdonald | `battle.defeat` |
| [144110](https://freesound.org/people/gfrog/sounds/144110/) | Page Turn 1 | gfrog | `ui.page` |
| [148849](https://freesound.org/people/iluppai/sounds/148849/) | 1ring | iluppai | `notify.skill` |
| [162370](https://freesound.org/people/lewisisminted/sounds/162370/) | Punch #1.mp3 | lewisisminted | `battle.hit.blunt` |
| [164315](https://freesound.org/people/Rickmk2/sounds/164315/) | Footsteps on wooden flooring.wav | Rickmk2 | `step.wood` |
| [202107](https://freesound.org/people/spookymodem/sounds/202107/) | Unrolling Scroll.wav | spookymodem | `ui.close` |
| [316643](https://freesound.org/people/bevibeldesign/sounds/316643/) | dishes clearing.aiff | bevibeldesign | `amb.inn` |
| [326868](https://freesound.org/people/JohnBuhr/sounds/326868/) | Sword_Clash (7).wav | JohnBuhr | `battle.block` |
| [336580](https://freesound.org/people/Anthousai/sounds/336580/) | coins - in cloth 09.wav | Anthousai | `notify.item` |
| [346694](https://freesound.org/people/deleted_user_2104797/sounds/346694/) | Body fall_02.wav | deleted_user_2104797 | `battle.down` |
| [352611](https://freesound.org/people/macdaddyno1/sounds/352611/) | singing bowl.MP3 | macdaddyno1 | `battle.heal` |
| [352870](https://freesound.org/people/PotatokingXII/sounds/352870/) | Footsteps Dirt Gravel | PotatokingXII | `step.dirt` |
| [364530](https://freesound.org/people/Christopherderp/sounds/364530/) | Swords Clash - High Quality #2 | Christopherderp | `battle.block` |
| [369428](https://freesound.org/people/cabled_mess/sounds/369428/) | Small Gong_Soft Hit_RAW | cabled_mess | `battle.victory`、`notify.quest` |
| [390462](https://freesound.org/people/Huminaatio/sounds/390462/) | Punch in the face | Huminaatio | `battle.hit.blunt` |
| [395370](https://freesound.org/people/ihitokage/sounds/395370/) | Sword stab 1 | ihitokage | `battle.hit.blade` |
| [395373](https://freesound.org/people/ihitokage/sounds/395373/) | Sword stab 2 | ihitokage | `battle.hit.blade` |
| [420668](https://freesound.org/people/SypherZent/sounds/420668/) | Basic Melee Swing / Miss / Whoosh | SypherZent | `battle.swing` |
| [420670](https://freesound.org/people/SypherZent/sounds/420670/) | Strong Melee Swing | SypherZent | `battle.swing` |
| [422513](https://freesound.org/people/Nightflame/sounds/422513/) | Swinging staff whoosh (strong) 04.wav | Nightflame | `battle.swing` |
| [444920](https://freesound.org/people/NomadApe/sounds/444920/) | Fire crackling in fireplace | NomadApe | `amb.inn` |
| [450628](https://freesound.org/people/kyles/sounds/450628/) | river or stream thick soft flow bubbly2.flac | kyles | `amb.river` |
| [455727](https://freesound.org/people/kyles/sounds/455727/) | crowd int medium murmur restaurant busy buffet dishes cutlery tinkling Montreal, Canada.flac | kyles | `amb.inn` |
| [464492](https://freesound.org/people/elynch0901/sounds/464492/) | Face/Body Being Punched | elynch0901 | `battle.hit.blunt` |
| [471095](https://freesound.org/people/spycrah/sounds/471095/) | Sword clash 1.wav | spycrah | `battle.block` |
| [474575](https://freesound.org/people/ethanchase7744/sounds/474575/) | Sword stab.wav | ethanchase7744 | `battle.hit.blade` |
| [478973](https://freesound.org/people/SkibkaMusic/sounds/478973/) | Oarsmanship_SS_1_HQ.wav | SkibkaMusic | `travel.oar` |
| [480840](https://freesound.org/people/craigsmith/sounds/480840/) | R23-38-Oar Splash.wav | craigsmith | `travel.oar` |
| [496188](https://freesound.org/people/JonasTisell/sounds/496188/) | Whoosh (Clothing Drag) | JonasTisell | `battle.buff` |
| [521590](https://freesound.org/people/Fission9/sounds/521590/) | Hiking Boot Footsteps on Stone | Fission9 | `step.stone` |
| [577619](https://freesound.org/people/paulfabb/sounds/577619/) | Sword Drawing 1.wav | paulfabb | `battle.draw` |
| [607215](https://freesound.org/people/jonopodmore/sounds/607215/) | Mokugyo.wav | jonopodmore | `notify.clue` |
| [613960](https://freesound.org/people/Garuda1982/sounds/613960/) | strong wind on field with rustling reeds | Garuda1982 | `amb.reeds` |
| [614081](https://freesound.org/people/mateusboga/sounds/614081/) | Opening a book | mateusboga | `ui.open` |
| [614924](https://freesound.org/people/Rimmer/sounds/614924/) | Bird song early morning long.wav | Rimmer | `amb.town` |
| [682127](https://freesound.org/people/HenKonen/sounds/682127/) | Footsteps Dirt Road 1.wav | HenKonen | `step.dirt` |
| [683185](https://freesound.org/people/NearTheAtmoshphere/sounds/683185/) | Power Up | NearTheAtmoshphere | `battle.charge` |
| [692828](https://freesound.org/people/hollandm/sounds/692828/) | Woodblock-soft.wav | hollandm | `ui.confirm` |
| [707576](https://freesound.org/people/Garuda1982/sounds/707576/) | gentle river flow | Garuda1982 | `amb.river`、`amb.town` |
| [717167](https://freesound.org/people/rrehl/sounds/717167/) | - Deep Breath | rrehl | `battle.heal` |
| [742356](https://freesound.org/people/NoisyRedFox/sounds/742356/) | SingleKnock_Wood | NoisyRedFox | `ui.cancel` |
| [757207](https://freesound.org/people/HenKonen/sounds/757207/) | Footsteps stone floor | HenKonen | `step.stone` |
| [840321](https://freesound.org/people/Robo9418/sounds/840321/) | Wooden Short Click! | Robo9418 | `ui.move` |
