using System.Text.Json;
using Godot;
using WuxiaWorld.Game.Presentation.Art;
using WuxiaWorld.Game.Preview.Samples;

namespace WuxiaWorld.Game.Preview.Pages;

/// <summary>
/// 芦湾街的光影（M3-01）：按光色时段（<see cref="SceneTime"/>）整体调色、铺光色层，房屋、廊棚、桥栏与杂件把投影压到地上，
/// 墙脚压一道接地暗影；黄昏起客栈窗纸与灯笼发光、廊下灯笼点亮，地上铺暖色光斑。
/// 白天与河滩同为雨后初晴的申时（同一套偏冷调色），黄昏与旧渡同一套暖调。
/// </summary>
public partial class ExploreTownPreview
{
    /// <summary>午后日在西南偏高处（<see cref="TownView.Sun"/>，高约 45°）：每单位高度投影向东北偏北伸出约一个单位。</summary>
    private static readonly Vector2 ShadowDirection = new(0.49f, -0.87f);

    private static readonly Color LampWarm = new(1f, 0.66f, 0.32f);

    private SceneShadows? _waterShadows;
    private SceneShadows? _shadows;

    private (Color Tint, float ShadowLength, float ShadowAlpha, float Lamp) Look => Light switch
    {
        SceneTime.Dusk => (new Color(1f, 0.88f, 0.76f), 1.7f, 0.24f, 0.55f),
        SceneTime.Night => (new Color(0.4f, 0.47f, 0.66f), 0.5f, 0.14f, 1f),
        _ => (new Color(0.97f, 1f, 1f), 1f, 0.2f, 0f),
    };

    /// <summary>落在河面上的投影（平桥桥面）：画在驳岸与桥面之前，免得压到桥面本身。</summary>
    private void BeginWaterShadows()
    {
        _waterShadows = new SceneShadows(Look.ShadowAlpha * 0.8f);
        GroundLayer.AddChild(_waterShadows);
        _waterShadows.Cast(TownGroundDetail.BridgeFaces().Select(f => f.Points), ShadowDirection * Look.ShadowLength, TownSamples.WaterLevel);
    }

    /// <summary>落在地上与桥面上的投影与接地暗影：画在地面细节之后、排序件之前。</summary>
    private void BeginGroundShadows()
    {
        _shadows = new SceneShadows(Look.ShadowAlpha);
        GroundLayer.AddChild(_shadows);
        GroundLayer.AddChild(new TownContactShade(Light == SceneTime.Night ? 0.2f : 0.3f));
    }

    /// <summary>件都搭好后：投影、调色、光色层与灯火。</summary>
    private void FinishLight(IReadOnlyList<TownHouseNode> houses)
    {
        var (tint, length, _, lamp) = Look;
        var shift = ShadowDirection * length;
        foreach (var piece in Pieces)
        {
            if (piece is TownPropNode { IsBoat: true } || piece is TownTreeNode || piece.Walker)
            {
                continue;
            }

            _shadows!.Cast(piece.ShadowFaces, shift);
        }

        TintWorld(tint);
        OverheadLayer.AddChild(Light switch
        {
            SceneTime.Dusk => new SceneWash(Color.FromHtml("#FFB66B") with { A = 0.2f }, Color.FromHtml("#FFB66B") with { A = 0.08f },
                Color.FromHtml("#2A3550") with { A = 0.16f }, Color.FromHtml("#FFB66B") with { A = 0.06f }) { Area = ViewArea },
            SceneTime.Night => new SceneWash(Color.FromHtml("#0E1830") with { A = 0.3f }, Color.FromHtml("#0E1830") with { A = 0.18f },
                Color.FromHtml("#05080F") with { A = 0.3f }, Color.FromHtml("#0E1830") with { A = 0.22f }) { Area = ViewArea },
            // 雨后初晴：上方一层湿润天光，下方略偏青（与河滩同一套）。
            _ => new SceneWash(Color.FromHtml("#E8F0EE") with { A = 0.08f }, Color.FromHtml("#E8F0EE") with { A = 0.1f },
                Color.FromHtml("#4F7480") with { A = 0.06f }, Color.FromHtml("#4F7480") with { A = 0.05f }) { Area = ViewArea },
        });

        if (lamp <= 0)
        {
            return;
        }

        var pools = new SceneLamps(tint);
        GroundLayer.AddChild(pools);
        var warm = LampWarm with { A = 0.36f * lamp };

        // 廊下灯笼：檐下挂点处光晕（随屋面一起淡出），廊下地上一片光。
        foreach (var roof in Pieces.OfType<TownCorridorPart>().Where(p => p.LanternTops.Count > 0))
        {
            var halos = new SceneLamps(tint);
            foreach (var top in roof.LanternTops)
            {
                halos.Halo(TownView.P(top) + new Vector2(0, 34 * TownView.Upright), 80, LampWarm with { A = 0.5f * lamp });
                pools.Pool(new Vector2(top.X, top.Y - 60), 210, warm);
            }

            roof.AddChild(halos);
        }

        // 客栈：窗纸与灯笼发光（发光图作房屋子节点，前面走过的人照样挡住），灯笼与一层窗下的街面铺光。
        foreach (var (node, house) in houses.Zip(TownSamples.Houses))
        {
            if (PieceArt.Find($"town.{house.Id}.glow") is not { } glow)
            {
                continue;
            }

            node.AddChild(new PieceGlow(glow, node.Position, new Color(1.3f, 1.05f, 0.8f, lamp), tint));
            var (lanterns, windows) = GlowSpots($"town.{house.Id}.glow");
            var halos = new SceneLamps(tint);
            foreach (var screen in lanterns)
            {
                halos.Halo(screen, 46, LampWarm with { A = 0.5f * lamp });
                if (OnFront(house, screen) is { } at)
                {
                    pools.Pool(at.Ground, 190, warm);
                }
            }

            foreach (var (screen, area) in windows)
            {
                if (OnFront(house, screen) is { Z: < 300 } at)
                {
                    pools.Pool(at.Ground, Mathf.Clamp(Mathf.Sqrt(area) * 1.6f, 90, 220), LampWarm with { A = 0.24f * lamp });
                }
            }

            node.AddChild(halos);
        }
    }

