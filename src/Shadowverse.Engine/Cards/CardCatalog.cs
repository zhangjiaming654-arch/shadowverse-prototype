using System.Globalization;
using Shadowverse.Engine.Models;

namespace Shadowverse.Engine.Cards;

public static class CardIds
{
    public const string GreedyArchangelRuby = "BASE-001";
    public const string SeraphsGospel = "BASE-002";
    public const string ValiantFallenAngelOlivia = "BASE-003";
    public const string UltimateSinLordOfAbyss = "BASE-004";
    public const string SilentDemonGeneral = "BASE-005";
    public const string ServantOfTheAbyssLord = "BASE-006";
    public const string PurgatoryEvilWorship = "BASE-007";
    public const string AstarothsVerdict = "BASE-008";
    public const string Goliath = "BASE-009";
    public const string Gladiator = "BASE-010";
    public const string RoyalSeveringHeavenStarchium = "BASE-011";
    public const string Knight = "BASE-012";
    public const string ShatteredBandit = "BASE-013";
    public const string WhelpTemperTantrum = "BASE-014";
    public const string BlazingWhelp = "BASE-015";
    public const string OverwhelmingAssailant = "BASE-016";
    public const string JawsParting = "BASE-017";
    public const string DustLawbreaker = "BASE-018";
    public const string Fang = "BASE-019";
    public const string PiercingSinnerAntimaria = "BASE-020";
    public const string FangedTransfigurationNormagdala = "BASE-021";
    public const string DragonOracle = "BASE-022";
    public const string GoldenSilverThroneRumioreAndAlberte = "BASE-023";
    public const string MasterOfSkyFateLuria = "BASE-024";
    public const string PresentationOfTheWorld = "BASE-025";
    public const string AncientHeavenbladePolalai = "BASE-026";
    public const string HeavenbladeAbyss = "BASE-027";
    public const string HeraldingDragonewt = "BASE-028";
    public const string SmilingChefKimika = "BASE-029";
    public const string LazyWaveflower = "BASE-030";
    public const string DarkDimension = "BASE-031";
    public const string WorldPartnerZoe = "BASE-032";
    public const string AshenAnathemaBanderst = "BASE-033";
    public const string HeadchoppingExecutionerShangfengjin = "BASE-034";
    public const string RedCurrent = "BASE-035";
    public const string SolarFlareRoar = "BASE-036";
    public const string BoundJusticeIlantza = "BASE-037";
    public const string CuteDemonLilim = "BASE-038";
    public const string Bat = "BASE-039";
    public const string DevilDrummerLath = "BASE-040";
    public const string SkeletonSoldier = "BASE-041";
    public const string ElementalResonanceBaru = "BASE-042";
    public const string TroublesomeSummoner = "BASE-043";
    public const string Wraith = "BASE-044";
    public const string RottenZombie = "BASE-045";
    public const string NightSongConcert = "BASE-046";
    public const string AncientHeavenEyeBibati = "BASE-047";
    public const string HeavenEyeAbyss = "BASE-048";
    public const string CatTightropeWalker = "BASE-049";
    public const string DeathbedAnathemaTohime = "BASE-050";
    public const string AbyssalColonel = "BASE-051";
    public const string DepartingAspirationFencingAndMutsuki = "BASE-052";
    public const string GalatadeVersusZet = "BASE-053";
    public const string DeathHostMacmillan = "BASE-054";
    public const string IstanbulDeadVersusMalchiget = "BASE-055";
    public const string NetherLieutenant = "BASE-056";
    public const string Parkour = "BASE-057";

    /// <summary>The two 「创造物」 tokens that <see cref="Parkour"/> adds to the hand.</summary>
    public const string AnalyzedCreation = "BASE-058";
    public const string AncientCreation = "BASE-059";

    /// <summary>The trait that <see cref="Parkour"/> counts across the battle.</summary>
    public const string CreationTrait = "创造物";

    public const string ContraptionOperatorGilque = "BASE-060";

    public const string SincereKotobukiKohana = "BASE-061";

    /// <summary>The token 「诚心的尽小花」 turns a card on the board into.</summary>
    public const string IkuNoKodomo = "BASE-062";

    public const string ForgottenInnocenceAika = "BASE-063";

    public const string AncientAxeYozeta = "BASE-064";

    /// <summary>The spell 「古旧天斧·尤泽塔」 adds to the hand — cost 0, reduces a follower's cost by 3.</summary>
    public const string AncientAxeAbyss = "BASE-065";

    public const string LeisurelySkater = "BASE-066";

    public const string YourSeniorEuphie = "BASE-067";

    /// <summary>【吟唱_2】amulet that hands out 悬丝傀儡 tokens.</summary>
    public const string MarionetteTheater = "BASE-068";

    /// <summary>0-cost 【突进】 token that destroys itself at the end of the opponent's turn.</summary>
    public const string Marionette = "BASE-069";

    public const string SpinningWheelOfFortuneSloth = "BASE-070";

    public const string DoorwaySuccessorLazuli = "BASE-071";

    /// <summary>3-cost 2/2 【疾驰】 token handed out by 门扉接续者·拉姿莉.</summary>
    public const string GorgeousCreation = "BASE-072";

    /// <summary>
    /// Filter token for <see cref="CardEffectKind.DrawTraitCards"/>: 「抽取2张随从」 draws followers only.
    /// Stored in <see cref="CardEffect.ReferencedCardId"/> so the filter is part of the card definition
    /// rather than a hard-coded branch in the engine.
    /// </summary>
    public const string DrawFollowerFilter = "follower";

    public const string SkyConqueringSkytrooperGranAndDjeeta = "BASE-073";

    public const string DiligentPursuitMiu = "BASE-074";

    public const string IndividualShopkeeper = "BASE-075";

    /// <summary>4/5 【守护】 token summoned by 个性店主.</summary>
    public const string MysteriousCreation = "BASE-076";

    public const string KotobukiKohanaIku = "BASE-077";

    public const string MythicalReporter = "BASE-078";

    public const string BladeboundSinnerCatherslott = "BASE-079";

    public const string HumiliatingExile = "BASE-080";

    public const string PuppetLancer = "BASE-081";

    /// <summary>1-cost 3/3 【突进】 token that destroys itself at the end of the opponent's turn.</summary>
    public const string ImprovedMarionette = "BASE-082";

    public const string CleverCreator = "BASE-083";

    public const string MaliciousPureheartKamihira = "BASE-084";

    /// <summary>6-cost 1/3 【守护】 token; its Fanfare draws 3.</summary>
    public const string InferiorToy = "BASE-085";

    /// <summary>5-cost 2/1 token; its Fanfare summons a copy and evolves both.</summary>
    public const string ClumsyDoll = "BASE-086";

    public const string TearfulTransformationAizuIden = "BASE-087";

    /// <summary>3-cost 3/3 【守护】【创造物】 token with a 2-point Last Words heal.</summary>
    public const string FiringPinGuard = "BASE-088";

    public const string FoolishWeapon = "BASE-089";

    public const string VoidCarvedAnathemaScarlet = "BASE-090";

    public const string SoleSovereignBeelzebub = "BASE-091";

    public const string NobleBlackWingOlivie = "BASE-092";

    public const string AlbionBahamut = "BASE-093";

    // 以下 5 张是上一批（创造物系）的卡：本批的「聪明的创造者」会召唤『毁灭创造物α』，
    // 所以必须一起录入，否则引用悬空。
    public const string AttackCreation = "BASE-094";
    public const string DestroyerCreationAlpha = "BASE-095";
    public const string DestroyerCreationBeta = "BASE-096";
    public const string DestroyerCreationGamma = "BASE-097";
    public const string TranscendentCreationOmega = "BASE-098";

    public const string GratitudeArtisanIsaac = "BASE-099";
    public const string WildBroadcaster = "BASE-100";
    public const string WhiteFangPhosphorescence = "BASE-101";
    public const string TemperedBodyguard = "BASE-102";
    public const string SkyRidingGuardianCatalina = "BASE-103";
    public const string DecisiveCrossingAshureAndLitier = "BASE-104";
    public const string SpecialTargetHaremhani = "BASE-105";
    public const string HeirOfTheCelestialDirectorSaintDefen = "BASE-106";

    /// <summary>
    /// Marker used where an effect's card text names no trait at all, so every trait qualifies —
    /// 「遗忘的纯真·爱卡」 says just "your own follower" with no type named.
    /// </summary>
    public const string AnyTraitMarker = "*";
}

public static class CrestIds
{
    public const string AshenAnathemaBanderst = "CREST-001";
    public const string IstanbulDeadVersusMalchiget = "CREST-002";

    /// <summary>「转动的《命运之轮》·斯洛士」hands this crest to the opponent when it vanishes.</summary>
    public const string SpinningWheelOfFortuneSloth = "CREST-003";

    /// <summary>The mirror-image (all-negative) crest the vanishing follower gives its opponent.</summary>
    public const string SpinningWheelOfFortuneSlothCurse = "CREST-004";

    /// <summary>「束刃的罪人·卡特斯拉特」：自己使用随从时，每回合1次使其进化。</summary>
    public const string BladeboundSinnerCatherslott = "CREST-005";

    /// <summary>「特殊目标·海雷姆哈妮」的纹章（【吟唱_2】+【谢幕曲】）。</summary>
    public const string SpecialTargetHaremhani = "CREST-006";
}

/// <summary>Definitions for named effects that persist in a leader's crest area.</summary>
public static class CrestCatalog
{
    private static readonly CrestDefinition[] Definitions =
    [
        new(
            CrestIds.AshenAnathemaBanderst,
            "焦灰的安纳提玛·班德奈特",
            "自己的回合开始时，对自己的主战者造成2点伤害。\n自己的主战者回复时，自己的每回合中可触发1次，对自己的主战者造成1点伤害。",
            StartOfOwnTurnEffects: [new CardEffect(CardEffectKind.DealDamageToOwnLeader, 2)],
            OwnLeaderRestoredEffects: [new CardEffect(CardEffectKind.DealDamageToOwnLeader, 1)]),
        new(
            CrestIds.IstanbulDeadVersusMalchiget,
            "伊斯坦戴德 对 玛尔奇盖特",
            "自己的回合结束时，若自己的战场上有拥有【谢幕曲】的卡牌，则破坏自己的战场上的随机1张拥有【谢幕曲】的卡牌和对手的战场上的随机1个随从。",
            EndOfOwnTurnEffects: [new CardEffect(CardEffectKind.ShatterRandomLastWordsCardAndEnemyFollower, 1)]),
        // 「转动的《命运之轮》·斯洛士」的**正面**纹章：斯洛士的持有者自己在回合开始随机发动1个。
        // 三个能力分别编码为 cost=1（手牌费用-1）/ buff=2（全体+2/+2）/ heal=3（回复3点）。
        new(
            CrestIds.SpinningWheelOfFortuneSloth,
            "转动的《命运之轮》·斯洛士",
            "自己的回合开始时，从以下未发动的能力中随机发动1个能力。\n（1）回合结束前，使自己的所有手牌的费用-1。\n（2）使自己的战场上的所有随从+2/+2。\n（3）回复自己的主战者3点生命值。",
            StartOfOwnTurnEffects:
            [
                new CardEffect(
                    CardEffectKind.FireRandomUnusedNumberedAbility,
                    1,
                    "1=cost:1;2=buff:2;3=heal:3")
            ]),
        // 同一张卡的**反面**纹章（图片 2）：回合开始随机发动1个"负面"能力。
        // 同名但效果相反，所以用不同 ID 分开存放；引擎按 ID 查找，不存在歧义。
        new(
            CrestIds.SpinningWheelOfFortuneSlothCurse,
            "转动的《命运之轮》·斯洛士",
            "自己的回合开始时，从以下未发动的能力中随机发动1个能力。\n（1）回合结束前，使自己的所有手牌的费用+1。\n（2）使自己的战场上的所有随从-2/-2。\n（3）对自己的主战者造成3点伤害。",
            StartOfOwnTurnEffects:
            [
                new CardEffect(
                    CardEffectKind.FireRandomUnusedNumberedAbility,
                    1,
                    "1=cost:-1;2=debuff:2;3=damage:3")
            ]),
        // 「束刃的罪人·卡特斯拉特」的纹章：自己使用随从时，每回合1次使其进化。
        // 触发点在 ApplyPlayFollower 里（纹章挂在主战者区域，不是某个卡上的效果）。
        new(
            CrestIds.BladeboundSinnerCatherslott,
            "束刃的罪人·卡特斯拉特",
            "自己使用随从时，自己的每回合中可触发1次，使其进化。",
            PassiveEffects:
            [
                new CardEffect(CardEffectKind.EvolvePlayedFollowerOncePerTurn, 1)
            ]),
        // 「特殊目标·海雷姆哈妮」的纹章：【吟唱_2】+【谢幕曲】召唤1个自己并使其进化。
        new(
            CrestIds.SpecialTargetHaremhani,
            "特殊目标·海雷姆哈妮",
            "【吟唱_2】\n【谢幕曲】召唤1个『特殊目标·海雷姆哈妮』，使其进化。",
            Countdown: 2,
            LastWordsEffects:
            [
                new CardEffect(
                    CardEffectKind.SummonFollowerAndEvolveIt,
                    1,
                    CardIds.SpecialTargetHaremhani)
            ])
    ];

