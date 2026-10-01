namespace WuxiaWorld.Domain.World;

/// <summary>
/// 世界规则：条件求值、效果执行、任务状态机与地区事件筛选（架构文档 9.1）。
/// 所有方法只改调用方给的状态；应用层负责在副本上执行并整体提交，失败时丢弃副本。
/// </summary>
public sealed class WorldRules
{
    /// <summary>主角 + 最多 3 名伙伴。</summary>
    public const int MaxParty = 4;

    /// <summary>任务自动推进的最大轮数；超过说明内容里有无条件自循环（校验器应先拦下）。</summary>
    private const int MaxRefreshPasses = 64;

    public WorldRules(WorldContent content) => Content = content;

    public WorldContent Content { get; }

    public bool Check(Condition? condition, WorldState state) => Conditions.Check(condition, state, Content);

    public WorldState NewGame()
    {
        var ng = Content.NewGame;
        var s = new WorldState
        {
            ArcId = ng.Arc, ChapterId = ng.Chapter, MapId = ng.Map, SpawnId = ng.Spawn, Silver = ng.Silver,
        };
        s.SetRng(new Common.Pcg32(ng.Seed, WorldStream));
        s.Party.AddRange(ng.Party);
        s.Met.UnionWith(ng.Party);
        var r = new EffectResult();
        Apply(s, ng.Effects, r, "new_game");
        if (!r.Ok)
        {
            throw new InvalidOperationException($"新游戏初始效果失败：{r.Error}");
        }

        return s;
    }

    /// <summary>世界随机流的流选择量，与战斗流（0xB4771E00）分开。</summary>
    public const ulong WorldStream = 0x57A1D000;

    /// <summary>
    /// 执行一组效果并推进任务。给出 <paramref name="settleKey"/> 时整组只结算一次：已结算则什么也不做。
    /// 任一效果失败即停止并在结果中记录原因，调用方应丢弃这份状态。
    /// </summary>
    public void Apply(WorldState s, IEnumerable<WorldEffect> effects, EffectResult r, string? settleKey = null)
    {
        if (settleKey is not null && !s.Settled.Add(settleKey))
        {
            return;
        }

        foreach (var e in effects)
        {
            ApplyOne(s, e, r, settleKey);
            if (!r.Ok)
            {
                return;
            }
        }

        Refresh(s, r);
    }

