using Shadowverse.Engine.Models;

namespace Shadowverse.Engine.Game;

public enum GamePhase
{
    Mulligan,
    Main,
    GameOver
}

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
    internal List<(int PlayerIndex, CardInstance Card)> PendingLastWordsInternal { get; } = [];

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
        return copy;
    }
}
