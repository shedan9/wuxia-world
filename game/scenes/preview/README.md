# M0 展示页

每个展示页一个场景，放在本目录，例如 `ExploreTown.tscn`。制作完成后在
`scripts/preview/PreviewPages.cs` 为对应条目填写 `ScenePath`，目录页即可进入。
页面清单与验收见[开发计划第 1.2 节](../../../docs/DEVELOPMENT_PLAN.md#12-第一阶段场景与-ui-展示清单)。

界面类页面继承 `scripts/preview/PreviewScreen.cs`（印章标题、竖排页签、宣纸页面），
脚本放 `scripts/preview/Pages/`，固定样例放 `scripts/preview/Samples/`。
