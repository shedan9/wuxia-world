# ArtGen：本地 AI 美术生成

用 SDXL 在本地显卡上批量生成美术草图与底稿。输出只在 `out/`（不入库）；人工挑选、修整后的成品放入 `art_source/`，并在[资产台账](../../docs/art/ASSET_LEDGER.md)登记。

## 两台机器的分工

| 机器 | 配置 | 负责 |
|---|---|---|
| 笔记本（本机） | AMD Ryzen 9 7845HX、16 GB 内存、RTX 4060 Laptop 8 GB、Windows 11 家庭版 26200 | 简单资源：物品与招式图标、纸张水墨肌理、UI 装饰、构图小样；任务文件 `"tier": "simple"` |
| 家用台式机 | Intel i7-14700KF、32 GB 内存、RTX 4070 Ti SUPER 16 GB、Windows 11 家庭版 26200 | 复杂资源：人物立绘与三视图、探索与战斗场景、大图高清放大；任务文件 `"tier": "complex"` |

8 GB 显卡上 `generate.py` 自动启用模型 CPU 卸载（慢但不爆显存）；12 GB 以上显卡整体放入显存。两台机器用同一份 `requirements*.txt`、同一模型和同一种子，结果可相互复现（不同显卡架构可能有细微像素差异，以记录中的哈希为准）。

## 环境

| 项目 | 版本 / 位置 |
|---|---|
| Python | 3.12（笔记本 3.12.6，`C:\Python312`；台式机 3.12.10，winget 按用户安装于 `%LOCALAPPDATA%\Programs\Python\Python312`，与已有 3.13 并存） |
| NVIDIA 驱动 | 笔记本 591.86（CUDA 13.1）、台式机 617.14（CUDA 13.4）；需支持 CUDA 12.8 及以上 |
| PyTorch | 见 `requirements-torch.txt`，从 `https://download.pytorch.org/whl/cu128` 安装 |
| 其他依赖 | 见 `requirements.txt`（diffusers、transformers、accelerate 等，已锁版本） |
| 虚拟环境 | `tools/ArtGen/.venv`（不入库） |
| 模型 | `stabilityai/stable-diffusion-xl-base-1.0`（fp16，约 7 GB，CreativeML Open RAIL++-M，允许商用）；`madebyollin/sdxl-vae-fp16-fix`（MIT）；人物立绘用 `cagliostrolab/animagine-xl-4.0`（任务文件 `"model": "animagine4"`，约 7 GB，CreativeML Open RAIL++-M，模型卡写明允许商用，Euler Ancestral 采样）；布景件引导用 `xinsir/controlnet-canny-sdxl-1.0`（约 2.5 GB，Apache-2.0） |
| 模型缓存 | `%USERPROFILE%\.cache\huggingface`；设置 `HF_HOME` 可改到其他磁盘 |

## 本机实测（2026-09-24）

- `setup.ps1` 等效步骤在笔记本完成：torch 2.11.0+cu128、diffusers 0.40.0，`torch.cuda.is_available()` 为真。
- 笔记本 SDXL fp16 + CPU 卸载，1024×1024、30 步：约 30 秒一张。
- 基础 SDXL 对器物造型把握不稳（例：剑会画成日式刀或多出零件），每项生成 4 个种子挑选，必要时人工修整；负面词挡不住偶发的印章，入库前裁掉。
- diffusers 0.40 起 VAE 分块写作 `pipe.vae.enable_tiling()`；`torch_dtype` 参数有弃用警告，不影响结果。

## 台式机实测（2026-09-24）