    private static readonly IReadOnlyDictionary<string, CrestDefinition> DefinitionsById =
        Definitions.ToDictionary(crest => crest.Id, StringComparer.Ordinal);

    static CrestCatalog()
    {
        foreach (var crest in Definitions)
        {
            crest.Validate();
        }
    }

    public static CrestDefinition Get(string crestId) =>
        !string.IsNullOrWhiteSpace(crestId) && DefinitionsById.TryGetValue(crestId, out var crest)
            ? crest
            : throw new ArgumentException($"Unknown crest ID: {crestId}", nameof(crestId));
}

/// <summary>
/// The single source of truth for card definitions used by the prototype.
/// IDs are never reused after a card has been entered.
/// </summary>
public static class CardCatalog
{
    private static readonly CardDefinition[] Definitions =
    [
        new(
            CardIds.GreedyArchangelRuby,
            "贪婪的智天使·露比",
            2,
            2,
            2,
            CardKeyword.None,
            CardType.Follower,
            "【入场曲】选择自己的1张手牌，使其返回牌组。抽取1张卡牌。",
            new CardEffect(CardEffectKind.ReturnOwnHandCardToDeckThenDrawCards, 1),
            CardRarity.Bronze,
            CardProfession.Neutral),
        new(
            CardIds.SeraphsGospel,
            "炽天使的福音",
            3,
            0,
            0,
            CardKeyword.None,
            CardType.Spell,
            "抽取2张卡牌。",
            new CardEffect(CardEffectKind.DrawCards, 2),
            CardRarity.Silver,
            CardProfession.Neutral),
        new(
            CardIds.ValiantFallenAngelOlivia,
            "勇武的堕天使·奥莉薇",
            7,
            4,
            4,
            CardKeyword.None,
            CardType.Follower,
            "【入场曲】抽取2张卡牌。回复自己的主战者2点生命值。回复自己2点能量点。\n【超进化时】选择自己的战场上的1张进化前的其他随从，使其超进化。",
            Effect: null,
            CardRarity.Rainbow,
            CardProfession.Neutral,
            FanfareEffects:
            [
                new CardEffect(CardEffectKind.DrawCards, 2),
                new CardEffect(CardEffectKind.RestoreOwnLeaderHealth, 2),
                new CardEffect(CardEffectKind.RestoreOwnPlayPoints, 2)
            ],
            SuperEvolutionEffects:
            [
                new CardEffect(CardEffectKind.SuperEvolveAnotherUnevolvedFollower, 1)
            ]),
        new(
            CardIds.UltimateSinLordOfAbyss,
            "终极之罪·深渊之主",
            10,
            10,
            10,
            CardKeyword.None,
            CardType.Follower,
            "【入场曲】使自己的牌组变为『启示录牌组』。",
            Effect: null,
            CardRarity.Rainbow,
            CardProfession.Neutral,
            FanfareEffects:
            [
                new CardEffect(CardEffectKind.ReplaceOwnDeckWithApocalypseDeck, 1)
            ]),
        new(
            CardIds.SilentDemonGeneral,
            "沉默的魔将",
            6,
            10,
            10,
            CardKeyword.Storm,
            CardType.Follower,
            "【疾驰】",
            Effect: null,
            CardRarity.Rainbow,
            CardProfession.Neutral,
            IsCollectible: false),
        new(
            CardIds.ServantOfTheAbyssLord,
            "深渊之主的仆从",
            1,
            13,
            13,
            CardKeyword.None,
            CardType.Follower,
            "",
            Effect: null,
            CardRarity.Rainbow,
            CardProfession.Neutral,
            IsCollectible: false),
        new(
            CardIds.PurgatoryEvilWorship,
            "边狱的邪崇",
            5,
            9,
            6,
            CardKeyword.None,
            CardType.Follower,
            "【入场曲】选择对手的战场上至多2个随从，对这些随从和对手的主战者造成6点伤害。",
            Effect: null,
            CardRarity.Rainbow,
            CardProfession.Neutral,
            FanfareEffects:
            [
                new CardEffect(CardEffectKind.DealDamageToUpToTwoEnemyFollowersAndLeader, 6)
            ],
            IsCollectible: false),
        new(
            CardIds.AstarothsVerdict,
            "阿斯塔罗特的宣判",
            10,
            0,
            0,
            CardKeyword.None,
            CardType.Spell,
            "使对手的主战者的生命值的最大值变为1。",
            new CardEffect(CardEffectKind.SetEnemyLeaderMaxHealth, 1),
            CardRarity.Rainbow,
            CardProfession.Neutral,
            IsCollectible: false),
        new(
            CardIds.Goliath,
            "歌莉娅",
            4,
            4,
            5,
            CardKeyword.Ward,
            CardType.Follower,
            "【守护】",
            Effect: null,
            CardRarity.Bronze,
            CardProfession.Neutral),
        new(
            CardIds.Gladiator,
            "剑斗士",
            2,
            2,
            2,
            CardKeyword.None,
            CardType.Follower,
            "【爆能强化 4】这个随从+3/+3。",
            Effect: null,
            CardRarity.Bronze,
            CardProfession.Neutral,
            EnhanceEffects:
            [
                new EnhanceDefinition(4, [new CardEffect(CardEffectKind.GainStats, 3)])
            ]),
        new(
            CardIds.RoyalSeveringHeavenStarchium,
            "王断的天宫·斯塔奇乌姆",
            4,
            4,
            4,
            CardKeyword.None,
            CardType.Follower,
            "【进化时】召唤2个『骑士』。使自己的战场上的其他所有随从+1/+1。",
            Effect: null,
            CardRarity.Gold,
            CardProfession.Royal,
            EvolutionEffects:
            [
                new CardEffect(CardEffectKind.SummonFollower, 2, CardIds.Knight),
                new CardEffect(CardEffectKind.GainStatsToOtherAlliedFollowers, 1)
            ]),
        new(
            CardIds.Knight,
            "骑士",
            0,
            1,
            1,
            CardKeyword.None,
            CardType.Follower,
            "",
            Effect: null,
            CardRarity.Bronze,
            CardProfession.Royal,
            IsCollectible: false,
            Traits: ["士兵"]),
        new(
            CardIds.ShatteredBandit,
            "碎裂的盗匪",
            1,
            1,
            1,
            CardKeyword.None,
            CardType.Follower,
            "【入场曲】若自己的随从在自己的上一回合中攻击过主战者，则本随从获得【疾驰】。\n【谢幕曲】将1张『碎裂的盗匪』加入手牌，使其失去【谢幕曲】。",
            Effect: null,
            CardRarity.Bronze,
            CardProfession.Dragon,
            FanfareEffects:
            [
                new CardEffect(CardEffectKind.GainStormIfOwnFollowersAttackedEnemyLeaderPreviousTurn, 1)
            ],
            LastWordsEffects:
            [
                new CardEffect(CardEffectKind.AddCopyToHandWithoutLastWords, 1, CardIds.ShatteredBandit)
            ]),
        new(
            CardIds.WhelpTemperTantrum,
            "幼龙闹脾气",
            1,
            0,
            0,
            CardKeyword.None,
            CardType.Spell,
            "召唤1个『炽焰幼龙』。\n【爆能强化 3】对对手的战场上的随机1个随从造成3点伤害。",
            new CardEffect(CardEffectKind.SummonFollower, 1, CardIds.BlazingWhelp),
            CardRarity.Gold,
            CardProfession.Dragon,
            EnhanceEffects:
            [
                new EnhanceDefinition(3, [new CardEffect(CardEffectKind.DealDamageToRandomEnemyFollower, 3)])
            ]),
        new(
            CardIds.BlazingWhelp,
            "炽焰幼龙",
            1,
            1,
            1,
            CardKeyword.Intimidate,
            CardType.Follower,
            "【威慑】",
            Effect: null,
            CardRarity.Bronze,
            CardProfession.Dragon,
            IsCollectible: false),
        new(
            CardIds.OverwhelmingAssailant,
            "绝倒的袭击者",
            2,
            1,
            2,
            CardKeyword.Storm,
            CardType.Follower,
            "【疾驰】\n【攻击时】若自己的随从在自己的上一回合中攻击过主战者，则回合结束前，本随从+1/+0。",
            Effect: null,
            CardRarity.Silver,
            CardProfession.Dragon,
            AttackEffects:
            [
                new CardEffect(CardEffectKind.GainTemporaryAttackIfOwnFollowersAttackedEnemyLeaderPreviousTurn, 1)
            ]),
        new(
            CardIds.JawsParting,
            "颚口之别",
            3,
            0,
            0,
            CardKeyword.None,
            CardType.Spell,
            "选择自己的2张手牌，舍弃这些手牌。对对手的战场上的随机1个随从和对手的主战者造成3点伤害。",
            new CardEffect(CardEffectKind.DiscardOwnHandCards, 2),
            CardRarity.Silver,
            CardProfession.Dragon,
            SpellEffects:
            [
                new CardEffect(CardEffectKind.DealDamageToRandomEnemyFollowerAndLeader, 3)
            ]),
        new(
            CardIds.DustLawbreaker,
            "尘土的不法者",
            5,
            4,
            4,
            CardKeyword.None,
            CardType.Follower,
            "【入场曲】发动1次『对对手的战场上的随机1个随从造成4点伤害』。若自己的随从在自己的上一回合中攻击过主战者，则改为发动2次。\n【进化时】召唤1个『绝倒的袭击者』。",
            Effect: null,
            CardRarity.Gold,
            CardProfession.Dragon,
            FanfareEffects:
            [
                new CardEffect(CardEffectKind.DealDamageToRandomEnemyFollower, 4),
                new CardEffect(CardEffectKind.DealDamageToRandomEnemyFollowerIfOwnFollowersAttackedEnemyLeaderPreviousTurn, 4)
            ],
            EvolutionEffects:
            [
                new CardEffect(CardEffectKind.SummonFollower, 1, CardIds.OverwhelmingAssailant)
            ]),
        new(
            CardIds.Fang,
            "利牙",
            2,
            0,
            0,
            CardKeyword.None,
            CardType.Spell,
            "对对手的战场上的所有随从分配3点伤害。若自己的随从在自己的上一回合中攻击过主战者，则改为分配6点伤害。",
            new CardEffect(
                CardEffectKind.DistributeDamageAmongEnemyFollowersByEntryOrder,
                3,
                BonusAmountIfOwnFollowersAttackedEnemyLeaderPreviousTurn: 3),
            CardRarity.Gold,
            CardProfession.Dragon),
        new(
            CardIds.PiercingSinnerAntimaria,
            "穿孔的罪人·安缇马丽亚",
            7,
            6,
            5,
            CardKeyword.Rush | CardKeyword.IgnoreWard,
            CardType.Follower,
            "【入场曲】若自己的随从在自己的上一回合中攻击过主战者，则本随从获得【疾驰】。\n【突进】\n可以无视【守护】进行攻击。",
            Effect: null,
            CardRarity.Rainbow,
            CardProfession.Dragon,
            FanfareEffects:
            [
                new CardEffect(CardEffectKind.GainStormIfOwnFollowersAttackedEnemyLeaderPreviousTurn, 1)
            ]),
        new(
            CardIds.FangedTransfigurationNormagdala,
            "禁牙的变貌·诺玛格达拉",
            7,
            5,
            6,
            CardKeyword.Ward,
            CardType.Follower,
            "【入场曲】【模式】选择1个能力发动。\n（1）抽取1张卡牌。回复自己的主战者3点生命值。\n（2）使对手的战场上的所有随从-0/-4。\n【守护】\n【进化时】发动与【入场曲】相同的能力。",
            Effect: null,
            CardRarity.Rainbow,
            CardProfession.Dragon,
            FanfareModeOptions:
            [
                new ModeDefinition(
                    "抽牌并回复生命",
                    [
                        new CardEffect(CardEffectKind.DrawCards, 1),
                        new CardEffect(CardEffectKind.RestoreOwnLeaderHealth, 3)
                    ]),
                new ModeDefinition(
                    "敌方全体-0/-4",
                    [
                        new CardEffect(CardEffectKind.DecreaseAllEnemyFollowersDefense, 4)
                    ])
            ],
            EvolutionRepeatsFanfareMode: true),
        new(
            CardIds.DragonOracle,
            "龙之启示",
            3,
            0,
            0,
            CardKeyword.None,
            CardType.Spell,
            "使自己的能量点最大值+1，之后，若自己的能量点最大值为10，则抽取1张卡牌。",
            new CardEffect(CardEffectKind.IncreaseOwnMaxPlayPointsAndDrawIfAtTen, 1),
            CardRarity.Rainbow,
            CardProfession.Dragon),
        new(
            CardIds.GoldenSilverThroneRumioreAndAlberte,
            "金银御座·璐米欧儿&雅尔贝特",
            8,
            6,
            6,
            CardKeyword.None,
            CardType.Follower,
            "【入场曲】选择自己的2张手牌，舍弃这些手牌。对对手的战场上的所有随从和对手的主战者造成4点伤害。（手牌不足2张时，舍弃所有剩余手牌，仍结算后续效果。）\n【超进化时】抽取3张卡牌。\n【激奏 3】使自己的能量点最大值+1。",
            Effect: null,
            CardRarity.Rainbow,
            CardProfession.Dragon,
            FanfareEffects:
            [
                new CardEffect(CardEffectKind.DiscardOwnHandCardsUpTo, 2),
                new CardEffect(CardEffectKind.DealDamageToAllEnemyFollowersAndLeader, 4)
            ],
            SuperEvolutionEffects:
            [
                new CardEffect(CardEffectKind.DrawCards, 3)
            ],
            Accelerate: new AccelerateDefinition(
                3,
                [
                    new CardEffect(CardEffectKind.IncreaseOwnMaxPlayPoints, 1)
                ])),
        new(
            CardIds.MasterOfSkyFateLuria,
            "掌握天空命运的少女·露莉亚",
            2,
            1,
            1,
            CardKeyword.Barrier,
            CardType.Follower,
            "【爆能强化 8】抽取1张费用为7或以上的随从。回复自己7点能量点。\n【屏障】",
            Effect: null,
            CardRarity.Gold,
            CardProfession.Neutral,
            EnhanceEffects:
            [
                new EnhanceDefinition(
                    8,
                    [
                        new CardEffect(CardEffectKind.SearchDeckForFollowerWithMinimumCostToHand, 7),
                        new CardEffect(CardEffectKind.RestoreOwnPlayPoints, 7)
                    ])
            ]),
        new(
            CardIds.PresentationOfTheWorld,
            "《世界》的呈现",
            5,
            0,
            0,
            CardKeyword.None,
            CardType.Spell,
            "抽取2张卡牌。破坏对手的战场上的随机1个攻击力最大的随从。\n【爆能强化 10】对对手的战场上的所有随从和对手的主战者造成4点伤害。",
            Effect: null,
            CardRarity.Gold,
            CardProfession.Neutral,
            SpellEffects:
            [
                new CardEffect(CardEffectKind.DrawCards, 2),
                new CardEffect(CardEffectKind.DestroyRandomEnemyFollowerWithHighestAttack, 1)
            ],
            EnhanceEffects:
            [
                new EnhanceDefinition(
                    10,
                    [
                        new CardEffect(CardEffectKind.DealDamageToAllEnemyFollowersAndLeader, 4)
                    ])
            ]),
        new(
            CardIds.AncientHeavenbladePolalai,
            "古旧天刀·波菈莱",
            2,
            0,
            2,
            CardKeyword.Bane,
            CardType.Follower,
            "本卡牌被舍弃时，召唤1个『古旧天刀·波菈莱』。\n【毁灭】\n【进化时】将1张『天刀深渊』加入手牌。\n【超进化时】改为3张。",
            Effect: null,
            CardRarity.Rainbow,
            CardProfession.Dragon,
            EvolutionEffects:
            [
                new CardEffect(CardEffectKind.AddCopyToHand, 1, CardIds.HeavenbladeAbyss)
            ],
            DiscardedEffects:
            [
                new CardEffect(CardEffectKind.SummonFollower, 1, CardIds.AncientHeavenbladePolalai)
            ],
            Traits: ["侵蚀者"],
            SuperEvolutionEvolutionEffects:
            [
                new CardEffect(CardEffectKind.AddCopyToHand, 3, CardIds.HeavenbladeAbyss)
            ]),
        new(
            CardIds.HeavenbladeAbyss,
            "天刀深渊",
            2,
            0,
            0,
            CardKeyword.None,
            CardType.Spell,
            "本卡牌被舍弃时，对对手的主战者造成1点伤害。回复自己的主战者1点生命值。\n对对手的主战者造成1点伤害。回复自己的主战者1点生命值。",
            Effect: null,
            CardRarity.Silver,
            CardProfession.Dragon,
            IsCollectible: false,
            DiscardedEffects:
            [
                new CardEffect(CardEffectKind.DealDamageToEnemyLeader, 1),
                new CardEffect(CardEffectKind.RestoreOwnLeaderHealth, 1)
            ],
            SpellEffects:
            [
                new CardEffect(CardEffectKind.DealDamageToEnemyLeader, 1),
                new CardEffect(CardEffectKind.RestoreOwnLeaderHealth, 1)
            ],
            Traits: ["侵蚀者"]),
        new(
            CardIds.HeraldingDragonewt,
            "宣扬的龙人",
            2,
            2,
            1,
            CardKeyword.Rush,
            CardType.Follower,
            "【爆能强化 4】召唤2个『宣扬的龙人』。\n【突进】",
            Effect: null,
            CardRarity.Bronze,
            CardProfession.Dragon,
            EnhanceEffects:
            [
                new EnhanceDefinition(4, [new CardEffect(CardEffectKind.SummonFollower, 2, CardIds.HeraldingDragonewt)])
            ]),
        new(
            CardIds.SmilingChefKimika,
            "满面笑容的烹饪·琪米卡",
            2,
            2,
            1,
            CardKeyword.None,
            CardType.Follower,
            "【入场曲】选择自己的1张手牌，舍弃该手牌。抽取1张卡牌。回复自己的主战者1点生命值。\n【进化时】发动与【入场曲】相同的能力。",
            Effect: null,
            CardRarity.Silver,
            CardProfession.Dragon,
            FanfareEffects:
            [
                new CardEffect(CardEffectKind.DiscardOwnHandCardsUpTo, 1),
                new CardEffect(CardEffectKind.DrawCards, 1),
                new CardEffect(CardEffectKind.RestoreOwnLeaderHealth, 1)
            ],
            EvolutionEffects:
            [
                new CardEffect(CardEffectKind.DiscardOwnHandCardsUpTo, 1),
                new CardEffect(CardEffectKind.DrawCards, 1),
                new CardEffect(CardEffectKind.RestoreOwnLeaderHealth, 1)
            ]),
        new(
            CardIds.LazyWaveflower,
            "懒惰的波摇花",
            2,
            0,
            0,
            CardKeyword.None,
            CardType.Spell,
            "发动2次『对对手的战场上的随机1个随从造成2点伤害』。若为【觉醒】，则对对手的主战者造成2点伤害。",
            Effect: null,
            CardRarity.Gold,
            CardProfession.Dragon,
            SpellEffects:
            [
                new CardEffect(CardEffectKind.DealDamageToRandomEnemyFollower, 2),
                new CardEffect(CardEffectKind.DealDamageToRandomEnemyFollower, 2),
                new CardEffect(CardEffectKind.DealDamageToEnemyLeaderIfAwakened, 2)
            ]),
        new(
            CardIds.DarkDimension,
            "黑暗次元",
            4,
            0,
            0,
            CardKeyword.None,
            CardType.Amulet,
            "【吟唱 2】\n自己的回合结束时，对战场上的所有非侵蚀者随从造成2点伤害。",
            Effect: null,
            CardRarity.Gold,
            CardProfession.Neutral,
            Countdown: 2,
            EndOfOwnTurnEffects:
            [
                new CardEffect(CardEffectKind.DealDamageToAllFollowersWithoutTrait, 2, "侵蚀者")
            ]),
        new(
            CardIds.WorldPartnerZoe,
            "世界的伙伴·佐伊",
            5,
            5,
            5,
            CardKeyword.None,
            CardType.Follower,
            "【入场曲】使自己的能量点最大值+1。\n【爆能强化 10】本随从获得【疾驰】。使自己的主战者的生命值最大值变为1。对手的回合结束前，使自己的主战者获得『受到的1点或以上的伤害变为0点』。",
            Effect: null,
            CardRarity.Rainbow,
            CardProfession.Dragon,
            FanfareEffects:
            [
                new CardEffect(CardEffectKind.IncreaseOwnMaxPlayPoints, 1)
            ],
            EnhanceEffects:
            [
                new EnhanceDefinition(10,
                [
                    new CardEffect(CardEffectKind.GainStorm, 1),
                    new CardEffect(CardEffectKind.SetOwnLeaderMaxHealth, 1),
                    new CardEffect(CardEffectKind.GrantOwnLeaderDamageImmunityUntilEndOfOpponentTurn, 1)
                ])
            ]),
        new(
            CardIds.AshenAnathemaBanderst,
            "焦灰的安纳提玛·班德奈特",
            9,
            9,
            9,
            CardKeyword.None,
            CardType.Follower,
            "【入场曲】对对手的战场上的所有随从造成9点伤害。\n【超进化时】使对手获得『纹章：焦灰的安纳提玛·班德奈特』。",
            Effect: null,
            CardRarity.Rainbow,
            CardProfession.Dragon,
            FanfareEffects:
            [
                new CardEffect(CardEffectKind.DealDamageToAllEnemyFollowers, 9)
            ],
            SuperEvolutionEffects:
            [
                new CardEffect(CardEffectKind.GiveEnemyCrest, 1, CrestIds.AshenAnathemaBanderst)
            ],
            Traits: ["安纳提玛"]),
        new(
            CardIds.HeadchoppingExecutionerShangfengjin,
            "断头的斩姬·相枫津",
            7,
            5,
            4,
            CardKeyword.Storm | CardKeyword.Bane | CardKeyword.Aura,
            CardType.Follower,
            "【入场曲】选择自己的1张手牌，舍弃该手牌。将2张『赤流』加入手牌。\n【疾驰】【毁灭】【灵气】",
            Effect: null,
            CardRarity.Rainbow,
            CardProfession.Dragon,
            FanfareEffects:
            [
                new CardEffect(CardEffectKind.DiscardOwnHandCardsUpTo, 1),
                new CardEffect(CardEffectKind.AddCopyToHand, 2, CardIds.RedCurrent)
            ]),
        new(
            CardIds.RedCurrent,
            "赤流",
            1,
            0,
            0,
            CardKeyword.None,
            CardType.Spell,
            "选择自己的1张手牌，舍弃该手牌。选择对手的战场上的1个随从，破坏该随从。",
            Effect: null,
            CardRarity.Silver,
            CardProfession.Dragon,
            SpellEffects:
            [
                new CardEffect(CardEffectKind.DiscardOwnHandCards, 1),
                new CardEffect(CardEffectKind.DestroyEnemyFollower, 1)
            ]),
        new(
            CardIds.SolarFlareRoar,
            "日珥咆哮",
            4,
            0,
            0,
            CardKeyword.None,
            CardType.Spell,
            "对战场上的所有随从造成X点伤害。X为战场上的随从的张数。",
            Effect: null,
            CardRarity.Silver,
            CardProfession.Dragon,
            SpellEffects:
            [
                new CardEffect(CardEffectKind.DealDamageToAllFollowersByFollowerCount, 1)
            ]),
        new(
            CardIds.BoundJusticeIlantza,
            "约束的《正义》·伊兰翠",
            10,
            8,
            8,
            CardKeyword.Ward,
            CardType.Follower,
            "【守护】\n自己的回合结束时，若本随从为进化前，则对对手的战场上的随机2个随从造成8点伤害。回复自己的主战者8点生命值。若为进化后，则对对手的主战者造成8点伤害。\n【进化时】本随从失去【守护】。本随从获得【威慑】。",
            Effect: null,
            CardRarity.Rainbow,
            CardProfession.Dragon,
            UnevolvedEndOfOwnTurnEffects:
            [
                new CardEffect(CardEffectKind.DealDamageToUpToTwoRandomEnemyFollowers, 8),
                new CardEffect(CardEffectKind.RestoreOwnLeaderHealth, 8)
            ],
            EvolvedEndOfOwnTurnEffects:
            [
                new CardEffect(CardEffectKind.DealDamageToEnemyLeader, 8)
            ],
            EvolutionGrantedKeywords: CardKeyword.Intimidate,
            EvolutionRemovedKeywords: CardKeyword.Ward),
        new(
            CardIds.CuteDemonLilim,
            "可爱恶魔·莉莉姆",
            1,
            1,
            1,
            CardKeyword.None,
            CardType.Follower,
            "【攻击时】对所有主战者造成1点伤害。\n【谢幕曲】将1张『蝙蝠』加入手牌。",
            Effect: null,
            CardRarity.Bronze,
            CardProfession.Nightmare,
            LastWordsEffects:
            [
                new CardEffect(CardEffectKind.AddCopyToHand, 1, CardIds.Bat)
            ],
            AttackEffects:
            [
                new CardEffect(CardEffectKind.DealDamageToAllLeaders, 1)
            ]),
        new(
            CardIds.Bat,
            "蝙蝠",
            1,
            1,
            1,
            CardKeyword.Drain,
            CardType.Follower,
            "【虹吸】",
            Effect: null,
            CardRarity.Bronze,
            CardProfession.Nightmare,
            IsCollectible: false),
        new(
            CardIds.DevilDrummerLath,
            "恶魔鼓手·拉兹",
            2,
            1,
            2,
            CardKeyword.None,
            CardType.Follower,
            "【谢幕曲】召唤1个『骸骨士兵』。\n【进化时】选择对手的战场上的1个随从，对其造成3点伤害。",
            Effect: null,
            CardRarity.Bronze,
            CardProfession.Nightmare,
            EvolutionEffects:
            [
                new CardEffect(CardEffectKind.DealDamageToEnemyFollower, 3)
            ],
            LastWordsEffects:
            [
                new CardEffect(CardEffectKind.SummonFollower, 1, CardIds.SkeletonSoldier)
            ]),
        new(
            CardIds.SkeletonSoldier,
            "骸骨士兵",
            0,
            1,
            1,
            CardKeyword.None,
            CardType.Follower,
            "",
            Effect: null,
            CardRarity.Bronze,
            CardProfession.Nightmare,
            IsCollectible: false,
            Traits: ["亡者"]),
        new(
            CardIds.ElementalResonanceBaru,
            "元素共鸣·巴尔",
            3,
            2,
            2,
            CardKeyword.None,
            CardType.Follower,
            "【入场曲】【模式】选择1个能力发动。\n（1）使自己的战场上的随机1个其他随从和本随从+1/+1。\n（2）对对手的战场上的随机1个随从造成3点伤害。",
            Effect: null,
            CardRarity.Silver,
            CardProfession.Nightmare,
            FanfareModeOptions:
            [
                new ModeDefinition(
                    "本随从与随机1个其他己方随从+1/+1",
                    [
                        new CardEffect(CardEffectKind.GainStats, 1),
                        new CardEffect(CardEffectKind.GainStatsToRandomOtherAlliedFollower, 1)
                    ]),
                new ModeDefinition(
                    "对随机1个敌方随从造成3点伤害",
                    [
                        new CardEffect(CardEffectKind.DealDamageToRandomEnemyFollower, 3)
                    ])
            ]),
        new(
            CardIds.TroublesomeSummoner,
            "制造麻烦的唤灵师",
            3,
            1,
            1,
            CardKeyword.None,
            CardType.Follower,
            "【入场曲】召唤1个『怨灵』和1个『骸骨士兵』。\n【进化时】召唤1个『腐臭的僵尸』。",
            Effect: null,
            CardRarity.Silver,
            CardProfession.Nightmare,
            FanfareEffects:
            [
                new CardEffect(CardEffectKind.SummonFollower, 1, CardIds.Wraith),
                new CardEffect(CardEffectKind.SummonFollower, 1, CardIds.SkeletonSoldier)
            ],
            EvolutionEffects:
            [
                new CardEffect(CardEffectKind.SummonFollower, 1, CardIds.RottenZombie)
            ]),
        new(
            CardIds.Wraith,
            "怨灵",
            1,
            1,
            1,
            CardKeyword.Storm,
            CardType.Follower,
            "【疾驰】\n离场时，使本随从消失。\n自己的回合结束时，使本随从消失。",
            Effect: null,
            CardRarity.Bronze,
            CardProfession.Nightmare,
            IsCollectible: false,
            Traits: ["亡者"],
            EndOfOwnTurnEffects:
            [
                new CardEffect(CardEffectKind.BanishSelf, 1)
            ],
            BanishesWhenLeavingBoard: true),
        new(
            CardIds.RottenZombie,
            "腐臭的僵尸",
            3,
            2,
            2,
            CardKeyword.None,
            CardType.Follower,
            "【谢幕曲】召唤1个『腐臭的僵尸』，使其失去【谢幕曲】。",
            Effect: null,
            CardRarity.Bronze,
            CardProfession.Nightmare,
            LastWordsEffects:
            [
                new CardEffect(CardEffectKind.SummonFollowerWithoutLastWords, 1, CardIds.RottenZombie)
            ],
            IsCollectible: false,
            Traits: ["亡者"]),
        new(
            CardIds.NightSongConcert,
            "夜之歌的演唱会",
            3,
            0,
            0,
            CardKeyword.None,
            CardType.Spell,
            "对对手的战场上的所有随从分配6点伤害。\n【唤灵_6】对对手的主战者造成2点伤害。",
            Effect: null,
            CardRarity.Gold,
            CardProfession.Nightmare,
            SpellEffects:
            [
                new CardEffect(CardEffectKind.DistributeDamageAmongEnemyFollowersByEntryOrder, 6),
                new CardEffect(CardEffectKind.DealDamageToEnemyLeader, 2, NecromancyCost: 6)
            ],
            Traits: ["安纳提玛"]),
        new(
            CardIds.AncientHeavenEyeBibati,
            "古旧天眼·比芭提",
            4,
            3,
            3,
            CardKeyword.None,
            CardType.Follower,
            "【入场曲】【唤灵_4】本随从进化。\n本随从进化时，将1张『天眼深渊』加入手牌。",
            Effect: null,
            CardRarity.Rainbow,
            CardProfession.Nightmare,
            FanfareEffects:
            [
                new CardEffect(CardEffectKind.EvolveSelf, 1, NecromancyCost: 4)
            ],
            Traits: ["侵蚀者"],
            OnEvolveEffects:
            [
                new CardEffect(CardEffectKind.AddCopyToHand, 1, CardIds.HeavenEyeAbyss)
            ]),
        new(
            CardIds.HeavenEyeAbyss,
            "天眼深渊",
            1,
            0,
            0,
            CardKeyword.None,
            CardType.Spell,
            "抽取2张卡牌。",
            Effect: null,
            CardRarity.Bronze,
            CardProfession.Nightmare,
            SpellEffects:
            [
                new CardEffect(CardEffectKind.DrawCards, 2)
            ],
            IsCollectible: false,
            Traits: ["侵蚀者"]),
        new(
            CardIds.CatTightropeWalker,
            "猫咪走绳师",
            5,
            4,
            5,
            CardKeyword.None,
            CardType.Follower,
            "【入场曲】选择对手的战场上的1个随从，对其造成3点伤害。召唤1个『骸骨士兵』。\n【进化时】发动与【入场曲】相同的能力。",
            Effect: null,
            CardRarity.Silver,
            CardProfession.Nightmare,
            FanfareEffects:
            [
                new CardEffect(CardEffectKind.DealDamageToEnemyFollower, 3),
                new CardEffect(CardEffectKind.SummonFollower, 1, CardIds.SkeletonSoldier)
            ],
            EvolutionEffects:
            [
                new CardEffect(CardEffectKind.DealDamageToEnemyFollower, 3),
                new CardEffect(CardEffectKind.SummonFollower, 1, CardIds.SkeletonSoldier)
            ]),
        new(
            CardIds.DeathbedAnathemaTohime,
            "傍死的安纳提玛·徒姬",
            6,
            5,
            4,
            CardKeyword.None,
            CardType.Follower,
            "【入场曲】召唤随机2种自己的牌组中费用为2或以下的梦魇·随从。\n自己的其他梦魇·随从进入战场时，使其获得【突进】。\n【超进化时】使自己的战场上的其他所有梦魇·随从+2/+2。",
            Effect: null,
            CardRarity.Rainbow,
            CardProfession.Nightmare,
            FanfareEffects:
            [
                new CardEffect(
                    CardEffectKind.SummonRandomDistinctDeckFollowers,
                    2,
                    nameof(CardProfession.Nightmare),
                    MaximumCost: 2)
            ],
            SuperEvolutionEffects:
            [
                new CardEffect(
                    CardEffectKind.GainStatsToOtherAlliedFollowers,
                    2,
                    nameof(CardProfession.Nightmare))
            ],
            Traits: ["安纳提玛"],
            PassiveEffects:
            [
                new CardEffect(
                    CardEffectKind.GrantRushToEnteringAlliedFollowers,
                    1,
                    nameof(CardProfession.Nightmare))
            ]),
        new(
            CardIds.AbyssalColonel,
            "渊底上校",
            6,
            4,
            6,
            CardKeyword.Ward,
            CardType.Follower,
            "【守护】\n【谢幕曲】破坏对手的战场上的随机1个随从。回复自己的主战者2点生命值。\n【结晶 2】【吟唱_4】【谢幕曲】召唤1个『渊底上校』。",
            Effect: null,
            CardRarity.Silver,
            CardProfession.Nightmare,
            LastWordsEffects:
            [
                new CardEffect(CardEffectKind.DestroyRandomEnemyFollower, 1),
                new CardEffect(CardEffectKind.RestoreOwnLeaderHealth, 2)
            ],
            Crystallize: new CrystallizeDefinition(
                Cost: 2,
                Countdown: 4,
                LastWordsEffects:
                [
                    new CardEffect(CardEffectKind.SummonFollower, 1, CardIds.AbyssalColonel)
                ])),
        new(
            CardIds.DepartingAspirationFencingAndMutsuki,
            "出发的憧憬·苇剑&武津御",
            8,
            6,
            5,
            CardKeyword.None,
            CardType.Follower,
            "【入场曲】【模式】选择1个能力发动。\n（1）对对手的主战者造成4点伤害。回复自己的主战者4点生命值。\n（2）对对手的战场上的所有随从造成5点伤害。回复自己1点进化点。\n【进化时】【模式】选择1个能力发动。\n（1）抽取2张卡牌。\n（2）回复自己2点能量点。",
            Effect: null,
            CardRarity.Rainbow,
            CardProfession.Nightmare,
            FanfareModeOptions:
            [
                new ModeDefinition(
                    "对对手主战者造成4点伤害并回复4点生命",
                    [
                        new CardEffect(CardEffectKind.DealDamageToEnemyLeader, 4),
                        new CardEffect(CardEffectKind.RestoreOwnLeaderHealth, 4)
                    ]),
                new ModeDefinition(
                    "对对手所有随从造成5点伤害并回复1点进化点",
                    [
                        new CardEffect(CardEffectKind.DealDamageToAllEnemyFollowers, 5),
                        new CardEffect(CardEffectKind.RestoreOwnEvolutionPoints, 1)
                    ])
            ],
            EvolutionModeOptions:
            [
                new ModeDefinition(
                    "抽取2张卡牌",
                    [
                        new CardEffect(CardEffectKind.DrawCards, 2)
                    ]),
                new ModeDefinition(
                    "回复2点能量点",
                    [
                        new CardEffect(CardEffectKind.RestoreOwnPlayPoints, 2)
                    ])
            ]),
        new(
            CardIds.GalatadeVersusZet,
            "伽罗塔德 对 泽特",
            8,
            8,
            8,
            CardKeyword.None,
            CardType.Follower,
            "在手牌中发动。自己的回合结束时，若自己的主战者的生命值为12或以下，则使本卡牌的费用-1。\n【入场曲】【模式】选择1个能力发动。\n（1）本随从获得【疾驰】。对自己的主战者造成2点伤害。\n（2）本随从获得【守护】。对对手的战场上的所有随从造成8点伤害。",
            Effect: null,
            CardRarity.Rainbow,
            CardProfession.Nightmare,
            FanfareModeOptions:
            [
                new ModeDefinition(
                    "本随从获得【疾驰】并对自己的主战者造成2点伤害",
                    [
                        new CardEffect(CardEffectKind.GainStorm, 1),
                        new CardEffect(CardEffectKind.DealDamageToOwnLeader, 2)
                    ]),
                new ModeDefinition(
                    "本随从获得【守护】并对对手所有随从造成8点伤害",
                    [
                        new CardEffect(CardEffectKind.GainWard, 1),
                        new CardEffect(CardEffectKind.DealDamageToAllEnemyFollowers, 8)
                    ])
            ],
            HandCostReduction: new HandCostReductionDefinition(HealthThreshold: 12, Reduction: 1)),
        new(
            CardIds.DeathHostMacmillan,
            "死亡主持人·马克米朗",
            9,
            4,
            4,
            CardKeyword.None,
            CardType.Follower,
            "【入场曲】【唤灵_10】召唤3个『腐臭的僵尸』。\n自己的亡者·随从进入战场时，若为自己的回合，则使其+1/+0且获得【突进】和【守护】。对对手的主战者造成1点伤害。",
            Effect: null,
            CardRarity.Rainbow,
            CardProfession.Nightmare,
            FanfareEffects:
            [
                new CardEffect(
                    CardEffectKind.SummonFollower,
                    3,
                    CardIds.RottenZombie,
                    NecromancyCost: 10)
            ],
            PassiveEffects:
            [
                new CardEffect(CardEffectKind.EmpowerEnteringAlliedTraitFollowers, 1, "亡者")
            ]),
        new(
            CardIds.IstanbulDeadVersusMalchiget,
            "伊斯坦戴德 对 玛尔奇盖特",
            7,
            3,
            3,
            CardKeyword.None,
            CardType.Follower,
            "【入场曲】发动3次【亡者召回_2】。对对手的战场上的所有随从造成2点伤害。\n【超进化时】使自己获得『纹章：伊斯坦戴德 对 玛尔奇盖特』。",
            Effect: null,
            CardRarity.Rainbow,
            CardProfession.Nightmare,
            FanfareEffects:
            [
                new CardEffect(CardEffectKind.RecallFollowerFromGraveyard, 3, MaximumCost: 2),
                new CardEffect(CardEffectKind.DealDamageToAllEnemyFollowers, 2)
            ],
            SuperEvolutionEffects:
            [
                new CardEffect(CardEffectKind.GiveSelfCrest, 1, CrestIds.IstanbulDeadVersusMalchiget)
            ]),
        new(
            CardIds.NetherLieutenant,
            "幽冥中尉",
            2,
            1,
            1,
            CardKeyword.None,
            CardType.Follower,
            "【谢幕曲】召唤1个『幽冥中尉』，使其+1/+0且获得【突进】，使其失去【谢幕曲】。",
            Effect: null,
            CardRarity.Bronze,
            CardProfession.Nightmare,
            LastWordsEffects:
            [
                new CardEffect(
                    CardEffectKind.SummonFollowerWithBonusWithoutLastWords,
                    1,
                    CardIds.NetherLieutenant,
                    SecondaryAmount: 1)
            ]),
        new(
            CardIds.Parkour,
            "跑酷",
            1,
            0,
            0,
            CardKeyword.None,
            CardType.Spell,
            "【模式】选择1个能力发动。若本次对战中进入战场的自己的创造物·随从的种类为3种或以上，则改为发动所有能力。\n（1）将1张『解析的创造物』加入手牌。\n（2）将1张『古老的创造物』加入手牌。",
            new CardEffect(
                CardEffectKind.ParkourChoiceOrAllModes,
                3,
                $"{CardIds.CreationTrait}|3|{CardIds.AnalyzedCreation}|{CardIds.AncientCreation}"),
            CardRarity.Bronze,
            CardProfession.Nemesis),
        new(
            CardIds.AnalyzedCreation,
            "解析的创造物",
            1,
            1,
            1,
            CardKeyword.None,
            CardType.Follower,
            "本随从进入战场时，抽取1张卡牌。",
            Effect: null,
            CardRarity.Bronze,
            CardProfession.Nemesis,
            Traits: [CardIds.CreationTrait],
            FanfareEffects:
            [
                new CardEffect(CardEffectKind.DrawCards, 1)
            ],
            IsCollectible: false),
        new(
            CardIds.AncientCreation,
            "古老的创造物",
            1,
            3,
            1,
            CardKeyword.Rush,
            CardType.Follower,
            "【突进】",
            Effect: null,
            CardRarity.Bronze,
            CardProfession.Nemesis,
            Traits: [CardIds.CreationTrait],
            IsCollectible: false),
        new(
            CardIds.ContraptionOperatorGilque,
            "机械操纵者·吉尔克",
            1,
            1,
            1,
            CardKeyword.None,
            CardType.Follower,
            "【入场曲】将随机1张与本次对战中破坏的自己的创造物·随从同名的卡牌，以非公开形式加入手牌。",
            Effect: null,
            CardRarity.Bronze,
            CardProfession.Nemesis,
            FanfareEffects:
            [
                new CardEffect(
                    CardEffectKind.AddRandomDestroyedTraitFollowerCopyToHandPrivately,
                    1,
                    CardIds.CreationTrait)
            ]),
        new(
            CardIds.SincereKotobukiKohana,
            "诚心的尽小花",
            1,
            0,
            0,
            CardKeyword.None,
            CardType.Spell,
            "选择战场上的1张卡牌，使其变身为『伊鞠的小鬼』。",
            new CardEffect(CardEffectKind.TransformInto, 1, CardIds.IkuNoKodomo),
            CardRarity.Gold,
            CardProfession.Nemesis),
        new(
            CardIds.IkuNoKodomo,
            "伊鞠的小鬼",
            2,
            3,
            3,
            CardKeyword.Rush,
            CardType.Follower,
            "【突进】",
            Effect: null,
            CardRarity.Bronze,
            CardProfession.Nemesis,
            IsCollectible: false),
        new(
            CardIds.ForgottenInnocenceAika,
            "遗忘的纯真·爱卡",
            2,
            2,
            1,
            CardKeyword.None,
            CardType.Follower,
            "【入场曲】将随机1张与本次对战中被破坏的自己的随从同名的卡牌，以非公开形式加入手牌。\n【进化时】发动与【入场曲】相同的能力。",
            Effect: null,
            CardRarity.Gold,
            CardProfession.Neutral,
            FanfareEffects:
            [
                new CardEffect(
                    CardEffectKind.AddRandomDestroyedTraitFollowerCopyToHandPrivately,
                    1,
                    CardIds.AnyTraitMarker)
            ],
            // 【进化时】发动与【入场曲】相同的能力：本引擎的 EvolutionRepeatsFanfareMode 只用于
            // 【模式】卡（无【模式】时会直接拒绝），所以不带【模式】的普通入场曲要用同一份效果列表
            // 明确重复一次 —— 语义上就是"同一个能力再发动一次"，而不是两张不同的效果。
            EvolutionEffects:
            [
                new CardEffect(
                    CardEffectKind.AddRandomDestroyedTraitFollowerCopyToHandPrivately,
                    1,
                    CardIds.AnyTraitMarker)
            ]),
        new(
            CardIds.AncientAxeYozeta,
            "古旧天斧·尤泽塔",
            2,
            2,
            1,
            CardKeyword.Rush,
            CardType.Follower,
            "【入场曲】若自己的战场上有原始费用为5或以上的随从，则将1张『天斧深渊』加入手牌。\n【突进】",
            Effect: null,
            CardRarity.Rainbow,
            CardProfession.Nemesis,
            Traits: ["侵蚀者"],
            FanfareEffects:
            [
                new CardEffect(
                    CardEffectKind.AddCardToHandIfOwnFollowerPrintedCostAtLeast,
                    5,
                    CardIds.AncientAxeAbyss)
            ]),
        new(
            CardIds.AncientAxeAbyss,
            "天斧深渊",
            0,
            0,
            0,
            CardKeyword.None,
            CardType.Spell,
            "选择自己的战场上的1个原始费用为5或以上的随从，将1张与其同名的卡牌以非公开形式加入手牌，使其费用-3。",
            new CardEffect(
                CardEffectKind.AddCopyOfTargetFollowerToHandPrivatelyWithCostReduction,
                5,
                SecondaryAmount: 3),
            CardRarity.Rainbow,
            CardProfession.Nemesis,
            Traits: ["侵蚀者"],
            IsCollectible: false),
        new(
            CardIds.LeisurelySkater,
            "悠然的滑手",
            2,
            2,
            1,
            CardKeyword.None,
            CardType.Follower,
            "【入场曲】将1张『古老的创造物』加入手牌。\n【进化时】发动与【入场曲】相同的能力。",
            Effect: null,
            CardRarity.Silver,
            CardProfession.Nemesis,
            FanfareEffects:
            [
                new CardEffect(CardEffectKind.AddCopyToHand, 1, CardIds.AncientCreation)
            ],
            EvolutionEffects:
            [
                new CardEffect(CardEffectKind.AddCopyToHand, 1, CardIds.AncientCreation)
            ]),
        new(
            CardIds.YourSeniorEuphie,
            "你的前辈·欧丝",
            2,
            2,
            2,
            CardKeyword.None,
            CardType.Follower,
            "【入场曲】将1张『解析的创造物』加入手牌。\n【进化时】选择自己的战场上的1个进化前的其他随从，使其进化。",
            Effect: null,
            CardRarity.Rainbow,
            CardProfession.Nemesis,
            FanfareEffects:
            [
                new CardEffect(CardEffectKind.AddCopyToHand, 1, CardIds.AnalyzedCreation)
            ],
            EvolutionEffects:
            [
                new CardEffect(CardEffectKind.EvolveAnotherOwnUnevolvedFollower, 1)
            ]),
        new(
            CardIds.MarionetteTheater,
            "人偶剧场",
            2,
            0,
            0,
            CardKeyword.None,
            CardType.Amulet,
            "【入场曲】将1张『悬丝傀儡』加入手牌。\n【吟唱_2】自己的回合结束时，将1张『悬丝傀儡』加入手牌。",
            Effect: null,
            CardRarity.Silver,
            CardProfession.Nemesis,
            Countdown: 2,
            FanfareEffects:
            [
                new CardEffect(CardEffectKind.AddCopyToHand, 1, CardIds.Marionette)
            ],
            EndOfOwnTurnEffects:
            [
                new CardEffect(CardEffectKind.AddCopyToHand, 1, CardIds.Marionette)
            ]),
        new(
            CardIds.Marionette,
            "悬丝傀儡",
            0,
            1,
            1,
            CardKeyword.Rush,
            CardType.Follower,
            "【突进】\n对手的回合结束时，破坏本卡牌。",
            Effect: null,
            CardRarity.Bronze,
            CardProfession.Nemesis,
            Traits: ["人偶"],
            DestroysAtEndOfOpponentTurn: true,
            IsCollectible: false),
        new(
            CardIds.SpinningWheelOfFortuneSloth,
            "转动的《命运之轮》·斯洛士",
            3,
            0,
            2,
            CardKeyword.Stealth,
            CardType.Follower,
            "【潜伏】\n自己的回合结束时，若本随从为进化后，则使对手获得『纹章：转动的《命运之轮》·斯洛士』。使本随从消失。",
            Effect: null,
            CardRarity.Rainbow,
            CardProfession.Nemesis,
            GrantedCrestId: CrestIds.SpinningWheelOfFortuneSlothCurse,
            EndOfOwnTurnEffects:
            [
                // 把反面纹章交给**对手**，然后本随从消失（消滅，不是破坏；对手获得的是负面纹章）。
                new CardEffect(
                    CardEffectKind.GrantEnemyCrestAndVanishSelfIfEvolved,
                    1,
                    CrestIds.SpinningWheelOfFortuneSlothCurse)
            ]),
        new(
            CardIds.DoorwaySuccessorLazuli,
            "门扉接续者·拉姿莉",
            3,
            3,
            3,
            CardKeyword.None,
            CardType.Follower,
            "【入场曲】将1张『绚烂的创造物』加入手牌。",
            Effect: null,
            CardRarity.Silver,
            CardProfession.Nemesis,
            FanfareEffects:
            [
                new CardEffect(CardEffectKind.AddCopyToHand, 1, CardIds.GorgeousCreation)
            ]),
        new(
            CardIds.GorgeousCreation,
            "绚烂的创造物",
            3,
            2,
            2,
            CardKeyword.Storm,
            CardType.Follower,
            "【疾驰】",
            Effect: null,
            CardRarity.Bronze,
            CardProfession.Nemesis,
            Traits: [CardIds.CreationTrait],
            IsCollectible: false),
        new(
            CardIds.SkyConqueringSkytrooperGranAndDjeeta,
            "征服苍空的骑空士·古兰&姬塔",
            4,
            3,
            2,
            CardKeyword.None,
            CardType.Follower,
            "【入场曲】【模式】选择1个能力发动。【奥义】本随从进化。\n（1）对对手的战场上的随机1个随从造成5点伤害。\n（2）抽取2张随从。",
            Effect: null,
            CardRarity.Rainbow,
            CardProfession.Neutral,
            FanfareModeOptions:
            [
                new ModeDefinition(
                    "对对手战场上的随机1个随从造成5点伤害",
                    [
                        new CardEffect(CardEffectKind.DealDamageToRandomEnemyFollower, 5)
                    ]),
                new ModeDefinition(
                    "抽取2张随从",
                    [
                        // "抽取2张随从"：只抽随从，不是任意卡。
                        new CardEffect(CardEffectKind.DrawTraitCards, 2, CardIds.DrawFollowerFilter)
                    ])
            ],
            OathEffects:
            [
                new CardEffect(CardEffectKind.EvolveSelfByOath, 1)
            ]),
        new(
            CardIds.DiligentPursuitMiu,
            "奋厉追赶·米乌",
            4,
            3,
            5,
            CardKeyword.None,
            CardType.Follower,
            "自己的创造物·随从进入战场时，对对手的战场上的随机1个随从造成3点伤害。\n【进化时】召唤1个『古老的创造物』。\n【超进化时】之后，若本次对战中进入战场的自己的创造物·随从的种类为3种或以上，则本随从获得【疾驰】。",
            Effect: null,
            CardRarity.Rainbow,
            CardProfession.Nemesis,
            EvolutionEffects:
            [
                new CardEffect(CardEffectKind.SummonFollower, 1, CardIds.AncientCreation)
            ],
            PassiveEffects:
            [
                new CardEffect(CardEffectKind.DealDamageToRandomEnemyFollowerWhenCreationEnters, 3)
            ],
            SuperEvolutionEffects:
            [
                new CardEffect(CardEffectKind.GainStormIfOwnTraitFollowerKindsEnteredAtLeast, 3, CardIds.CreationTrait)
            ]),
        new(
            CardIds.IndividualShopkeeper,
            "个性店主",
            4,
            3,
            3,
            CardKeyword.None,
            CardType.Follower,
            "自己的创造物·随从进入战场时，回复自己的主战者1点生命值。\n【进化时】召唤1个『神秘的创造物』。",
            Effect: null,
            CardRarity.Bronze,
            CardProfession.Nemesis,
            EvolutionEffects:
            [
                new CardEffect(CardEffectKind.SummonFollower, 1, CardIds.MysteriousCreation)
            ],
            PassiveEffects:
            [
                new CardEffect(CardEffectKind.RestoreOwnLeaderWhenCreationEnters, 1)
            ]),
        new(
            CardIds.MysteriousCreation,
            "神秘的创造物",
            3,
            4,
            5,
            CardKeyword.Ward,
            CardType.Follower,
            "【守护】",
            Effect: null,
            CardRarity.Bronze,
            CardProfession.Nemesis,
            Traits: [CardIds.CreationTrait],
            IsCollectible: false),
        new(
            CardIds.KotobukiKohanaIku,
            "尽小花·伊鞠",
            2,
            2,
            2,
            CardKeyword.None,
            CardType.Follower,
            "【入场曲】选择自己的1张手牌，舍弃该手牌。抽取1张法术。\n自己使用法术时，若本随从为进化后，则召唤1个『伊鞠的小鬼』。\n【超进化时】抽取2种费用为1的法术。",
            Effect: null,
            CardRarity.Rainbow,
            CardProfession.Nemesis,
            FanfareEffects:
            [
                new CardEffect(CardEffectKind.DiscardOwnHandCards, 1),
                // "抽取1张法术"：只抽法术类型，不是任意卡。
                new CardEffect(CardEffectKind.DrawTraitCards, 1, "spell")
            ],
            SuperEvolutionEffects:
            [
                // "抽取2种费用为1的法术"：distinct=1 ⇒ 同名只算一种。
                new CardEffect(
                    CardEffectKind.SearchDeckToHand,
                    2,
                    "type=spell,cost=1,distinct=1")
            ],
            PassiveEffects:
            [
                new CardEffect(CardEffectKind.SummonFollowerWhenSpellPlayed, 1, CardIds.IkuNoKodomo)
            ]),
        new(
            CardIds.MythicalReporter,
            "神话记者",
            3,
            3,
            2,
            CardKeyword.None,
            CardType.Follower,
            "【谢幕曲】抽取1张卡牌。\n【超进化时】召唤2个『神话记者』。",
            Effect: null,
            CardRarity.Silver,
            CardProfession.Neutral,
            LastWordsEffects:
            [
                new CardEffect(CardEffectKind.DrawCards, 1)
            ],
            SuperEvolutionEffects:
            [
                new CardEffect(CardEffectKind.SummonFollower, 2, CardIds.MythicalReporter)
            ]),
        new(
            CardIds.BladeboundSinnerCatherslott,
            "束刃的罪人·卡特斯拉特",
            1,
            1,
            1,
            CardKeyword.Bane,
            CardType.Follower,
            "【毁灭】\n【进化时】抽取1张拥有【毁灭】的超越者·随从。之后，若自己的牌组中没有重复卡牌，则使自己获得『纹章：束刃的罪人·卡特斯拉特』。",
            Effect: null,
            CardRarity.Rainbow,
            CardProfession.Nemesis,
            EvolutionEffects:
            [
                new CardEffect(
                    CardEffectKind.SearchDeckToHand,
                    1,
                    "type=follower,profession=Nemesis,keyword=Bane"),
                new CardEffect(
                    CardEffectKind.GiveSelfCrestIfDeckHasNoDuplicates,
                    1,
                    CrestIds.BladeboundSinnerCatherslott)
            ]),
        new(
            CardIds.HumiliatingExile,
            "屈辱流放",
            1,
            0,
            0,
            CardKeyword.None,
            CardType.Spell,
            "选择自己的1张手牌，舍弃该手牌。抽取1张拥有【毁灭】的超越者·随从。之后，若自己的牌组中没有重复卡牌，则抽取2张卡牌。",
            new CardEffect(CardEffectKind.DiscardOwnHandCards, 1),
            CardRarity.Bronze,
            CardProfession.Nemesis,
            SpellEffects:
            [
                new CardEffect(
                    CardEffectKind.SearchDeckToHand,
                    1,
                    "type=follower,profession=Nemesis,keyword=Bane"),
                new CardEffect(
                    CardEffectKind.DrawCardsIfDeckHasNoDuplicates,
                    2)
            ]),
        new(
            CardIds.PuppetLancer,
            "人偶长矛手",
            2,
            2,
            1,
            CardKeyword.None,
            CardType.Follower,
            "【入场曲】将1张『改良型·悬丝傀儡』加入手牌。",
            Effect: null,
            CardRarity.Bronze,
            CardProfession.Nemesis,
            FanfareEffects:
            [
                new CardEffect(CardEffectKind.AddCopyToHand, 1, CardIds.ImprovedMarionette)
            ]),
        new(
            CardIds.ImprovedMarionette,
            "改良型·悬丝傀儡",
            1,
            3,
            3,
            CardKeyword.Rush,
            CardType.Follower,
            "【突进】\n对手的回合结束时，破坏本卡牌。",
            Effect: null,
            CardRarity.Bronze,
            CardProfession.Nemesis,
            Traits: ["人偶"],
            DestroysAtEndOfOpponentTurn: true,
            IsCollectible: false),
        new(
            CardIds.CleverCreator,
            "聪明的创造者",
            6,
            1,
            1,
            CardKeyword.None,
            CardType.Follower,
            "【入场曲】召唤1个『毁灭创造物α』，使其获得【毁灭】和【守护】。",
            Effect: null,
            CardRarity.Bronze,
            CardProfession.Nemesis,
            FanfareEffects:
            [
                new CardEffect(
                    CardEffectKind.SummonFollowerWithKeywords,
                    1,
                    CardIds.DestroyerCreationAlpha,
                    SecondaryAmount: (int)(CardKeyword.Bane | CardKeyword.Ward))
            ]),
        new(
            CardIds.MaliciousPureheartKamihira,
            "恶劣的纯心·卡密希拉",
            7,
            6,
            6,
            CardKeyword.None,
            CardType.Follower,
            "【入场曲】召唤1个『低劣的玩具』和1个『拙劣的人偶』。\n自己的原始费用为5或以上的其他随从进入战场时，使其进化。\n【超进化时】对对手的主战者造成X点伤害。X为自己的战场上的原始费用为5或以上的随从的张数。",
            Effect: null,
            CardRarity.Rainbow,
            CardProfession.Nemesis,
            FanfareEffects:
            [
                new CardEffect(CardEffectKind.SummonFollower, 1, CardIds.InferiorToy),
                new CardEffect(CardEffectKind.SummonFollower, 1, CardIds.ClumsyDoll)
            ],
            PassiveEffects:
            [
                new CardEffect(CardEffectKind.EvolveOtherFollowerEnteringWithPrintedCostAtLeast, 5)
            ],
            SuperEvolutionEffects:
            [
                new CardEffect(
                    CardEffectKind.DealDamageToEnemyLeaderEqualToOwnFollowerCountWithPrintedCostAtLeast,
                    5)
            ]),
        new(
            CardIds.InferiorToy,
            "低劣的玩具",
            6,
            1,
            3,
            CardKeyword.Ward,
            CardType.Follower,
            "【入场曲】抽取3张卡牌。\n【守护】\n激奏：召唤1个『低劣的玩具』。",
            Effect: null,
            CardRarity.Bronze,
            CardProfession.Nemesis,
            FanfareEffects:
            [
                new CardEffect(CardEffectKind.DrawCards, 3)
            ],
            Accelerate: new AccelerateDefinition(
                2,
                [new CardEffect(CardEffectKind.SummonFollower, 1, CardIds.InferiorToy)])),
        new(
            CardIds.ClumsyDoll,
            "拙劣的人偶",
            5,
            2,
            1,
            CardKeyword.None,
            CardType.Follower,
            "【入场曲】召唤1个『拙劣的人偶』，该随从和本随从进化。\n激奏：召唤2个『拙劣的人偶』。",
            Effect: null,
            CardRarity.Silver,
            CardProfession.Nemesis,
            FanfareEffects:
            [
                new CardEffect(CardEffectKind.SummonFollowerAndEvolveBoth, 1, CardIds.ClumsyDoll)
            ],
            Accelerate: new AccelerateDefinition(
                2,
                [new CardEffect(CardEffectKind.SummonFollower, 2, CardIds.ClumsyDoll)])),
        new(
            CardIds.TearfulTransformationAizuIden,
            "弹哭的变貌·艾兹伊甸",
            7,
            5,
            5,
            CardKeyword.None,
            CardType.Follower,
            "【入场曲】召唤1个『击针看守』。\n自己的创造物·随从进入战场时，破坏对手的战场上的随机1个随从。\n【超进化时】发动与【入场曲】相同的能力。",
            Effect: null,
            CardRarity.Rainbow,
            CardProfession.Nemesis,
            FanfareEffects:
            [
                new CardEffect(CardEffectKind.SummonFollower, 1, CardIds.FiringPinGuard)
            ],
            PassiveEffects:
            [
                new CardEffect(CardEffectKind.DestroyRandomEnemyFollowerWhenCreationEnters, 1)
            ],
            SuperEvolutionEffects:
            [
                new CardEffect(CardEffectKind.SummonFollower, 1, CardIds.FiringPinGuard)
            ]),
        new(
            CardIds.FiringPinGuard,
            "击针看守",
            3,
            3,
            3,
            CardKeyword.Ward,
            CardType.Follower,
            "【守护】\n【谢幕曲】回复自己的主战者2点生命值。",
            Effect: null,
            CardRarity.Rainbow,
            CardProfession.Nemesis,
            Traits: [CardIds.CreationTrait],
            LastWordsEffects:
            [
                new CardEffect(CardEffectKind.RestoreOwnLeaderHealth, 2)
            ],
            IsCollectible: false),
        new(
            CardIds.FoolishWeapon,
            "愚劣的兵器",
            8,
            3,
            4,
            CardKeyword.None,
            CardType.Follower,
            "【入场曲】召唤2个『愚劣的兵器』。\n自己的回合结束时，对对手的战场上的所有随从分配3点伤害。\n【进化时】对对手的战场上的所有随从分配3点伤害。\n激奏4：召唤1个『愚劣的兵器』。",
            Effect: null,
            CardRarity.Gold,
            CardProfession.Nemesis,
            FanfareEffects:
            [
                new CardEffect(CardEffectKind.SummonFollower, 2, CardIds.FoolishWeapon)
            ],
            EndOfOwnTurnEffects:
            [
                new CardEffect(CardEffectKind.DistributeDamageAmongEnemyFollowersByEntryOrder, 3)
            ],
            EvolutionEffects:
            [
                new CardEffect(CardEffectKind.DistributeDamageAmongEnemyFollowersByEntryOrder, 3)
            ],
            Accelerate: new AccelerateDefinition(
                4,
                [new CardEffect(CardEffectKind.SummonFollower, 1, CardIds.FoolishWeapon)])),
        new(
            CardIds.VoidCarvedAnathemaScarlet,
            "虚刻的安纳提玛·斯卡雷特",
            8,
            6,
            6,
            CardKeyword.Storm | CardKeyword.Ward,
            CardType.Follower,
            "【入场曲】对对手的战场上的所有随从造成X点伤害。X为本次对战中进入战场的自己的创造物·随从的种类。\n【疾驰】【守护】",
            Effect: null,
            CardRarity.Rainbow,
            CardProfession.Nemesis,
            Traits: ["安纳提玛"],
            FanfareEffects:
            [
                new CardEffect(
                    CardEffectKind.DealDamageToAllEnemyFollowersEqualToCreationKindsEntered,
                    1)
            ]),
        new(
            CardIds.SoleSovereignBeelzebub,
            "唯一王者·别西卜",
            9,
            9,
            9,
            CardKeyword.None,
            CardType.Follower,
            "【入场曲】选择对手的战场上的2个随从，使其失去所有能力，对其造成9点伤害。使对手的主战者获得「受到的伤害+1」。",
            Effect: null,
            CardRarity.Rainbow,
            CardProfession.Nemesis,
            FanfareEffects:
            [
                new CardEffect(CardEffectKind.RemoveAbilitiesFromEnemyFollowers, 2),
                new CardEffect(CardEffectKind.DealDamageToSelectedEnemyFollowers, 9),
                new CardEffect(CardEffectKind.GrantEnemyLeaderDamageTakenBonus, 1)
            ]),
        new(
            CardIds.NobleBlackWingOlivie,
            "高洁的黑翼·奥莉薇",
            9,
            7,
            7,
            CardKeyword.Ward,
            CardType.Follower,
            "【入场曲】回复自己2点超进化点。\n【守护】",
            Effect: null,
            CardRarity.Rainbow,
            CardProfession.Neutral,
            FanfareEffects:
            [
                new CardEffect(CardEffectKind.RestoreOwnSuperEvolutionPoints, 2)
            ]),
        new(
            CardIds.AlbionBahamut,
            "阿尔比昂巴哈姆特",
            9,
            13,
            13,
            CardKeyword.None,
            CardType.Follower,
            "【入场曲】【模式】选择1个能力发动。\n（1）使战场上的其他所有随从消失。\n（2）使战场上的所有护符消失。\n（3）使所有纹章消失。",
            Effect: null,
            CardRarity.Rainbow,
            CardProfession.Neutral,
            FanfareModeOptions:
            [
                new ModeDefinition(
                    "使战场上的其他所有随从消失",
                    [new CardEffect(CardEffectKind.VanishAllOtherFollowers, 1)]),
                new ModeDefinition(
                    "使战场上的所有护符消失",
                    [new CardEffect(CardEffectKind.VanishAllAmulets, 1)]),
                new ModeDefinition(
                    "使所有纹章消失",
                    [new CardEffect(CardEffectKind.VanishAllCrests, 1)])
            ]),
        new(
            CardIds.AttackCreation,
            "攻击创造物",
            3,
            5,
            1,
            CardKeyword.Rush,
            CardType.Follower,
            "【融合】创造物·卡牌\n根据与本卡牌【融合】的卡牌的费用的合计而变身。\n1⇒『毁灭创造物α』\n2⇒『毁灭创造物β』\n3或以上⇒『毁灭创造物γ』\n【突进】",
            Effect: null,
            CardRarity.Bronze,
            CardProfession.Nemesis,
            Traits: [CardIds.CreationTrait],
            Fusion: new FusionDefinition(
                RequiredMaterialTrait: CardIds.CreationTrait,
                TransformByTotalCost:
                [
                    new FusionTransform(1, CardIds.DestroyerCreationAlpha),
                    new FusionTransform(2, CardIds.DestroyerCreationBeta),
                    new FusionTransform(3, CardIds.DestroyerCreationGamma)
                ]),
            IsCollectible: false),
        new(
            CardIds.DestroyerCreationAlpha,
            "毁灭创造物α",
            5,
            3,
            5,
            CardKeyword.None,
            CardType.Follower,
            "【融合】『毁灭创造物β』或『毁灭创造物γ』与本卡牌【融合】时，若与本卡牌【融合】的种类的为2，则本卡牌变身为『卓越创造物Ω』。\n自己的回合结束时，回复自己的主战者3点生命值。",
            Effect: null,
            CardRarity.Gold,
            CardProfession.Nemesis,
            Traits: [CardIds.CreationTrait],
            Fusion: new FusionDefinition(
                AllowedMaterialCardIds: [CardIds.DestroyerCreationBeta, CardIds.DestroyerCreationGamma],
                TransformWhenDistinctMaterialKindsAtLeast: 2,
                DistinctKindsTransformCardId: CardIds.TranscendentCreationOmega),
            EndOfOwnTurnEffects:
            [
                new CardEffect(CardEffectKind.RestoreOwnLeaderHealth, 3)
            ],
            IsCollectible: false),
        new(
            CardIds.DestroyerCreationBeta,
            "毁灭创造物β",
            5,
            4,
            4,
            CardKeyword.None,
            CardType.Follower,
            "自己的回合结束时，对对手的主战者造成3点伤害。",
            Effect: null,
            CardRarity.Gold,
            CardProfession.Nemesis,
            Traits: [CardIds.CreationTrait],
            EndOfOwnTurnEffects:
            [
                new CardEffect(CardEffectKind.DealDamageToEnemyLeader, 3)
            ],
            IsCollectible: false),
        new(
            CardIds.DestroyerCreationGamma,
            "毁灭创造物γ",
            5,
            5,
            3,
            CardKeyword.None,
            CardType.Follower,
            "自己的回合结束时，对对手的战场上的所有随从造成3点伤害。",
            Effect: null,
            CardRarity.Gold,
            CardProfession.Nemesis,
            Traits: [CardIds.CreationTrait],
            EndOfOwnTurnEffects:
            [
                new CardEffect(CardEffectKind.DealDamageToAllEnemyFollowers, 3)
            ],
            IsCollectible: false),
        new(
            CardIds.TranscendentCreationOmega,
            "卓越创造物Ω",
            10,
            10,
            10,
            CardKeyword.Storm | CardKeyword.Ward | CardKeyword.Aura,
            CardType.Follower,
            "【入场曲】对对手的战场上的所有随从造成5点伤害。回复自己的主战者5点生命值。\n【疾驰】\n【守护】\n【灵气】",
            Effect: null,
            CardRarity.Rainbow,
            CardProfession.Nemesis,
            Traits: [CardIds.CreationTrait],
            FanfareEffects:
            [
                new CardEffect(CardEffectKind.DealDamageToAllEnemyFollowers, 5),
                new CardEffect(CardEffectKind.RestoreOwnLeaderHealth, 5)
            ],
            IsCollectible: false),
        new(
            CardIds.GratitudeArtisanIsaac,
            "报恩工匠·艾萨克",
            2,
            2,
            2,
            CardKeyword.None,
            CardType.Follower,
            "【谢幕曲】将1张『攻击创造物』加入手牌。",
            Effect: null,
            CardRarity.Bronze,
            CardProfession.Nemesis,
            LastWordsEffects:
            [
                new CardEffect(CardEffectKind.AddCopyToHand, 1, CardIds.AttackCreation)
            ]),
        new(
            CardIds.WildBroadcaster,
            "狂野播报员",
            3,
            1,
            1,
            CardKeyword.None,
            CardType.Follower,
            "【入场曲】召唤1个『解析的创造物』。\n【爆能强化_5】召唤1个『神秘的创造物』。\n自己的创造物·随从进入战场时，使其获得【突进】。",
            Effect: null,
            CardRarity.Gold,
            CardProfession.Nemesis,
            FanfareEffects:
            [
                new CardEffect(CardEffectKind.SummonFollower, 1, CardIds.AnalyzedCreation)
            ],
            EnhanceEffects:
            [
                new EnhanceDefinition(
                    5,
                    [new CardEffect(CardEffectKind.SummonFollower, 1, CardIds.MysteriousCreation)])
            ],
            PassiveEffects:
            [
                new CardEffect(CardEffectKind.GrantRushToEnteringCreationFollower, 1)
            ]),
        new(
            CardIds.WhiteFangPhosphorescence,
            "白牙燐敛",
            3,
            0,
            0,
            CardKeyword.None,
            CardType.Spell,
            "选择对手的战场上的1个随从，破坏该随从。若自己的牌组中没有重复卡牌，则改为破坏对手的战场上的所有随从。",
            new CardEffect(CardEffectKind.DestroyEnemyFollowerOrAllIfDeckHasNoDuplicates, 1),
            CardRarity.Gold,
            CardProfession.Nemesis),
        new(
            CardIds.TemperedBodyguard,
            "锻磨保镖",
            4,
            4,
            4,
            CardKeyword.Ward,
            CardType.Follower,
            "【入场曲】若自己的牌组中没有重复卡牌，则选择对手的战场上的1个随从，对其造成4点伤害。回复自己的主战者4点生命值。\n【守护】",
            Effect: null,
            CardRarity.Silver,
            CardProfession.Nemesis,
            FanfareEffects:
            [
                new CardEffect(
                    CardEffectKind.DealDamageToEnemyFollowerAndHealOwnLeaderIfDeckHasNoDuplicates,
                    4)
            ]),
        new(
            CardIds.SkyRidingGuardianCatalina,
            "驰骋天空的守护者·卡塔莉娜",
            5,
            5,
            5,
            CardKeyword.Ward,
            CardType.Follower,
            "【入场曲】【奥义】对对手的战场上的随机2个随从造成5点伤害。\n【守护】\n受到的4点或以上的伤害变为3点。",
            Effect: null,
            CardRarity.Bronze,
            CardProfession.Neutral,
            // 【入场曲】【奥义】写在同一句里 ⇒ 入场曲**恒发**，奥义槽≥10 时**再发一次**。
            FanfareEffects:
            [
                new CardEffect(
                    CardEffectKind.DealDamageToRandomEnemyFollowerCount,
                    2,
                    SecondaryAmount: 5)
            ],
            OathEffects:
            [
                new CardEffect(
                    CardEffectKind.DealDamageToRandomEnemyFollowerCount,
                    2,
                    SecondaryAmount: 5)
            ],
            IncomingDamageCap: 4,
            IncomingDamageFloor: 3),
        new(
            CardIds.DecisiveCrossingAshureAndLitier,
            "决断的交错·亚修雷&莉缇雅",
            5,
            5,
            5,
            CardKeyword.None,
            CardType.Follower,
            "【入场曲】选择对手的战场上的1个随从，使其获得【守护】。\n【爆能强化_9】本随从进化。本随从获得【疾驰】。\n本随从进化时，破坏对手的战场上的随机2个拥有【守护】的随从。",
            Effect: null,
            CardRarity.Rainbow,
            CardProfession.Nemesis,
            FanfareEffects:
            [
                new CardEffect(CardEffectKind.GrantWardToEnemyFollower, 1)
            ],
            EnhanceEffects:
            [
                new EnhanceDefinition(
                    9,
                    [
                        new CardEffect(CardEffectKind.EvolveSelf, 1),
                        new CardEffect(CardEffectKind.GainStorm, 1)
                    ])
            ],
            EvolutionEffects:
            [
                new CardEffect(CardEffectKind.DestroyRandomEnemyWardFollowers, 2)
            ]),
        new(
            CardIds.SpecialTargetHaremhani,
            "特殊目标·海雷姆哈妮",
            6,
            1,
            4,
            CardKeyword.None,
            CardType.Follower,
            "【攻击时】若攻击随从，则本随从获得【屏障】。使交战对手获得「无法攻击随从或主战者」和「自己的回合结束时，使本随从消失」。\n【谢幕曲】使自己获得『纹章：特殊目标·海雷姆哈妮』。",
            Effect: null,
            CardRarity.Rainbow,
            CardProfession.Neutral,
            AttackEffects:
            [
                new CardEffect(CardEffectKind.GainBarrierWhenAttackingFollower, 1),
                new CardEffect(CardEffectKind.ApplyCannotAttackAndVanishesToBattleOpponent, 1)
            ],
            LastWordsEffects:
            [
                new CardEffect(CardEffectKind.GiveSelfCrest, 1, CrestIds.SpecialTargetHaremhani)
            ]),
        new(
            CardIds.HeirOfTheCelestialDirectorSaintDefen,
            "天司长的继承者·圣德芬",
            6,
            7,
            6,
            CardKeyword.None,
            CardType.Follower,
            "在牌组中发动。自己的回合开始时，若本次对战中自己的随从的进化次数为6次或以上，则【瞬念召唤】本卡牌。\n本卡牌被【瞬念召唤】时，使自己获得『纹章：天司长的继承者·圣德芬』。本卡牌返回手牌。\n【入场曲】【解放奥义】发动5次「随机对对手的战场上的1个随从或对手的主战者造成2点伤害」。",
            Effect: null,
            CardRarity.Rainbow,
            CardProfession.Neutral,
            TranscendentSummon: new TranscendentSummonDefinition(
                RequiredOwnEvolutions: 6,
                GrantCrestId: null,
                ReturnsToHand: true),
            // 【入场曲】【解放奥义】写在同一句里 ⇒ 入场曲**恒发**，奥义槽≥15 时**再发一次**。
            FanfareEffects:
            [
                new CardEffect(
                    CardEffectKind.DealRandomDamageToEnemyFollowerOrLeaderRepeatedly,
                    5,
                    SecondaryAmount: 2)
            ],
            SuperOathEffects:
            [
                new CardEffect(
                    CardEffectKind.DealRandomDamageToEnemyFollowerOrLeaderRepeatedly,
                    5,
                    SecondaryAmount: 2)
            ])
    ];

