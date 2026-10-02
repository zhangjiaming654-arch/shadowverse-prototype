namespace Shadowverse.Engine.Models;

/// <summary>
/// A concrete copy of a card. An effect can create a copy that has had its Last Words
/// removed without mutating the shared catalog definition.
/// </summary>
public sealed record CardInstance(
    int InstanceId,
    CardDefinition Definition,
    bool HasSuppressedLastWords = false)
{
    /// <summary>
    /// Cost reductions a card accumulated while it sat in hand, such as 『伽罗塔德 对 泽特』's
    /// end-of-turn reduction. The reduction itself has no floor, but the cost that is actually paid
    /// never drops below zero.
    /// </summary>
    public int CostReduction { get; internal set; }

    /// <summary>
    /// 「回合结束前，使自己的所有手牌的费用-1」: a reduction that lasts only until the end of the current
    /// turn. Kept separate from <see cref="CostReduction"/> (which is permanent) so the end-of-turn wipe
    /// can clear exactly this part without touching reductions a card earned on its own.
    /// </summary>
    public int TemporaryCostReduction { get; internal set; }

    /// <summary>
    /// 【融合】已吃掉的素材卡号（按融合顺序）。"若与本卡牌融合的种类的为2"这条要按它算**种类**，
    /// 所以记的是卡号而不是张数。
    /// </summary>
    internal List<string> FusedMaterialCardIdsInternal { get; } = [];

    /// <summary>
    /// 【奥义】槽的加数只算"**这张卡在手上时**"发生的进化（术语表原文：「在手牌中时自己的随从的进化次数」）。
    /// 所以记下它**进手那一刻**玩家的本局进化计数，槽 = 回合数 + (当前计数 − 这个快照)。
    /// 每次进手都重新打一次快照（离场后再回手，只算回手之后的那段）。
    /// </summary>
    internal int HandEntryEvolvedCountInternal { get; set; }

    /// <summary>
    /// 「使手牌中的所有随从 +A/+B」：加成记在**手牌里的卡**上，进场时加到随从身上。
    /// 与随从身上的临时加成分开，因为此时它还不是随从。
    /// </summary>
    internal int HandAttackBonusInternal { get; set; }

    internal int HandDefenseBonusInternal { get; set; }

    /// <summary>进手时的进化计数快照，公开只读供自检核对奥义槽算法。</summary>
    public int HandEntryEvolvedCount => HandEntryEvolvedCountInternal;

    /// <summary>已融合素材的卡号，公开只读供控制台自检断言。</summary>
    public IReadOnlyList<string> FusedMaterialCardIds => FusedMaterialCardIdsInternal;

    internal CardInstance CopyForState()
    {
        var copy = new CardInstance(InstanceId, Definition, HasSuppressedLastWords)
        {
            CostReduction = CostReduction,
            TemporaryCostReduction = TemporaryCostReduction,
            HandEntryEvolvedCountInternal = HandEntryEvolvedCountInternal,
            HandAttackBonusInternal = HandAttackBonusInternal,
            HandDefenseBonusInternal = HandDefenseBonusInternal
        };
        copy.FusedMaterialCardIdsInternal.AddRange(FusedMaterialCardIdsInternal);
        return copy;
    }
}

public enum EvolutionState
{
    Unevolved,
    Evolved,
    SuperEvolved
}

public sealed class FollowerInstance
{
    public FollowerInstance(CardInstance card, int summonedOnTurn)
    {
        Card = card;
        SummonedOnTurn = summonedOnTurn;
        Attack = card.Definition.Attack;
        MaxDefense = card.Definition.Defense;
        CurrentDefense = MaxDefense;
    }