- `setup.ps1 -Python <3.12 路径>` 一次通过：torch 2.11.0+cu128、依赖全部按锁定版本安装，CUDA 识别 RTX 4070 Ti SUPER，模型缓存到 `%USERPROFILE%\.cache\huggingface`。
- 模型整体放入显存，832×1216、40 步：约 9 秒一张（首张 9.0 秒，其余 8.7–9.3 秒）。模型已缓存后可设 `HF_HUB_OFFLINE=1` 跳过联网检查。
- 首次人物立绘 `char_lu_qinghe` 6 个种子：工笔画风、全身比例和江南渡口气质可用；但宽袖多于窄袖、肤色偏白、船篙常画成短杖、多为草鞋而非短靴，6 张中 4 张出现题款或印章。立绘需换用更强的人物描述或局部重绘，入库前裁掉题款。
- 第二轮 `char_lu_qinghe_v2` 8 个种子：人物风格改用 `character_plain`（空白纸底），负面词改用 `negative_character` 预设（加入宽袖、长袍、裙、白肤、草鞋、赤足、背景景物）。8 张全部为短衣长裤加短靴，窄袖明显增多，背景基本空白；仍有问题：肤色依旧偏白，船篙多数只到腰或胸、未触地，3 张出现清式盘扣（与宋代江南不符），5 张仍有题款或小印。以 303、202 为较好候选。
- 任务文件可用 `negative_style` 选用另一套负面词预设；共享负面词已占 67 token，直接追加会超过 77 token 被截断。
- 局部重绘（`inpaint.py`，任务 `jobs/m0_fix.json`）：以 v2 种子 303 为底，先去掉短篙与飘带、画入一根从手中穿过并触地的长篙，strength 0.4 重绘（选种子 4）；再对脸、颈、双手调成晒肤色后 strength 0.3 重绘（选种子 1，五官保持最好）。成品 `out/m0_fix/lu303_skin_1.png`，对比图 `lu303_review_before_after.png`。每张约 4 秒。strength 0.55 时模型会把手以上的篙身抹成纸面；0.4 能保住画入的形状。
- 仍待处理：新篙颜色偏浅、笔触比衣纹平；下颌旁有一条来历不明的黑色垂绳；左下角小印与纸面折痕需在入库修整时裁除或修掉。

## 画风改定（2026-09-24）

用户否定了水墨宣纸 / 工笔方向，要求蓝绿清新基调、偏写实的卡通立绘（参照 `docs/art/example/`，只作沟通参照，见资产台账）。基础 SDXL 画不出赛璐璐/厚涂立绘，且旧负面词专门排除了 anime；人物立绘改用 Animagine XL 4.0，任务文件 `jobs/m0_portrait.json`。该模型按标签式提示词训练：人物标签在前，风格与质量标签（`masterpiece, high score, absurdres`）在后，CFG 5、28 步、832×1216。旧 `m0_complex.json` 与 `m0_fix.json` 的工笔立绘结果作废，只保留作管线记录。

立绘迭代记录（台式机，每张 4–7 秒）：
- v2：交领汉服、米色麻布、青头巾，年龄感对；用户选定半身 505 的画风，但嫌服装单调，并要求全身构图。
- v3：参照 `docs/art/example/` 加入粗线稿、撞色与纹样。以 505 为底的图生图（任务键 `init_image` / `strength`，共用子模型不增显存）在 strength 0.6 时几乎只加配饰；重新生成丰富但易出宽袖和竹林背景。
- v4：全身 768×1344，负面词加 `wide sleeves, bamboo forest, scenery, cropped`；丰富度达标，`tan skin` 会让多数种子肤色过深，去掉 `pale teal background` 后底色变灰粉。
- v5：改 `light tan skin`、恢复 `pale teal background`，肤色与底色回正；`thick lineart` 让画面偏美式平涂卡通，全身 768×1344 下头部只占画面约八分之一，五官细节不足。
- SDXL 两个文本编码器只读 77 token，丰富服装描述时必须删减其他词；若继续加细节，需要引入长提示词分段编码。
- `sheet.py out/<任务集> <名称前缀>` 把一组结果拼成带种子号的对比图。
- 定稿：用户选定 `lu_qinghe_v3_from505_7` 为陆青禾立绘 v1（半身），已入 `art_source/ai/characters/`。其后以它为底的补光重绘（`relight.py` 换底色加柔光，再图生图）与扩成全身（`inpaint.py` 的 `canvas` 步骤）均按用户指示停止：扩图后原图区与新生成区背景色调不同，出现矩形边框和裙摆横向接缝；统一背景时颜色容差过大会吃掉竹篙高光与头巾飘带。
- 经验：每轮改提示词重新生成会逐渐偏离用户已认可的样子；有选定图后，只以它为底做局部修改。

## 复现步骤

```powershell
# 在仓库根目录；首次约需下载 PyTorch 3 GB 与模型 7 GB
./tools/ArtGen/setup.ps1                  # 或 -Python 'C:\Python312\python.exe'
cd tools/ArtGen
.venv/Scripts/python generate.py jobs/m0_simple.json --dry-run   # 只看提示词
.venv/Scripts/python generate.py jobs/m0_simple.json --only icon_  # 生成
```

## 布景件：引导出件（方案 C，2026-09-25）

架构文档 10.3 的第二步。流程：

