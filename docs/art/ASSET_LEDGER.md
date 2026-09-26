# 资产台账

记录随包或进入 `art_source/` 的每项外部与生成资产的来源、许可和用途。新增资产须先登记再入库；许可不明的资产不得进入 `game/assets/`。公开发行前仍须复核本台账（AGENTS.md「修改内容时守住的边界」）。

## 取得方式（2026-09-24 用户确定）

| 方式 | 用于 | 许可要求 |
|---|---|---|
| 本地 AI 生成（SDXL 系模型，`tools/ArtGen`） | 人物、场景、道具、图标的草图与成品底稿；人物立绘用 Animagine XL 4.0（CreativeML Open RAIL++-M，模型卡写明允许商用）；布景件用 SDXL 1.0 base（CreativeML Open RAIL++-M）加 ControlNet `xinsir/controlnet-canny-sdxl-1.0`（Apache-2.0，允许商用），引导图取自本作程序化占位件 | 模型许可须允许商用；每张图保留生成记录（模型、种子、提示词、哈希） |
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

生成记录由 `tools/ArtGen/generate.py` 写在每张图旁的 `.json` 中；入库时把该记录一并放入 `art_source/ai/`，并在上表登记。
