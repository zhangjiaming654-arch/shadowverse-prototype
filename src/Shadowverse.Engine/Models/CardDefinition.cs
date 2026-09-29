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
    Drain = 256,
    /// <summary>
    /// 【潜伏】: cannot be chosen by the opponent's abilities and cannot be attacked by enemy followers.
    /// Lost when this follower attacks, or when it deals damage through an ability
    /// (official glossary 「潜行」).
    /// </summary>
    Stealth = 512
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
    ShatterRandomLastWordsCardAndEnemyFollower,
    /// <summary>
    /// 「跑酷」式【模式】: normally the player picks one mode and only that one resolves, but when the
    /// player has already brought <c>Threshold</c> or more <b>distinct</b> cards of a trait into play
    /// this battle, every mode resolves instead.
    /// <para>
    /// <see cref="CardEffect.ReferencedCardId"/> carries the whole definition as
    /// <c>"trait|threshold|cardId1|cardId2"</c>: the trait to count across the battle, how many
    /// distinct kinds are required, then one card ID per mode in mode order. Keeping it in a single
    /// string is deliberate — this is one effect whose modes are conditional, not two effects, so it
    /// cannot be modelled as a plain effect list.
    /// </para>
    /// </summary>
    ParkourChoiceOrAllModes,
    /// <summary>
    /// Adds one card to the hand that is a copy of a card sharing the name of a random one of the
    /// owner's own followers of the trait in <see cref="CardEffect.ReferencedCardId"/> that have been
    /// destroyed this battle. The addition is private: the added card's identity is deliberately not
    /// written to the public play record, so the opponent cannot tell which card was taken.
    /// <para>
    /// An empty graveyard for that trait resolves as "nothing happens" rather than an error — the card
    /// is still playable, it simply adds nothing.
    /// </para>
    /// </summary>
    AddRandomDestroyedTraitFollowerCopyToHandPrivately,
    /// <summary>
    /// 【变身】: replaces one card on the board — a follower <b>or</b> an amulet, on either side — with a
    /// freshly created follower named by <see cref="CardEffect.ReferencedCardId"/>, in the same slot
    /// and under the same owner.
    /// <para>
    /// The rules model this as "<b>banish</b> the target, then <b>create</b> a token in that zone"
    /// (Shadowverse EVOLVE Comprehensive Rules §5.16 / §5.6). Two consequences are load-bearing and
    /// deliberately implemented here:
    /// </para>
    /// <list type="bullet">
    /// <item>Banishing is <b>not</b> destruction, so the replaced card never enters a graveyard and its
    /// <b>Last Words must not fire</b>. That is the whole point of the keyword.</item>
    /// <item>The replacement is a brand-new card, so it carries <b>none</b> of the old card's bonuses,
    /// damage, keywords or evolution state.</item>
    /// </list>
    /// <para>
    /// The replacement arrives through the normal summon path, so it does <b>not</b> trigger its own
    /// Fanfare — matching every other token this engine creates (only playing a card from hand does).
    /// A 【突进】 replacement can still attack the same turn, because this engine only restricts
    /// followers that arrived before the current turn.
    /// </para>
    /// </summary>
    TransformInto,
    /// <summary>
    /// 【入场曲】: if the owner already controls a follower whose <b>printed</b> cost is at least
    /// <see cref="CardEffect.Amount"/>, adds <c>Amount</c> copies of the card named by
    /// <see cref="CardEffect.ReferencedCardId"/> to the hand. "Printed cost" is
    /// <see cref="CardDefinition.Cost"/> — cost reduction only ever applies to a card sitting in hand,
    /// never to a follower in play.
    /// </summary>
    AddCardToHandIfOwnFollowerPrintedCostAtLeast,
    /// <summary>
    /// 选择自己战场上的1个原始费用 ≥ <see cref="CardEffect.Amount"/> 的随从，把与它同名的1张卡
    /// **非公开**加入手牌，并使其费用 −<see cref="CardEffect.SecondaryAmount"/>。
    /// The target arrives in <see cref="GameAction"/>'s follower target and is validated during
    /// resolution, so an illegal target fails loudly instead of silently doing nothing.
    /// </summary>
    AddCopyOfTargetFollowerToHandPrivatelyWithCostReduction,
    /// <summary>
    /// 【进化时】: evolves one other unevolved follower the owner controls, without spending an
    /// evolution point. The chosen follower arrives in the action's follower target.
    /// </summary>
    EvolveAnotherOwnUnevolvedFollower,
    /// <summary>
    /// 「转动的《命运之轮》·斯洛士」: when the owner's turn ends while this follower is <b>evolved</b>,
    /// grants the opponent the crest named by <see cref="CardEffect.ReferencedCardId"/> and then makes
    /// this follower <b>vanish</b> (removed from play without a graveyard, so Last Words stay silent).
    /// </summary>
    GrantEnemyCrestAndVanishSelfIfEvolved,
    /// <summary>
    /// Crest ability 「从以下未发动的能力中随机发动1个能力」. <see cref="CardEffect.ReferencedCardId"/>
    /// carries numbered slots as <c>"1=descriptor;2=descriptor;…"</c>; descriptors are decoded by
    /// <c>ParseNumberedAbilities</c> in the engine. A slot already spent by this crest is never picked
    /// again, and when every slot has been spent the ability does nothing.
    /// </summary>
    FireRandomUnusedNumberedAbility,
    /// <summary>
    /// 【奥义】: resolves <see cref="CardEffect.ReferencedCardId"/>'s numbered abilities when the owner's
    /// oath gauge (current turn number + followers evolved this battle) is at least
    /// <see cref="CardEffect.Amount"/>. The gauge's definition comes from the official glossary.
    /// </summary>
    ResolveOathGaugeAbilities,
    /// <summary>
    /// 「自己的创造物·随从进入战场时」: fires whenever a follower carrying the trait
    /// <see cref="CardEffect.ReferencedCardId"/> enters the controller's board (entering play by any
    /// route — summoned, generated, transformed — because this is a passive trigger, not a Fanfare).
    /// </summary>
    OnOwnTraitFollowerEntering,
    /// <summary>
    /// Passive: while this card is in play, each of the owner's 【创造物】 followers that enters play
    /// deals <see cref="CardEffect.Amount"/> damage to a random enemy follower, or restores that much
    /// leader health for the owner. Two separate kinds keep the printed abilities distinguishable.
    /// </summary>
    DealDamageToRandomEnemyFollowerWhenCreationEnters,
    RestoreOwnLeaderWhenCreationEnters,
    /// <summary>
    /// Draws <see cref="CardEffect.Amount"/> cards from the deck, but only cards whose type matches
    /// <see cref="CardEffect.ReferencedCardId"/> (<c>"follower"</c> or <c>"spell"</c>). Non-matching cards
    /// are skipped and stay on the bottom of the deck in their original order, so the draw never
    /// silently becomes "draw any card".
    /// </summary>
    DrawTraitCards,
    /// <summary>
    /// 【超进化时】条件能力: grants 【疾驰】 to this follower when the number of <b>distinct</b> kinds of
    /// followers carrying the trait <see cref="CardEffect.ReferencedCardId"/> that entered play this
    /// battle is at least <see cref="CardEffect.Amount"/>.
    /// </summary>
    GainStormIfOwnTraitFollowerKindsEnteredAtLeast,
    /// <summary>
    /// 【奥义】's 「本随从进化」: evolves the follower this ability was printed on, without spending an
    /// evolution point. Distinct from <see cref="EvolveSelf"/> because the 【奥义】 path resolves through
    /// the evolution dispatcher, which has no follower reference of its own.
    /// </summary>
    EvolveSelfByOath,
    /// <summary>
    /// 从**牌组**里找最多 <see cref="CardEffect.Amount"/> 张符合条件的卡加入手牌（找不到就什么都不做）。
    /// 过滤条件写在 <see cref="CardEffect.ReferencedCardId"/> 里，逗号分隔的键值对：
    /// <c>type=follower|spell|amulet</c>、<c>profession=Witch</c>（<c>Nemesis</c> 等枚举名）、
    /// <c>keyword=Bane|Rush</c>（位标志按 | 组合）、<c>cost=N</c>、<c>costMax=N</c>、<c>distinct=1</c>
    /// （同名只算一张）。放在卡定义里而不是引擎里，是为了让"搜什么"成为卡面的一部分。
    /// </summary>
    SearchDeckToHand,
    /// <summary>
    /// 「若自己的牌组中没有重复卡牌，则…」: 牌组里每个卡号最多出现一次时，结算 <see cref="CardEffectKind.DrawCards"/>
    /// 以外的这份附加效果——用 <see cref="CardEffect.Amount"/> 作为抽牌数，<see cref="CardEffect.ReferencedCardId"/>
    /// 说明类型过滤（同 <see cref="SearchDeckToHand"/> 的键值对）。
    /// </summary>
    DrawCardsIfDeckHasNoDuplicates,
    /// <summary>
    /// 纹章「自己使用随从时，自己的每回合中可触发1次，使其进化」：主战者区域里的纹章在**打出随从**时
    /// 让该随从进化，每回合限一次（记录在 <c>CrestInstance</c> 上，与"主战者回复"那次同一套写法）。
    /// </summary>
    EvolvePlayedFollowerOncePerTurn,
    /// <summary>
    /// 「自己使用法术时，若本随从为进化后，则召唤1个『X』」: a <b>passive</b> watcher. Whenever this
    /// player plays a spell, each of their evolved followers carrying this effect summons
    /// <see cref="CardEffect.ReferencedCardId"/> once. It is not a Fanfare, so it persists across turns.
    /// </summary>
    SummonFollowerWhenSpellPlayed,
    /// <summary>
    /// 「之后，若自己的牌组中没有重复卡牌，则使自己获得『纹章：X』」: grants the crest named by
    /// <see cref="CardEffect.ReferencedCardId"/> to the resolving player, but only while every card id in
    /// their deck appears exactly once.
    /// </summary>
    GiveSelfCrestIfDeckHasNoDuplicates,
    /// <summary>
    /// 「召唤1个『X』，使其获得【毁灭】和【守护】」: summons <see cref="CardEffect.ReferencedCardId"/> and
    /// grants it the keywords in <see cref="CardEffect.Amount"/> (a <see cref="CardKeyword"/> flag value).
    /// A plain <see cref="SummonFollower"/> cannot express this: the summoned token is a shared catalog
    /// definition, so per-summmon keywords must be granted on the instance, not the definition.
    /// </summary>
    SummonFollowerWithKeywords,
    /// <summary>
    /// 被动「自己的原始费用为 <see cref="CardEffect.Amount"/> 或以上的其他随从进入战场时，使其进化」。
    /// Triggers per entering follower instance, on the controller's side only, and never for this
    /// follower itself ("其他").
    /// </summary>
    EvolveOtherFollowerEnteringWithPrintedCostAtLeast,
    /// <summary>
    /// 被动「自己的创造物·随从进入战场时，破坏对手的战场上的随机1个随从」.
    /// </summary>
    DestroyRandomEnemyFollowerWhenCreationEnters,
    /// <summary>
    /// 「选择对手的战场上的N个随从，使其失去所有能力」: clears every keyword on the chosen followers
    /// (including granted ones). Targets arrive through the action's follower target list.
    /// </summary>
    RemoveAbilitiesFromEnemyFollowers,
    /// <summary>
    /// 「使对手的主战者获得『受到的伤害+1』」: a timed leader effect on the opponent that raises every
    /// damage instance dealt to them by <see cref="CardEffect.Amount"/>.
    /// </summary>
    GrantEnemyLeaderDamageTakenBonus,
    /// <summary>
    /// 「回复自己N点超进化点」.
    /// </summary>
    RestoreOwnSuperEvolutionPoints,
    /// <summary>【模式】「使战场上的其他所有随从消失」.</summary>
    VanishAllOtherFollowers,
    /// <summary>【模式】「使战场上的所有护符消失」.</summary>
    VanishAllAmulets,
    /// <summary>【模式】「使所有纹章消失」.</summary>
    VanishAllCrests,
    /// <summary>「召唤1个『X』，该随从和本随从进化」：two followers, both evolved.</summary>
    SummonFollowerAndEvolveBoth,
    /// <summary>「对对手的主战者造成X点伤害。X为自己战场上原始费用≥<see cref="CardEffect.Amount"/>的随从张数」.</summary>
    DealDamageToEnemyLeaderEqualToOwnFollowerCountWithPrintedCostAtLeast,
    /// <summary>「对对手的所有随从造成X点伤害。X为本次对战中进入战场的自己创造物·随从的**种类数**」.</summary>
    DealDamageToAllEnemyFollowersEqualToCreationKindsEntered,
    /// <summary>「对**被选中的**对手随从各造成<see cref="CardEffect.Amount"/>点伤害」：目标由动作里的
    /// EnemyFollowerTargetInstanceIds 传入（前面通常紧跟一条"使其失去所有能力"）。</summary>
    DealDamageToSelectedEnemyFollowers
}

