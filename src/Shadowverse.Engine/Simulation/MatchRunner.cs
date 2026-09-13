using Shadowverse.Engine.Agents;
using Shadowverse.Engine.Game;

namespace Shadowverse.Engine.Simulation;

public sealed record ActionLogEntry(int TurnNumber, int Player, GameAction Action);

/// <summary>
/// A fully resolved decision. The callback is intended for replay/reporting tools, not for agents.
/// </summary>
public sealed record MatchStep(
    GameState BeforeState,
    int ActingPlayer,
    GameAction Action,
    GameState AfterState);

public sealed record MatchResult(
    int Winner,
    int ActionCount,
    IReadOnlyList<ActionLogEntry> Actions,
    GameState FinalState);

public static class MatchRunner
{
    public static MatchResult PlayToEnd(
        GameState initialState,
        IPlayerAgent firstPlayer,
        IPlayerAgent secondPlayer,
        int maximumActions = 2_000,
        Action<MatchStep>? onStep = null)
    {
        ArgumentNullException.ThrowIfNull(initialState);
        ArgumentNullException.ThrowIfNull(firstPlayer);
        ArgumentNullException.ThrowIfNull(secondPlayer);

        var state = initialState;
        var history = new List<ActionLogEntry>();

        while (!state.IsGameOver)
        {
            if (history.Count >= maximumActions)
            {
                throw new InvalidOperationException("Action limit reached; a match should always finish by leader defeat or deck exhaustion.");
            }

            var playerIndex = state.ActivePlayer;
            var agent = playerIndex == 0 ? firstPlayer : secondPlayer;
            var observation = GameEngine.ToObservation(state, playerIndex);
            var legalActions = GameEngine.GetLegalActions(state);
            var action = agent is IStateAwarePlayerAgent stateAwareAgent
                ? stateAwareAgent.ChooseAction(state, observation, legalActions)
                : agent.ChooseAction(observation, legalActions);

            var nextState = GameEngine.Apply(state, action);
            history.Add(new ActionLogEntry(state.TurnNumber, playerIndex, action));
            onStep?.Invoke(new MatchStep(state, playerIndex, action, nextState));
            state = nextState;
        }

        return new MatchResult(
            state.Winner ?? throw new InvalidOperationException("A completed game must have a winner."),
            history.Count,
            history,
            state);
    }
}
