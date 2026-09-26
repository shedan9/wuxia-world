using System.Text.Json;
using Godot;
using WuxiaWorld.Game.Preview.Samples;

namespace WuxiaWorld.Game.Preview.Pages;

/// <summary>
/// AI 出件的引导图导出（架构文档 10.3 “布局 → 引导出件 → 拼装”的第二步，2026-09-25 用户选定方案 C）。
/// 把探索布景（城镇、客栈、山路）的每个占位件单独渲染成透明底 PNG：同一投影、同一受光，贴地阴影关闭（阴影留给引擎画）。
/// tools/ArtGen/piece.py 以它为图生图底图、以其线稿为 ControlNet 引导生成正式件，再按它的 alpha 抠出，
/// 因此 AI 件的视角、尺度与占地天然与布局对齐。一件由多个部件组成时（平桥 = 贴地桥面 + 两道排序栏杆），
/// 整件合成一张引导图，另为每个部件导出同框的 alpha 遮罩，生成后按遮罩拆回各层。
/// 运行：<c>Godot --path game -- --scene=res://scenes/preview/PieceGuideExport.tscn --out=绝对目录 [--region=town|inn|wild] [--only=前缀] [--px=2]</c>。
/// 输出 <c>&lt;id&gt;.png</c>、只有面与轮廓的结构图 <c>&lt;id&gt;__shape.png</c>（贴花关闭，供 ControlNet 取边线）、部件遮罩 <c>&lt;id&gt;__&lt;部件&gt;.png</c> 与 <c>&lt;id&gt;.json</c>
/// （origin 为图像左上角对应的投影坐标，px 为每投影单位的像素数）。只做美术生产，不进展示目录。
/// </summary>
public partial class PieceGuideExport : Node
{
    private const int Margin = 24;
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public override async void _Ready()
    {
        var args = OS.GetCmdlineUserArgs()
            .Select(a => a.Split('=', 2))
            .Where(a => a.Length == 2)
            .ToDictionary(a => a[0], a => a[1]);
        if (!args.TryGetValue("--out", out var outDir))
        {
            GD.PrintErr("缺少 --out=输出目录");
            GetTree().Quit(1);
            return;
        }

        var px = args.TryGetValue("--px", out var pxText) ? float.Parse(pxText, System.Globalization.CultureInfo.InvariantCulture) : 2f;
        var only = args.GetValueOrDefault("--only", "");
        var region = args.GetValueOrDefault("--region", "town");
        DirAccess.MakeDirRecursiveAbsolute(outDir);
        Cel.Shadows = false;
        PieceArt.Enabled = false;

        var viewport = new SubViewport
        {
            TransparentBg = true,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
        };
        AddChild(viewport);

        var count = 0;
        foreach (var (id, parts) in Targets(region).Where(t => t.Id.StartsWith(only, StringComparison.Ordinal)))
        {
            var box = parts.Select(p => p.Box).Aggregate((a, b) => a.Merge(b));
            var size = new Vector2I(Mathf.CeilToInt(box.Size.X * px) + 2 * Margin, Mathf.CeilToInt(box.Size.Y * px) + 2 * Margin);
            var origin = box.Position - new Vector2(Margin, Margin) / px;
            viewport.Size = size;

            var guide = await Render(viewport, parts.Select(p => p.Node), origin, px);
            guide.SavePng($"{outDir}/{id}.png");
            Face.StructureOnly = true;
            var shape = await Render(viewport, parts.Select(p => p.Node), origin, px);
            Face.StructureOnly = false;
            shape.SavePng($"{outDir}/{id}__shape.png");
            if (parts.Count > 1)
            {
                foreach (var part in parts)
                {
                    var mask = await Render(viewport, [part.Node], origin, px);
                    mask.SavePng($"{outDir}/{id}__{part.Name}.png");
                }
            }

            var meta = new
            {
                id,
                px,
                origin = new[] { origin.X, origin.Y },
                size = new[] { size.X, size.Y },
                parts = parts.Select(p => p.Name).ToArray(),
                view = new { yaw = TownView.Yaw, pitch = TownView.Pitch, sun = new[] { TownView.Sun.X, TownView.Sun.Y, TownView.Sun.Z } },
            };
            File.WriteAllText($"{outDir}/{id}.json", JsonSerializer.Serialize(meta, Json));
            foreach (var part in parts) part.Node.QueueFree();
            count++;
        }

        GD.Print($"已导出 {count} 件引导图：{outDir}");
        GetTree().Quit(0);
    }

    private async Task<Image> Render(SubViewport viewport, IEnumerable<CanvasItem> nodes, Vector2 origin, float px)
    {
        var holder = new Node2D { Scale = new Vector2(px, px), Position = -origin * px };
        viewport.AddChild(holder);
        foreach (var node in nodes)
        {
            node.GetParent()?.RemoveChild(node);
            holder.AddChild(node);
        }

        for (var i = 0; i < 3; i++)
        {
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        }

        var image = viewport.GetTexture().GetImage();
        foreach (var node in nodes) holder.RemoveChild(node);
        holder.QueueFree();
        return image;
    }

