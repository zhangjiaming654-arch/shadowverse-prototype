using Shadowverse.Engine.Cards;
using Shadowverse.Engine.Game;
using Shadowverse.Engine.Models;

namespace Shadowverse.Engine.Agents;

/// <summary>
/// A transparent Monte-Carlo lookahead agent. For every legal main-phase action it samples
/// several determinizations, lets the simple GreedyPlayerAgent play both sides for a short
/// horizon, and chooses the action with the strongest average evaluated position.
/// </summary>
/// <summary>
/// FROZEN BASELINE SNAPSHOT of <see cref="LookaheadPlayerAgent"/> as it stood on 2026-09-12,
/// before the opponent-reach work and the evaluation-weight iteration.
/// <para>
/// Keep it behaviourally identical. It exists so every later change to the planner can be measured
/// against a fixed reference (牌手 1.0) instead of against a moving target.
/// Do not improve this class: add new behaviour to <see cref="LookaheadPlayerAgent"/> instead.
/// </para>
/// </summary>
public sealed class BaselineLookaheadPlayerAgent : IStateAwarePlayerAgent
{
    // Rollouts use sampled hidden information, so a tiny apparent advantage is usually
    // noise rather than a genuine strategic discovery. In that case retain the reliable
    // rule-agent action instead of replacing it with a speculative deviation.
    private const double RuleAgentConfidenceMargin = 0.035;
    // 用冻结的规则牌手，而不是当前那份。否则这个"1.0"会跟着规则牌手的改进一起变强，
    // 让"新牌手 vs 1.0"的对比把规则牌手那部分增益抵消掉，测出来偏向"没差别"。
    private readonly BaselineGreedyPlayerAgent _rolloutAgent = new();
    private readonly int _rolloutsPerAction;
    private readonly int _futureTurnHorizon;
    private readonly ulong _seed;
    private long _decisionNumber;

    public BaselineLookaheadPlayerAgent(
        int rolloutsPerAction = 60,
        int futureTurnHorizon = 3,
        ulong seed = 88_210UL)
    {
        if (rolloutsPerAction is < 1 or > 500)
        {
            throw new ArgumentOutOfRangeException(nameof(rolloutsPerAction), "Rollouts per action must be from 1 to 500.");
        }

        if (futureTurnHorizon is < 1 or > 10)
        {
            throw new ArgumentOutOfRangeException(nameof(futureTurnHorizon), "Future turn horizon must be from 1 to 10.");
        }

        _rolloutsPerAction = rolloutsPerAction;
        _futureTurnHorizon = futureTurnHorizon;
        _seed = seed == 0 ? 1UL : seed;
    }

    /// <summary>The latest main-phase comparison, exposed for reports and later advisor UI.</summary>
    public LookaheadDecision? LastDecision { get; private set; }

    public GameAction ChooseAction(GameObservation observation, IReadOnlyList<GameAction> legalActions) =>
        throw new InvalidOperationException("LookaheadPlayerAgent must be run by MatchRunner so it can create safe determinizations.");

    public GameAction ChooseAction(
        GameState state,
        GameObservation observation,
        IReadOnlyList<GameAction> legalActions)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(observation);
        ArgumentNullException.ThrowIfNull(legalActions);

        if (legalActions.Count == 0)
        {
            throw new InvalidOperationException("An agent was asked to act without any legal actions.");
        }

        if (observation.Phase == GamePhase.Mulligan)
        {
            LastDecision = null;
            return _rolloutAgent.ChooseAction(observation, legalActions);
        }

        var decisionNumber = ++_decisionNumber;
        var ruleAgentAction = _rolloutAgent.ChooseAction(observation, legalActions);
        var evaluations = legalActions
            .Select((action, originalIndex) => EvaluateAction(
                state,
                observation.PerspectivePlayer,
                action,
                originalIndex,
                decisionNumber))
            .OrderByDescending(evaluation => evaluation.EstimatedWinChance)
            .ThenByDescending(evaluation => evaluation.CompletedWins)
            .ThenBy(evaluation => evaluation.OriginalLegalActionIndex)
            .ToArray();

