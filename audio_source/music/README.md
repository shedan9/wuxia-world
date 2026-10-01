# 配乐母带

用户选定的 BGM 工作稿（无损 WAV，经 Git LFS 管理）。由 `tools/MusicGen` 用本地 ACE-Step 1.5 生成并剪辑，每首旁的同名 `.json` 记录模型、种子、任务文件与逐步剪辑过程；资产来源登记在[资产台账](../../docs/art/ASSET_LEDGER.md#ai-配乐)。

| 文件 | 曲目 | 状态 |
|---|---|---|
| `bgm.town.luwan.wav` | 芦湾水镇·探索 | 选定，待剪循环与统一响度 |
| `bgm.battle.common.wav` | 普通战斗 | 选定，待剪循环与统一响度 |
| `bgm.boss.old_ferry.wav` | 旧渡首领战 | 选定，待剪循环与统一响度 |

使用原则见架构文档 10.5 节：BGM 只配主要场景，剧情中只在急转、高潮或情绪变化处进入。运行时 Ogg 导出到 `game/assets/audio/` 时再定循环点与响度。
