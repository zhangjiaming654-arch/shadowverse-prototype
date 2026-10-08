using Shadowverse.Engine.Cards;
using Shadowverse.Engine.Game;
using Shadowverse.Engine.Models;

namespace Shadowverse.ConsoleApp;

internal static partial class AgentSelfTests
{
    internal static void RunDecisiveCrossingTargetTest()
    {
        static void Check(bool value, string message)
        { if (!value) throw new InvalidOperationException("Decisive Crossing: " + message); }
        static (GameState State, int SourceId, int TargetId) Scenario(int actor, int ownTurn,
            CardKeyword victimKeywords = CardKeyword.None)
        {
            var crossing = CardCatalog.Get(CardIds.DecisiveCrossingAshureAndLitier);
            var filler = CardCatalog.Get(CardIds.Gladiator) with { Cost = 0, Attack = 0, Defense = 20 };
            var victim = CardCatalog.Get(CardIds.AshenAnathemaBanderst) with { Cost = 0, Keywords = victimKeywords };
            var own = Enumerable.Repeat(filler, 39).Append(crossing).ToArray();
            var enemy = Enumerable.Repeat(victim, 40).ToArray();
            for (ulong seed = 1; seed <= 512; seed++)
            {
                var state = GameEngine.CreateGame(new DeckDefinition("crossing-p1", actor == 0 ? own : enemy),
                    new DeckDefinition("crossing-p2", actor == 0 ? enemy : own), seed);
                if (!state.Players[actor].Hand.Any(c => c.Definition.Id == crossing.Id)) continue;
                while (state.Phase == GamePhase.Mulligan) state = GameEngine.Apply(state, new MulliganAction([]));
                while (state.ActivePlayer == actor || state.Players[1 - actor].OwnTurnNumber < 7)
                    state = GameEngine.Apply(state, new EndTurnAction());
                for (var i = 0; i < 3; i++) state = GameEngine.Apply(state, GameEngine.GetLegalActions(state).OfType<PlayFollowerAction>().First());
                var target = state.Players[1 - actor].Board[0].InstanceId;
                state = GameEngine.Apply(state, GameEngine.GetLegalActions(state).OfType<SuperEvolveAction>().First(a => a.FollowerInstanceId == target));
                state = GameEngine.Apply(state, new EndTurnAction());
                while (state.ActivePlayer != actor || state.Players[actor].OwnTurnNumber < ownTurn)
                    state = GameEngine.Apply(state, new EndTurnAction());
                return (state, state.Players[actor].Hand.Single(c => c.Definition.Id == crossing.Id).InstanceId, target);
            }
            throw new InvalidOperationException("Could not deal Crossing scenario.");
        }
        foreach (var actor in new[] { 0, 1 })
        foreach (var kind in new[] { "evolve", "super", "enhance" })
        {
            var (state, source, target) = Scenario(actor, kind == "enhance" ? 9 : 7);
            var legal = GameEngine.GetLegalActions(state);
            var plays = legal.OfType<PlayFollowerAction>().Where(a => a.CardInstanceId == source).ToArray();
            Check(plays.Length == 3 && plays.All(a => a.EnemyFollowerTargetInstanceIds is { Count: 1 }), "must offer every legal enemy without targetless bypass");
            var gesture = HumanActionResolver.FromHand(legal, source, target);
            Check(gesture.Count == 1, "drag must resolve the selected target");
            var observation = GameEngine.ToObservation(state, actor);
            var selection = new HumanTargetSelection(observation, plays);
            Check(selection.Available.Count == 3 && selection.Toggle(new(HumanTargetZone.EnemyBoard, target)) && selection.Advance() && selection.IsComplete,
                "guided target choice must complete this actual Fanfare");
            var before = GameEngine.StateFingerprint(state);
            var after = GameEngine.Apply(state, gesture.Single());
            Check(before == GameEngine.StateFingerprint(state), "original replay snapshot must not mutate");
            if (kind != "enhance")
            {
                Check(after.Players[1 - actor].Board.Single(f => f.InstanceId == target).HasWard, "selected 12/12 Anathema must receive Ward");
                Check(after.Players[1 - actor].Board.Where(f => f.InstanceId != target).All(f => !f.HasWard), "unselected followers must not receive Ward");
                var evolution = kind == "super"
                    ? (GameAction)GameEngine.GetLegalActions(after).OfType<SuperEvolveAction>().First(a => a.FollowerInstanceId == source)
                    : GameEngine.GetLegalActions(after).OfType<EvolveAction>().First(a => a.FollowerInstanceId == source);
                after = GameEngine.Apply(after, evolution);
            }
            Check(after.Players[1 - actor].Board.Count == 2 && after.Players[1 - actor].Board.All(f => f.InstanceId != target),
                "evolution must destroy chosen super-evolved Anathema on opponent turn");
            Check(after.Players[1 - actor].Graveyard.Any(c => c.InstanceId == target), "destroyed target must reach its owner's graveyard");
            if (kind == "enhance") Check(after.Players[actor].Board.Single(f => f.InstanceId == source).HasStorm, "enhance must grant Storm");
            var rejected = false;
            try { GameEngine.Apply(state, new PlayFollowerAction(source)); } catch (InvalidOperationException) { rejected = true; }
            Check(rejected, "mandatory target cannot be omitted");
            foreach (var invalid in new[] { new[] { source }, new[] { target, target }, state.Players[1 - actor].Board.Take(2).Select(f => f.InstanceId).ToArray() })
            {
                rejected = false;
                try { GameEngine.Apply(state, new PlayFollowerAction(source, EnemyFollowerTargetInstanceIds: invalid)); }
                catch (InvalidOperationException) { rejected = true; }
                Check(rejected, "wrong-side, repeated, or multiple targets must be rejected");
            }

        }
        foreach (var protection in new[] { CardKeyword.Aura, CardKeyword.Stealth })
        foreach (var ward in new[] { false, true })
        {
            var (state, source, _) = Scenario(1, 7, protection | (ward ? CardKeyword.Ward : CardKeyword.None));
            var play = GameEngine.GetLegalActions(state).OfType<PlayFollowerAction>().Single(a => a.CardInstanceId == source);
            Check(play.EnemyFollowerTargetInstanceIds is not { Count: > 0 }, "Aura/Stealth cannot be chosen by Fanfare");
            var rejected = false;
            try { GameEngine.Apply(state, play with { EnemyFollowerTargetInstanceIds = [state.Players[0].Board[0].InstanceId] }); }
            catch (InvalidOperationException) { rejected = true; }
            Check(rejected, "cannot force an Aura/Stealth target");
            var after = GameEngine.Apply(state, play);
            after = GameEngine.Apply(after, GameEngine.GetLegalActions(after).OfType<EvolveAction>().First(a => a.FollowerInstanceId == source));
            Check(after.Players[0].Board.Count == (ward ? 1 : 3), "random Ward destruction must ignore targeting immunity and choose two distinct followers");
        }
        Console.WriteLine("Decisive Crossing target test passed: both players, evolve/super/enhance, 12/12 Anathema, Aura/Stealth, guided and drag targeting.");
    }
}
