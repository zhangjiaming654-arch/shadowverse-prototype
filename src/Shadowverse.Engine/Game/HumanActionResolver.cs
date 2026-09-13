using Shadowverse.Engine.Cards;
using Shadowverse.Engine.Models;

namespace Shadowverse.Engine.Game;

/// <summary>
/// 把真人的一个"拖拽手势"翻译成<b>引擎已经给出的合法动作</b>的子集。
/// <para>
/// 这个类存在的唯一理由：界面<b>绝对不允许自己判断一个动作合不合法</b>。
/// 自己写一套规则判断，迟早会和 <c>GameEngine</c> 分叉 —— 界面允许了引擎拒绝的动作，
/// 或者反过来。所以这里只做一件事：在 <c>legalActions</c> 里<b>筛</b>。
/// 筛出来是空集就说明这个手势没有对应动作，筛出多个就让用户再选一次。
/// </para>
/// <para>
/// <b>覆盖性</b>：任何一个合法动作都必须能被某个手势筛出来，否则真人就有动作点不到。
/// 每个"手势"都是一个粗筛 —— 因为筛选失败会退回全集，所以粗筛是安全的：
/// 最坏情况只是多弹一次选择菜单，不会漏动作。<c>HumanActionResolverSelfTest</c> 守住这条性质。
/// </para>
/// <para>
/// 纯逻辑，不依赖任何界面类型，所以能在控制台里跑自检。
/// </para>
/// </summary>
public static class HumanActionResolver
{
    /// <summary>某个动作是从手牌里的哪张牌打出的；不是"从手牌打出"就返回 null。</summary>
    public static int? PlayedCardOf(GameAction action) => action switch
    {
        PlayFollowerAction play => play.CardInstanceId,
        PlayAmuletAction play => play.CardInstanceId,
        PlaySpellAction play => play.CardInstanceId,
        PlayCrystallizeAction play => play.CardInstanceId,
        PlayAccelerateAction play => play.CardInstanceId,
        _ => null
    };

    /// <summary>这个动作是否要求指定"敌方主战者"为目标。</summary>
    public static bool TargetsEnemyLeader(GameAction action) =>
        action is PlaySpellAction { Target: EnemyLeaderTarget };

    /// <summary>这个动作是否要求指定某个敌方随从为目标。</summary>
    public static bool TargetsFollower(GameAction action, int followerInstanceId) => action switch
    {
        PlaySpellAction spell => spell.Target is EnemyFollowerTarget target
                                 && target.FollowerInstanceId == followerInstanceId,
        PlayFollowerAction play => play.EnemyFollowerTargetInstanceIds?.Contains(followerInstanceId) == true,
        EvolveAction evolve => evolve.EnemyFollowerTargetInstanceId == followerInstanceId,
        SuperEvolveAction super => super.EnemyFollowerTargetInstanceId == followerInstanceId,
        _ => false
    };

    /// <summary>这个动作是否要指定<b>某个</b>敌方随从为目标（具体是哪个由选择决定）。</summary>
    public static bool TargetsAnyFollower(GameAction action) => action switch
    {
        PlaySpellAction spell => spell.Target is EnemyFollowerTarget,
        PlayFollowerAction play => play.EnemyFollowerTargetInstanceIds is { Count: > 0 },
        EvolveAction evolve => evolve.EnemyFollowerTargetInstanceId is not null,
        SuperEvolveAction super => super.EnemyFollowerTargetInstanceId is not null,
        _ => false
    };

    /// <summary>
    /// 从手牌把 <paramref name="cardInstanceId"/> 拖到某个落点后的候选动作。
    /// <paramref name="targetFollowerInstanceId"/> 和 <paramref name="targetLeader"/> 描述落点；
    /// 两者都是空/假就表示"拖到了自己的场上"，此时优先选<b>不需要目标</b>的那些变体 ——
    /// 拖到自己场上不应该被理解成"指定了敌方目标"。
    /// </summary>
    public static IReadOnlyList<GameAction> FromHand(
        IReadOnlyList<GameAction> legalActions,
        int cardInstanceId,
        int? targetFollowerInstanceId = null,
        bool targetLeader = false)
    {
        var played = legalActions.Where(action => PlayedCardOf(action) == cardInstanceId).ToList();
        if (played.Count <= 1)
        {
            return played;
        }

        List<GameAction> preferred;
        if (targetLeader)
        {
            preferred = played.Where(TargetsEnemyLeader).ToList();
        }
        else if (targetFollowerInstanceId is int followerId)
        {
            preferred = played.Where(action => TargetsFollower(action, followerId)).ToList();
        }
        else
        {
            preferred = played
                .Where(action => !TargetsEnemyLeader(action) && !TargetsAnyFollower(action))
                .ToList();
        }

        // 优先子集为空 = 这张牌只有"带目标"的变体。退回全集让用户自己挑，绝不静默丢掉动作。
        return preferred.Count > 0 ? preferred : played;
    }

