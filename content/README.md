# 结构化内容源

手工维护的 UTF-8 JSON，是内容的唯一来源；`game/generated/content/` 由 ContentCompiler 生成，禁止手改。
ID 规则、对象字段与校验要求见[架构文档第 9 节](../docs/ARCHITECTURE.md#9-剧情任务与内容数据)，战斗规则取值见[第 7.6 节](../docs/ARCHITECTURE.md#76-m1-实现规格)。

- `schema/`：JSON Schema（M2 起；M1 的战斗内容由 `CombatContentValidator` 做语义校验）
- `shared/skills/`：招式（`basic`、`sword`、`fist`、`inner`、`staff` 与敌方 `enemy`）
- `shared/statuses/`：状态；`core.json` 为内核必需的防御、破绽、调息、蓄力、护援、免控、点穴
- `shared/arts/`：心法、轻功与被动天赋
- `shared/items/`：`catalog.json` 物品目录（类别、堆叠、价格、主线必要物品）；`battle.json` 战斗消耗品的使用效果（每条须在目录中有条目）
- `shared/combat/counters.json`：克制倍率表（限定 0.8–1.25）
- `shared/text/zh-Hans.json`：中文文本表，键为 `<id>.name` / `<id>.desc` 等；各地区另有 `regions/<region_id>/text/zh-Hans.json`，键不得重复。任务阶段与目标的键为 `<任务>.stage.<阶段>`、`<任务>.objective.<目标>`；地图交互物的交互提示对象名为 `<地图>.<交互物>.name`，需要走近按 E 开始的地区事件（非 `auto`）须有 `<事件>.verb`（动作，如“讨教”）与 `<事件>.name`（对象）——校验器缺一即报错
- `characters/`：`characters.json` 人物定义（来源、剧情锚点，未核验写 `pending`，可选战斗模板）；`combat_presets.json` 为 M1 战斗预设（主角三流派、陆青禾与占位同行者）
- `world/new_game.json`：新游戏的起点、队伍、银两、初始物品与开局效果
- `dialogue/arcXX/chapterXX.json`：逐章对白，台词的唯一可编辑来源，格式见 [docs/dialogue/README.md](../docs/dialogue/README.md)
- `regions/<region_id>/{maps,routes,events,quests,text,combatants,encounters}/`：按地区拆分的地图、路线、地区事件、任务、文本、敌人与遭遇；第一章为 `regions/jiangnan/`

字段一律蛇形小写，枚举值也是蛇形小写（如 `"target_rule": "single_reachable_enemy"`）；未知字段直接报错。

修改后运行 `dotnet run --project tools/ContentCompiler` 校验并生成内容包 `combat.json` 与 `world.json`（导出脚本会自动运行）；剧情改动再跑 `dotnet test tests/Domain/WuxiaWorld.Domain.Tests` 中的走查；数值改动再用 `dotnet run --project tools/BattleSimulator` 对比胜率与轮数。

`combatant.placeholder.companion`（同行者（占位））只用于旧渡水门战斗原型的第三人，正式 Demo 由经典人物援手替换，接入前须完成人物剧情锚点核验（开发计划 M2-10）。M0 展示用样例仍只在 `game/scripts/preview`。