    public CardInstance Card { get; }
    public int InstanceId => Card.InstanceId;
    public CardDefinition Definition => Card.Definition;
    public int Attack { get; internal set; }
    public int MaxDefense { get; internal set; }
    public int CurrentDefense { get; set; }
    public int SummonedOnTurn { get; }
    public bool HasAttacked { get; set; }
    public EvolutionState EvolutionState { get; internal set; }
    public CardKeyword GrantedKeywords { get; internal set; }
    /// <summary>Keywords that have been used up on this particular follower.</summary>
    public CardKeyword ConsumedKeywords { get; internal set; }
    public int TemporaryAttackBonus { get; internal set; }
    /// <summary>
    /// 【潜伏】"通过能力造成伤害时将失去潜行": set by an attack effect that deals damage, and consumed at
    /// the start of this follower's controller's next turn (see <c>StartTurn</c>).
    /// </summary>
    public bool DealtDamageByAbility { get; internal set; }

    /// <summary>「无法攻击随从或主战者」：被交战对手施加的持续限制。</summary>
    public bool CannotAttackInternal { get; internal set; }

    /// <summary>「自己的回合结束时，使本随从消失」：被交战对手施加。</summary>
    public bool VanishesAtEndOfOwnTurnInternal { get; internal set; }
    public CardKeyword Keywords => (Definition.Keywords | GrantedKeywords) & ~ConsumedKeywords;
    public bool HasWard => Keywords.HasFlag(CardKeyword.Ward);
    public bool HasStorm => Keywords.HasFlag(CardKeyword.Storm);
    public bool HasBane => Keywords.HasFlag(CardKeyword.Bane);
    public bool HasIntimidate => Keywords.HasFlag(CardKeyword.Intimidate);
    public bool HasRush => Keywords.HasFlag(CardKeyword.Rush);
    public bool CanIgnoreWard => Keywords.HasFlag(CardKeyword.IgnoreWard);
    public bool HasBarrier => Keywords.HasFlag(CardKeyword.Barrier);
    public bool HasAura => Keywords.HasFlag(CardKeyword.Aura);
    public bool HasStealth => Keywords.HasFlag(CardKeyword.Stealth);
    public bool HasDrain => Keywords.HasFlag(CardKeyword.Drain);
    public bool IsEvolved => EvolutionState is EvolutionState.Evolved or EvolutionState.SuperEvolved;
    public bool IsSuperEvolved => EvolutionState == EvolutionState.SuperEvolved;

    /// <summary>
    /// Copies the follower **and its card**. The card must be copied, not shared:
    /// <see cref="CardInstance.CostReduction"/> has an internal setter, so a shared card
    /// would let one branch's cost reduction leak into another branch (and into the
    /// original state) as soon as a board object travels back to a hand.
    /// </summary>
    internal FollowerInstance DeepCopy() => new(Card.CopyForState(), SummonedOnTurn)
    {
        Attack = Attack,
        MaxDefense = MaxDefense,
        CurrentDefense = CurrentDefense,
        HasAttacked = HasAttacked,
        EvolutionState = EvolutionState,
        GrantedKeywords = GrantedKeywords,
        ConsumedKeywords = ConsumedKeywords,
        TemporaryAttackBonus = TemporaryAttackBonus,
        DealtDamageByAbility = DealtDamageByAbility,
        CannotAttackInternal = CannotAttackInternal,
        VanishesAtEndOfOwnTurnInternal = VanishesAtEndOfOwnTurnInternal
    };
}

/// <summary>A permanent amulet occupying one shared board slot. It cannot attack or be attacked.</summary>
public sealed class AmuletInstance
{
    /// <summary>【启动】「1回合仅限1次」：记下最近一次启动时的持有者回合数。</summary>
    public int StartAbilityUsedOnOwnTurnInternal { get; internal set; } = -1;

    public AmuletInstance(CardInstance card, CrystallizeDefinition? crystallized = null)
    {
        if (crystallized is null && card.Definition.Type != CardType.Amulet)
        {
            throw new ArgumentException("Only an amulet card can create an amulet instance.", nameof(card));
        }

        Card = card;
        Crystallized = crystallized;
        Countdown = crystallized?.Countdown ?? card.Definition.Countdown;
    }