    /// <summary>发光图记下的灯笼中心与窗块（投影坐标，窗块带面积）。</summary>
    private static (List<Vector2> Lanterns, List<(Vector2 Screen, float Area)> Windows) GlowSpots(string id)
    {
        var region = id.Split('.', 2)[0];
        var path = $"res://assets/art/{region}/{id}.json";
        var lanterns = new List<Vector2>();
        var windows = new List<(Vector2, float)>();
        if (!Godot.FileAccess.FileExists(path))
        {
            return (lanterns, windows);
        }

        using var doc = JsonDocument.Parse(Godot.FileAccess.GetFileAsString(path));
        if (doc.RootElement.TryGetProperty("lanterns", out var ls))
        {
            lanterns.AddRange(ls.EnumerateArray().Select(l => new Vector2(l[0].GetSingle(), l[1].GetSingle())));
        }

        if (doc.RootElement.TryGetProperty("blobs", out var bs))
        {
            windows.AddRange(bs.EnumerateArray().Select(b => (new Vector2(b[0].GetSingle(), b[1].GetSingle()), b[2].GetSingle())));
        }

        return (lanterns, windows);
    }

    /// <summary>
    /// 画面上的一点落在房屋朝镜头的南墙或西墙上时，反求它在墙面上的高度，并给出墙前的街面一点（铺光用）；两面都不在返回 null。
    /// </summary>
    private static (Vector2 Ground, float Z)? OnFront(TownHouse h, Vector2 screen)
    {
        var c = Mathf.Cos(Mathf.DegToRad(TownView.Yaw));
        var s = Mathf.Sin(Mathf.DegToRad(TownView.Yaw));
        var sp = Mathf.Sin(Mathf.DegToRad(TownView.Pitch));
        var cp = Mathf.Cos(Mathf.DegToRad(TownView.Pitch));
        float Height(float x, float y) => ((x * s + y * c) * sp - screen.Y / TownView.Scale) / cp;

        // 南墙 y = Y1：屏幕 X =（x c − Y1 s）。
        var xs = (screen.X / TownView.Scale + h.Y1 * s) / c;
        if (xs >= h.X0 && xs <= h.X1)
        {
            var z = Height(xs, h.Y1);
            if (z >= 0 && z <= h.WallHeight + 40)
            {
                return (new Vector2(xs, h.Y1 + 70), z);
            }
        }

        // 西墙 x = X0。
        var ys = (h.X0 * c - screen.X / TownView.Scale) / s;
        if (ys >= h.Y0 && ys <= h.Y1)
        {
            var z = Height(h.X0, ys);
            if (z >= 0 && z <= h.WallHeight + 40)
            {
                return (new Vector2(h.X0 - 70, ys), z);
            }
        }

        return null;
    }
}

/// <summary>
/// 墙脚接地暗影：房屋朝镜头的南墙与西墙脚下，自墙根向外约 60 渐淡的一道暗边（AI 件入库时擦掉了自带的地面阴影）。
/// 画在贴地层，投影之后、排序件之前。
/// </summary>
public partial class TownContactShade : Node2D
{
    private readonly float _alpha;

    public TownContactShade(float alpha) => _alpha = alpha;

    public override void _Draw()
    {
        var dark = new Color(0.08f, 0.13f, 0.16f, _alpha);
        var clear = dark with { A = 0 };
        const float reach = 60;
        using var batch = new PolyBatch(this);
        foreach (var h in TownSamples.Houses)
        {
            // 南墙：自 (X0, Y1) 到 (X1, Y1)，向南渐淡；西墙：自 (X0, Y0) 到 (X0, Y1)，向西渐淡；转角处补一块。
            batch.Add([TownView.P(h.X0, h.Y1), TownView.P(h.X1, h.Y1), TownView.P(h.X1, h.Y1 + reach), TownView.P(h.X0, h.Y1 + reach)], [dark, dark, clear, clear]);
            batch.Add([TownView.P(h.X0, h.Y0), TownView.P(h.X0, h.Y1), TownView.P(h.X0 - reach, h.Y1), TownView.P(h.X0 - reach, h.Y0)], [dark, dark, clear, clear]);
            batch.Add([TownView.P(h.X0, h.Y1), TownView.P(h.X0, h.Y1 + reach), TownView.P(h.X0 - reach, h.Y1 + reach), TownView.P(h.X0 - reach, h.Y1)], [dark, clear, clear, clear]);
        }
    }
}