    private void ApplyOne(WorldState s, WorldEffect e, EffectResult r, string? settleKey)
    {
        switch (e.Type)
        {
            case WorldEffectType.SetFact:
                s.Facts[e.Id!] = e.Value ?? "true";
                break;
            case WorldEffectType.ClearFact:
                s.Facts.Remove(e.Id!);
                break;
            case WorldEffectType.GrantItem:
                if (!Content.Items.ContainsKey(e.Id!))
                {
                    r.Fail($"未定义的物品 {e.Id}");
                    return;
                }

                s.Items[e.Id!] = s.CountOf(e.Id!) + e.Amount;
                r.Notices.Add(new WorldNotice("item_gained", e.Id!, e.Amount));
                break;
            case WorldEffectType.RemoveItem:
                if (s.CountOf(e.Id!) < e.Amount)
                {
                    r.Fail($"物品不足：{e.Id} 需要 {e.Amount}");
                    return;
                }

                s.Items[e.Id!] -= e.Amount;
                if (s.Items[e.Id!] == 0)
                {
                    s.Items.Remove(e.Id!);
                }

                r.Notices.Add(new WorldNotice("item_lost", e.Id!, e.Amount));
                break;
            case WorldEffectType.ChangeSilver:
                if (s.Silver + e.Amount < 0)
                {
                    r.Fail($"银两不足：需要 {-e.Amount}，现有 {s.Silver}");
                    return;
                }

                s.Silver += e.Amount;
                r.Notices.Add(new WorldNotice("silver", "silver", e.Amount));
                break;
            case WorldEffectType.ChangeRelationship:
                var rel = s.Relationship(e.Id!);
                if (e.Axis == RelationshipAxis.Trust)
                {
                    rel.Trust += e.Amount;
                }
                else
                {
                    rel.Affection += e.Amount;
                }

                r.Notices.Add(new WorldNotice(e.Axis == RelationshipAxis.Trust ? "trust" : "affection", e.Id!, e.Amount));
                break;
            case WorldEffectType.AddCommitment:
                s.Relationship(e.Id!).Commitments.Add(e.Value!);
                break;
            case WorldEffectType.AddClue:
                if (s.Clues.Add(e.Id!))
                {
                    r.Notices.Add(new WorldNotice("clue", e.Id!));
                }

                break;
            case WorldEffectType.MeetCharacter:
                s.Met.Add(e.Id!);
                break;
            case WorldEffectType.StartQuest:
                StartQuest(s, e.Id!, r);
                break;
            case WorldEffectType.CompleteObjective:
                if (s.Quests.TryGetValue(e.Id!, out var qp) && qp.Status == QuestStatus.Active
                    && Content.Quests[e.Id!].StageById(qp.Stage) is { } stage && stage.Objectives.Any(o => o.Id == e.Value))
                {
                    qp.Objectives.Add(e.Value!);
                }

                break;
            case WorldEffectType.FailQuest:
            case WorldEffectType.AbandonQuest:
                EndQuest(s, e.Id!, e.Type == WorldEffectType.FailQuest ? QuestStatus.Failed : QuestStatus.Abandoned, r);
                break;
            case WorldEffectType.JoinParty:
                if (!Content.Characters.ContainsKey(e.Id!))
                {
                    r.Fail($"未定义的人物 {e.Id}");
                }
                else if (!s.Party.Contains(e.Id!, StringComparer.Ordinal))
                {
                    if (s.Party.Count >= MaxParty)
                    {
                        r.Fail("队伍已满（主角 + 3 名伙伴）");
                        return;
                    }

                    s.Party.Add(e.Id!);
                    s.Met.Add(e.Id!);
                    r.Notices.Add(new WorldNotice("joined", e.Id!));
                }

                break;
            case WorldEffectType.LeaveParty:
                if (s.Party.Count > 0 && s.Party[0] == e.Id)
                {
                    r.Fail("主角不能离队");
                    return;
                }

                if (s.Party.Remove(e.Id!))
                {
                    r.Notices.Add(new WorldNotice("left", e.Id!));
                }

                break;
            case WorldEffectType.AdvanceClock:
                s.Clock += Math.Max(0, e.Amount);
                break;
            case WorldEffectType.LearnSkill:
                if (s.Skills.Add(e.Id!))
                {
                    r.Notices.Add(new WorldNotice("skill", e.Id!));
                }

                break;
            case WorldEffectType.GrantExperience:
                s.Experience += Math.Max(0, e.Amount);
                r.Notices.Add(new WorldNotice("experience", "experience", e.Amount));
                break;
            case WorldEffectType.ReachGate:
                s.Gates.Add(e.Id!);
                break;
            case WorldEffectType.SetChapter:
                s.ArcId = e.Id!;
                s.ChapterId = e.Value!;
                break;
            case WorldEffectType.RequestBattle:
                if (s.Battle is not null)
                {
                    r.Fail($"已有待开战斗 {s.Battle.RequestId}");
                    return;
                }

                var requestId = e.Value ?? $"{settleKey ?? "battle"}>{e.Id}";
                var attempt = 1;
                while (s.Settled.Contains($"battle:{requestId}#{attempt}"))
                {
                    // 放弃后再次请求同一场战斗：实例号接着往后排，不与已结算的实例重名。
                    attempt++;
                }

                s.Battle = new PendingBattle
                {
                    RequestId = requestId, Attempt = attempt,
                    Encounter = e.Id!, OnVictory = e.OnVictory, OnDefeat = e.OnDefeat, Retry = e.Retry,
                };
                r.Battle = s.Battle;
                break;
            case WorldEffectType.RequestTravel:
                r.Travel = new TravelRequest(e.Id!, e.Value);
                break;
            default:
                r.Fail($"未实现的效果 {e.Type}");
                break;
        }
    }

