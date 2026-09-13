using Shadowverse.Engine.Models;

namespace Shadowverse.Engine.Game;

public sealed record HandCardView(int InstanceId, CardDefinition Definition);

public sealed record VisibleFollower(
    int InstanceId,
    string CardId,
    string Name,
    int Attack,
    int CurrentDefense,
    int MaxDefense,
    CardKeyword Keywords,
    EvolutionState EvolutionState,
    bool HasAttacked,
    IReadOnlyList<string>? Traits = null);

public sealed record VisibleAmulet(
    int InstanceId,
    string CardId,
    string Name,
    int? Countdown);

public sealed record VisibleCrest(string Id, string Name, string EffectText);

public sealed record PlayerView(
    int Health,
    int MaxHealth,
    int CurrentPlayPoints,
    int MaxPlayPoints,
    int OwnTurnNumber,
    int EvolutionPoints,
    int SuperEvolutionPoints,
    bool UsedEarlyExtraPlayPoint,
    bool UsedLateExtraPlayPoint,
    bool AttackedEnemyLeaderOnPreviousTurn,
    int HandCount,
    int DeckCount,
    int GraveyardCount,
    IReadOnlyList<VisibleFollower> Board,
    IReadOnlyList<VisibleAmulet>? Amulets = null,
    IReadOnlyList<VisibleCrest>? Crests = null,
    /// <summary>
    /// Catalog IDs this player has played in public — followers, amulets, spells and their
    /// Crystallize / Accelerate forms. It never contains a card that only passed through the hand
    /// or the deck. The opposing side uses it to identify which saved deck this player is on, and
    /// so to bound what they could still be holding.
    /// </summary>
    IReadOnlyList<string>? RevealedCardIds = null);

/// <summary>
/// The only state an agent may inspect. Opponent hand contents, deck order, and RNG state are excluded.
/// </summary>
public sealed record GameObservation(
    int PerspectivePlayer,
    int ActivePlayer,
    int TurnNumber,
    GamePhase Phase,
    PlayerView Self,
    PlayerView Opponent,
    IReadOnlyList<HandCardView> OwnHand,
    /// <summary>
    /// 自己牌库里还剩哪些牌（卡牌编号），**已按编号排序**。
    /// <para>
    /// 内容是本人本来就该知道的——牌是自己组的。但**顺序必须藏住**：牌库顺序对本人也是隐藏信息，
    /// 直接暴露抽取顺序等于让他看见牌库顶。所以这里刻意排序，只保留"还剩什么"，丢掉"下一张是什么"。
    /// </para>
    /// 有了它，牌手才能做卡组相关的判断，例如"我这套牌有没有某张关键牌、该不该全换去赌"。
    /// </summary>
    IReadOnlyList<string>? OwnDeckCardIds = null);