```powershell
# 1. Godot 导出占位件引导图（在仓库根目录；输出不入库；不能加 --headless，否则视口不渲染会卡住）
& $env:GODOT_BIN.Replace('.exe','_console.exe') --path game -- --scene=res://scenes/preview/PieceGuideExport.tscn --out=$PWD/tools/ArtGen/out/guides/town
& $env:GODOT_BIN.Replace('.exe','_console.exe') --path game -- --scene=res://scenes/preview/PieceGuideExport.tscn --region=wild --out=$PWD/tools/ArtGen/out/guides/wild   # inn 同理
# 2. 生成（--preview 只看底图与边线；--model 临时换基础模型比较）
cd tools/ArtGen
.venv/Scripts/python piece.py jobs/m0_town_pieces.json --only r5_house
# 3. 选定后入库并贴进游戏（然后 Godot --headless --path game --import）
.venv/Scripts/python place.py out/m0_town_pieces/r5_house_g_44.png town.house.north.1
.venv/Scripts/python place.py out/m0_town_pieces/r4_bridge_55.png town.bridge --parts
.venv/Scripts/python place.py out/m0_town_pieces_sdxl/r3_willow_22.png town.tree.1 --recut 22 --soft 10,50 --erase 700,1036,1000,1180
.venv/Scripts/python place.py out/m0_town_pieces/r6_flagstone_11.png town.ground.flagstone
# 批量入库的树类：按底色抠、软抠、清图底部 12% 内树干根部以外的地影
.venv/Scripts/python place.py out/m0_wild_pieces/b1_tree_17_33.png wild.tree.17 --recut 22 --soft 10,50 --deshadow 0.12
```

任务键：`mode`（`txt2img` 只受边线约束 / `img2img` 以占位配色为底，`strength`；`init_from` 改以指定原图为底）、`control_from`（`shape` 结构图 / `guide` 带瓦垄等纹理线的完整引导图）、`control`（ControlNet 强度）、`control_end`（约束施加到第几成步数）、`grow`（抠图外扩）。纹理任务 `"kind": "texture"` 可带 `bond`（条石错缝格线引导：`tile` 世界边长、`rows` 行数、`lengths` 条石长度候选），生成时 UNet、VAE、ControlNet 卷积改为环绕填充，输出旁附 2×2 平铺预览。

2026-09-25 台式机试做实测（每张 8–16 秒）：
- 图生图 strength 0.7–0.95 + 完整引导图：几何准，但几乎只照描平涂占位，材质出不来。
- 文生图 + 结构图边线（control 0.8–0.95，control_end 0.6–0.8）：材质、水渍、瓦片才出来。Animagine 画场景偏平涂卡通、绿色发荧光；SDXL base 更丰富，定为布景基础模型。
- 结构图若连门窗也去掉，模型会画西式窗、门的位置也不再与交互点对应：结构图只去纹理贴花，门窗开口保留；负面词加 `european, western windows, shutters, brick`。屋面用 `control_from: guide`（带瓦垄线）时黛瓦更稳。
- 石桥栏杆常被画成木扶手，负面词加 `wood railing`。
- 树：强约束会照搬占位的粗直柳条，改为 control 0.35、control_end 0.4 的弱约束，再用 `place.py --recut` 按底色抠图；模型会自带地面阴影，用 `--erase` 羽化擦除。树冠里的雾状灰层是模型把底色混进后排枝条，边缘连通抠图碰不到；用 `--soft 10,50` 按与底色的色差软抠图，灰层变为半透明枝条（12–60 以上会让树冠顶部也透）。
- 无约束的无缝纹理会画成斜向碎石；加错缝格线引导后条石成行。

2026-09-25 批量出件（台式机，每张约 18 秒；任务 `m0_town_pieces.json` 的 `b1_`–`b4_`、`m0_inn_pieces.json`、`m0_wild_pieces.json`，共约 500 张）：
- 招牌、匾额、碑文、路牌上的字不交给模型：引导图导出时不画字（`Face.Lettering`），负面词加 `chinese characters, calligraphy`，入库后由引擎用登记字体补写。
- 屋面：提示词以 `black clay roof tiles in rows` 开头才稳定是黛瓦；把门面描述放在前面时，店铺与客栈屋面变成木板色 / 红褐色。铺面负面词去掉 `shutters`（与“排门”冲突）、加 `roller shutter, garage door`；即便如此铺面仍易出现现代感，north.2 取较好的一张。客栈外观在三轮中都偏暗、不画匾额，取 `b2_44`，幌子由引擎按占位画法整面补画。
- 杂件：告示牌、船用完整引导图（`control_from: guide`）才出瓦顶与乌篷；石痕须写 `upright ... side view`，否则画成俯视石板；井台下半截四个种子都只照描线框，未入库。
- 树：山松以种子 33 最干净，种子 44 常画成一整张小树图集；浅灰树干会被软抠图抠成半透明，换棕色树干的种子。`--deshadow` 在图底部一段里只保留最宽不透明段（树干）左右各一倍宽度的范围，按颜色判断会误伤树干背光面。
- 山石与矮丛：弱约束（0.35）画成满屏素材图集；山石改 0.55 / 0.5 并写 `dark muted olive green moss` 后苔色正常；矮丛在 0.6 时照描占位笔画，最终取两轮中较好的，部分同形矮丛出图相同（同一引导图、同一种子）。

