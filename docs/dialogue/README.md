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

**战斗喊声**（2026-10-04 起，“关键战斗语音”）：每章另存 `content/dialogue/arcXX/chapterXX_battle.json`，顶层同为 `arc`、`chapter`、`status`，正文为 `barks` 列表。每句 `line_id`（`chXX.battle.…`，与对白共用唯一性）、`speaker`、`trigger`（`battle_start` / `skill` / `charge` / `interrupted` / `phase` / `low_hp` / `downed` / `victory`；`interrupted` 为本人蓄力被打断，2026-10-05 增）、`priority`（1 普通、2 重要、3 关键；倍速只说 3）、`text`（不超过 20 字，短促口语），按需加 `unit`（敌方说话人的单位 ID）、`encounter`、`skill`、`phase`、`cooldown_rounds`（招式句隔几轮可再说）。战斗中由规则挑句，同一时机只说一句；规格见[架构文档 9.4.9](../ARCHITECTURE.md#949-m3-06-关键战斗语音与对话自动推进规格)。喊声同样配音、同样经试玩修订后锁稿。

阅读时按 `entry` 顺着 `next` 读即可；分支条件与效果的写法见架构文档 9.4。修改已用的 `line_id` 须同步配音清单；修改后运行 `dotnet run --project tools/ContentCompiler -- --check` 校验，`dotnet test tests/Domain/WuxiaWorld.Domain.Tests` 中的第一章走查会检查每句台词都能被走到。

审阅时可运行 `python tools/scripts/export-dialogue.py` 生成按场景排列的阅读版（写到 `build/review/dialogue/`，不入库；同章的战斗喊声按时机列在末尾）。阅读版只供阅读，修改仍改 JSON。

## 索引

| 篇 | 章 | 文件 | 状态 |
|---|---|---|---|
| 第一篇《众路归潮》 | 第一章 江南会客 | [content/dialogue/arc01/chapter01.json](../../content/dialogue/arc01/chapter01.json) | 第一稿，未锁稿（2026-10-01）：15 段 104 句台词（主角心里话 5 句，不配音）、27 个演出提示（同日按用户要求删去 26 句旁白，改为演出提示；补主角说出副页签押结论、陆青禾认出杜三篙各 1 句），覆盖主线全部可达分支与失踪渡工支线；开场 `dlg.ch01.opening_luwan` 沿用 M0 展示样例的 `line_id` 与台词。2026-10-05 增补讨教后的后院切磋：讨教收尾三位侠客各一句邀约（`dlg.ch01.mentor_choice`），切磋邀约 `dlg.ch01.mentor_spar`（9 句、选项 2、演出提示 1）与战后点评 `dlg.ch01.mentor_spar_after`（8 句、演出提示 2），合计现为 17 段 124 句、30 个演出提示；新句试听配音已全部生成装入（2026-10-05，未经人耳审核）。经典人物剧情锚点待核，核验并经试玩修订后锁稿 |
| 第一篇《众路归潮》 | 第一章 战斗喊声 | [content/dialogue/arc01/chapter01_battle.json](../../content/dialogue/arc01/chapter01_battle.json) | 第一稿，未锁稿（2026-10-04）：32 句关键战斗语音——押运队与水门开战 4、三侠个人招式 8、唐守亭蓄力预兆与阶段 4、重伤 6、倒下 6、胜利 4；试听配音已生成（2026-10-04，未经人耳审核）。2026-10-05 增补后院切磋 12 句（三位侠客开场、蓄力起手提示、被破招反应各 3，主角胜后“承让”3），现共 44 句；新句试听配音已全部装入。武学分式名待原著对校 |

其余 17 章尚未编写。
