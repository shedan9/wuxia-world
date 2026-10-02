# 对白章节索引

全部台词（含展示样例）按三篇 18 章存为独立文件。2026-10-01 起对白已工程化：每章一个 `content/dialogue/arcXX/chapterXX.json`，它是该章台词的**唯一可编辑来源**，游戏运行、对话展示页与后续配音清单都从它（经内容编译器生成的 `world.json`）读取，不再另存 Markdown 副本。规则见[架构文档第 9.1、9.4 节](../ARCHITECTURE.md#94-m2-实现规格)与 [AGENTS.md](../../AGENTS.md)。

## 文件格式

章节文件顶层为 `arc`、`chapter`、`status`（整章状态说明）与 `dialogues`。每段对白：

- `id`：`dlg.chXX.<场景>`，由地区事件或交互物引用；`status`：`draft`（未锁稿）或 `locked`（锁稿，可配音）。
- `entry` 与 `nodes`：节点类型 `line`（台词）、`choice`（选项）、`branch`（条件分流）、`effect`（效果）、`jump`、`end`。
- 台词节点：`line_id`（`chXX.<场景>.<说话人>.<序号>`，稳定不改）、`speaker`（人物 ID）、`text`、可选 `mood`，`next` 指向下一节点。
- **不写旁白**（2026-10-01 用户决定）。场景、动作与物件细节写成演出提示节点 `type: stage`：`kind`（`scene` 场景 / `action` 动作 / `closeup` 特写 / `title` 标题卡）、`direction`（制作说明，玩家看不到）；特写与标题卡可加 `caption` 与 `caption_id`，显示画面上的物件文字或章节标题。演出提示不配音；线索结论须由人物说出，不能只靠画面暗示。
- **心里话**：只允许主角，台词节点加 `"inner": true`，文字不带括号；只显示字幕、不配音。单句不超过 30 字，不连续出现，全书心里话不超过台词的一成。其他角色不写心里话。
- 选项：`line_id`、`text`、可选 `when` / `locked_hint` / `effects`、`next`。

阅读时按 `entry` 顺着 `next` 读即可；分支条件与效果的写法见架构文档 9.4。修改已用的 `line_id` 须同步配音清单；修改后运行 `dotnet run --project tools/ContentCompiler -- --check` 校验，`dotnet test tests/Domain/WuxiaWorld.Domain.Tests` 中的第一章走查会检查每句台词都能被走到。

审阅时可运行 `python tools/scripts/export-dialogue.py` 生成按场景排列的阅读版（写到 `build/review/dialogue/`，不入库）。阅读版只供阅读，修改仍改 JSON。

## 索引

| 篇 | 章 | 文件 | 状态 |
|---|---|---|---|
| 第一篇《众路归潮》 | 第一章 江南会客 | [content/dialogue/arc01/chapter01.json](../../content/dialogue/arc01/chapter01.json) | 第一稿，未锁稿（2026-10-01）：15 段 104 句台词（主角心里话 5 句，不配音）、27 个演出提示（同日按用户要求删去 26 句旁白，改为演出提示；补主角说出副页签押结论、陆青禾认出杜三篙各 1 句），覆盖主线全部可达分支与失踪渡工支线；开场 `dlg.ch01.opening_luwan` 沿用 M0 展示样例的 `line_id` 与台词。经典人物剧情锚点待核，核验并经试玩修订后锁稿 |

其余 17 章尚未编写。
