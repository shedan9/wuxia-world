# 结构化内容源

手工维护的 UTF-8 JSON，是内容的唯一来源；`game/generated/content/` 由 ContentCompiler 生成，禁止手改。
ID 规则、对象字段与校验要求见[架构文档第 9 节](../docs/ARCHITECTURE.md#9-剧情任务与内容数据)，战斗规则取值见[第 7.6 节](../docs/ARCHITECTURE.md#76-m1-实现规格)。

- `schema/`：JSON Schema（M2 起；M1 的战斗内容由 `CombatContentValidator` 做语义校验）
- `shared/skills/`：招式（`basic`、`sword`、`fist`、`inner`、`staff` 与敌方 `enemy`）
- `shared/statuses/`：状态；`core.json` 为内核必需的防御、破绽、调息、蓄力、护援、免控、点穴
- `shared/arts/`：心法、轻功与被动天赋
- `shared/items/`：`catalog.json` 物品目录（类别、堆叠、价格、主线必要物品；装备类——`weapon`、`armor`、`boots`、`accessory`、`charm`——须单件并带 `bonus` 固定加成）；`battle.json` 战斗消耗品的使用效果（每条须在目录中有条目）
- `shared/combat/counters.json`：克制倍率表（限定 0.8–1.25）
- `shared/text/zh-Hans.json`：中文文本表，键为 `<id>.name` / `<id>.desc` 等；各地区另有 `regions/<region_id>/text/zh-Hans.json`，键不得重复。任务阶段与目标的键为 `<任务>.stage.<阶段>`、`<任务>.objective.<目标>`；地图交互物的交互提示对象名为 `<地图>.<交互物>.name`，需要走近按 E 开始的地区事件（非 `auto`）须有 `<事件>.verb`（动作，如“讨教”）与 `<事件>.name`（对象）——校验器缺一即报错
- `characters/`：`characters.json` 人物定义（来源、剧情锚点，未选定写 `pending`，可选战斗模板；能入队的人物写同行身份 `party`：`temporary` 暂时同行 / `recruitable` 可招募伙伴，见架构文档 9.4.4）；`anchors.json` 经典人物的原著剧情锚点（所据文本、章节区间、原文年龄依据与本作年龄、身份、已发生与不得预知的事件，考据档案见 `docs/canon/ANCHORS.md`）；`combat_presets.json` 为 M1 战斗预设（主角三流派、陆青禾与占位同行者；剧情战的主角自 M2-05 起由成长数据现推，预设只供战斗原型页与模拟器）
- `world/new_game.json`：新游戏的起点、队伍、银两、初始物品与开局效果
- `world/progression.json`：成长设置——升级累计经验表、主角基础属性与形象、熟练度消耗与每阶强度、三种流派（`fact.hero.style` 的取值）的入门武学与推荐加点、开局衣物、旧档追赶表（见架构文档 9.4.2）
- `world/world_map.json`：江湖大地图——设计稿尺寸、地标（`node.*`：地域、图标、坐标、所属小地图、出现条件、开放条件文本键）与图上道路（陆路 / 水路、途经点）；能否通行仍由各地域的 `routes/` 决定（见架构文档 6.5）
- `dialogue/arcXX/chapterXX.json`：逐章对白，台词的唯一可编辑来源，格式见 [docs/dialogue/README.md](../docs/dialogue/README.md)
- `regions/<region_id>/{maps,routes,events,quests,shops,text,combatants,encounters}/`：按地区拆分的地图、路线、地区事件、任务、店铺、文本、敌人与遭遇；第一章为 `regions/jiangnan/`。店铺由地图里 `kind: "shop"` 的交互物（带 `shop` 字段）打开；遭遇可给 `experience` 与 `cultivation`

字段一律蛇形小写，枚举值也是蛇形小写（如 `"target_rule": "single_reachable_enemy"`）；未知字段直接报错。

修改后运行 `dotnet run --project tools/ContentCompiler` 校验并生成内容包 `combat.json` 与 `world.json`（导出脚本会自动运行）；剧情改动再跑 `dotnet test tests/Domain/WuxiaWorld.Domain.Tests` 中的走查；数值改动再用 `dotnet run --project tools/BattleSimulator` 对比胜率与轮数。

`combatant.placeholder.companion`（同行者（占位））只用于旧渡水门战斗原型的第三人，正式 Demo 由经典人物援手替换，接入前须完成人物剧情锚点核验（开发计划 M2-10）。M0 展示用样例仍只在 `game/scripts/preview`。
