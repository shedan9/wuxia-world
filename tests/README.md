# 测试

测试框架于 M1（2026-09-30）锁定：xUnit 2.9.3、xunit.runner.visualstudio 3.1.4、Microsoft.NET.Test.Sdk 17.14.1（架构文档 4.2）；升级另开分支回归。

- `Domain/WuxiaWorld.Domain.Tests`：伤害层次、回合次序、冷却与持续时间、控制与反应、非法命令不改状态、确定性（100 次同哈希）、重放、4 对 6、首领机制与内容校验。运行：`dotnet test tests/Domain/WuxiaWorld.Domain.Tests`
- `Application/`：旅行回滚、奖励幂等、任务互斥、事件占用（M2 起）
- `Integration/`：Godot 加载、输入、UI 焦点、存读档（M2 起）
- `Fixtures/`：固定测试存档与内容样本（M2 起）

测试读取仓库里的正式 `content/`，只在测试代码中叠加测试专用招式、单位与遭遇（`skill.test.*`、`t.*`、`battle.test.*`），不写回内容源。
