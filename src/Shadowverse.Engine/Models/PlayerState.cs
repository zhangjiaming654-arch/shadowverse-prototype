namespace Shadowverse.Engine.Models;

public sealed class PlayerState
{
    internal PlayerState(string deckName)
    {
        DeckName = deckName;
    }

    public const int StartingHealth = 20;
    public const int HandLimit = 9;
    public const int BoardLimit = 5;
    public const int StartingEvolutionPoints = 2;
    public const int StartingSuperEvolutionPoints = 2;
    public const int LeaderAreaLimit = 5;

    public string DeckName { get; }
    public int Health { get; internal set; } = StartingHealth;
    public int MaxHealth { get; internal set; } = StartingHealth;
    public int CurrentPlayPoints { get; internal set; }
    public int MaxPlayPoints { get; internal set; }
    public int OwnTurnNumber { get; internal set; }
    public int EvolutionPoints { get; internal set; } = StartingEvolutionPoints;
    public int SuperEvolutionPoints { get; internal set; } = StartingSuperEvolutionPoints;
    public bool UsedEvolutionOrSuperEvolutionThisTurn { get; internal set; }
    public bool UsedEarlyExtraPlayPoint { get; internal set; }
    public bool UsedLateExtraPlayPoint { get; internal set; }
    public bool AttackedEnemyLeaderThisTurn { get; internal set; }
    public bool AttackedEnemyLeaderOnPreviousTurn { get; internal set; }

    internal List<CardInstance> DeckInternal { get; } = [];
    internal List<CardInstance> HandInternal { get; } = [];
    internal List<FollowerInstance> BoardInternal { get; } = [];
    internal List<AmuletInstance> AmuletsInternal { get; } = [];
    internal List<TimedLeaderEffectInstance> TimedLeaderEffectsInternal { get; } = [];
    internal List<CrestInstance> CrestsInternal { get; } = [];
    internal List<CardInstance> GraveyardInternal { get; } = [];

    /// <summary>
    /// Catalog IDs of cards this player has played in public: followers, amulets, spells and the
    /// Crystallize / Accelerate forms. One entry per play, so duplicates are meaningful — the
    /// opponent can tell that one of three copies has already been spent. Board contents are
    /// visible anyway, so the value here is remembering cards that have since left the board.
    /// <para>
    /// This is public information only. It exists so the other side can work out which saved deck
    /// this player is on and thereby bound their hidden reach, which is the one way to model what
    /// they could still hold without reading their hand.
    /// </para>
    /// </summary>
    internal List<string> RevealedCardIdsInternal { get; } = [];

    public IReadOnlyList<string> RevealedCardIds => RevealedCardIdsInternal;

    public IReadOnlyList<CardInstance> Deck => DeckInternal;
    public IReadOnlyList<CardInstance> Hand => HandInternal;
    public IReadOnlyList<FollowerInstance> Board => BoardInternal;
    public IReadOnlyList<AmuletInstance> Amulets => AmuletsInternal;
    public IReadOnlyList<TimedLeaderEffectInstance> TimedLeaderEffects => TimedLeaderEffectsInternal;
    public IReadOnlyList<CrestInstance> Crests => CrestsInternal;
    public IReadOnlyList<CardInstance> Graveyard => GraveyardInternal;
    public int OccupiedBoardSlots => BoardInternal.Count + AmuletsInternal.Count;

    internal PlayerState DeepCopy()
    {
        var copy = new PlayerState(DeckName)
        {
            Health = Health,
            MaxHealth = MaxHealth,
            CurrentPlayPoints = CurrentPlayPoints,
            MaxPlayPoints = MaxPlayPoints,
            OwnTurnNumber = OwnTurnNumber,
            EvolutionPoints = EvolutionPoints,
            SuperEvolutionPoints = SuperEvolutionPoints,
            UsedEvolutionOrSuperEvolutionThisTurn = UsedEvolutionOrSuperEvolutionThisTurn,
            UsedEarlyExtraPlayPoint = UsedEarlyExtraPlayPoint,
            UsedLateExtraPlayPoint = UsedLateExtraPlayPoint,
            AttackedEnemyLeaderThisTurn = AttackedEnemyLeaderThisTurn,
            AttackedEnemyLeaderOnPreviousTurn = AttackedEnemyLeaderOnPreviousTurn
        };

        // Cards are copied instead of shared so that in-hand cost reductions stay per state.
        copy.DeckInternal.AddRange(DeckInternal.Select(card => card.CopyForState()));
        copy.HandInternal.AddRange(HandInternal.Select(card => card.CopyForState()));
        copy.BoardInternal.AddRange(BoardInternal.Select(follower => follower.DeepCopy()));
        copy.AmuletsInternal.AddRange(AmuletsInternal.Select(amulet => amulet.DeepCopy()));
        copy.TimedLeaderEffectsInternal.AddRange(TimedLeaderEffectsInternal.Select(effect => effect.DeepCopy()));
        copy.CrestsInternal.AddRange(CrestsInternal.Select(crest => crest.DeepCopy()));
        copy.GraveyardInternal.AddRange(GraveyardInternal.Select(card => card.CopyForState()));
        copy.RevealedCardIdsInternal.AddRange(RevealedCardIdsInternal);
        return copy;
    }
}