        var selected = evaluations[0];
        var ruleAgentEvaluation = evaluations.Single(evaluation =>
            ReferenceEquals(evaluation.Action, ruleAgentAction));
        if (selected.EstimatedWinChance - ruleAgentEvaluation.EstimatedWinChance <= RuleAgentConfidenceMargin)
        {
            selected = ruleAgentEvaluation;
        }
        LastDecision = new LookaheadDecision(
            observation.PerspectivePlayer,
            observation.TurnNumber,
            selected.Action,
            evaluations);
        return selected.Action;
    }

    private LookaheadActionEvaluation EvaluateAction(
        GameState liveState,
        int perspectivePlayer,
        GameAction candidate,
        int originalLegalActionIndex,
        long decisionNumber)
    {
        var totalValue = 0.0;
        var completedWins = 0;
        var completedLosses = 0;
        // The deadline belongs to the live position, not to the candidate action. In the
        // old order an EndTurnAction incremented TurnNumber before this was calculated,
        // so passing was judged from a later, higher-PP future than playing a card.
        var targetTurnNumber = liveState.TurnNumber + _futureTurnHorizon;

        for (var rollout = 0; rollout < _rolloutsPerAction; rollout++)
        {
            // Every candidate receives the same sequence of sampled hidden worlds, making
            // comparison less noisy than giving each action unrelated random draws.
            var simulationSeed = SimulationSeed(decisionNumber, rollout);
            var simulation = GameEngine.CreateDeterminization(liveState, perspectivePlayer, simulationSeed);
            simulation = GameEngine.Apply(simulation, candidate);

            var simulatedActions = 0;
            while (!simulation.IsGameOver &&
                   simulation.TurnNumber < targetTurnNumber &&
                   simulatedActions < 400)
            {
                var activePlayer = simulation.ActivePlayer;
                var observation = GameEngine.ToObservation(simulation, activePlayer);
                var legalActions = GameEngine.GetLegalActions(simulation);
                var rolloutAction = _rolloutAgent.ChooseAction(observation, legalActions);
                simulation = GameEngine.Apply(simulation, rolloutAction);
                simulatedActions++;
            }

            if (simulation.IsGameOver)
            {
                if (simulation.Winner == perspectivePlayer)
                {
                    completedWins++;
                    totalValue += 1.0;
                }
                else
                {
                    completedLosses++;
                }

                continue;
            }

            totalValue += EvaluatePosition(simulation, perspectivePlayer);
        }

        return new LookaheadActionEvaluation(
            candidate,
            originalLegalActionIndex,
            totalValue / _rolloutsPerAction,
            _rolloutsPerAction,
            completedWins,
            completedLosses);
    }

    private static double EvaluatePosition(GameState state, int perspectivePlayer)
    {
        var self = state.Players[perspectivePlayer];
        var opponent = state.Players[perspectivePlayer == 0 ? 1 : 0];

        var rawScore = 0.0;
        rawScore += (self.Health - opponent.Health) * 2.0;
        rawScore += (BoardValue(self.Board) - BoardValue(opponent.Board)) * 0.7;
        rawScore += (AmuletValue(self.Amulets) - AmuletValue(opponent.Amulets)) * 0.4;
        rawScore += (CrestBurden(opponent.Crests) - CrestBurden(self.Crests)) * 0.5;
        rawScore += (self.Hand.Count - opponent.Hand.Count) * 0.6;
        rawScore += (self.Deck.Count - opponent.Deck.Count) * 0.15;
        rawScore += (self.CurrentPlayPoints - opponent.CurrentPlayPoints) * 0.05;
        rawScore += (self.MaxPlayPoints - opponent.MaxPlayPoints) * 0.45;
        rawScore += (self.EvolutionPoints + self.SuperEvolutionPoints - opponent.EvolutionPoints - opponent.SuperEvolutionPoints) * 0.4;
        rawScore += (DragonRainbowBoardValue(self) - DragonRainbowBoardValue(opponent)) * 0.35;
        rawScore += (EndOfOwnTurnThreat(self, opponent) - EndOfOwnTurnThreat(opponent, self)) * 0.45;

        // The logistic conversion turns a readable material/health score into a stable
        // 0..1 short-horizon win-chance estimate, without claiming it is an exact full-game win rate.
        return 1.0 / (1.0 + Math.Exp(-rawScore / 12.0));
    }

    private static int BoardValue(IReadOnlyList<FollowerInstance> board) => board.Sum(follower =>
        (follower.Attack * 2) +
        follower.CurrentDefense +
        RarityBoardValue(follower.Definition.Rarity) +
        (follower.HasWard ? 2 : 0) +
        (follower.HasStorm ? 1 : 0) +
        (follower.HasBane ? 3 : 0) +
        (follower.HasIntimidate ? 3 : 0) +
        (follower.HasBarrier ? 2 : 0) +
        (follower.HasAura ? 3 : 0));

    private static double DragonRainbowBoardValue(PlayerState player)
    {
        // A rainbow follower that has reached the board is a concrete threat. A rainbow
        // still in hand is not automatically value: rewarding it made the old evaluator
        // prefer hoarding expensive cards and passing instead of using its PP.
        return player.Board
            .Where(follower => follower.Definition.Profession == CardProfession.Dragon &&
                               follower.Definition.Rarity == CardRarity.Rainbow)
            .Sum(follower => 8.0 + (follower.Definition.Cost * 0.5));
    }

    private static double EndOfOwnTurnThreat(PlayerState source, PlayerState target)
    {
        var value = 0.0;
        foreach (var follower in source.Board)
        {
            var effects = follower.IsEvolved
                ? follower.Definition.EvolvedEndOfOwnTurnEffects ?? []
                : follower.Definition.UnevolvedEndOfOwnTurnEffects ?? [];
            foreach (var effect in effects)
            {
                value += effect.Kind switch
                {
                    CardEffectKind.DealDamageToUpToTwoRandomEnemyFollowers =>
                        Math.Min(2, target.Board.Count) * effect.Amount * 0.45,
                    CardEffectKind.RestoreOwnLeaderHealth =>
                        Math.Min(effect.Amount, source.MaxHealth - source.Health) * 0.55,
                    CardEffectKind.DealDamageToEnemyLeader =>
                        Math.Min(effect.Amount, target.Health) * 0.9,
                    _ => 0
                };
            }
        }

        return value;
    }

    private static int RarityBoardValue(CardRarity rarity) => rarity switch
    {
        CardRarity.Rainbow => 5,
        CardRarity.Gold => 2,
        _ => 0
    };

    private static int AmuletValue(IReadOnlyList<AmuletInstance> amulets) =>
        amulets?.Sum(amulet => amulet.Countdown ?? 1) ?? 0;

    private static int CrestBurden(IReadOnlyList<CrestInstance> crests) =>
        crests.Count(crest => crest.Definition.Id == CrestIds.AshenAnathemaBanderst) * 3;

    private ulong SimulationSeed(long decisionNumber, int rollout)
    {
        var value = _seed +
                    (ulong)decisionNumber * 0x9E3779B97F4A7C15UL +
                    (ulong)(rollout + 1) * 0xBF58476D1CE4E5B9UL;
        value ^= value >> 30;
        value *= 0xBF58476D1CE4E5B9UL;
        value ^= value >> 27;
        value *= 0x94D049BB133111EBUL;
        return value ^ (value >> 31);
    }
}
