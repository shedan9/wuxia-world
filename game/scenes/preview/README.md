# M0 展示页

每个展示页一个场景，放在本目录，例如 `ExploreTown.tscn`。制作完成后在
`scripts/preview/PreviewPages.cs` 为对应条目填写 `ScenePath`，目录页即可进入。
页面清单与验收见[开发计划第 1.2 节](../../../docs/DEVELOPMENT_PLAN.md#12-第一阶段场景与-ui-展示清单)。

菜单类页面继承 `scripts/preview/PreviewScreen.cs`（虚化山水底、顶栏页名章与分区签、玉版子页签、键帽底栏）；
标题、对话、战斗、探索 HUD 等全屏页直接继承 `Control`。脚本放 `scripts/preview/Pages/`，固定样例放
`scripts/preview/Samples/`，样例对白放 `game/dialogue/`。视觉规范见 [docs/art/UI_DESIGN.md](../../../docs/art/UI_DESIGN.md)。
