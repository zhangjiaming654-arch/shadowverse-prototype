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

/// <summary>队列里的一条待结算触发效果。<see cref="Sequence"/> 用于同优先级内的稳定先后。</summary>
internal readonly record struct PendingEffect(
    PendingEffectPriority Priority,
    long Sequence,
    int PlayerIndex,
    CardInstance Card);

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
        return copy;
    }
}
