using WuxiaWorld.Domain.World;

namespace WuxiaWorld.Domain.Tests;

/// <summary>世界规则测试用的最小内容：街道、客栈、旧渡三张图，一条主线、两条互斥支线、两段对白、两个地区事件。</summary>
internal static class WorldFixture
{
    public const string Main = "quest.main.test";
    public const string SideA = "quest.side.test_a";
    public const string SideB = "quest.side.test_b";

    public static readonly Condition Choice = Condition.Fact("fact.test.choice", "open");

    public static WorldContent Content(IEnumerable<WorldEffect>? newGameEffects = null) => new(
        maps:
        [
            new MapDefinition
            {
                Id = "map.test.street", Region = "region.test", Scene = "town", SafeSpawn = "gate", Spawns = ["gate", "inn_door", "pier"],
                Exits = [new MapExit { Id = "to_inn", To = "map.test.inn", Spawn = "door" }],
                Interactables =
                [
                    new MapInteractable { Id = "chest", Kind = InteractableKind.Pickup, Once = true, Effects = [WorldEffect.Item("item.test.pill", 2)] },
                    new MapInteractable { Id = "stone", Kind = InteractableKind.Inspect, Dialogue = "dlg.test.stone",
                        Effects = [new WorldEffect { Type = WorldEffectType.AddClue, Id = "clue.test.stone" }] },
                ],
            },
            new MapDefinition
            {
                Id = "map.test.inn", Region = "region.test", Scene = "inn", SafeSpawn = "door", Spawns = ["door"],
                Exits = [new MapExit { Id = "out", To = "map.test.street", Spawn = "inn_door" }],
            },
            new MapDefinition
            {
                Id = "map.test.ferry", Region = "region.test", Scene = "wild", SafeSpawn = "landing", Spawns = ["landing"],
                Exits = [new MapExit { Id = "back", To = "map.test.street", Spawn = "pier" }],
            },
        ],
        routes:
        [
            new RouteDefinition
            {
                Id = "route.test.ferry", From = "map.test.street", To = "map.test.ferry", Spawn = "landing",
                Modes = [new RouteMode { Mode = TravelMode.Ferry, Silver = 10, Ticks = 2 }, new RouteMode { Mode = TravelMode.Walk, Ticks = 5 }],
                Encounters = [new RouteEncounter { Event = "event.test.ambush", ChanceBp = 5000 }],
            },
        ],
        events:
        [
            new StoryEventDefinition { Id = "event.test.meet", Map = "map.test.inn", Priority = EventPriority.MainUrgent,
                Participants = ["char.test.ally"], Dialogue = "dlg.test.meet" },
            new StoryEventDefinition { Id = "event.test.gossip", Map = "map.test.inn", Participants = ["char.test.ally"], Dialogue = "dlg.test.stone" },
            new StoryEventDefinition { Id = "event.test.ambush", Map = "map.test.ferry", Dialogue = "dlg.test.stone" },
        ],
        quests:
        [
            new QuestDefinition
            {
                Id = Main, Kind = QuestKind.Main, GuaranteedClue = "clue.test.stone",
                Stages =
                [
                    new QuestStage { Id = "meet", Next = "investigate", Objectives = [new QuestObjective { Id = "talk", Kind = ObjectiveKind.Talk }] },
                    new QuestStage
                    {
                        Id = "investigate", Next = "decide",
                        Objectives =
                        [
                            new QuestObjective { Id = "clue", When = new Condition { Type = ConditionType.ClueKnown, Id = "clue.test.stone" } },
                            new QuestObjective { Id = "extra", Optional = true, When = new Condition { Type = ConditionType.HasItem, Id = "item.test.letter" } },
                        ],
                        OnComplete = [new WorldEffect { Type = WorldEffectType.GrantExperience, Amount = 30 }],
                    },
                    new QuestStage
                    {
                        Id = "decide", Next = "sealed",
                        Objectives = [new QuestObjective { Id = "choose", When = new Condition { Type = ConditionType.Any,
                            Of = [Choice, Condition.Fact("fact.test.choice", "sealed")] } }],
                        Branches = [new Branch { When = Choice, Next = "public" }],
                    },
                    new QuestStage { Id = "public", OnComplete = [WorldEffect.Fact("fact.test.ending", "public")] },
                    new QuestStage { Id = "sealed", OnComplete = [WorldEffect.Fact("fact.test.ending", "sealed")] },
                ],
                Rewards = [new WorldEffect { Type = WorldEffectType.ChangeSilver, Amount = 50 }],
            },
            new QuestDefinition
            {
                Id = SideA, Kind = QuestKind.Side, ExclusiveGroup = "group.test",
                Prerequisites = new Condition { Type = ConditionType.CharacterMet, Id = "char.test.ally" },
                Stages = [new QuestStage { Id = "go", Objectives = [new QuestObjective { Id = "pill", Kind = ObjectiveKind.Deliver }] }],
                OnAbandon = [WorldEffect.Fact("fact.test.side_a", "abandoned")],
                Rewards = [WorldEffect.Item("item.test.letter")],
            },
            new QuestDefinition
            {
                Id = SideB, Kind = QuestKind.Side, ExclusiveGroup = "group.test",
                Stages = [new QuestStage { Id = "go", Objectives = [new QuestObjective { Id = "x" }] }],
            },
        ],
        dialogues:
        [
            new DialogueDefinition
            {
                Id = "dlg.test.meet", Entry = "l1",
                Nodes =
                [
                    new DialogueNode { Id = "l1", Type = DialogueNodeType.Line, LineId = "test.meet.001", Speaker = "char.test.ally", Text = "来了？", Next = "gift" },
                    new DialogueNode { Id = "gift", Type = DialogueNodeType.Effect, Next = "again",
                        Effects = [WorldEffect.Item("item.test.pill"), new WorldEffect { Type = WorldEffectType.MeetCharacter, Id = "char.test.ally" },
                            WorldEffect.Objective(Main, "talk")] },
                    new DialogueNode { Id = "again", Type = DialogueNodeType.Effect, Repeatable = true, Next = "ask",
                        Effects = [new WorldEffect { Type = WorldEffectType.AdvanceClock, Amount = 1 }] },
                    new DialogueNode
                    {
                        Id = "ask", Type = DialogueNodeType.Choice,
                        Options =
                        [
                            new DialogueOption { LineId = "test.meet.opt.trust", Text = "信你", Next = "end",
                                Effects = [new WorldEffect { Type = WorldEffectType.ChangeRelationship, Id = "char.test.ally", Axis = RelationshipAxis.Trust, Amount = 5 }] },
                            new DialogueOption { LineId = "test.meet.opt.rich", Text = "我有钱", Next = "end",
                                When = new Condition { Type = ConditionType.SilverAtLeast, Amount = 1000 }, LockedHint = "需要 1000 两" },
                            new DialogueOption { LineId = "test.meet.opt.secret", Text = "暗号", Next = "end",
                                When = Condition.Fact("fact.test.secret", "true") },
                            new DialogueOption { LineId = "test.meet.opt.fail", Text = "交出信", Next = "end",
                                Effects = [new WorldEffect { Type = WorldEffectType.RemoveItem, Id = "item.test.letter" }] },
                        ],
                    },
                    new DialogueNode { Id = "end", Type = DialogueNodeType.End },
                ],
            },
            new DialogueDefinition
            {
                Id = "dlg.test.stone", Entry = "l1",
                Nodes = [new DialogueNode { Id = "l1", Type = DialogueNodeType.Stage, Kind = StageKind.Closeup, Direction = "石上的水痕特写" }],
            },
        ],
        items:
        [
            new ItemDefinition { Id = "item.test.pill", Category = ItemCategory.Medicine, Price = 5 },
            new ItemDefinition { Id = "item.test.letter", Category = ItemCategory.Quest, Key = true, Stack = 1 },
        ],
        characters:
        [
            new CharacterDefinition { Id = "char.test.hero", Origin = CharacterOrigin.Hero },
            new CharacterDefinition { Id = "char.test.ally", Origin = CharacterOrigin.Original },
            new CharacterDefinition { Id = "char.test.c2", Origin = CharacterOrigin.Original },
            new CharacterDefinition { Id = "char.test.c3", Origin = CharacterOrigin.Original },
            new CharacterDefinition { Id = "char.test.c4", Origin = CharacterOrigin.Original },
        ],
        newGame: new NewGameDefinition
        {
            Arc = "arc.01", Chapter = "chapter.01", Map = "map.test.inn", Spawn = "door", Party = ["char.test.hero"], Silver = 20, Seed = 7,
            Effects = newGameEffects?.ToList() ?? [new WorldEffect { Type = WorldEffectType.StartQuest, Id = Main }],
        });

    public static WorldRules Rules() => new(Content());
}
