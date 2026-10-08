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
        PlaySpellAction spell => spell.Target switch
        {
            EnemyFollowerTarget target => target.FollowerInstanceId == followerInstanceId,
            FollowerTarget target => target.FollowerInstanceId == followerInstanceId,
            AmuletTarget target => target.AmuletInstanceId == followerInstanceId,
            _ => false
        },
        PlayFollowerAction play => play.EnemyFollowerTargetInstanceIds?.Contains(followerInstanceId) == true,
        EvolveAction evolve => evolve.EnemyFollowerTargetInstanceId == followerInstanceId,
        SuperEvolveAction super => super.EnemyFollowerTargetInstanceId == followerInstanceId,
        _ => false
    };

    /// <summary>这个动作是否要指定<b>某个</b>敌方随从为目标（具体是哪个由选择决定）。</summary>
    public static bool TargetsAnyFollower(GameAction action) => action switch
    {
        PlaySpellAction spell => spell.Target is EnemyFollowerTarget or FollowerTarget or AmuletTarget,
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

    /// <summary>
    /// 把自己的随从拖到某个落点后的候选动作。
    /// <para>
    /// <b>不只是攻击</b>。【进化时】指定敌方随从的效果（例如恶魔鼓手·拉兹的"对1个敌方随从造成伤害"）
    /// 本质也是"把这个随从指向那个目标"，所以拖动它才是自然的操作方式；
    /// 超进化指定己方随从（"另一个未进化的己方随从也进化"）同理，落点是自己的随从。
    /// </para>
    /// <para>
    /// 落点同时满足多种动作时（既能攻击、又能指定它进化）返回多个候选，
    /// 由界面弹菜单让用户选 —— 不替用户猜。
    /// </para>
    /// </summary>
    public static IReadOnlyList<GameAction> FromFollower(
        IReadOnlyList<GameAction> legalActions,
        int followerInstanceId,
        int? enemyFollowerTargetInstanceId = null,
        bool enemyLeaderTarget = false,
        int? allyFollowerTargetInstanceId = null)
    {
        var candidates = new List<GameAction>();

        // 能指向敌方主战者的只有攻击。
        if (enemyLeaderTarget)
        {
            candidates.AddRange(legalActions
                .OfType<AttackLeaderAction>()
                .Where(action => action.AttackerInstanceId == followerInstanceId));
            return candidates;
        }

        if (enemyFollowerTargetInstanceId is int enemyId)
        {
            candidates.AddRange(legalActions
                .OfType<AttackFollowerAction>()
                .Where(action => action.AttackerInstanceId == followerInstanceId
                                 && action.DefenderInstanceId == enemyId));

            // 【进化时】指定这个敌方随从。
            candidates.AddRange(legalActions
                .OfType<EvolveAction>()
                .Where(action => action.FollowerInstanceId == followerInstanceId
                                 && action.EnemyFollowerTargetInstanceId == enemyId));

            candidates.AddRange(legalActions
                .OfType<SuperEvolveAction>()
                .Where(action => action.FollowerInstanceId == followerInstanceId
                                 && action.EnemyFollowerTargetInstanceId == enemyId));
        }

        // 超进化指定"另一个未进化的己方随从"，落点是自己的随从。
        if (allyFollowerTargetInstanceId is int allyId && allyId != followerInstanceId)
        {
            candidates.AddRange(legalActions
                .OfType<SuperEvolveAction>()
                .Where(action => action.FollowerInstanceId == followerInstanceId
                                 && action.OtherFollowerTargetInstanceId == allyId));
        }

        return candidates;
    }

    /// <summary>点击自己场上的护符：只返回这张护符的合法启动动作。</summary>
    public static IReadOnlyList<GameAction> StartAbility(IReadOnlyList<GameAction> legalActions, int amuletInstanceId) =>
        legalActions.OfType<UseStartAbilityAction>().Where(action => action.AmuletInstanceId == amuletInstanceId)
            .Distinct().Cast<GameAction>().ToArray();

    /// <summary>启动不可用时显示已知原因；是否可用仍由合法动作清单决定。</summary>
    public static string? StartAbilityBlockReason(GameObservation observation, IReadOnlyList<GameAction> legalActions, int amuletInstanceId)
    {
        var amulet = observation.Self.Amulets?.FirstOrDefault(a => a.InstanceId == amuletInstanceId);
        if (amulet is null) return "护符已不在场上";
        var start = CardCatalog.Get(amulet.CardId).StartAbility;
        if (start is null) return "这张护符没有启动能力";
        if (observation.Phase != GamePhase.Main || observation.ActivePlayer != observation.PerspectivePlayer)
            return "现在不是你的行动回合";
        if (StartAbility(legalActions, amuletInstanceId).Count > 0) return null;
        if (amulet.StartAbilityUsedThisTurn) return "本回合已启动";
        if (observation.Self.CurrentPlayPoints < start.Cost) return $"PP 不足（需要 {start.Cost}）";
        if (observation.OwnHand.Count == 0 && start.Effects.Any(e => e.Kind == CardEffectKind.TransformOwnHandCardIntoRandomOpponentDeckCopy))
            return "没有可选择的手牌";
        return "当前不可启动";
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

    /// <summary>点击手牌选择出牌；保留这张牌的全部合法形式与目标。</summary>
    public static IReadOnlyList<GameAction> ClickHand(IReadOnlyList<GameAction> legalActions, int cardInstanceId) =>
        legalActions.Where(a => PlayedCardOf(a) == cardInstanceId).ToArray();

    /// <summary>Enemy drops require an exact legal target; own-board drops may begin a guided play.</summary>
    public static IReadOnlyList<GameAction> DropHand(GameObservation observation, IReadOnlyList<GameAction> legalActions,
        int cardInstanceId, HumanTarget? target)
    {
        var plays = ClickHand(legalActions, cardInstanceId);
        if (target is null) return plays;
        var exact = plays.Where(a => HumanTargetSelection.Targets(observation, a, HumanTargetRole.Effect).Contains(target)).ToArray();
        return exact.Length > 0 || target.Zone != HumanTargetZone.OwnBoard ? exact : plays;
    }

    // ───────────────────────────── 模式（【模式】卡牌）─────────────────────────────

    /// <summary>这个动作选的是第几个模式；不涉及模式就返回 null。</summary>
    public static int? ModeIndexOf(GameAction action) => action switch
    {
        PlayFollowerAction play => play.ModeChoiceIndex,
        // **法术也有【模式】**（例如「跑酷」的抉择）。之前漏了这一条，于是法术的两个模式
        // 在界面上只能显示同一个通用标签，用户根本无从选起。
        PlaySpellAction spell => spell.ModeChoiceIndex,
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
        PlaySpellAction spell => SpellModeOptionsOf(observation, spell.CardInstanceId),
        EvolveAction evolve => EvolutionModeOptionsOf(observation, evolve.FollowerInstanceId),
        SuperEvolveAction super => EvolutionModeOptionsOf(observation, super.FollowerInstanceId),
        _ => null
    };

    /// <summary>
    /// 法术的【模式】。目前只有一种编码：<c>ParkourChoiceOrAllModes</c> 把
    /// "特征|阈值|卡名1|卡名2…" 打包在一个字符串里，每个卡名就是一个可选的模式
    /// （「将1张『X』加入手牌」）。模式名直接取自卡牌目录，所以和卡面文字一致。
    /// </summary>
    private static IReadOnlyList<ModeDefinition>? SpellModeOptionsOf(
        GameObservation observation,
        int cardInstanceId)
    {
        var definition = observation.OwnHand
            .FirstOrDefault(card => card.InstanceId == cardInstanceId)?.Definition;
        var payload = (definition?.Effect?.Kind == CardEffectKind.ParkourChoiceOrAllModes
            ? definition.Effect : definition?.SpellEffects?
                .FirstOrDefault(effect => effect.Kind == CardEffectKind.ParkourChoiceOrAllModes))?.ReferencedCardId;
        if (payload is null)
        {
            return null;
        }

        var parts = payload.Split('|');
        if (parts.Length < 3)
        {
            return null;
        }

        return parts.Skip(2)
            .Select(cardId =>
            {
                var name = CardCatalog.Get(cardId).Name;
                return new ModeDefinition(
                    $"将1张『{name}』加入手牌",
                    [new CardEffect(CardEffectKind.AddCopyToHand, 1, cardId)]);
            })
            .ToArray();
    }

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
