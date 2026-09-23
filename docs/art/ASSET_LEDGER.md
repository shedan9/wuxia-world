# 资产台账

记录随包或进入 `art_source/` 的每项外部与生成资产的来源、许可和用途。新增资产须先登记再入库；许可不明的资产不得进入 `game/assets/`。公开发行前仍须复核本台账（AGENTS.md「修改内容时守住的边界」）。

## 取得方式（2026-09-24 用户确定）

| 方式 | 用于 | 许可要求 |
|---|---|---|
| 本地 AI 生成（SDXL，`tools/ArtGen`） | 人物、场景、道具、图标的草图与成品底稿 | 模型许可须允许商用；每张图保留生成记录（模型、种子、提示词、哈希） |
| 博物馆开放获取（CC0） | 写意远景、纸张与水墨肌理 | 仅用 CC0 / 公有领域；记录馆藏号与原始链接 |
| 开源字体 | 正文与标题字体 | SIL OFL 1.1，许可文本随包附带 |

AI 生成图在多数司法辖区可能无法取得著作权保护，别人复制使用时难以主张权利；这不影响本作使用，但发行前须知悉。生成图须人工挑选并修整后使用，不直接把未经检查的输出放入游戏。

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
| — | — | — | — | 尚无入库的生成资产 |

生成记录由 `tools/ArtGen/generate.py` 写在每张图旁的 `.json` 中；入库时把该记录一并放入 `art_source/ai/`，并在上表登记。