    private void StartQuest(WorldState s, string id, EffectResult r)
    {
        if (!Content.Quests.TryGetValue(id, out var def))
        {
            r.Fail($"未定义的任务 {id}");
            return;
        }

        var status = s.QuestStatusOf(id);
        if (status == QuestStatus.Active)
        {
            return;
        }

        if (status is QuestStatus.Completed or QuestStatus.Failed or QuestStatus.Abandoned)
        {
            r.Fail($"任务 {id} 已结束（{status}）");
            return;
        }

        if (!Check(def.Prerequisites, s))
        {
            r.Fail($"任务 {id} 的前置条件未满足");
            return;
        }

        if (def.ExclusiveGroup is { } group && Content.Quests.Values.Any(q => q.Id != id && q.ExclusiveGroup == group
                && s.QuestStatusOf(q.Id) is QuestStatus.Active or QuestStatus.Completed))
        {
            r.Fail($"任务 {id} 与同组 {group} 的任务互斥");
            return;
        }

        var first = def.Stages[0].Id;
        var p = new QuestProgress { Status = QuestStatus.Active, Stage = first };
        p.History.Add(first);
        s.Quests[id] = p;
        r.Notices.Add(new WorldNotice("quest_started", id));
    }

    private void EndQuest(WorldState s, string id, QuestStatus end, EffectResult r)
    {
        var def = Content.Quests[id];
        if (def.Kind == QuestKind.Main)
        {
            r.Fail($"主线任务 {id} 不能失败或放弃");
            return;
        }

        if (s.QuestStatusOf(id) != QuestStatus.Active)
        {
            return;
        }

        s.Quests[id].Status = end;
        r.Notices.Add(new WorldNotice(end == QuestStatus.Failed ? "quest_failed" : "quest_abandoned", id));
        var effects = end == QuestStatus.Failed ? def.OnFail : def.OnAbandon;
        foreach (var e in effects)
        {
            ApplyOne(s, e, r, $"quest:{id}:{end}");
            if (!r.Ok)
            {
                return;
            }
        }

        s.Settled.Add($"quest:{id}:{end}");
    }

    /// <summary>
    /// 推进任务直到稳定：前置满足的锁定任务变为可接、不再满足的可接任务收回锁定，
    /// 条件已满足的目标自动完成，目标齐全的阶段结算并转入下一阶段。
    /// </summary>
    public void Refresh(WorldState s, EffectResult r)
    {
        for (var pass = 0; pass < MaxRefreshPasses; pass++)
        {
            var changed = false;
            foreach (var def in Content.Quests.Values)
            {
                if (!r.Ok)
                {
                    return;
                }

                var status = s.QuestStatusOf(def.Id);
                if (status == QuestStatus.Locked && Check(def.Prerequisites, s))
                {
                    s.Quests[def.Id] = new QuestProgress { Status = QuestStatus.Available };
                    r.Notices.Add(new WorldNotice("quest_available", def.Id));
                    changed = true;
                }
                else if (status == QuestStatus.Available && !Check(def.Prerequisites, s))
                {
                    // 局势变了（如支线要救的人已由主线救出）：未接取的任务收回，不留下无法完成的入口。
                    s.Quests.Remove(def.Id);
                    changed = true;
                }
                else if (status == QuestStatus.Active)
                {
                    changed |= Step(s, def, r);
                }
            }

            if (!changed)
            {
                return;
            }
        }

        r.Fail("任务推进超过上限，内容可能有无条件自循环");
    }