/// <summary>
/// 「1⇒α 2⇒β 3或以上⇒γ」：按融合素材的**费用合计**决定变身对象。
/// </summary>
public sealed record FusionTransform(int MinimumTotalCost, string ResultCardId);

/// <summary>
/// 【融合】（官方术语表）：将手牌中指定的卡牌作为素材进行融合以强化本卡牌。
/// 1回合仅限1次；没有指定数量时可融合任意数量的素材；被融合的卡牌从手牌移除且**墓场数量不增加**。
/// </summary>
public sealed record FusionDefinition(
    /// <summary>只有这些卡号能当素材（「『毁灭创造物β』或『毁灭创造物γ』与本卡牌融合」）。空＝不限。</summary>
    IReadOnlyList<string>? AllowedMaterialCardIds = null,
    /// <summary>素材必须带有的类别（「【融合】创造物·卡牌」）。null＝不限。</summary>
    string? RequiredMaterialTrait = null,
    /// <summary>按素材费用合计变身：取满足 MinimumTotalCost 的**最大值**那条（覆盖"3或以上"）。</summary>
    IReadOnlyList<FusionTransform>? TransformByTotalCost = null,
    /// <summary>「若与本卡牌融合的种类的为2，则变身为X」。</summary>
    int? TransformWhenDistinctMaterialKindsAtLeast = null,
    string? DistinctKindsTransformCardId = null)
{
    /// <summary>这条卡是否根本没有融合能力。</summary>
    public bool IsEmpty =>
        AllowedMaterialCardIds is null &&
        TransformByTotalCost is null &&
        TransformWhenDistinctMaterialKindsAtLeast is null;
}

