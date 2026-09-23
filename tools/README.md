# 开发工具

| 目录 | 用途 | 建设阶段 |
|---|---|---|
| `scripts/` | 本地构建与导出脚本；CI 使用同一入口 | M0 |
| `ContentCompiler/` | Schema 与语义校验，生成 `game/generated/content` | M2–M4 |
| `BattleSimulator/` | 固定种子批量战斗模拟 | M1 |
| `VoiceBuilder/` | AI 配音批量生成、审核状态与清单 | M1–M3 |

Windows 导出：`$env:GODOT_BIN = '<Godot .NET 编辑器路径>'; ./tools/scripts/export-windows.ps1`
