# 测试

测试框架与 NuGet 版本在 M1 锁定（架构文档 4.2），届时在各子目录建立测试项目并加入 `WuxiaWorld.sln`。

- `Domain/`：伤害边界、回合次序、效果持续期等规则测试
- `Application/`：旅行回滚、奖励幂等、任务互斥、事件占用
- `Integration/`：Godot 加载、输入、UI 焦点、存读档
- `Fixtures/`：固定测试存档与内容样本
