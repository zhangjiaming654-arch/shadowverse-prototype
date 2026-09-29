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

    /// <summary>
    /// 【奥义】槽的第二个加项：本局中"自己的随从进化过的次数"。官方术语表的算法是
    /// <c>奥义槽 = 现在的回合数 + 在手牌中时自己的随从的进化次数</c>。
    /// <para>
    /// 回合数直接用 <see cref="OwnTurnNumber"/>，所以这里只累计进化次数；手动进化与
    /// 能力造成的进化都算。这个数字**不是隐藏信息**（对手看得见进化），所以放进状态指纹不影响公平性。
    /// </para>
    /// </summary>
    public int OwnFollowersEvolvedThisBattle { get; internal set; }

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

    /// <summary>
    /// Instance ids of this player's followers that have already triggered the
    /// 「自己的创造物·随从进入战场时」 passives. One entry per follower ever entering play, so a
    /// follower that leaves and returns is a new entry — which matches "进入战场时".
    /// </summary>
    internal HashSet<int> EnteredTraitFollowerInstanceIdsInternal { get; } = [];

    /// <summary>
    /// Catalog ids of the distinct trait followers this player has brought into play this battle —
    /// the "种类" that conditions like 【超进化时】若…种类为3种或以上 count. Kept as a set so the same
    /// card entering twice counts once.
    /// </summary>
    internal HashSet<string> EnteredTraitFollowerKindIdsInternal { get; } = [];

    /// <summary>
    /// 「使对手的主战者获得『受到的伤害+1』」: a persistent bonus added to every damage instance this
    /// player's leader takes. Permanent for the rest of the match (the card states no duration), so it
    /// is part of the state fingerprint.
    /// </summary>
    internal int LeaderDamageTakenBonusInternal { get; set; }

    public IReadOnlyList<string> RevealedCardIds => RevealedCardIdsInternal;

    /// <summary>
    /// 「使对手的主战者获得『受到的伤害+1』」的当前值，以及"本局进场的类别随从卡号"。
    /// 公开只读，供控制台自检断言，不必为了测试开放引擎内部集合。
    /// </summary>
    public int LeaderDamageTakenBonus => LeaderDamageTakenBonusInternal;

    public IReadOnlyCollection<string> EnteredTraitFollowerKindIds => EnteredTraitFollowerKindIdsInternal;

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
            AttackedEnemyLeaderOnPreviousTurn = AttackedEnemyLeaderOnPreviousTurn,
            OwnFollowersEvolvedThisBattle = OwnFollowersEvolvedThisBattle
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
        copy.EnteredTraitFollowerInstanceIdsInternal.UnionWith(EnteredTraitFollowerInstanceIdsInternal);
        copy.EnteredTraitFollowerKindIdsInternal.UnionWith(EnteredTraitFollowerKindIdsInternal);
        copy.LeaderDamageTakenBonusInternal = LeaderDamageTakenBonusInternal;
        return copy;
    }
}