    /// <summary>The ten generated cards that replace the remaining deck after BASE-004 resolves.</summary>
    public static IReadOnlyList<CardDefinition> ApocalypseDeckCards { get; } =
    [
        .. Enumerable.Repeat(Definitions.Single(card => card.Id == CardIds.SilentDemonGeneral), 3),
        .. Enumerable.Repeat(Definitions.Single(card => card.Id == CardIds.ServantOfTheAbyssLord), 3),
        .. Enumerable.Repeat(Definitions.Single(card => card.Id == CardIds.PurgatoryEvilWorship), 3),
        Definitions.Single(card => card.Id == CardIds.AstarothsVerdict)
    ];

    private static readonly IReadOnlyDictionary<string, CardDefinition> DefinitionsById =
        Definitions.ToDictionary(card => card.Id, StringComparer.Ordinal);

    /// <summary>
    /// Checks the catalog once at startup so a bad entry fails immediately instead of
    /// halfway through a match.
    /// </summary>
    static CardCatalog()
    {
        foreach (var definition in Definitions)
        {
            definition.Validate();
        }

        var duplicateId = Definitions
            .GroupBy(card => card.Id, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateId is not null)
        {
            throw new InvalidOperationException($"Card ID {duplicateId.Key} is defined more than once.");
        }

        foreach (var definition in Definitions)
        {
            foreach (var effect in EffectsOf(definition))
            {
                ValidateReference(definition, effect);
            }

            ValidateNecromancyUsage(definition);
        }
    }

