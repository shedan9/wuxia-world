# 规则类库

依赖方向：`Infrastructure → Application → Domain`，Godot 工程（`game/`）引用这三者。详见[架构文档第 5 节](../docs/ARCHITECTURE.md#5-系统架构)。

| 项目 | 职责 | 现状 |
|---|---|---|
| `WuxiaWorld.Domain` | 战斗、成长、世界、任务、关系的规则与可序列化状态；不引用 Godot、文件系统或系统时间 | M1：`Common`（PCG32、万分比、状态哈希）、`Characters`（属性、派生公式、心法、装配校验）、`Combat`（内容定义、状态、命令、事件、内核、AI）；M2：`World`（世界状态、条件与效果、任务状态机、对话图、地区事件与人物占用） |
| `WuxiaWorld.Application` | 命令、事务、领域事件与端口接口 | M1：`Combat/BattleSession`（AI 决策、命令与哈希记录、重放）；M2：`World/GameSession`（对话、交互、换图、战斗结算事务）、`Persistence`（存档槽、存档头、`ISaveStore`、相容性检查） |
| `WuxiaWorld.Infrastructure` | 存档文件、JSON 内容加载等端口实现 | M1：`Content`（内容 JSON 选项、加载、语义校验、合成包）；M2：世界内容包与校验、`Saves/FileSaveStore`（校验和、备份回退、迁移） |

战斗规则的实际取值见[架构文档 7.6](../docs/ARCHITECTURE.md#76-m1-实现规格)，世界规则与存档见 [9.4](../docs/ARCHITECTURE.md#94-m2-实现规格) 与第 11 节。M0 视觉 Demo 不在这里放规则代码；展示用样例数据只存在于 `game/scripts/preview`（`PreviewSession`），不得混入这些类库。
