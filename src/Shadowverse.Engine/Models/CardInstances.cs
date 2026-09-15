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

    internal CardInstance CopyForState() => new(InstanceId, Definition, HasSuppressedLastWords)
    {
        CostReduction = CostReduction
    };
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
    public CardKeyword Keywords => (Definition.Keywords | GrantedKeywords) & ~ConsumedKeywords;
    public bool HasWard => Keywords.HasFlag(CardKeyword.Ward);
    public bool HasStorm => Keywords.HasFlag(CardKeyword.Storm);
    public bool HasBane => Keywords.HasFlag(CardKeyword.Bane);
    public bool HasIntimidate => Keywords.HasFlag(CardKeyword.Intimidate);
    public bool HasRush => Keywords.HasFlag(CardKeyword.Rush);
    public bool CanIgnoreWard => Keywords.HasFlag(CardKeyword.IgnoreWard);
    public bool HasBarrier => Keywords.HasFlag(CardKeyword.Barrier);
    public bool HasAura => Keywords.HasFlag(CardKeyword.Aura);
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
        TemporaryAttackBonus = TemporaryAttackBonus
    };
}

/// <summary>A permanent amulet occupying one shared board slot. It cannot attack or be attacked.</summary>
public sealed class AmuletInstance
{
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
    internal AmuletInstance DeepCopy() => new(Card.CopyForState(), Crystallized) { Countdown = Countdown };
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
    }

    public CrestDefinition Definition { get; }
    /// <summary>The controller turn number in which the heal trigger was last used.</summary>
    public int LastOwnLeaderRestoreTriggerTurn { get; internal set; } = -1;

    internal CrestInstance DeepCopy() => new(Definition)
    {
        LastOwnLeaderRestoreTriggerTurn = LastOwnLeaderRestoreTriggerTurn
    };
}