/// <summary>Persistent, named leader-area effects granted by cards.</summary>
public sealed record CrestDefinition(
    string Id,
    string Name,
    string EffectText,
    IReadOnlyList<CardEffect>? StartOfOwnTurnEffects = null,
    IReadOnlyList<CardEffect>? OwnLeaderRestoredEffects = null,
    IReadOnlyList<CardEffect>? EndOfOwnTurnEffects = null,
    /// <summary>
    /// Effects that fire from an ordinary game action rather than from a turn boundary — currently
    /// 「自己使用随从时，每回合1次使其进化」. Kept in its own slot so it is never handed to the
    /// start/end-of-turn or leader-restored dispatchers, which would reject or mis-fire it.
    /// </summary>
    IReadOnlyList<CardEffect>? PassiveEffects = null)
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
        ValidateEffects(PassiveEffects, nameof(PassiveEffects));
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
    /// <summary>
    /// 「对手的回合结束时，破坏本卡牌」: the follower destroys itself when the <b>opponent's</b> turn ends.
    /// <para>
    /// This is a destruction (it goes to the graveyard and Last Words would fire), unlike
    /// <see cref="BanishesWhenLeavingBoard"/>. Implemented as a printed flag rather than an effect so
    /// the token needs no end-of-turn effect slot of its own.
    /// </para>
    /// </summary>
    bool DestroysAtEndOfOpponentTurn = false,
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
    HandCostReductionDefinition? HandCostReduction = null,
    /// <summary>
    /// 【奥义】: resolved when the card is <b>played</b> and the owner's oath gauge is at least
    /// <see cref="OathGaugeThreshold"/>. Official glossary: gauge = current turn number + the number of
    /// times this player's followers evolved while the card sat in hand; approximated here as that
    /// player's evolutions this battle (the engine does not track per-card hand tenure).
    /// </summary>
    IReadOnlyList<CardEffect>? OathEffects = null,
    /// <summary>The gauge value <see cref="OathEffects"/> needs: 10 for 【奥义】, 15 for 【解放奥义】.</summary>
    int OathGaugeThreshold = 10,
    /// <summary>【解放奥义】: resolved in addition to <see cref="OathEffects"/> at gauge 15.</summary>
    IReadOnlyList<CardEffect>? SuperOathEffects = null,
    /// <summary>
    /// 「使对手获得『纹章：X』」: the crest this card hands to the <b>opponent</b> when its end-of-turn
    /// vanish ability resolves. Stored as an id rather than inline effects so the same crest name can
    /// have distinct positive and negative versions in <see cref="CrestCatalog"/>.
    /// </summary>
    string? GrantedCrestId = null,
    /// <summary>
    /// 【融合】能力。null 表示这张卡不能被融合。
    /// </summary>
    FusionDefinition? Fusion = null)
{
    /// <summary>
    /// 【进化时】真正可以选的模式：卡牌自己印的进化模式，或者它重复的【入场曲】模式（两者不会同时有）。
    /// <para>
    /// 放在 <see cref="CardDefinition"/> 上是为了让引擎和界面用<b>同一份</b>判断 ——
    /// 界面自己抄一遍这段"或者"迟早会和引擎分叉，而分叉的表现是"界面让你选一个引擎不认的模式"。
    /// </para>
    /// </summary>
    public IReadOnlyList<ModeDefinition>? EvolutionModeChoices =>
        EvolutionModeOptions ??
        (EvolutionRepeatsFanfareMode ? FanfareModeOptions : null);

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
