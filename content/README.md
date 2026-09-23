# 结构化内容源

手工维护的 UTF-8 JSON，是内容的唯一来源；`game/generated/content/` 由 ContentCompiler 生成，禁止手改。
ID 规则、对象字段与校验要求见[架构文档第 9 节](../docs/ARCHITECTURE.md#9-剧情任务与内容数据)。

- `schema/`：JSON Schema
- `shared/{skills,items,statuses}/`：跨地区共享定义
- `characters/`：角色定义、原著剧情锚点与关系
- `regions/<region_id>/{maps,quests,dialogues}/`：按地区拆分的地图、任务与对白

M0 不在此放样例数据；展示用样例由 `game/scripts/preview` 提供。
