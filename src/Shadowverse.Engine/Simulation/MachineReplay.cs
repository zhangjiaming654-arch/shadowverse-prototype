using System.Globalization;
using System.Text.Json;
using Shadowverse.Engine.Game;
using Shadowverse.Engine.Models;

namespace Shadowverse.Engine.Simulation;

/// <summary>
/// 机器可读对局回放（<c>SVP/R2</c>）。
/// <para>
/// 放在引擎里而不是界面里，是为了让**命令行也能导出** —— 否则这套格式只能在
/// WinForms 界面上点出来，没法在自检里验证。
/// </para>
/// <para>
/// R2 相对 R1 的两处关键增强：
/// <list type="number">
/// <item><b>完整卡表</b>：<c>i</c> 不只记初始手牌+牌库，而是**对局全程出现过的所有实例**
/// （含对局中生成的衍生卡）。R1 里衍生卡事后无法还原卡名。</item>
/// <item><b>每步状态快照</b>：<c>snap</c> 记录每一步结算之后双方的完整状态。
/// R1 只有动作序列，事后分析只能"从动作推断"谁被换掉、生命怎么变；
/// 有了快照就是**数据直证**。</item>
/// </list>
/// </para>
/// </summary>
public static class MachineReplay
{
    public const int Version = 2;
    public const string Format = "SVP/R2";

