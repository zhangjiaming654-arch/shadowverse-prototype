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

    public bool IsGameOver => Phase == GamePhase.GameOver;

    internal GameState DeepCopy()
    {
        return new GameState(Players[0].DeepCopy(), Players[1].DeepCopy(), RandomState)
        {
            Phase = Phase,
            StartingPlayer = StartingPlayer,
            ActivePlayer = ActivePlayer,
            TurnNumber = TurnNumber,
            Winner = Winner,
            NextInstanceId = NextInstanceId
        };
    }
}
