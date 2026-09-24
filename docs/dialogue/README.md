# 对白章节索引

全部台词（含展示样例）按三篇 18 章存为独立文件：规划阶段为 `arcXX/chapterXX.md`，工程化后可迁至 `content/dialogue/arcXX/chapterXX.json`，但只保留一套可编辑源。M0 对话展示页需要在导出包内运行时读取台词，因此 M0 样例放在游戏工程的对白内容目录 `game/dialogue/arcXX/chapterXX.md`（导出预设 `include_filter` 已包含），格式写在各文件开头；正式对白工程化时再统一迁移。规则见[架构文档第 9.1 节](../ARCHITECTURE.md#91-对话与任务)与 [AGENTS.md](../../AGENTS.md)。

| 篇 | 章 | 文件 | 状态 |
|---|---|---|---|
| 第一篇《众路归潮》 | 第一章 江南会客 | [game/dialogue/arc01/chapter01.md](../../game/dialogue/arc01/chapter01.md) | M0 展示样例，未锁稿；仅 `event.ch01.opening_luwan` 一段 |