    public CardInstance Card { get; }
    public int InstanceId => Card.InstanceId;
    public CardDefinition Definition => Card.Definition;
    /// <summary>Set when the amulet was played with a 「结晶 N」 ability instead of as a printed amulet.</summary>
    public CrystallizeDefinition? Crystallized { get; }
    /// <summary>
    /// The Last Words this amulet resolves when it is destroyed: the crystallized ones when it was
    /// played with 结晶, otherwise the ones printed on the amulet card.
    /// </summary>
    public IReadOnlyList<CardEffect>? LastWordsEffects =>
        Crystallized is not null ? Crystallized.LastWordsEffects : Definition.LastWordsEffects;
    public int? Countdown { get; internal set; }

    /// <summary>Copies the amulet **and its card**; see <see cref="FollowerInstance.DeepCopy"/> for why.</summary>
    internal AmuletInstance DeepCopy() => new(Card.CopyForState(), Crystallized)
    {
        Countdown = Countdown,
        StartAbilityUsedOnOwnTurnInternal = StartAbilityUsedOnOwnTurnInternal
    };
}

public enum TimedLeaderEffectKind
{
    PreventAllDamage
}

/// <summary>A non-crest leader effect with an explicit expiry point.</summary>
public sealed class TimedLeaderEffectInstance
{
    public TimedLeaderEffectInstance(TimedLeaderEffectKind kind, int expiresAtEndOfPlayerTurn)
    {
        Kind = kind;
        ExpiresAtEndOfPlayerTurn = expiresAtEndOfPlayerTurn;
    }

    public TimedLeaderEffectKind Kind { get; }
    public int ExpiresAtEndOfPlayerTurn { get; }

    internal TimedLeaderEffectInstance DeepCopy() => new(Kind, ExpiresAtEndOfPlayerTurn);
}

/// <summary>A named persistent effect displayed in a leader's crest area.</summary>
public sealed class CrestInstance
{
    public CrestInstance(CrestDefinition definition)
    {
        Definition = definition ?? throw new ArgumentNullException(nameof(definition));
        Countdown = definition.Countdown;
    }

    public CrestDefinition Definition { get; }
    /// <summary>The controller turn number in which the heal trigger was last used.</summary>
    public int LastOwnLeaderRestoreTriggerTurn { get; internal set; } = -1;

    /// <summary>
    /// 「自己使用随从时，自己的每回合中可触发1次，使其进化」: the controller turn number in which this crest
    /// last evolved a played follower. Kept separate from the heal trigger so the two once-per-turn
    /// limits do not consume each other.
    /// </summary>
    public int LastEvolvePlayedFollowerTriggerTurn { get; internal set; } = -1;

    /// <summary>【吟唱_N】的当前倒计数；null 表示这张纹章不计时。构造时按定义初始化。</summary>
    public int? Countdown { get; internal set; }

    /// <summary>
    /// 获得这张纹章的**先后顺序**（全局递增）。同一优先级（例如"自己的纹章效果"）里有多个纹章时，
    /// 按这个顺序**从早到晚**发动 —— 卡牌设计者给定的规则。
    /// </summary>
    public long AcquiredSequence { get; internal set; } = -1;

    /// <summary>
    /// 「从以下未发动的能力中随机发动1个能力」: which numbered slots this crest has already rolled.
    /// The ability only ever picks from the slots it has not used yet, and this record must survive
    /// <see cref="DeepCopy"/> or a search branch would re-roll an already-spent slot.
    /// </summary>
    internal HashSet<int> UsedAbilitySlotsInternal { get; } = [];

    /// <summary>
    /// How many numbered abilities this crest has already rolled. Public so the console self-tests can
    /// assert the "never repeats a spent slot" rule without reaching into engine internals.
    /// </summary>
    public int UsedAbilitySlotCount => UsedAbilitySlotsInternal.Count;

    internal CrestInstance DeepCopy()
    {
        var copy = new CrestInstance(Definition)
        {
            LastOwnLeaderRestoreTriggerTurn = LastOwnLeaderRestoreTriggerTurn,
            LastEvolvePlayedFollowerTriggerTurn = LastEvolvePlayedFollowerTriggerTurn,
            Countdown = Countdown,
            AcquiredSequence = AcquiredSequence
        };
        copy.UsedAbilitySlotsInternal.UnionWith(UsedAbilitySlotsInternal);
        return copy;
    }
}
