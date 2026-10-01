# 引擎适配

需要 Godot API 的端口实现（输入、音频播放、线程加载等）放在这里；纯文件与 JSON 适配放在 `src/WuxiaWorld.Infrastructure`。

- `GeneratedContent`：经 Godot 文件接口读取 `res://generated/content/` 下的 `combat.json` 与 `world.json`（导出包内同样可用）。
