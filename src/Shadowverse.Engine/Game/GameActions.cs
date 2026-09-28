namespace Shadowverse.Engine.Game;

public abstract record GameAction;

public sealed record MulliganAction(IReadOnlyList<int> ReplaceInstanceIds) : GameAction;

/// <summary>
/// Plays a follower. HandCardTargetInstanceId is used only by follower effects
/// that ask the player to choose another card from their hand. EnemyFollowerTargetInstanceIds
/// is used by effects that select enemy followers during the Fanfare. ModeChoiceIndex
/// chooses one Fanfare mode when the card has a mode ability. OwnHandCardTargetInstanceIds
/// is used by Fanfare effects that discard multiple other cards from the hand.
/// </summary>
public sealed record PlayFollowerAction(
    int CardInstanceId,
    int? HandCardTargetInstanceId = null,
    IReadOnlyList<int>? EnemyFollowerTargetInstanceIds = null,
    int? ModeChoiceIndex = null,
    IReadOnlyList<int>? OwnHandCardTargetInstanceIds = null) : GameAction;

/// <summary>Plays an amulet into one of the five shared board slots.</summary>
public sealed record PlayAmuletAction(int CardInstanceId) : GameAction;

/// <summary>
/// 结晶: plays a card as an amulet at its crystallize cost. It shares Accelerate's condition: the
/// play is only available while the player cannot pay the card's own cost.
/// </summary>
public sealed record PlayCrystallizeAction(int CardInstanceId) : GameAction;

/// <summary>
/// Uses a card's Accelerate ability. The source card is played as a spell at the
/// Accelerate cost, then goes to the graveyard instead of entering the board.
/// </summary>
public sealed record PlayAccelerateAction(int CardInstanceId) : GameAction;

public abstract record SpellTarget;

public sealed record EnemyLeaderTarget : SpellTarget;

public sealed record EnemyFollowerTarget(int FollowerInstanceId) : SpellTarget;

/// <summary>
/// A follower on either side of the board. Used by effects whose card text says "choose 1 card on the
/// board" without restricting whose it is, such as 【变身】. The engine decides whether a given side is
/// legal for a given effect — an effect that may only touch the opponent's board must reject the
/// owner's own followers itself, because <see cref="EnemyFollowerTarget"/> is not used for these.
/// </summary>
public sealed record FollowerTarget(int FollowerInstanceId) : SpellTarget;

/// <summary>
/// An amulet on either side of the shared board. Used when a card's text says "choose 1 card on the
/// board" and an amulet is a legal answer, such as 【变身】: transforming an amulet into a follower
/// removes the amulet and puts the follower in its place.
/// </summary>
public sealed record AmuletTarget(int AmuletInstanceId) : SpellTarget;

/// <summary>
/// Plays a spell. OwnHandCardTargetInstanceIds is used by spells that ask the
/// player to choose cards from their own hand, such as discard effects.
/// ModeChoiceIndex chooses one 【模式】 when the spell prints a mode ability.
/// </summary>
public sealed record PlaySpellAction(
    int CardInstanceId,
    SpellTarget? Target,
    IReadOnlyList<int>? OwnHandCardTargetInstanceIds = null,
    int? ModeChoiceIndex = null) : GameAction;

/// <summary>
/// ModeChoiceIndex is used when evolution repeats a selectable Fanfare mode.
/// OwnHandCardTargetInstanceIds selects cards discarded by an evolution effect.
/// EnemyFollowerTargetInstanceId selects the enemy follower an evolution effect such as
/// 恶魔鼓手·拉兹's 【进化时】 damages; it stays null when that follower cannot be chosen.
/// </summary>
public sealed record EvolveAction(
    int FollowerInstanceId,
    int? ModeChoiceIndex = null,
    IReadOnlyList<int>? OwnHandCardTargetInstanceIds = null,
    int? EnemyFollowerTargetInstanceId = null) : GameAction;

/// <summary>
/// Super evolves a follower. OtherFollowerTargetInstanceId is used by a
/// super-evolution trigger that selects another unevolved allied follower;
/// ModeChoiceIndex is used when the super evolution also repeats an evolution mode.
/// EnemyFollowerTargetInstanceId carries the target of an evolution effect that the
/// super evolution repeats.
/// </summary>
public sealed record SuperEvolveAction(
    int FollowerInstanceId,
    int? OtherFollowerTargetInstanceId = null,
    int? ModeChoiceIndex = null,
    IReadOnlyList<int>? OwnHandCardTargetInstanceIds = null,
    int? EnemyFollowerTargetInstanceId = null) : GameAction;

public sealed record UseExtraPlayPointAction : GameAction;

public sealed record AttackLeaderAction(int AttackerInstanceId) : GameAction;

public sealed record AttackFollowerAction(int AttackerInstanceId, int DefenderInstanceId) : GameAction;

public sealed record EndTurnAction : GameAction;
