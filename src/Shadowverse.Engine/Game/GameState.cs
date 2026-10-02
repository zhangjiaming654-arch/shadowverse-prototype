using Shadowverse.Engine.Models;

namespace Shadowverse.Engine.Game;

public enum GamePhase
{
    Mulligan,
    Main,
    GameOver
}

/// <summary>
/// 触发式效果的**结算优先级**（由卡牌设计者给定，数字越小越先结算）。
/// <para>
///   0 随从自己的入场曲 ＞ 1 自己的纹章效果 ＞ 2 自己的其他随从的效果
///   ＞ 3 对方主战者纹章效果 ＞ 4 对方随从效果
/// </para>
/// <para>
/// 【入场曲】是**即时结算**的（不入队，就是正在执行的那一段），
/// 队列里只会出现 1～4 这几档的后续触发。
/// </para>
/// </summary>
internal enum PendingEffectPriority
{
    OwnFanfare = 0,
    OwnCrest = 1,
    OwnOtherFollower = 2,
    EnemyCrest = 3,
    EnemyFollower = 4
}

/// <summary>待结算触发效果的**种类**；决定要用哪个结算器把它**完整**跑完。</summary>
internal enum PendingEffectKind
{
    /// <summary>【谢幕曲】。</summary>
    LastWords,
    /// <summary>「自己的创造物·随从进入战场时」这类随从被动（针对某个刚进场的实例）。</summary>
    FollowerPassiveForEntrant,
    /// <summary>「其他随从进入战场时使其进化」被动（针对某个刚进场的实例）。</summary>
    EvolveEntrantPassive,
    /// <summary>自己的回合开始时触发的纹章效果。</summary>
    CrestStartOfOwnTurn,
    /// <summary>自己的回合结束时触发的纹章效果。</summary>
    CrestEndOfOwnTurn,
    /// <summary>「自己的主战者回复时」触发的纹章效果。</summary>
    CrestLeaderRestored,
    /// <summary>「自己使用随从时，每回合1次，使其进化」。</summary>
    CrestEvolvePlayedFollower
}

/// <summary>
/// 队列里的一条待结算触发效果。
/// <para>
/// **不变式**：取出任意一条后，必须把它**完整**结算完，才允许开始下一条 ——
/// 也就是不允许任何形式的插入结算。
/// </para>
/// <para><see cref="Sequence"/> 是同级内的稳定先后；对纹章来源它等于纹章的获取顺序，
/// 从而满足"多个纹章按获得早晚发动"。</para>
/// </summary>
internal readonly record struct PendingEffect(
    PendingEffectPriority Priority,
    long Sequence,
    int PlayerIndex,
    PendingEffectKind Kind,
    CardInstance? Card = null,
    string? CrestId = null);

/// <summary>
/// Full internal state. It contains hidden information and must never be passed directly to an agent.
/// </summary>
public sealed class GameState
{
    internal GameState(PlayerState firstPlayer, PlayerState secondPlayer, ulong randomState)
    {
        Players = [firstPlayer, secondPlayer];
        RandomState = randomState;
    }

    public PlayerState[] Players { get; }
    public GamePhase Phase { get; internal set; }
    public int StartingPlayer { get; internal set; }
    public int ActivePlayer { get; internal set; }
    public int TurnNumber { get; internal set; }
    public int? Winner { get; internal set; }

    // Stored inside the state so the same seed and actions produce the same replay.
    internal ulong RandomState { get; set; }
    internal int NextInstanceId { get; set; } = 1;

    /// <summary>
    /// 【谢幕曲】的**待结算队列**。
    /// <para>
    /// 为什么需要它：谢幕曲是**触发式能力**，不能在"造成破坏的那一步"里就地结算 ——
    /// 那会变成**插入结算**，把正在进行的入场曲打断。
    /// 例：铸铁亲信的入场曲破坏对手一个随从；对方那只随从的谢幕曲是"破坏对手随机1个随从"。
    /// 就地结算会把铸铁亲信（此刻我方场上唯一随从）带走，于是入场曲的第 2 个效果
    /// （"牌组无重复则获得【疾驰】"）去场上找自己就找不到了。
    /// 正确顺序是：**先完整结算入场曲，再统一结算谢幕曲**。
    /// </para>
    /// <para>破坏时只入队，由 <c>GameEngine.DrainPendingLastWords</c> 在动作结算完之后统一清空。</para>
    /// </summary>
    internal List<PendingEffect> PendingLastWordsInternal { get; } = [];

    /// <summary>同优先级内的稳定先后（入队序号）。进指纹，保证可复现。</summary>
    internal long NextPendingEffectSequence { get; set; }

    /// <summary>纹章获取顺序的计数器（全局递增，跨双方）。</summary>
    internal long NextCrestAcquiredSequence { get; set; }

    /// <summary>
    /// 观察量：**入场曲结算中途去找"刚打出的那张随从"却找不到**的次数。
    /// 正确顺序下必须恒为 0 —— 入场曲完整结算完之前，那张随从一定还在场上。
    /// 它是"插入结算"唯一的可区分信号（随从死后看不出它当时有没有拿到关键词）。
    /// </summary>
    public long PlayedFollowerLookupMissesInternal { get; internal set; }

    /// <summary>
    /// 观察量：纹章"回合开始效果"的**实际结算顺序**（记录纹章 Id，按结算先后追加）。
    /// 用于断言"同一优先级里多枚纹章按**获取从早到晚**发动"。
    /// 只读观察，不参与任何决策。
    /// </summary>
    public List<string> CrestResolutionOrderInternal { get; } = [];

    /// <summary>
    /// 观察量：**每批**队列结算的"优先级 + 种类"序列（批与批之间用 "|" 分隔）。
    /// 用于断言 5 档优先级：同一批内优先级必须**非递减**
    /// （0 入场曲 ＞ 1 自己纹章 ＞ 2 自己其他随从 ＞ 3 对方纹章 ＞ 4 对方随从）。
    /// 只读观察，不参与任何决策。
    /// </summary>
    public List<string> ResolutionOrderLogInternal { get; } = [];

    public bool IsGameOver => Phase == GamePhase.GameOver;

    internal GameState DeepCopy()
    {
        var copy = new GameState(Players[0].DeepCopy(), Players[1].DeepCopy(), RandomState)
        {
            Phase = Phase,
            StartingPlayer = StartingPlayer,
            ActivePlayer = ActivePlayer,
            TurnNumber = TurnNumber,
            Winner = Winner,
            NextInstanceId = NextInstanceId
        };
        copy.PendingLastWordsInternal.AddRange(PendingLastWordsInternal);
        copy.NextPendingEffectSequence = NextPendingEffectSequence;
        copy.NextCrestAcquiredSequence = NextCrestAcquiredSequence;
        copy.PlayedFollowerLookupMissesInternal = PlayedFollowerLookupMissesInternal;
        copy.CrestResolutionOrderInternal.AddRange(CrestResolutionOrderInternal);
        copy.ResolutionOrderLogInternal.AddRange(ResolutionOrderLogInternal);
        return copy;
    }
}
