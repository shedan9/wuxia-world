using Godot;
using WuxiaWorld.Game.Presentation.Ui;

namespace WuxiaWorld.Game.Presentation.Play;

/// <summary>文书特写的种类。</summary>
public enum DocumentKind
{
    /// <summary>转信副页：折过的旧纸、签押与小朱印；随后一封求援书的转信章滑过来叠在签押上，笔势重合。</summary>
    RelayCopy,

    /// <summary>章末客栈门口新贴的红纸名单。</summary>
    RedList,
}

/// <summary>
/// 文书特写卡（M3，架构文档 9.4.18）：演出里需要看清的纸面物件不在布景中，以界面同一套笔触画法在画面正中画出纸面——
/// 字迹是认不出的小字笔画（不写入任何剧情文字），签押是同一条程序生成的笔路，故两处“笔势相同”可直接叠看。
/// 文生图出过一轮特写插画（`tools/ArtGen/jobs/m3_closeups.json`），物件常画成西式门锁、划艇，未采用。
/// </summary>
public partial class DocumentCloseup : Control
{
    private static readonly Color Ink = new("#24201C");
    private static readonly Color OldPaper = new("#E8DCC0");
    private static readonly Color LetterPaper = new("#DCD3BC");
    private static readonly Color RedPaper = new("#B4362C");
    private static readonly Color Vermilion = new("#C23B2A");

    private readonly DocumentKind _kind;
    private float _t;

    public DocumentCloseup(DocumentKind kind)
    {
        _kind = kind;
        MouseFilter = MouseFilterEnum.Ignore;
    }

    /// <summary>直接给出最后一帧（跳过演出时）。</summary>
    public void Complete()
    {
        _t = 10;
        QueueRedraw();
    }

    public override void _Ready() => SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

    public override void _Process(double delta)
    {
        if (_t < 4)
        {
            _t += (float)delta;
            QueueRedraw();
        }
    }

    public override void _Draw()
    {
        var center = Size / 2 + new Vector2(0, -60);
        DrawRect(new Rect2(Vector2.Zero, Size), UiPalette.Abyss with { A = 0.55f * Mathf.Clamp(_t / 0.3f, 0, 1) });
        switch (_kind)
        {
            case DocumentKind.RelayCopy:
                DrawRelay(center);
                break;
            case DocumentKind.RedList:
                DrawRedList(center);
                break;
        }
    }

    private void DrawRelay(Vector2 center)
    {
        var item = GetCanvasItem();
        var a = Mathf.Clamp(_t / 0.35f, 0, 1);

        // 副页：折过两道的旧纸，右起竖排小字，左下签押，签押旁一方小朱印。
        var page = new Rect2(center - new Vector2(330, 250), new Vector2(560, 500));
        Paper(item, page, OldPaper with { A = a }, 11, folds: true);
        Columns(item, page.Grow(-46), 7, 8, Ink with { A = 0.8f * a }, 101, page.Size.Y * 0.62f);
        var mark = page.Position + new Vector2(150, 400);
        Signature(item, mark, 1, Ink with { A = a });
        DrawSeal(item, mark + new Vector2(118, -22), 34, Vermilion with { A = 0.85f * a }, 7);

        // 求援书：自右滑入，转信章里的签押与副页上的叠在一起，笔势重合。
        var k = Mathf.SmoothStep(0, 1, Mathf.Clamp((_t - 1.1f) / 1.3f, 0, 1));
        if (_t < 1.1f)
        {
            return;
        }

        var letterMark = mark + new Vector2(Mathf.Lerp(520, 0, k), Mathf.Lerp(-60, 0, k));
        var letter = new Rect2(letterMark - new Vector2(120, 330), new Vector2(430, 420));
        var la = Mathf.Lerp(0.95f, 0.5f, k) * Mathf.Clamp((_t - 1.1f) / 0.3f, 0, 1);
        Paper(item, letter, LetterPaper with { A = la }, 23, folds: false);
        Columns(item, letter.Grow(-40), 5, 6, Ink with { A = 0.7f * la }, 211, letter.Size.Y * 0.55f);

        // 转信章：朱色方框里压着同一笔签押。
        var seal = new Rect2(letterMark - new Vector2(70, 58), new Vector2(150, 96));
        DrawRect(seal, Vermilion with { A = 0.8f * la }, false, 4);
        Signature(item, letterMark, 1, Vermilion with { A = 0.9f * la });
        if (k >= 1)
        {
            // 重合之后，笔路上一圈泥金提示。
            var glow = Mathf.Clamp((_t - 2.4f) / 0.4f, 0, 1);
            DrawArc(letterMark + new Vector2(30, -8), 110, 0, Mathf.Tau, 48, UiPalette.Gilt with { A = 0.8f * glow }, 3, true);
        }
    }

    private void DrawRedList(Vector2 center)
    {
        var item = GetCanvasItem();
        var a = Mathf.Clamp(_t / 0.35f, 0, 1);
        var page = new Rect2(center - new Vector2(300, 300), new Vector2(600, 620));
        Paper(item, page, RedPaper with { A = a }, 31, folds: false);

        // 新墨：一列一个名字，自右往左，字比副页上的大、写得端正；最左一列还没写完。
        var reveal = Mathf.Clamp(_t / 2.2f, 0, 1);
        Columns(item, page.Grow(-60), 6, 4, Ink with { A = 0.92f * a }, 307, page.Size.Y * 0.5f, reveal);
    }