    private sealed record Part(string Name, CanvasItem Node, Rect2 Box);

    /// <summary>
    /// 各地区可出件的件：id 与游戏里 UseArt 查找的一致（城镇房屋、杂件用样例 Id，树、石、灌丛用 &lt;类&gt;.&lt;种子&gt;，
    /// 客栈柱与盆栽按序号）。多部件件（平桥、茶亭、山门）合成一张引导图，另出各部件遮罩。
    /// </summary>
    private static IEnumerable<(string Id, List<Part> Parts)> Targets(string region) => region switch
    {
        "inn" => InnTargets(),
        "wild" => WildTargets(),
        _ => TownTargets(),
    };

    private static Part Piece(string name, TownPiece piece) => new(name, piece, piece.ScreenBox);

    private static IEnumerable<(string Id, List<Part> Parts)> InnTargets()
    {
        yield return ("inn.counter", [Piece("body", new InnCounter())]);
        yield return ("inn.stairs", [Piece("body", new InnStairs())]);
        yield return ("inn.screen", [Piece("body", new InnScreenNode())]);
        for (var i = 0; i < InnSamples.Pillars.Length; i++) yield return ($"inn.pillar.{i + 1}", [Piece("body", new InnPillarNode(InnSamples.Pillars[i], i))]);
        foreach (var table in InnSamples.Tables) yield return ($"inn.{table.Id}", [Piece("body", new InnTableNode(table))]);
        for (var i = 0; i < InnSamples.Plants.Length; i++) yield return ($"inn.plant.{i + 1}", [Piece("body", new InnPlantNode(InnSamples.Plants[i], i))]);
    }

    private static IEnumerable<(string Id, List<Part> Parts)> WildTargets()
    {
        foreach (var tree in WildSamples.Trees) yield return ($"wild.tree.{tree.Seed}", [Piece("body", new WildTreeNode(tree))]);
        foreach (var rock in WildSamples.Rocks) yield return ($"wild.rock.{rock.Seed}", [Piece("body", new WildRockNode(rock))]);
        foreach (var shrub in WildSamples.Shrubs) yield return ($"wild.shrub.{shrub.Seed}", [Piece("body", new WildShrubNode(shrub))]);
        yield return ("wild.pavilion", Stack(WildPavilionPart.Build()));
        yield return ("wild.gate", Stack(WildGatePart.Build()));
        yield return ("wild.stele", [Piece("body", WildMarkerNode.Stele())]);
        yield return ("wild.signpost", [Piece("body", WildMarkerNode.Signpost())]);
        yield return ("wild.steps.1", [Piece("body", new WildStepsNode(WildSamples.Steps1, "wild.steps.1"))]);
        yield return ("wild.steps.2", [Piece("body", new WildStepsNode(WildSamples.Steps2, "wild.steps.2"))]);
        yield return ("wild.bridge", [Piece("body", new WildBridgeNode())]);
    }

    /// <summary>多部件按游戏里的画序合成：地面件由远及近（占地中心的屏幕纵坐标），悬空件（屋面、额枋）最后压上。</summary>
    private static List<Part> Stack(IEnumerable<TownPiece> parts) =>
        parts.OrderBy(p => p.Overhead).ThenBy(p => TownView.P(p.Foot.GetCenter()).Y).Select(p => Piece(p.Part!, p)).ToList();

    private static IEnumerable<(string Id, List<Part> Parts)> TownTargets()
    {

        foreach (var house in TownSamples.Houses) yield return ($"town.{house.Id}", [Piece("body", new TownHouseNode(house))]);
        foreach (var tree in TownSamples.Trees) yield return ($"town.tree.{tree.Seed}", [Piece("body", new TownTreeNode(tree))]);
        foreach (var prop in TownSamples.Props) yield return ($"town.{prop.Id}", [Piece("body", new TownPropNode(prop))]);
        yield return ("town.corridor.roof", [Piece("body", TownCorridorPart.Build(TownSamples.Corridor).First())]);

        var bridge = TownSamples.Bridge;
        var deck = new FaceSheet(TownGroundDetail.BridgeFaces());
        yield return ("town.bridge",
        [
            new("deck", deck, deck.Box),
            Piece("rail_w", new TownRailNode(bridge.Position.X + 12, bridge)),
            Piece("rail_e", new TownRailNode(bridge.End.X - 12, bridge)),
        ]);
    }
}

/// <summary>只画一组面的节点（引导图导出贴地部件用）。</summary>
public partial class FaceSheet : Node2D
{
    private readonly List<Face> _faces;

    public FaceSheet(List<Face> faces)
    {
        _faces = faces;
        var points = faces.SelectMany(f => f.Points).Select(TownView.P).ToList();
        Box = points.Aggregate(new Rect2(points[0], Vector2.Zero), (r, p) => r.Expand(p)).Grow(6);
    }

    public Rect2 Box { get; }

    public override void _Draw()
    {
        foreach (var face in _faces) face.Draw(this);
    }
}
