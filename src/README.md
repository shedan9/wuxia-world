# 规则类库

依赖方向：`Infrastructure → Application → Domain`，Godot 工程（`game/`）引用这三者。详见[架构文档第 5 节](../docs/ARCHITECTURE.md#5-系统架构)。

| 项目 | 职责 | 建设阶段 |
|---|---|---|
| `WuxiaWorld.Domain` | 战斗、成长、世界、任务、关系的规则与可序列化状态；不引用 Godot、文件系统或系统时间 | M1 起 |
| `WuxiaWorld.Application` | 命令、事务、领域事件与端口接口 | M1–M2 |
| `WuxiaWorld.Infrastructure` | 存档文件、JSON 内容加载等端口实现 | M2 起 |

M0 视觉 Demo 不在这里放规则代码；展示用样例数据只存在于 `game/scripts/preview`（`PreviewSession`），不得混入这些类库。
