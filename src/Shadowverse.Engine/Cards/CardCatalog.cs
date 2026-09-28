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
}

public static class CrestIds
{
    public const string AshenAnathemaBanderst = "CREST-001";
    public const string IstanbulDeadVersusMalchiget = "CREST-002";
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
            EndOfOwnTurnEffects: [new CardEffect(CardEffectKind.ShatterRandomLastWordsCardAndEnemyFollower, 1)])
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
                var destroyedTraitInUse = Definitions.Any(candidate =>
                    candidate.Type == CardType.Follower &&
                    candidate.Traits?.Contains(effect.ReferencedCardId!, StringComparer.Ordinal) == true);
                if (!destroyedTraitInUse)
                {
                    throw new InvalidOperationException(
                        $"Card {card.Id} references unknown follower trait {effect.ReferencedCardId}.");
                }

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