2026-09-26 地面纹理（`jobs/m0_ground_textures.json`，每张约 7 秒）：
- 方砖用直缝格线引导（`bond` 加 `"stagger": false`，`lengths: [60]`），首轮即可用；模型画的缝线偏浅，由引擎勾深。
- 土路：提示词里只要出现 pebbles / stones，整面就变成卵石铺地；取素土一张作底，碎石由着色器稀疏叠加。
- 草地：不加负面词时常出现石块、方格或巨型叶簇；负面词加 `cobblestone, pavement, tiles, rocks, pebbles` 后稳定。草叶纹理对比强、偏艳绿，入包后在着色器里收饱和度、向平均色收拢对比，并把循环尺度缩到 200 世界单位。

2026-09-26 竹丛与柳树返工（用户：竹子简陋、柳树偏写实）：
- 竹丛（`jobs/m0_bamboo.json`，每张约 21 秒）：弱约束下 AI 件跟着占位轮廓走，占位只有几根细竿时出图也稀；先把占位竹丛加密再导出引导图，control 0.5 出图饱满。部分种子会铺满整幅或换成深色底，挑底色干净的；种子 66 竹竿偏青蓝。
- 柳树（`jobs/m0_willow_restyle.json`）：任务键 `init_from` 以已入库件的生成原图（`__raw.png`）作图生图底图，只调画风。SDXL 0.4–0.6 只是更平滑；Animagine（`--model animagine4`）0.4 时树冠成团、树干出勾线且造型不变，0.5 起开始改枝形。

## 局部重绘

`inpaint.py` 读取 `jobs/*_fix.json`，对一张已生成的图依次做：`erase`（用周围纸色逐层填平旧物件）、`paint` / `tint`（画入粗略新形状或对皮肤区调色，`min_luma`/`max_luma` 把墨线、头发和纸底排除在外）、按 `mask` 与 `strength` 重绘，再只把遮罩内结果羽化贴回，其余像素不变。坐标以源图像素计；先用 `--preview` 输出预处理图和遮罩叠加图核对位置，再正式运行。多步修整写成串联任务，后一步的 `source` 指向前一步选定的输出。

```powershell
.venv/Scripts/python inpaint.py jobs/m0_fix.json --only lu303_skin --preview
.venv/Scripts/python inpaint.py jobs/m0_fix.json --only lu303_skin
```

重绘沿用同一 SDXL 基础模型，不新增权重。重绘管线必须用 `StableDiffusionXLInpaintPipeline(**base.components)` 共用子模型：diffusers 0.40 的 `from_pipe` 会再复制一份权重（显存 6.6 → 13.1 GB），16 GB 显卡溢出到共享内存后每张从 4 秒变成 5 分钟以上。

## 纯色底抠图

`cutout.py` 从图像边缘按颜色距离做连通填充，只去掉与边框相连的底色（人物身上与底色相近的饰物不受影响），边缘羽化并扣除底色溢色，输出 RGBA PNG 与同名 `.json` 记录（源图与输出哈希、阈值、羽化）。`--enclosed 距离` 另去掉离底色更近的封闭小块（树冠枝条间的空隙），默认不去；`--soft t0,t1` 按与底色的色差给半透明度（t0 以下全透、t1 以上不透），用于模型混进前景的雾状底色。

```powershell
.venv/Scripts/python cutout.py ../../art_source/ai/characters/lu_qinghe_portrait_v1.png ../../art_source/ai/characters/lu_qinghe_portrait_v1_cutout.png
```

## 任务文件

`jobs/*.json` 描述一批图：共享风格片段（`style`）、负面词、尺寸、步数、种子，以及每张图的名称和主体描述。提示词使用英文，SDXL 对中文理解差。负面词固定排除文字、印章、水印、动漫与日式画风。

每张输出旁有同名 `.json`：模型、VAE、种子、尺寸、步数、CFG、完整提示词、SHA-256、耗时和显卡型号。入库时连同记录一起复制。

## 许可与限制

- 更换或新增模型、LoRA 前先确认许可允许商用，并更新本文件与资产台账。
- AI 输出须人工修整：人物一致性、手部、兵器结构和服饰年代最容易出错；不能直接当作分层动画资产（架构文档 10.3）。
