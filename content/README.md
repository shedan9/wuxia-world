# 结构化内容源

手工维护的 UTF-8 JSON，是内容的唯一来源；`game/generated/content/` 由 ContentCompiler 生成，禁止手改。
ID 规则、对象字段与校验要求见[架构文档第 9 节](../docs/ARCHITECTURE.md#9-剧情任务与内容数据)，战斗规则取值见[第 7.6 节](../docs/ARCHITECTURE.md#76-m1-实现规格)。

- `schema/`：JSON Schema（M2 起；M1 的战斗内容由 `CombatContentValidator` 做语义校验）
- `shared/skills/`：招式（`basic`、`sword`、`fist`、`inner`、`staff` 与敌方 `enemy`）
- `shared/statuses/`：状态；`core.json` 为内核必需的防御、破绽、调息、蓄力、护援、免控、点穴
- `shared/arts/`：心法、轻功与被动天赋
- `shared/items/`：物品（M1 只有战斗消耗品）
- `shared/combat/counters.json`：克制倍率表（限定 0.8–1.25）
- `shared/text/zh-Hans.json`：中文文本表，键为 `<id>.name` / `<id>.desc` 等
- `characters/`：角色定义、原著剧情锚点与关系；M1 暂放 `combat_presets.json`（主角三流派、陆青禾与占位同行者的战斗预设）
- `regions/<region_id>/{maps,quests,dialogues,combatants,encounters}/`：按地区拆分的地图、任务、对白、敌人与遭遇；第一章为 `regions/jiangnan/`

字段一律蛇形小写，枚举值也是蛇形小写（如 `"target_rule": "single_reachable_enemy"`）；未知字段直接报错。

修改后运行 `dotnet run --project tools/ContentCompiler` 校验并生成内容包（导出脚本会自动运行）；数值改动再用 `dotnet run --project tools/BattleSimulator` 对比胜率与轮数。

`combatant.placeholder.companion`（同行者（占位））只用于旧渡水门战斗原型的第三人，正式 Demo 由经典人物援手替换，接入前须完成人物剧情锚点核验（开发计划 M2-10）。M0 展示用样例仍只在 `game/scripts/preview`。
