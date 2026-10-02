namespace WuxiaWorld.Domain.World;

/// <summary>
/// 队伍阵位与伙伴成长（架构文档 8.4、9.4.4）。只改调用方给的状态副本，返回失败原因或 null。
/// <list type="bullet">
/// <item>阵位：格位 <c>排 × 3 + 列</c>，0–2 前排、3–5 后排。入队时按默认次序占第一个空位，离队时让出；调换时与目标格上的人互换。</item>
/// <item>伙伴成长：可招募伙伴第一次入队时经验与主角持平；在队时每次与主角得同样的经验，离队期间得一半；
/// 再入队时若落后主角一级以上，经验补到“主角等级 − 1”的起点（有限追赶）。暂时同行者不随主角成长。</item>
/// </list>
/// </summary>
public static class PartyRules
{
    public const int CellCount = 6;

    /// <summary>
    /// 入队时依次取的默认格位：前排 1 号、后排 1 号、前排 2 号、前排 0 号、后排 2 号、后排 0 号
    /// （战斗画面里每排 0 号在上、2 号在下）。主角总是第一个，站前排 1 号。
    /// </summary>
    public static readonly int[] DefaultOrder = [1, 4, 2, 0, 5, 3];

    public static int RowOf(int cell) => cell / 3;

    public static int SlotOf(int cell) => cell % 3;

    /// <summary>
    /// 当前队伍每人的格位，按队伍次序。缺记录、越界或重叠的（旧档、内容改动）按默认次序补到空位；只读，不改状态。
    /// </summary>
    public static IReadOnlyList<(string Id, int Cell)> Cells(WorldState s)
    {
        var used = new HashSet<int>();
        var cell = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var id in s.Party)
        {
            if (s.Formation.TryGetValue(id, out var c) && c is >= 0 and < CellCount && used.Add(c))
            {
                cell[id] = c;
            }
        }

        foreach (var id in s.Party.Where(id => !cell.ContainsKey(id)))
        {
            var free = DefaultOrder.First(c => !used.Contains(c));
            used.Add(free);
            cell[id] = free;
        }

        return [.. s.Party.Select(id => (id, cell[id]))];
    }

    public static int CellOf(WorldState s, string id) => Cells(s).First(x => x.Id == id).Cell;

    /// <summary>把阵位整理成与队伍一致（补缺、去掉不在队的人）。</summary>
    public static void Normalize(WorldState s)
    {
        var cells = Cells(s);
        s.Formation.Clear();
        foreach (var (id, c) in cells)
        {
            s.Formation[id] = c;
        }
    }

    /// <summary>把在队人物移到某格；该格有人则与之互换。</summary>
    public static string? SetCell(WorldState s, string id, int cell)
    {
        if (!s.Party.Contains(id, StringComparer.Ordinal))
        {
            return "此人不在队中";
        }

        if (cell is < 0 or >= CellCount)
        {
            return $"没有第 {cell} 格";
        }

        Normalize(s);
        var from = s.Formation[id];
        if (from == cell)
        {
            return null;
        }

        if (s.Formation.FirstOrDefault(kv => kv.Value == cell).Key is { } other)
        {
            s.Formation[other] = from;
        }

        s.Formation[id] = cell;
        return null;
    }

    // ── 入队、离队与成长（由 WorldRules 的效果调用） ──────────

    /// <summary>
    /// 入队后：占阵位、记同行次数；可招募伙伴按规则设定或追赶经验。
    /// 返回追赶后的新等级（未追赶为 null），供通知使用。
    /// </summary>
    internal static int? Joined(WorldRules rules, WorldState s, string id)
    {
        Normalize(s);
        if (!s.Companions.TryGetValue(id, out var state))
        {
            state = new CompanionState();
            s.Companions[id] = state;
        }

        state.Joins++;
        if (id == s.Hero || RoleOf(rules, id) != PartyRole.Recruitable)
        {
            return null;
        }

        if (state.Joins == 1)
        {
            state.Experience = s.Experience;
            return null;
        }

        var floor = rules.ExperienceFor(rules.LevelOf(s.Experience) - 1);
        if (state.Experience >= floor)
        {
            return null;
        }

        var before = rules.LevelOf(state.Experience);
        state.Experience = floor;
        var after = rules.LevelOf(floor);
        return after > before ? after : null;
    }

    internal static void Left(WorldState s, string id) => s.Formation.Remove(id);

    /// <summary>主角得经验时：在队的可招募伙伴得同样多，离队的得一半（向下取整）。</summary>
    internal static void ShareExperience(WorldRules rules, WorldState s, int amount)
    {
        foreach (var (id, state) in s.Companions)
        {
            if (id == s.Hero || RoleOf(rules, id) != PartyRole.Recruitable)
            {
                continue;
            }

            state.Experience += s.Party.Contains(id, StringComparer.Ordinal) ? amount : amount / 2;
        }
    }

    public static PartyRole RoleOf(WorldRules rules, string id) =>
        rules.Content.Characters.TryGetValue(id, out var c) ? c.Party : PartyRole.None;
}