    public static IReadOnlyList<CardDefinition> All => Definitions;

    /// <summary>
    /// The ID the next catalog entry would receive. IDs are never reused, so this is always
    /// one past the highest number in use.
    /// </summary>
    public static string NextCardId
    {
        get
        {
            var highestNumber = 0;
            foreach (var card in Definitions)
            {
                if (card.Id.StartsWith("BASE-", StringComparison.Ordinal) &&
                    int.TryParse(card.Id[5..], out var number))
                {
                    highestNumber = Math.Max(highestNumber, number);
                }
            }

            return $"BASE-{highestNumber + 1:000}";
        }
    }

    public static CardDefinition Get(string cardId)
    {
        if (string.IsNullOrWhiteSpace(cardId) || !DefinitionsById.TryGetValue(cardId, out var card))
        {
            throw new ArgumentException($"Unknown card ID: {cardId}", nameof(cardId));
        }

        return card;
    }

    /// <summary>Every effect a card can run, in the order the engine looks them up.</summary>
    private static IEnumerable<CardEffect> EffectsOf(CardDefinition card)
    {
        if (card.Effect is not null)
        {
            yield return card.Effect;
        }

        IReadOnlyList<CardEffect>?[] effectLists =
        [
            card.FanfareEffects,
            card.EvolutionEffects,
            card.SuperEvolutionEffects,
            card.SuperEvolutionEvolutionEffects,
            card.SpellEffects,
            card.LastWordsEffects,
            card.DiscardedEffects,
            card.AttackEffects,
            card.EndOfOwnTurnEffects,
            card.UnevolvedEndOfOwnTurnEffects,
            card.EvolvedEndOfOwnTurnEffects,
            card.OnEvolveEffects,
            card.PassiveEffects
        ];
        foreach (var effects in effectLists)
        {
            if (effects is null)
            {
                continue;
            }

            foreach (var effect in effects)
            {
                yield return effect;
            }
        }

        if (card.EnhanceEffects is not null)
        {
            foreach (var enhance in card.EnhanceEffects)
            {
                foreach (var effect in enhance.Effects)
                {
                    yield return effect;
                }
            }
        }

        if (card.FanfareModeOptions is not null)
        {
            foreach (var mode in card.FanfareModeOptions)
            {
                foreach (var effect in mode.Effects)
                {
                    yield return effect;
                }
            }
        }

        if (card.Accelerate is not null)
        {
            foreach (var effect in card.Accelerate.Effects)
            {
                yield return effect;
            }
        }

        if (card.Crystallize?.LastWordsEffects is not null)
        {
            foreach (var effect in card.Crystallize.LastWordsEffects)
            {
                yield return effect;
            }
        }

        if (card.EvolutionModeOptions is not null)
        {
            foreach (var mode in card.EvolutionModeOptions)
            {
                foreach (var effect in mode.Effects)
                {
                    yield return effect;
                }
            }
        }
    }

