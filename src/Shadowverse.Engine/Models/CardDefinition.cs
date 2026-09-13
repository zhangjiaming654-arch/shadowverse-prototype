namespace Shadowverse.Engine.Models;

[Flags]
public enum CardKeyword
{
    None = 0,
    Ward = 1,
    Storm = 2,
    Bane = 4,
    Rush = 8,
    IgnoreWard = 16,
    Barrier = 32,
    Intimidate = 64,
    Aura = 128,
    Drain = 256
}

public enum CardType
{
    Follower,
    Spell,
    Amulet
}

/// <summary>Shadowverse card rarity, using the Chinese client terminology.</summary>
public enum CardRarity
{
    Bronze,
    Silver,
    Gold,
    Rainbow
}

/// <summary>The seven Worlds Beyond classes plus cards available to every class.</summary>
public enum CardProfession
{
    Elf,
    Royal,
    Witch,
    Dragon,
    Nightmare,
    Bishop,
    Nemesis,
    Neutral
}

public enum CardEffectKind
{
    DealDamageToEnemyFollowerOrLeader,
    DealDamageToUpToTwoEnemyFollowersAndLeader,
    DrawCards,
    ReturnOwnHandCardToDeckThenDrawCards,
    RestoreOwnLeaderHealth,
    RestoreOwnPlayPoints,
    SuperEvolveAnotherUnevolvedFollower,
    ReplaceOwnDeckWithApocalypseDeck,
    SetEnemyLeaderMaxHealth,
    GainStats,
    SummonFollower,
    GainStatsToOtherAlliedFollowers,
    GainStormIfOwnFollowersAttackedEnemyLeaderPreviousTurn,
    AddCopyToHandWithoutLastWords,
    DealDamageToRandomEnemyFollower,
    GainTemporaryAttackIfOwnFollowersAttackedEnemyLeaderPreviousTurn,
    DiscardOwnHandCards,
    DealDamageToRandomEnemyFollowerAndLeader,
    DealDamageToRandomEnemyFollowerIfOwnFollowersAttackedEnemyLeaderPreviousTurn,
    DistributeDamageAmongEnemyFollowersByEntryOrder,
    DecreaseAllEnemyFollowersDefense,
    IncreaseOwnMaxPlayPointsAndDrawIfAtTen,
    IncreaseOwnMaxPlayPoints,
    DealDamageToAllEnemyFollowersAndLeader,
    DiscardOwnHandCardsUpTo,
    SearchDeckForFollowerWithMinimumCostToHand,
    DestroyRandomEnemyFollowerWithHighestAttack,
    AddCopyToHand,
    DealDamageToEnemyLeader,
    DealDamageToEnemyLeaderIfAwakened,
    DealDamageToAllFollowersWithoutTrait,
    SetOwnLeaderMaxHealth,
    GrantOwnLeaderDamageImmunityUntilEndOfOpponentTurn,
    GiveEnemyCrest,
    DealDamageToOwnLeader,
    GainStorm,
    DealDamageToAllEnemyFollowers,
    DestroyEnemyFollower,
    DealDamageToAllFollowersByFollowerCount,
    DealDamageToUpToTwoRandomEnemyFollowers,
    /// <summary>Damages both leaders, including the controller's own. Appended last so the
    /// existing effect kinds keep their serialized numeric values.</summary>
    DealDamageToAllLeaders,
    /// <summary>Damages the single enemy follower chosen by the action that triggered it.</summary>
    DealDamageToEnemyFollower,
    /// <summary>Strengthens one randomly chosen other allied follower.</summary>
    GainStatsToRandomOtherAlliedFollower,
    /// <summary>Summons a follower copy that has lost its Last Words.</summary>
    SummonFollowerWithoutLastWords,
    /// <summary>Removes the follower from play; it is neither destroyed nor placed in a graveyard.</summary>
    BanishSelf,
    /// <summary>Evolves the follower without spending an evolution point.</summary>
    EvolveSelf,
    /// <summary>Summons different kinds of follower taken randomly from the controller's deck.</summary>
    SummonRandomDistinctDeckFollowers,
    /// <summary>Grants 【突进】 to a follower that enters the board while the source is in play.</summary>
    GrantRushToEnteringAlliedFollowers,
    /// <summary>Destroys one randomly chosen enemy follower.</summary>
    DestroyRandomEnemyFollower,
    /// <summary>Restores evolution points, capped at the usual maximum of two.</summary>
    RestoreOwnEvolutionPoints,
    /// <summary>
    /// While the source is in play, one of the owner's followers with the trait named in
    /// ReferencedCardId that enters during the owner's own turn gets +Amount/+0, 【突进】 and 【守护】,
    /// and the opponent's leader takes 1 damage.
    /// </summary>
    EmpowerEnteringAlliedTraitFollowers,
    /// <summary>Grants 【守护】 to the follower that resolved the effect.</summary>
    GainWard,
    /// <summary>Summons a copy of a random follower in the owner's graveyard; the graveyard is unchanged.</summary>
    RecallFollowerFromGraveyard,
    /// <summary>Grants a named crest to the player that resolved the effect.</summary>
    GiveSelfCrest,
    /// <summary>
    /// Summons Amount copies of the referenced follower. Each copy gets +SecondaryAmount/+0, 【突进】
    /// and has lost its Last Words, so the copy cannot repeat the summon.
    /// </summary>
    SummonFollowerWithBonusWithoutLastWords,
    /// <summary>
    /// Destroys a random one of the owner's own cards that has Last Words and a random enemy follower.
    /// The whole effect does nothing when the owner has no card with Last Words in play.
    /// </summary>
    ShatterRandomLastWordsCardAndEnemyFollower
}

