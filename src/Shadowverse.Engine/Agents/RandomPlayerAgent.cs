using Shadowverse.Engine.Game;

namespace Shadowverse.Engine.Agents;

/// <summary>Baseline agent used to smoke-test the engine and run initial self-play.</summary>
public sealed class RandomPlayerAgent : IPlayerAgent
{
    private readonly Random _random;

    public RandomPlayerAgent(int seed)
    {
        _random = new Random(seed);
    }

    public GameAction ChooseAction(GameObservation observation, IReadOnlyList<GameAction> legalActions)
    {
        if (legalActions.Count == 0)
        {
            throw new InvalidOperationException("An agent was asked to act without any legal actions.");
        }

        return legalActions[_random.Next(legalActions.Count)];
    }
}
