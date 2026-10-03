using Godot;

namespace WuxiaWorld.Game.Presentation.App;

/// <summary>可改键的一项操作：稳定 ID、所属场合、显示名与默认键。</summary>
public sealed record KeyAction(string Id, string Group, string Name, Key Default);

/// <summary>
/// 按键设置（M3-05，UI_DESIGN 第 7 节）：玩家可改的操作键，按场合分组，存 <c>user://settings.cfg</c> 的 <c>[keys]</c> 节。
/// 同一场合内一键只做一件事：改键撞上同场合的另一项时两者互换；不同场合可以共用（探索的 E 交互与菜单的 E 下一分区）。
/// 固定不改的键：Esc 取消 / 菜单、Enter 与空格确认、方向键、Tab、数字键、PgUp / PgDn、F12 开发信息、Alt+Enter 全屏；
/// 它们同时是各场合的保底操作（方向键在探索中行走、在菜单中移动焦点），不能被占用。
/// 匹配按逻辑键码：键帽上写什么就按什么。
/// </summary>
public static class KeyBindings
{
    public const string Explore = "探索";
    public const string Menu = "菜单";
    public const string Dialogue = "对话";
    public const string Battle = "战斗";

    public static readonly string[] Groups = [Explore, Menu, Dialogue, Battle];

    public static readonly KeyAction[] Actions =
    [
        new("move_up", Explore, "向上走", Key.W),
        new("move_down", Explore, "向下走", Key.S),
        new("move_left", Explore, "向左走", Key.A),
        new("move_right", Explore, "向右走", Key.D),
        new("run", Explore, "快跑（按住）", Key.Shift),
        new("interact", Explore, "交互", Key.E),
        new("open_character", Explore, "人物与武学", Key.C),
        new("open_party", Explore, "队伍", Key.P),
        new("open_inventory", Explore, "行囊", Key.I),
        new("open_journal", Explore, "江湖札记", Key.J),
        new("open_map", Explore, "江湖大地图", Key.M),
        new("quick_save", Explore, "快速存档", Key.F5),
        new("quick_load", Explore, "快速读档", Key.F9),
        new("section_prev", Menu, "上一分区", Key.Q),
        new("section_next", Menu, "下一分区", Key.E),
        new("dialogue_log", Dialogue, "对话记录", Key.L),
        new("dialogue_hide", Dialogue, "隐藏对话框", Key.H),
        new("dialogue_replay", Dialogue, "重播配音", Key.R),
        new("battle_attack", Battle, "普通攻击", Key.A),
        new("battle_defend", Battle, "防御", Key.D),
        new("battle_meditate", Battle, "调息", Key.R),
        new("battle_item", Battle, "物品", Key.I),
        new("battle_swap", Battle, "换位", Key.S),
        new("battle_retreat", Battle, "撤退", Key.X),
        new("battle_auto", Battle, "自动战斗", Key.P),
        new("battle_speed", Battle, "切换倍速", Key.F),
        new("battle_skip", Battle, "跳过演出", Key.Space),
        new("battle_log", Battle, "战斗日志", Key.L),
    ];

    /// <summary>固定键：不可分配给任何操作。</summary>
    private static readonly HashSet<Key> Reserved =
    [
        Key.Escape, Key.Enter, Key.KpEnter, Key.Tab, Key.Up, Key.Down, Key.Left, Key.Right,
        Key.Pageup, Key.Pagedown, Key.F12, Key.Alt, Key.Meta, Key.Backspace, Key.Delete,
        Key.Key1, Key.Key2, Key.Key3, Key.Key4, Key.Key5, Key.Key6, Key.Key7, Key.Key8, Key.Key9, Key.Key0,
    ];

    /// <summary>空格是对话的继续键，不能在对话里另作他用；战斗里默认是跳过演出，可改。</summary>
    private static readonly Dictionary<string, Key[]> ReservedIn = new()
    {
        [Dialogue] = [Key.Space],
    };

    private static readonly Dictionary<string, Key> Current = Actions.ToDictionary(a => a.Id, a => a.Default);