    /// <summary>
    /// Effects that name another catalog entry must resolve. Trait checks store a trait name
    /// instead of an ID, so they are checked against the traits actually in use.
    /// </summary>
    private static void ValidateNecromancyUsage(CardDefinition card)
    {
        // 【唤灵_N】 is only paid while a spell or a Fanfare resolves, so attaching the cost to any
        // other effect would silently never charge the graveyard.
        var spellEffects = new List<CardEffect>(card.SpellEffects ?? []);
        spellEffects.AddRange(card.FanfareEffects ?? []);
        if (card.Effect is not null)
        {
            spellEffects.Add(card.Effect);
        }

        foreach (var effect in EffectsOf(card))
        {
            if (effect.NecromancyCost > 0 && !spellEffects.Contains(effect))
            {
                throw new InvalidOperationException(
                    $"Card {card.Id} attaches a Necromancy cost to an effect outside its spell or Fanfare text.");
            }
        }
    }

    private static void ValidateReference(CardDefinition card, CardEffect effect)
    {
        switch (effect.Kind)
        {
            case CardEffectKind.SummonFollower:
            case CardEffectKind.AddCopyToHand:
            case CardEffectKind.AddCopyToHandWithoutLastWords:
            case CardEffectKind.SummonFollowerWithoutLastWords:
            case CardEffectKind.SummonFollowerWithBonusWithoutLastWords:
                if (!DefinitionsById.ContainsKey(effect.ReferencedCardId!))
                {
                    throw new InvalidOperationException(
                        $"Card {card.Id} references unknown card ID {effect.ReferencedCardId}.");
                }

                break;

            case CardEffectKind.ParkourChoiceOrAllModes:
                ValidateParkourModes(card, effect);
                break;

            case CardEffectKind.AddRandomDestroyedTraitFollowerCopyToHandPrivately:
                if (effect.ReferencedCardId == CardIds.AnyTraitMarker)
                {
                    // 「*」 means the card text names no trait, so any destroyed follower qualifies.
                    break;
                }

                var destroyedTraitInUse = Definitions.Any(candidate =>
                    candidate.Type == CardType.Follower &&
                    candidate.Traits?.Contains(effect.ReferencedCardId!, StringComparer.Ordinal) == true);
                if (!destroyedTraitInUse)
                {
                    throw new InvalidOperationException(
                        $"Card {card.Id} references unknown follower trait {effect.ReferencedCardId}.");
                }

                break;

            case CardEffectKind.TransformInto:
                if (!DefinitionsById.TryGetValue(effect.ReferencedCardId!, out var transformTarget) ||
                    transformTarget.Type != CardType.Follower)
                {
                    throw new InvalidOperationException(
                        $"Card {card.Id} transforms into {effect.ReferencedCardId}, which is not a catalog follower.");
                }

                break;

            case CardEffectKind.AddCardToHandIfOwnFollowerPrintedCostAtLeast:
                if (!DefinitionsById.ContainsKey(effect.ReferencedCardId!))
                {
                    throw new InvalidOperationException(
                        $"Card {card.Id} references unknown card ID {effect.ReferencedCardId}.");
                }

                break;

            case CardEffectKind.AddCopyOfTargetFollowerToHandPrivatelyWithCostReduction:
            case CardEffectKind.EvolveAnotherOwnUnevolvedFollower:
                // These name a follower on the board through the action's target, not through the
                // catalog, so there is no referenced card ID to check.
                break;

            case CardEffectKind.GiveEnemyCrest:
            case CardEffectKind.GiveSelfCrest:
                _ = CrestCatalog.Get(effect.ReferencedCardId!);
                break;

            case CardEffectKind.DealDamageToAllFollowersWithoutTrait:
                var traitInUse = Definitions.Any(candidate =>
                    candidate.Traits?.Contains(effect.ReferencedCardId!, StringComparer.Ordinal) == true);
                if (!traitInUse)
                {
                    throw new InvalidOperationException(
                        $"Card {card.Id} references unknown trait {effect.ReferencedCardId}.");
                }

                break;

            case CardEffectKind.SummonRandomDistinctDeckFollowers:
            case CardEffectKind.GrantRushToEnteringAlliedFollowers:
            case CardEffectKind.GainStatsToOtherAlliedFollowers:
                if (!string.IsNullOrWhiteSpace(effect.ReferencedCardId) &&
                    !Enum.TryParse<CardProfession>(effect.ReferencedCardId, out _))
                {
                    throw new InvalidOperationException(
                        $"Card {card.Id} references unknown profession {effect.ReferencedCardId}.");
                }

                break;

            case CardEffectKind.EmpowerEnteringAlliedTraitFollowers:
                var traitExists = Definitions.Any(candidate =>
                    candidate.Traits?.Contains(effect.ReferencedCardId!, StringComparer.Ordinal) == true);
                if (!traitExists)
                {
                    throw new InvalidOperationException(
                        $"Card {card.Id} references unknown trait {effect.ReferencedCardId}.");
                }

                break;
        }
    }