    /// <summary>把自己的随从拖到某个落点后的候选攻击动作。</summary>
    public static IReadOnlyList<GameAction> Attack(
        IReadOnlyList<GameAction> legalActions,
        int attackerInstanceId,
        int? defenderInstanceId = null,
        bool targetLeader = false)
    {
        if (targetLeader)
        {
            return legalActions
                .OfType<AttackLeaderAction>()
                .Where(action => action.AttackerInstanceId == attackerInstanceId)
                .Cast<GameAction>()
                .ToList();
        }

        if (defenderInstanceId is not int defenderId)
        {
            return [];
        }

        return legalActions
            .OfType<AttackFollowerAction>()
            .Where(action => action.AttackerInstanceId == attackerInstanceId
                             && action.DefenderInstanceId == defenderId)
            .Cast<GameAction>()
            .ToList();
    }

    /// <summary>右键自己的随从：进化 / 超进化的所有变体（可能带目标或模式选择，交给菜单）。</summary>
    public static IReadOnlyList<GameAction> Evolve(
        IReadOnlyList<GameAction> legalActions,
        int followerInstanceId) =>
        legalActions.Where(action => action switch
        {
            EvolveAction evolve => evolve.FollowerInstanceId == followerInstanceId,
            SuperEvolveAction super => super.FollowerInstanceId == followerInstanceId,
            _ => false
        }).ToList();

    /// <summary>
    /// 点"确认换牌"后要提交的动作。<paramref name="replaceInstanceIds"/> 是用户标记要换掉的那些牌。
    /// 引擎把换牌建模成"选一个子集"，所以这里必须找回<b>集合完全相同</b>的那一个动作。
    /// </summary>
    public static GameAction? Mulligan(
        IReadOnlyList<GameAction> legalActions,
        IReadOnlyCollection<int> replaceInstanceIds)
    {
        var wanted = replaceInstanceIds.OrderBy(id => id).ToArray();
        foreach (var action in legalActions.OfType<MulliganAction>())
        {
            var actual = action.ReplaceInstanceIds.OrderBy(id => id).ToArray();
            if (actual.Length == wanted.Length && actual.SequenceEqual(wanted))
            {
                return action;
            }
        }

        return null;
    }

    /// <summary>不需要拖拽、直接给按钮的动作。这些留在常驻按钮上。</summary>
    public static IReadOnlyList<GameAction> Direct(IReadOnlyList<GameAction> legalActions) =>
        legalActions.Where(action => action is UseExtraPlayPointAction or EndTurnAction).ToList();

    // ───────────────────────────── 模式（【模式】卡牌）─────────────────────────────

    /// <summary>这个动作选的是第几个模式；不涉及模式就返回 null。</summary>
    public static int? ModeIndexOf(GameAction action) => action switch
    {
        PlayFollowerAction play => play.ModeChoiceIndex,
        EvolveAction evolve => evolve.ModeChoiceIndex,
        SuperEvolveAction super => super.ModeChoiceIndex,
        _ => null
    };

    /// <summary>这个动作可以选的模式列表；不涉及模式就返回 null。</summary>
    public static IReadOnlyList<ModeDefinition>? ModeOptionsOf(
        GameObservation observation,
        GameAction action) => action switch
    {
        PlayFollowerAction play => observation.OwnHand
            .FirstOrDefault(card => card.InstanceId == play.CardInstanceId)
            ?.Definition.FanfareModeOptions,
        EvolveAction evolve => EvolutionModeOptionsOf(observation, evolve.FollowerInstanceId),
        SuperEvolveAction super => EvolutionModeOptionsOf(observation, super.FollowerInstanceId),
        _ => null
    };

    /// <summary>
    /// 这个动作选的模式叫什么。模式名本身就是完整的能力说明
    /// （例如"对对手所有随从造成5点伤害并回复1点进化点"），所以界面可以直接拿它当选项标签。
    /// </summary>
    public static string? ModeNameOf(GameObservation observation, GameAction action)
    {
        if (ModeIndexOf(action) is not int index || index < 0)
        {
            return null;
        }

        var options = ModeOptionsOf(observation, action);
        return options is not null && index < options.Count ? options[index].Name : null;
    }

    /// <summary>
    /// 这一组候选是不是"只在模式上不同"。是的话界面应该进<b>专门的选择模式界面</b>，
    /// 而不是弹一个列着好几条一模一样文字的通用菜单 ——
    /// 那样用户根本无从选起，等于没得选。
    /// </summary>
    public static bool IsModeOnlyChoice(IReadOnlyList<GameAction> candidates)
    {
        if (candidates.Count <= 1)
        {
            return false;
        }

        var modes = candidates.Select(ModeIndexOf).Distinct().ToList();
        return modes.Count > 1 && modes.All(index => index is not null);
    }

    private static IReadOnlyList<ModeDefinition>? EvolutionModeOptionsOf(
        GameObservation observation,
        int followerInstanceId)
    {
        var follower = observation.Self.Board.FirstOrDefault(board => board.InstanceId == followerInstanceId);
        return follower is null ? null : CardCatalog.Get(follower.CardId).EvolutionModeChoices;
    }
}