    private static readonly Dictionary<string, KeyAction> ById = Actions.ToDictionary(a => a.Id);

    /// <summary>按键改动后通知（按键提示、交互提示据此重建）。</summary>
    public static event Action? Changed;

    public static Key Of(string id) => Current[id];

    /// <summary>键帽上写的字：字母大写，特殊键用简短中文或通行写法。</summary>
    public static string Label(string id) => KeyName(Current[id]);

    public static string KeyName(Key key) => key switch
    {
        Key.Space => "空格",
        Key.Shift => "Shift",
        Key.Ctrl => "Ctrl",
        Key.Capslock => "Caps",
        Key.Quoteleft => "`",
        Key.Minus => "-",
        Key.Equal => "=",
        Key.Bracketleft => "[",
        Key.Bracketright => "]",
        Key.Backslash => "\\",
        Key.Semicolon => ";",
        Key.Apostrophe => "'",
        Key.Comma => ",",
        Key.Period => ".",
        Key.Slash => "/",
        _ => OS.GetKeycodeString(key),
    };

    /// <summary>这次按下（不含按住重复）是不是该操作。修饰键操作（快跑）只看按住状态，不走这里。</summary>
    public static bool Pressed(InputEvent e, string id) =>
        e is InputEventKey { Pressed: true, Echo: false } key && key.Keycode == Current[id];

    /// <summary>该操作的键是否正被按住（行走、快跑）。</summary>
    public static bool Held(string id) => Input.IsKeyPressed(Current[id]);

    /// <summary>
    /// 改键。不可分配的键返回原因；撞上同场合另一项时互换，返回被换走的那一项（无冲突为 null）。
    /// </summary>
    public static (bool Ok, string? Message, KeyAction? Swapped) Rebind(string id, Key key)
    {
        var action = ById[id];
        if (Reserved.Contains(key) || key is Key.None)
        {
            return (false, $"{KeyName(key)} 是固定键，不能改作“{action.Name}”。", null);
        }

        if (ReservedIn.TryGetValue(action.Group, out var fixedKeys) && fixedKeys.Contains(key))
        {
            return (false, $"{KeyName(key)} 在{action.Group}中用来继续，不能改作“{action.Name}”。", null);
        }

        var old = Current[id];
        if (old == key)
        {
            return (true, null, null);
        }

        var other = Actions.FirstOrDefault(a => a.Group == action.Group && a.Id != id && Current[a.Id] == key);
        if (other is not null && ReservedIn.TryGetValue(other.Group, out var otherFixed) && otherFixed.Contains(old))
        {
            return (false, $"{KeyName(key)} 已用作“{other.Name}”，换过去会占用继续键。", null);
        }

        Current[id] = key;
        if (other is not null)
        {
            Current[other.Id] = old;
        }

        Changed?.Invoke();
        return (true, other is null ? null : $"“{other.Name}”原用 {KeyName(key)}，已改为 {KeyName(old)}。", other);
    }

    public static void ResetAll()
    {
        foreach (var a in Actions)
        {
            Current[a.Id] = a.Default;
        }

        Changed?.Invoke();
    }

    public static bool IsDefault => Actions.All(a => Current[a.Id] == a.Default);

    /// <summary>读设置：缺项或无效的键（固定键、同场合重复）回到默认值。</summary>
    public static void Load(ConfigFile cfg)
    {
        foreach (var a in Actions)
        {
            var key = (Key)(long)cfg.GetValue("keys", a.Id, (long)a.Default);
            Current[a.Id] = Reserved.Contains(key) || key == Key.None ? a.Default : key;
        }

        foreach (var group in Groups)
        {
            var seen = new HashSet<Key>();
            if (Actions.Where(a => a.Group == group).Any(a => !seen.Add(Current[a.Id])))
            {
                foreach (var a in Actions.Where(a => a.Group == group))
                {
                    Current[a.Id] = a.Default;
                }
            }
        }
    }

    public static void Save(ConfigFile cfg)
    {
        foreach (var a in Actions)
        {
            cfg.SetValue("keys", a.Id, (long)Current[a.Id]);
        }
    }
}