/// <summary>Persistent, named leader-area effects granted by cards.</summary>
public sealed record CrestDefinition(
    string Id,
    string Name,
    string EffectText,
    IReadOnlyList<CardEffect>? StartOfOwnTurnEffects = null,
    IReadOnlyList<CardEffect>? OwnLeaderRestoredEffects = null,
    IReadOnlyList<CardEffect>? EndOfOwnTurnEffects = null)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Id) || string.IsNullOrWhiteSpace(Name))
        {
            throw new ArgumentException("A crest needs an ID and a name.");
        }

        ValidateEffects(StartOfOwnTurnEffects, nameof(StartOfOwnTurnEffects));
        ValidateEffects(OwnLeaderRestoredEffects, nameof(OwnLeaderRestoredEffects));
        ValidateEffects(EndOfOwnTurnEffects, nameof(EndOfOwnTurnEffects));
    }

    private static void ValidateEffects(IReadOnlyList<CardEffect>? effects, string parameterName)
    {
        if (effects is null)
        {
            return;
        }

        foreach (var effect in effects)
        {
            if (effect is null)
            {
                throw new ArgumentException("A crest effect cannot be null.", parameterName);
            }

            effect.Validate();
        }
    }
}

/// <summary>The structured behavior of a card effect that the game engine can resolve.</summary>
/// <param name="ReferencedCardId">
/// For effects that create a particular card, this is the catalog ID of that card. For
/// trait-sensitive effects, this is the trait name to check. For profession-sensitive effects,
/// this is the <see cref="CardProfession"/> name. Keeping this information structured allows the
/// engine to resolve the effect without parsing display text.
/// </param>
/// <param name="MaximumCost">
/// For effects that take cards out of a deck, the highest cost a candidate may have; 0 means no limit.
/// </param>
public sealed record CardEffect(
    CardEffectKind Kind,
    int Amount,
    string? ReferencedCardId = null,
    int BonusAmountIfOwnFollowersAttackedEnemyLeaderPreviousTurn = 0,
    int NecromancyCost = 0,
    int MaximumCost = 0,
    int SecondaryAmount = 0)
{
    public void Validate()
    {
        if (Amount < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(Amount), "An effect amount must be at least 1.");
        }

        if (NecromancyCost < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(NecromancyCost), "A Necromancy cost cannot be negative.");
        }

        if (MaximumCost < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaximumCost), "A maximum cost cannot be negative.");
        }

        if (SecondaryAmount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(SecondaryAmount), "A secondary amount cannot be negative.");
        }

        if ((Kind is CardEffectKind.SummonFollower or
             CardEffectKind.AddCopyToHandWithoutLastWords or
             CardEffectKind.AddCopyToHand or
             CardEffectKind.DealDamageToAllFollowersWithoutTrait or
             CardEffectKind.GiveEnemyCrest) &&
            string.IsNullOrWhiteSpace(ReferencedCardId))
        {
            throw new ArgumentException("A summon effect requires the catalog ID of the follower to summon.", nameof(ReferencedCardId));
        }

        if (BonusAmountIfOwnFollowersAttackedEnemyLeaderPreviousTurn < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(BonusAmountIfOwnFollowersAttackedEnemyLeaderPreviousTurn),
                "A conditional effect bonus cannot be negative.");
        }
    }
}

