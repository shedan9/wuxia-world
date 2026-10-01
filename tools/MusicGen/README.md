# MusicGen：本地 AI 配乐生成

用本地 [ACE-Step 1.5](https://github.com/ace-step/ACE-Step-1.5)（MIT，生成物可商用）在家用台式机上生成 BGM 候选。输出只在 `build/music/`（不入库）；用户试听选定后，母带进 `audio_source/`（LFS），运行时 Ogg 进 `game/assets/audio/`，并在资产台账登记来源、模型与种子。

## 为什么选 ACE-Step 1.5

2026-10-01 比较过的本地方案：MiniMax 音乐接口自 2026-08-20 起不对新用户开放；MiniMax-Music3 开放权重为社区许可（第三方另有不可商用的解读，待核），且没有局部重画；HeartMuLa、YuE 偏带歌词的歌曲；MusicGen 权重不可商用；Stable Audio Open 只适合短的环境声与音效。ACE-Step 1.5 有局部重画（repaint）、续写和风格 LoRA，便于修小节、剪循环和统一全作配乐的风格。

## 环境

| 项目 | 说明 |
|---|---|
| 位置 | `C:\code\ACE-Step-1.5`（仓库外，`git clone --depth 1`；环境变量 `ACESTEP_HOME` 可改） |
| 依赖 | `uv sync`（uv 装在 `%USERPROFILE%\.local\bin`），虚拟环境在 `C:\code\ACE-Step-1.5\.venv` |
| 模型 | `checkpoints/` 下：主包 `ACE-Step/Ace-Step1.5`（VAE、文本编码器、LM 1.7B、2B turbo）与 `ACE-Step/acestep-v15-xl-turbo`（4B DiT）；16 GB 显存下 XL 需 CPU 卸载 |

## 用法

```powershell
# 在仓库根目录
C:/code/ACE-Step-1.5/.venv/Scripts/python tools/MusicGen/sample.py tools/MusicGen/jobs/m1_bgm_samples.json --dry-run
C:/code/ACE-Step-1.5/.venv/Scripts/python tools/MusicGen/sample.py tools/MusicGen/jobs/m1_bgm_samples.json --only bgm.town.luwan --seeds 11
```

任务文件：`model` / `lm` 选模型；每首曲目给 `caption`（英文描述：情绪、乐器、禁用项）、`structure`（只写段落标签不写词，生成纯音乐）、`bpm`、`keyscale`、`duration` 与 `seeds`。脚本关闭 LM 对描述、节拍与调式的改写，结果写到 `build/music/samples/<任务名>/`：WAV、`manifest.json`（参数、种子、SHA-256、耗时、审核状态）与试听页 `index.html`。参数与种子不变的条目不重复生成；`--force` 强制重跑（旧文件改名 `.prev.wav`）。每条结果另存 LM 音乐编码 `.codes.txt`，曲目写 `codes` 可沿用它只换 DiT 渲染。

**剪辑选中的曲子：** `edit.py jobs/<任务>.json`（`--dry-run` 只打印对齐后的时间）。每个 edit 给底稿 `base`、同一时间轴的段落替换 `splice`（两稿须同一节拍网格，例如同一底稿的两版 cover）、局部换音色 `rerender`（整首以底稿加噪为起点由 XL 做 cover，`rerender_noise` 即 `cover_noise_strength`，越大越接近原稿，只取指定区间换回，保留音符与节奏）与 XL 重画区间 `repaint`（区间内从噪声重新生成，旋律会变；强度只影响边界过渡）；`grid_from` 指定估节拍用的原稿；`snap: true` 时所有时间点对齐到底稿估出的拍点，`inner: true` 让重画区间只向内对齐，不越过须原样保留的段落。重画区间外由 ACE-Step 拼回原波形（实测与原稿逐采样一致），但输出整首按峰值 −1 dB 归一化；脚本用改动区以外与底稿比较求出这个增益并还原（还原后会削波时不还原，清单 `gain_restored` 注明），最终响度在母带阶段统一。结果在 `build/music/edits/<任务名>/`，试听页同时列出原稿。

**正式配乐用 XL**（2026-10-01 用户试听：2B 转音生硬、结构拼凑）。16 GB 显存下 XL 须在任务文件写 `"offload_to_cpu": true, "quantization": null`，并且 cover 任务不加载 LM（`"lm": null`），峰值约 12 GB、100 秒一首 5–7 秒；不卸载时溢出到共享内存要 19 分钟一首，开 INT8 则加载时耗尽 32 GB 内存。Windows 上 LM 采样不随种子复现，选中的 2B 结果不能“记种子换模型”，改用 `"task": "cover", "src_audio": "<相对 build/music/samples 的路径>", "cover_strength": 1.0` 由 XL 重新渲染（例：`jobs/m1_bgm_xl.json`）。用户选中的原始文件先复制到 `picks/` 再做其他操作。

**定点去掉或收敛一个音：** `note_gate.py <输入> <输出> --f0 <低> <高> --start <秒> --end <秒> --mode remove|swell [--full-at <秒>] [--depth -50 --harmonics 12]`。逐帧跟踪基频，只衰减该音的基频与泛音（remove 删掉，swell 渐强到 `--full-at` 恢复原音量），其他声音不动、处理段外逐采样不变，并写 `<输出>.json` 记录参数与哈希。AI 重画会连带改动其他声音时用它。

**任务文件一览：** `m1_bgm_samples`（第一轮样带）、`m1_bgm_xl`（XL cover 重渲染）、`m1_bgm_edit` 至 `m1_bgm_edit11`（逐轮剪辑，每个文件的 `note` 记录当轮用户意见与做法；`edit8`、`edit9` 的结果最终未采用；第 7、10 轮是 `note_gate.py` 与差值合成的手工步骤，没有任务文件，过程记在母带旁的 `.json`）、`m1_bgm_probe_*`（探针，验证描述词与参数是否真的起作用）。选定结果的母带与来源记录在 `audio_source/music/`。
