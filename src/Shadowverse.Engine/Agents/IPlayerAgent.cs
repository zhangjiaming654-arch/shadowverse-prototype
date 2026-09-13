using Shadowverse.Engine.Game;

namespace Shadowverse.Engine.Agents;

public interface IPlayerAgent
{
    GameAction ChooseAction(GameObservation observation, IReadOnlyList<GameAction> legalActions);
}

/// <summary>
/// Optional extension for agents that need to run forward simulations. Implementations must
/// construct their rollouts through GameEngine.CreateDeterminization rather than reading the
/// opponent's hidden cards or deck order from the supplied live state.
/// </summary>
public interface IStateAwarePlayerAgent : IPlayerAgent
{
    GameAction ChooseAction(
        GameState state,
        GameObservation observation,
        IReadOnlyList<GameAction> legalActions);
}