/// <summary>A selectable mode on a card. One option is chosen every time the mode resolves.</summary>
public sealed record ModeDefinition(string Name, IReadOnlyList<CardEffect> Effects)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            throw new ArgumentException("A mode needs a name.", nameof(Name));
        }

        if (Effects is null || Effects.Count == 0)
        {
            throw new ArgumentException("A mode needs at least one effect.", nameof(Effects));
        }

        foreach (var effect in Effects)
        {
            if (effect is null)
            {
                throw new ArgumentException("A mode cannot contain a null effect.", nameof(Effects));
            }

            effect.Validate();
        }
    }
}

/// <summary>
/// An Enhance threshold and the effects it adds when that threshold is paid.
/// If several thresholds are available, the largest affordable one is paid and
/// every Enhance effect at or below it resolves.
/// </summary>
public sealed record EnhanceDefinition(int Cost, IReadOnlyList<CardEffect> Effects)
{
    public void Validate(int baseCardCost)
    {
        if (Cost <= baseCardCost)
        {
            throw new ArgumentOutOfRangeException(nameof(Cost), "An Enhance cost must be greater than the card's base cost.");
        }

        if (Effects is null || Effects.Count == 0)
        {
            throw new ArgumentException("An Enhance needs at least one effect.", nameof(Effects));
        }

        foreach (var effect in Effects)
        {
            if (effect is null)
            {
                throw new ArgumentException("An Enhance effect cannot be null.", nameof(Effects));
            }

            effect.Validate();
        }
    }
}

/// <summary>
/// An Accelerate ability. When its cost is paid, the source card is resolved as a spell
/// and goes directly to the graveyard instead of being played as its normal card type.
/// </summary>
public sealed record AccelerateDefinition(int Cost, IReadOnlyList<CardEffect> Effects)
{
    public void Validate(int baseCardCost)
    {
        if (Cost < 0 || Cost >= baseCardCost)
        {
            throw new ArgumentOutOfRangeException(nameof(Cost), "An Accelerate cost must be non-negative and lower than the base cost.");
        }

        if (Effects is null || Effects.Count == 0)
        {
            throw new ArgumentException("An Accelerate ability needs at least one effect.", nameof(Effects));
        }

        foreach (var effect in Effects)
        {
            if (effect is null)
            {
                throw new ArgumentException("An Accelerate effect cannot be null.", nameof(Effects));
            }

            effect.Validate();
        }
    }
}

/// <summary>
/// 在手牌中发动：at the end of its owner's turn, while this card sits in hand and the owner's leader
/// is at or below HealthThreshold, the card's cost drops by Reduction. Reductions stack and stay even
/// after the leader heals back up.
/// </summary>
public sealed record HandCostReductionDefinition(int HealthThreshold, int Reduction)
{
    public void Validate()
    {
        if (HealthThreshold < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(HealthThreshold), "A health threshold must be at least 1.");
        }

        if (Reduction < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(Reduction), "A cost reduction must be at least 1.");
        }
    }
}

/// <summary>
/// A 「结晶 N」 ability, the amulet-shaped sibling of Accelerate: while the player cannot pay the
/// card's own cost the card may be played as an amulet costing N with a Countdown and its own Last
/// Words instead of the abilities printed on the follower.
/// </summary>
public sealed record CrystallizeDefinition(
    int Cost,
    int Countdown,
    IReadOnlyList<CardEffect>? LastWordsEffects = null)
{
    public void Validate(int baseCardCost)
    {
        if (Cost < 0 || Cost >= baseCardCost)
        {
            throw new ArgumentOutOfRangeException(
                nameof(Cost),
                "A Crystallize cost must be non-negative and lower than the base cost.");
        }

        if (Countdown < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(Countdown), "A Crystallize amulet needs a Countdown of at least 1.");
        }

        if (LastWordsEffects is null)
        {
            return;
        }

        foreach (var effect in LastWordsEffects)
        {
            if (effect is null)
            {
                throw new ArgumentException("A Crystallize Last Words effect cannot be null.", nameof(LastWordsEffects));
            }

            effect.Validate();
        }
    }
}