    private static readonly JsonSerializerOptions CompactJson = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = false
    };

    public static object Build(
        ulong seed,
        string firstDeckInfo,
        string firstAgentInfo,
        string secondDeckInfo,
        string secondAgentInfo,
        GameState initialState,
        int winner,
        IReadOnlyList<MatchStep> steps)
    {
        ArgumentNullException.ThrowIfNull(initialState);
        ArgumentNullException.ThrowIfNull(steps);

        var final = steps.Count > 0 ? steps[^1].AfterState : initialState;
        return new
        {
            v = Version,
            f = Format,
            seed = seed.ToString(CultureInfo.InvariantCulture),
            p = new[]
            {
                new { d = firstDeckInfo, a = firstAgentInfo },
                new { d = secondDeckInfo, a = secondAgentInfo }
            },
            first = initialState.StartingPlayer,
            win = winner,
            z = new[] { final.Players[0].Health, final.Players[1].Health },
            i = CardMap(initialState, steps),
            e = steps.Select(ToEvent).ToArray(),
            snap = steps.Select(ToSnapshot).ToArray()
        };
    }

    public static string Serialize(
        ulong seed,
        string firstDeckInfo,
        string firstAgentInfo,
        string secondDeckInfo,
        string secondAgentInfo,
        GameState initialState,
        int winner,
        IReadOnlyList<MatchStep> steps) =>
        JsonSerializer.Serialize(
            Build(seed, firstDeckInfo, firstAgentInfo, secondDeckInfo, secondAgentInfo,
                initialState, winner, steps),
            CompactJson);

    /// <summary>
    /// 对局**全程**出现过的所有实例 → 卡号。含手牌、牌库、战场、护符、墓地，
    /// 所以衍生卡（对局中才生成的实例）也能还原卡名。
    /// </summary>
    private static object[][][] CardMap(GameState initialState, IReadOnlyList<MatchStep> steps)
    {
        var maps = new[] { new Dictionary<int, string>(), new Dictionary<int, string>() };

        void Harvest(GameState? state)
        {
            if (state is null)
            {
                return;
            }

            for (var seat = 0; seat < state.Players.Length; seat++)
            {
                var player = state.Players[seat];
                foreach (var card in player.Hand.Concat(player.Deck).Concat(player.Graveyard))
                {
                    maps[seat][card.InstanceId] = card.Definition.Id;
                }

                foreach (var follower in player.Board)
                {
                    maps[seat][follower.InstanceId] = follower.Definition.Id;
                }

                foreach (var amulet in player.Amulets)
                {
                    maps[seat][amulet.InstanceId] = amulet.Definition.Id;
                }
            }
        }

        Harvest(initialState);
        foreach (var step in steps)
        {
            Harvest(step.BeforeState);
            Harvest(step.AfterState);
        }

        return maps
            .Select(map => map.OrderBy(pair => pair.Key)
                .Select(pair => new object[] { pair.Key, pair.Value })
                .ToArray())
            .ToArray();
    }

    // 紧凑事件格式：[回合, 行动方, 动作码, …动作相关值]。
    private static object?[] ToEvent(MatchStep step)
    {
        var before = step.BeforeState.Players[step.ActingPlayer];
        return step.Action switch
        {
            MulliganAction action => [step.BeforeState.TurnNumber, step.ActingPlayer, "M", action.ReplaceInstanceIds],
            PlayFollowerAction action => [step.BeforeState.TurnNumber, step.ActingPlayer, "F", CardId(before, action.CardInstanceId), action.CardInstanceId, action.HandCardTargetInstanceId, action.EnemyFollowerTargetInstanceIds, action.ModeChoiceIndex, action.OwnHandCardTargetInstanceIds],
            PlayAmuletAction action => [step.BeforeState.TurnNumber, step.ActingPlayer, "A", CardId(before, action.CardInstanceId), action.CardInstanceId],
            PlayCrystallizeAction action => [step.BeforeState.TurnNumber, step.ActingPlayer, "Y", CardId(before, action.CardInstanceId), action.CardInstanceId],
            PlayAccelerateAction action => [step.BeforeState.TurnNumber, step.ActingPlayer, "X", CardId(before, action.CardInstanceId), action.CardInstanceId],
            PlaySpellAction action => [step.BeforeState.TurnNumber, step.ActingPlayer, "S", CardId(before, action.CardInstanceId), action.CardInstanceId, SpellTargetCode(action.Target), action.OwnHandCardTargetInstanceIds],
            EvolveAction action => [step.BeforeState.TurnNumber, step.ActingPlayer, "E", action.FollowerInstanceId, action.ModeChoiceIndex, action.OwnHandCardTargetInstanceIds, action.EnemyFollowerTargetInstanceId],
            SuperEvolveAction action => [step.BeforeState.TurnNumber, step.ActingPlayer, "U", action.FollowerInstanceId, action.OtherFollowerTargetInstanceId, action.ModeChoiceIndex, action.OwnHandCardTargetInstanceIds, action.EnemyFollowerTargetInstanceId],
            UseExtraPlayPointAction => [step.BeforeState.TurnNumber, step.ActingPlayer, "P"],
            AttackLeaderAction action => [step.BeforeState.TurnNumber, step.ActingPlayer, "L", action.AttackerInstanceId],
            AttackFollowerAction action => [step.BeforeState.TurnNumber, step.ActingPlayer, "B", action.AttackerInstanceId, action.DefenderInstanceId],
            EndTurnAction => [step.BeforeState.TurnNumber, step.ActingPlayer, "T"],
            _ => [step.BeforeState.TurnNumber, step.ActingPlayer, "?", step.Action.GetType().Name]
        };
    }

    private static string? CardId(PlayerState player, int instanceId) =>
        player.Hand.Concat(player.Deck)
            .FirstOrDefault(card => card.InstanceId == instanceId)
            ?.Definition.Id;

    private static object? SpellTargetCode(SpellTarget? target) => target switch
    {
        null => null,
        EnemyLeaderTarget => "L",
        EnemyFollowerTarget follower => follower.FollowerInstanceId,
        FollowerTarget follower => follower.FollowerInstanceId,
        AmuletTarget amulet => $"A{amulet.AmuletInstanceId}",
        _ => "?"
    };

    /// <summary>
    /// 一步结算之后的完整快照：[回合, 行动方, 玩家0状态, 玩家1状态]。
    /// 玩家状态 = [生命, 生命上限, 当前PP, PP上限, EP, SEP,
    ///              手牌数, 牌库数, 墓地数,
    ///              战场 [[实例, 攻, 当前防, 最大防, 进化状态, 关键词], …],
    ///              护符 [[实例, 吟唱剩余], …],
    ///              纹章 [纹章号, …]]
    /// </summary>
    private static object?[] ToSnapshot(MatchStep step) =>
    [
        step.AfterState.TurnNumber,
        step.ActingPlayer,
        PlayerSnapshot(step.AfterState.Players[0]),
        PlayerSnapshot(step.AfterState.Players[1])
    ];

    private static object?[] PlayerSnapshot(PlayerState player) =>
    [
        player.Health,
        player.MaxHealth,
        player.CurrentPlayPoints,
        player.MaxPlayPoints,
        player.EvolutionPoints,
        player.SuperEvolutionPoints,
        player.Hand.Count,
        player.Deck.Count,
        player.Graveyard.Count,
        player.Board.Select(follower => new object?[]
        {
            follower.InstanceId,
            follower.Attack,
            follower.CurrentDefense,
            follower.MaxDefense,
            (int)follower.EvolutionState,
            (int)follower.GrantedKeywords
        }).ToArray(),
        player.Amulets.Select(amulet => new object?[]
        {
            amulet.InstanceId,
            amulet.Countdown
        }).ToArray(),
        player.Crests.Select(crest => crest.Definition.Id).ToArray()
    ];
}
