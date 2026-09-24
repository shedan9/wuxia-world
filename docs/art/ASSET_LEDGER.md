# 资产台账

记录随包或进入 `art_source/` 的每项外部与生成资产的来源、许可和用途。新增资产须先登记再入库；许可不明的资产不得进入 `game/assets/`。公开发行前仍须复核本台账（AGENTS.md「修改内容时守住的边界」）。

## 取得方式（2026-09-24 用户确定）

| 方式 | 用于 | 许可要求 |
|---|---|---|
| 本地 AI 生成（SDXL 系模型，`tools/ArtGen`） | 人物、场景、道具、图标的草图与成品底稿；人物立绘用 Animagine XL 4.0（CreativeML Open RAIL++-M，模型卡写明允许商用） | 模型许可须允许商用；每张图保留生成记录（模型、种子、提示词、哈希） |
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

生成记录由 `tools/ArtGen/generate.py` 写在每张图旁的 `.json` 中；入库时把该记录一并放入 `art_source/ai/`，并在上表登记。
