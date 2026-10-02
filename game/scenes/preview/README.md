# M0 展示页

每个展示页一个场景，放在本目录，例如 `ExploreTown.tscn`。制作完成后在
`scripts/preview/PreviewPages.cs` 为对应条目填写 `ScenePath`，目录页即可进入。
页面清单与验收见[开发计划第 1.2 节](../../../docs/DEVELOPMENT_PLAN.md#12-第一阶段场景与-ui-展示清单)。

菜单类页面继承 `scripts/preview/PreviewScreen.cs`（虚化山水底、顶栏页名章与分区签、绢本子页签、键帽底栏）；
标题、对话、战斗、探索 HUD 等全屏页直接继承 `Control`。脚本放 `scripts/preview/Pages/`，固定样例放
`scripts/preview/Samples/`；对话页台词取自正式对白文件 `content/dialogue/`（经内容包读取），不另存样例对白。视觉规范见 [docs/art/UI_DESIGN.md](../../../docs/art/UI_DESIGN.md)。

探索布景（`ExploreStage` 与城镇、客栈、山路三类布景页及其布局数据）同时被游戏内探索页 `scenes/world/Exploration.tscn` 经 `IExploreDriver` 复用（M2，见架构文档 9.4.1）；改动布景时要同时检查展示页与 `--check-staging`。标题页也已接上真实存档。