    private bool Step(WorldState s, QuestDefinition def, EffectResult r)
    {
        var p = s.Quests[def.Id];
        var stage = def.StageById(p.Stage)!;
        var changed = false;
        foreach (var o in stage.Objectives)
        {
            if (o.When is not null && !p.Objectives.Contains(o.Id) && Check(o.When, s))
            {
                p.Objectives.Add(o.Id);
                changed = true;
            }
        }

        if (!stage.Objectives.Where(o => !o.Optional).All(o => p.Objectives.Contains(o.Id)))
        {
            return changed;
        }

        var key = $"quest:{def.Id}:{stage.Id}";
        if (s.Settled.Add(key))
        {
            foreach (var e in stage.OnComplete)
            {
                ApplyOne(s, e, r, key);
                if (!r.Ok)
                {
                    return true;
                }
            }
        }

        var next = stage.Branches.FirstOrDefault(b => Check(b.When, s))?.Next ?? stage.Next;
        p.Objectives.Clear();
        if (next is not null)
        {
            p.Stage = next;
            p.History.Add(next);
            r.Notices.Add(new WorldNotice("quest_updated", def.Id));
            return true;
        }

        p.Status = QuestStatus.Completed;
        r.Notices.Add(new WorldNotice("quest_completed", def.Id));
        var rewardKey = $"quest:{def.Id}:rewards";
        if (s.Settled.Add(rewardKey))
        {
            foreach (var e in def.Rewards)
            {
                ApplyOne(s, e, r, rewardKey);
                if (!r.Ok)
                {
                    break;
                }
            }
        }

        return true;
    }

    public static string EventKey(string eventId) => "event:" + eventId;

    public static string DeltaKey(string mapId, string interactableId) => mapId + "/" + interactableId;

    /// <summary>
    /// 当前地图可展示的地区事件：地点、条件、参与人物未被其他事件占用、一次性事件未结算同时满足；
    /// 按“主线紧急 → 已接限时 → 普通地区故事”及事件 ID 排序（架构文档 9.1）。
    /// </summary>
    public IReadOnlyList<StoryEventDefinition> EventsAt(WorldState s, string? mapId = null)
    {
        if (!Content.EventsByMap.TryGetValue(mapId ?? s.MapId, out var events))
        {
            return [];
        }

        return [.. events.Where(e => IsEventOpen(s, e))];
    }

    public bool IsEventOpen(WorldState s, StoryEventDefinition e) =>
        !(e.Once && s.Settled.Contains(EventKey(e.Id)))
        && e.Participants.All(p => !s.Reservations.TryGetValue(p, out var by) || by == e.Id)
        && Check(e.When, s);

    /// <summary>当前地图上可见的交互物（一次性的用过即不再出现）。</summary>
    public IReadOnlyList<MapInteractable> InteractablesAt(WorldState s)
    {
        if (!Content.Maps.TryGetValue(s.MapId, out var map))
        {
            return [];
        }

        return [.. map.Interactables.Where(i => !(i.Once && s.MapDeltas.Contains(DeltaKey(map.Id, i.Id))) && Check(i.When, s))];
    }

    /// <summary>预留事件人物（事件开始时）；已被其他事件占用则返回占用者。</summary>
    public static string? Reserve(WorldState s, StoryEventDefinition e)
    {
        foreach (var p in e.Participants)
        {
            if (s.Reservations.TryGetValue(p, out var by) && by != e.Id)
            {
                return by;
            }
        }

        foreach (var p in e.Participants)
        {
            s.Reservations[p] = e.Id;
        }

        return null;
    }

    /// <summary>释放事件占用的全部人物。</summary>
    public static void Release(WorldState s, string eventId)
    {
        foreach (var key in s.Reservations.Where(kv => kv.Value == eventId).Select(kv => kv.Key).ToList())
        {
            s.Reservations.Remove(key);
        }
    }
}