/// <summary>
/// Immutable definition shared by all copies of the same card.
/// <para>
/// <c>OnEvolveEffects</c> models 「本随从进化时」, which fires for every evolution including the ones an
/// ability causes. <c>EvolutionEffects</c> models 【进化时】, which only fires for a manual evolution.
/// </para>
/// </summary>
public sealed record CardDefinition(
    string Id,
    string Name,
    int Cost,
    int Attack,
    int Defense,
    CardKeyword Keywords = CardKeyword.None,
    CardType Type = CardType.Follower,
    string EffectText = "",
    CardEffect? Effect = null,
    CardRarity Rarity = CardRarity.Bronze,
    CardProfession Profession = CardProfession.Neutral,
    IReadOnlyList<CardEffect>? FanfareEffects = null,
    IReadOnlyList<CardEffect>? SuperEvolutionEffects = null,
    bool IsCollectible = true,
    IReadOnlyList<EnhanceDefinition>? EnhanceEffects = null,
    IReadOnlyList<CardEffect>? EvolutionEffects = null,
    IReadOnlyList<CardEffect>? LastWordsEffects = null,
    IReadOnlyList<CardEffect>? DiscardedEffects = null,
    IReadOnlyList<CardEffect>? AttackEffects = null,
    IReadOnlyList<string>? Traits = null,
    IReadOnlyList<CardEffect>? SpellEffects = null,
    IReadOnlyList<ModeDefinition>? FanfareModeOptions = null,
    bool EvolutionRepeatsFanfareMode = false,
    AccelerateDefinition? Accelerate = null,
    IReadOnlyList<CardEffect>? SuperEvolutionEvolutionEffects = null,
    int? Countdown = null,
    IReadOnlyList<CardEffect>? EndOfOwnTurnEffects = null,
    IReadOnlyList<CardEffect>? UnevolvedEndOfOwnTurnEffects = null,
    IReadOnlyList<CardEffect>? EvolvedEndOfOwnTurnEffects = null,
    CardKeyword EvolutionGrantedKeywords = CardKeyword.None,
    CardKeyword EvolutionRemovedKeywords = CardKeyword.None,
    bool BanishesWhenLeavingBoard = false,
    IReadOnlyList<CardEffect>? OnEvolveEffects = null,
    /// <summary>
    /// Abilities that apply continuously while this follower is in play, such as granting 【突进】 to
    /// allied followers that enter later.
    /// </summary>
    IReadOnlyList<CardEffect>? PassiveEffects = null,
    /// <summary>
    /// The 「结晶 N」 ability: the card can be played as an amulet with these Last Words instead.
    /// </summary>
    CrystallizeDefinition? Crystallize = null,
    /// <summary>
    /// 【进化时】【模式】: the mode list printed for the evolution. A card either prints its own
    /// evolution modes or repeats its Fanfare modes, never both.
    /// </summary>
    IReadOnlyList<ModeDefinition>? EvolutionModeOptions = null,
    /// <summary>在手牌中发动: an end-of-turn cost reduction while the card stays in hand.</summary>
    HandCostReductionDefinition? HandCostReduction = null)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Id))
        {
            throw new ArgumentException("Card ID is required.", nameof(Id));
        }

        if (string.IsNullOrWhiteSpace(Name))
        {
            throw new ArgumentException("Card name is required.", nameof(Name));
        }

        if (!Enum.IsDefined(Rarity))
        {
            throw new ArgumentOutOfRangeException(nameof(Rarity), "A card rarity must be Bronze, Silver, Gold, or Rainbow.");
        }

        if (!Enum.IsDefined(Profession))
        {
            throw new ArgumentOutOfRangeException(
                nameof(Profession),
                "A card profession must be one of the seven classes or Neutral.");
        }

        if (Cost < 0 || Attack < 0 || Defense < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(Cost),
                "A card needs non-negative cost, attack, and defense.");
        }

        if (Type == CardType.Follower && Defense < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(Defense), "A follower needs at least 1 defense.");
        }

        if (Type != CardType.Follower && (Attack != 0 || Defense != 0))
        {
            throw new ArgumentException("Spells and amulets do not have attack or defense.");
        }

        Effect?.Validate();
        ValidateEffects(FanfareEffects, nameof(FanfareEffects));
        ValidateEffects(EvolutionEffects, nameof(EvolutionEffects));
        ValidateEffects(LastWordsEffects, nameof(LastWordsEffects));
        ValidateEffects(DiscardedEffects, nameof(DiscardedEffects));
        ValidateEffects(AttackEffects, nameof(AttackEffects));
        ValidateEffects(SuperEvolutionEffects, nameof(SuperEvolutionEffects));
        ValidateEffects(SpellEffects, nameof(SpellEffects));
        ValidateEffects(SuperEvolutionEvolutionEffects, nameof(SuperEvolutionEvolutionEffects));
        ValidateEffects(EndOfOwnTurnEffects, nameof(EndOfOwnTurnEffects));
        ValidateEffects(UnevolvedEndOfOwnTurnEffects, nameof(UnevolvedEndOfOwnTurnEffects));
        ValidateEffects(EvolvedEndOfOwnTurnEffects, nameof(EvolvedEndOfOwnTurnEffects));
        ValidateEffects(OnEvolveEffects, nameof(OnEvolveEffects));
        ValidateEffects(PassiveEffects, nameof(PassiveEffects));
        ValidateModeOptions(FanfareModeOptions);
        ValidateModeOptions(EvolutionModeOptions);
        if (EvolutionRepeatsFanfareMode && EvolutionModeOptions is not null)
        {
            throw new ArgumentException(
                "A card cannot both repeat its Fanfare modes and print its own evolution modes.",
                nameof(EvolutionModeOptions));
        }
        ValidateEnhanceEffects(EnhanceEffects);
        Accelerate?.Validate(Cost);
        Crystallize?.Validate(Cost);
        HandCostReduction?.Validate();
        ValidateTraits(Traits);

        if (Countdown is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(Countdown), "A Countdown value must be at least 1.");
        }

        if (Countdown is not null && Type != CardType.Amulet)
        {
            throw new ArgumentException("Only amulets can have Countdown.", nameof(Countdown));
        }

        if (EvolutionRepeatsFanfareMode && FanfareModeOptions is null)
        {
            throw new ArgumentException(
                "A card can only repeat a Fanfare mode on evolution when it has Fanfare mode options.",
                nameof(EvolutionRepeatsFanfareMode));
        }
    }

    private static void ValidateEffects(IReadOnlyList<CardEffect>? effects, string parameterName)
    {
        if (effects is null)
        {
            return;
        }

        foreach (var effect in effects)
        {
            if (effect is null)
            {
                throw new ArgumentException("An effect list cannot contain null.", parameterName);
            }

            effect.Validate();
        }
    }

    private void ValidateEnhanceEffects(IReadOnlyList<EnhanceDefinition>? enhanceEffects)
    {
        if (enhanceEffects is null)
        {
            return;
        }

        foreach (var enhance in enhanceEffects)
        {
            if (enhance is null)
            {
                throw new ArgumentException("An Enhance list cannot contain null.", nameof(EnhanceEffects));
            }

            enhance.Validate(Cost);
        }

        if (enhanceEffects.GroupBy(enhance => enhance.Cost).Any(group => group.Count() > 1))
        {
            throw new ArgumentException("A card cannot have two Enhance entries with the same cost.", nameof(EnhanceEffects));
        }
    }

    private static void ValidateModeOptions(IReadOnlyList<ModeDefinition>? modeOptions)
    {
        if (modeOptions is null)
        {
            return;
        }

        if (modeOptions.Count < 2)
        {
            throw new ArgumentException("A mode card needs at least two options.", nameof(modeOptions));
        }

        if (modeOptions.Any(option => option is null) ||
            modeOptions.Select(option => option.Name).Distinct(StringComparer.Ordinal).Count() != modeOptions.Count)
        {
            throw new ArgumentException("Mode options must be non-null and have distinct names.", nameof(modeOptions));
        }

        foreach (var option in modeOptions)
        {
            option.Validate();
        }
    }

    private static void ValidateTraits(IReadOnlyList<string>? traits)
    {
        if (traits is null)
        {
            return;
        }

        if (traits.Any(string.IsNullOrWhiteSpace) ||
            traits.Distinct(StringComparer.Ordinal).Count() != traits.Count)
        {
            throw new ArgumentException("Traits must be non-empty and cannot repeat.", nameof(Traits));
        }
    }
}

/// <summary>A deck-list entry: a stable catalog ID and the number of copies to use.</summary>
public sealed record DeckCardEntry(string CardId, int Count)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(CardId))
        {
            throw new ArgumentException("A deck entry needs a card ID.", nameof(CardId));
        }

        if (Count < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(Count), "A deck entry needs at least one copy.");
        }
    }
}

public sealed class DeckDefinition
{
    public const int RequiredCardCount = 40;

    public DeckDefinition(string name, IEnumerable<CardDefinition> cards)
    {
        Name = string.IsNullOrWhiteSpace(name)
            ? throw new ArgumentException("Deck name is required.", nameof(name))
            : name;

        Cards = cards?.ToArray() ?? throw new ArgumentNullException(nameof(cards));

        if (Cards.Count != RequiredCardCount)
        {
            throw new ArgumentException(
                $"A deck must contain exactly {RequiredCardCount} cards.",
                nameof(cards));
        }

        foreach (var card in Cards)
        {
            card.Validate();
        }
    }

    public string Name { get; }
    public IReadOnlyList<CardDefinition> Cards { get; }
}