    /// <summary>纸面：边缘略不齐的多边形，平铺纸纹，折痕两道。</summary>
    private static void Paper(Rid item, Rect2 r, Color color, float seed, bool folds)
    {
        var poly = Brushwork.RaggedRect(r, 3, seed);
        RenderingServer.CanvasItemAddPolygon(item, poly, [color]);
        Brushwork.GrainFill(item, poly, Colors.Black with { A = 0.12f * color.A });
        if (folds)
        {
            var c = new Color(0, 0, 0, 0.12f * color.A);
            RenderingServer.CanvasItemAddLine(item, new Vector2(r.GetCenter().X, r.Position.Y + 4), new Vector2(r.GetCenter().X, r.End.Y - 4), c, 2);
            RenderingServer.CanvasItemAddLine(item, new Vector2(r.Position.X + 4, r.GetCenter().Y), new Vector2(r.End.X - 4, r.GetCenter().Y), c, 2);
        }
    }

    /// <summary>
    /// 自右往左的竖排“字”：每个字是 2–4 笔认不出的短笔画（横、竖、点、撇），只表现有字，不写具体内容。
    /// reveal 小于 1 时只写出前面一部分（边写边看）。
    /// </summary>
    private static void Columns(Rid item, Rect2 area, int columns, int perColumn, Color ink, int seed, float height, float reveal = 1)
    {
        var colW = area.Size.X / columns;
        var cell = Math.Min(colW * 0.75f, height / perColumn);
        var total = columns * perColumn;
        for (var c = 0; c < columns; c++)
        {
            var x = area.End.X - colW * (c + 0.5f);
            var count = perColumn - (int)(Brushwork.Hash(seed + c * 3.7f) * 3);
            for (var i = 0; i < count; i++)
            {
                if ((c * perColumn + i) / (float)total > reveal)
                {
                    return;
                }

                var o = new Vector2(x, area.Position.Y + cell * (i + 0.5f));
                Glyph(item, o, cell * 0.8f, ink, seed + c * 31 + i * 7);
            }
        }
    }

    private static void Glyph(Rid item, Vector2 o, float s, Color ink, float seed)
    {
        // 4–7 笔：先一两道横、竖撑起字形，再补点、撇、捺，笔画落在字格里互相搭连。
        var n = 4 + (int)(Brushwork.Hash(seed) * 4);
        for (var k = 0; k < n; k++)
        {
            var h = Brushwork.Hash(seed * 1.3f + k * 5.1f);
            var kind = k < 2 ? (k == 0 ? 0.2f : 0.5f) : h;
            var p = o + new Vector2((Brushwork.Hash(seed + k * 2.3f) - 0.5f) * s * 0.55f, (Brushwork.Hash(seed + k * 4.7f) - 0.5f) * s * 0.6f);
            var len = s * (0.45f + 0.35f * Brushwork.Hash(seed * 3 + k));
            Vector2 d = kind switch
            {
                < 0.35f => new(len, (Brushwork.Hash(k + seed) - 0.6f) * s * 0.08f),
                < 0.62f => new((Brushwork.Hash(k + seed * 2) - 0.5f) * s * 0.06f, len),
                < 0.78f => new(-len * 0.6f, len * 0.7f),
                < 0.9f => new(len * 0.6f, len * 0.6f),
                _ => new(s * 0.1f, s * 0.12f),
            };
            Brushwork.Stroke(item, [p - d / 2, p + d / 2], Math.Max(2.2f, s * 0.1f), ink, seed + k, 0, 0.15f, 0.45f);
        }
    }

    /// <summary>签押：一条固定的连笔花押（同一组控制点按 Catmull-Rom 平滑，画出完全相同的笔路）。</summary>
    private static void Signature(Rid item, Vector2 o, float scale, Color ink)
    {
        Vector2[] ctrl =
        [
            new(-64, 14), new(-36, -28), new(-8, -10), new(-26, 20), new(10, 24), new(34, -14), new(60, -30), new(72, 2), new(48, 26), new(98, 18),
        ];
        var path = new List<Vector2>();
        for (var i = 0; i < ctrl.Length - 1; i++)
        {
            var p0 = ctrl[Math.Max(0, i - 1)];
            var p1 = ctrl[i];
            var p2 = ctrl[i + 1];
            var p3 = ctrl[Math.Min(ctrl.Length - 1, i + 2)];
            for (var t = 0f; t < 1; t += 0.125f)
            {
                var t2 = t * t;
                var t3 = t2 * t;
                var q = 0.5f * (2 * p1 + (p2 - p0) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t2 + (3 * p1 - p0 - 3 * p2 + p3) * t3);
                path.Add(o + q * scale);
            }
        }

        path.Add(o + ctrl[^1] * scale);
        Brushwork.Stroke(item, path.ToArray(), 7 * scale, ink, 77, 0.6f, 0.05f, 0.5f);
    }

    private static void DrawSeal(Rid item, Vector2 c, float half, Color color, float seed)
    {
        var poly = Brushwork.RaggedRect(new Rect2(c - new Vector2(half, half), new Vector2(half * 2, half * 2)), 1.5f, seed);
        RenderingServer.CanvasItemAddPolygon(item, poly, [color]);
        var light = new Color(1, 0.95f, 0.88f, 0.75f * color.A);
        Brushwork.Stroke(item, [c + new Vector2(-half * 0.5f, -half * 0.4f), c + new Vector2(half * 0.5f, -half * 0.4f)], 3, light, seed);
        Brushwork.Stroke(item, [c + new Vector2(0, -half * 0.6f), c + new Vector2(0, half * 0.6f)], 3, light, seed + 1);
        Brushwork.Stroke(item, [c + new Vector2(-half * 0.45f, half * 0.35f), c + new Vector2(half * 0.45f, half * 0.35f)], 3, light, seed + 2);
    }
}