    /// <summary>
    /// Checks the <c>"trait|threshold|cardId1|cardId2…"</c> payload of a conditional 【模式】 effect:
    /// the trait must actually be in use by some card, and every mode must name a real card. A typo
    /// here would otherwise only surface in the middle of a match.
    /// </summary>
    private static void ValidateParkourModes(CardDefinition card, CardEffect effect)
    {
        var parts = effect.ReferencedCardId!.Split('|');
        if (parts.Length < 3 ||
            !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var threshold) ||
            threshold < 1)
        {
            throw new InvalidOperationException(
                $"Card {card.Id} has a conditional mode payload that is not \"trait|threshold|cardIds\": " +
                $"\"{effect.ReferencedCardId}\".");
        }

        var traitInUse = Definitions.Any(candidate =>
            candidate.Traits?.Contains(parts[0], StringComparer.Ordinal) == true);
        if (!traitInUse)
        {
            throw new InvalidOperationException(
                $"Card {card.Id} counts the trait {parts[0]}, but no card carries it.");
        }

        foreach (var referencedId in parts.Skip(2))
        {
            if (!DefinitionsById.ContainsKey(referencedId))
            {
                throw new InvalidOperationException(
                    $"Card {card.Id} references unknown mode card ID {referencedId}.");
            }
        }
    }

    /// <summary>
    /// Validates the entries of a stored deck list without demanding a full deck, so the editor can save
    /// an unfinished deck and the console can still refuse to start a match with it.
    /// </summary>
    public static void ValidateDeckEntries(IEnumerable<DeckCardEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var deckEntries = entries.ToArray();
        var duplicateId = deckEntries
            .GroupBy(entry => entry.CardId, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateId is not null)
        {
            throw new ArgumentException(
                $"Card ID {duplicateId.Key} appears more than once in the deck list.",
                nameof(entries));
        }

        foreach (var entry in deckEntries)
        {
            entry.Validate();
            var card = Get(entry.CardId);
            if (!card.IsCollectible)
            {
                throw new ArgumentException(
                    $"Card {card.Id} is a generated card and cannot be added to a normal deck.",
                    nameof(entries));
            }
        }
    }

    public static DeckDefinition CreateDeck(string name, IEnumerable<DeckCardEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var deckEntries = entries.ToArray();

        var duplicateId = deckEntries
            .GroupBy(entry => entry.CardId, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateId is not null)
        {
            throw new ArgumentException($"Card ID {duplicateId.Key} appears more than once in the deck list.", nameof(entries));
        }

        var cards = new List<CardDefinition>();
        foreach (var entry in deckEntries)
        {
            entry.Validate();
            var card = Get(entry.CardId);
            if (!card.IsCollectible)
            {
                throw new ArgumentException(
                    $"Card {card.Id} is a generated card and cannot be added to a normal deck.",
                    nameof(entries));
            }

            for (var copy = 0; copy < entry.Count; copy++)
            {
                cards.Add(card);
            }
        }

        return new DeckDefinition(name, cards);
    }
}
