# ArtGen：本地 AI 美术生成

用 SDXL 在本地显卡上批量生成美术草图与底稿。输出只在 `out/`（不入库）；人工挑选、修整后的成品放入 `art_source/`，并在[资产台账](../../docs/art/ASSET_LEDGER.md)登记。

## 两台机器的分工

| 机器 | 配置 | 负责 |
|---|---|---|
| 笔记本（本机） | AMD Ryzen 9 7845HX、16 GB 内存、RTX 4060 Laptop 8 GB、Windows 11 家庭版 26200 | 简单资源：物品与招式图标、纸张水墨肌理、UI 装饰、构图小样；任务文件 `"tier": "simple"` |
| 家用台式机 | RTX 4070 Ti SUPER 16 GB、32 GB 内存 | 复杂资源：人物立绘与三视图、探索与战斗场景、大图高清放大；任务文件 `"tier": "complex"` |

8 GB 显卡上 `generate.py` 自动启用模型 CPU 卸载（慢但不爆显存）；12 GB 以上显卡整体放入显存。两台机器用同一份 `requirements*.txt`、同一模型和同一种子，结果可相互复现（不同显卡架构可能有细微像素差异，以记录中的哈希为准）。

## 环境

| 项目 | 版本 / 位置 |
|---|---|
| Python | 3.12（本机 3.12.6，`C:\Python312`） |
| NVIDIA 驱动 | 本机 591.86（支持 CUDA 13.1）；需支持 CUDA 12.8 及以上 |
| PyTorch | 见 `requirements-torch.txt`，从 `https://download.pytorch.org/whl/cu128` 安装 |
| 其他依赖 | 见 `requirements.txt`（diffusers、transformers、accelerate 等，已锁版本） |
| 虚拟环境 | `tools/ArtGen/.venv`（不入库） |
| 模型 | `stabilityai/stable-diffusion-xl-base-1.0`（fp16，约 7 GB，CreativeML Open RAIL++-M，允许商用）；`madebyollin/sdxl-vae-fp16-fix`（MIT） |
| 模型缓存 | `%USERPROFILE%\.cache\huggingface`；设置 `HF_HOME` 可改到其他磁盘 |

## 本机实测（2026-09-24）

- `setup.ps1` 等效步骤在笔记本完成：torch 2.11.0+cu128、diffusers 0.40.0，`torch.cuda.is_available()` 为真。
- SDXL fp16 + CPU 卸载，1024×1024、30 步：约 30 秒一张。家用台式机整体放入显存，预计明显更快，首次运行后补记。
- 基础 SDXL 对器物造型把握不稳（例：剑会画成日式刀或多出零件），每项生成 4 个种子挑选，必要时人工修整；负面词挡不住偶发的印章，入库前裁掉。
- diffusers 0.40 起 VAE 分块写作 `pipe.vae.enable_tiling()`；`torch_dtype` 参数有弃用警告，不影响结果。

## 复现步骤

```powershell
# 在仓库根目录；首次约需下载 PyTorch 3 GB 与模型 7 GB
./tools/ArtGen/setup.ps1                  # 或 -Python 'C:\Python312\python.exe'
cd tools/ArtGen
.venv/Scripts/python generate.py jobs/m0_simple.json --dry-run   # 只看提示词
.venv/Scripts/python generate.py jobs/m0_simple.json --only icon_  # 生成
```

## 任务文件

`jobs/*.json` 描述一批图：共享风格片段（`style`）、负面词、尺寸、步数、种子，以及每张图的名称和主体描述。提示词使用英文，SDXL 对中文理解差。负面词固定排除文字、印章、水印、动漫与日式画风。

每张输出旁有同名 `.json`：模型、VAE、种子、尺寸、步数、CFG、完整提示词、SHA-256、耗时和显卡型号。入库时连同记录一起复制。

## 许可与限制

- 更换或新增模型、LoRA 前先确认许可允许商用，并更新本文件与资产台账。
- AI 输出须人工修整：人物一致性、手部、兵器结构和服饰年代最容易出错；不能直接当作分层动画资产（架构文档 10.3）。
