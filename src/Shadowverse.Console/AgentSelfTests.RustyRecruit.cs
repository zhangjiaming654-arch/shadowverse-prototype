using Shadowverse.Engine.Cards;
using Shadowverse.Engine.Game;
using Shadowverse.Engine.Models;

namespace Shadowverse.ConsoleApp;

internal static partial class AgentSelfTests
{
    internal static void RunRustyRecruitFanfareTest()
    {
        static void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("Rusty Recruit Fanfare: " + message);
        }

        static (GameState State, int CardId) Scenario(CardDefinition source, bool duplicates = false,
            int enemies = 2, int defense = 10, CardKeyword protectedBy = CardKeyword.None)
        {
            var filler = CardCatalog.Get(CardIds.Gladiator) with { Cost = 0, Attack = 0, Defense = 10 };
            var own = Enumerable.Range(0, 39).Select(i => filler with { Id = "rusty-test-" + (duplicates ? 0 : i) }).ToList();
            own.Add(source);
            var enemy = filler with { Id = "rusty-test-enemy", Defense = defense, Keywords = protectedBy };
            for (ulong seed = 1; seed <= 512; seed++)
            {
                var state = GameEngine.CreateGame(new DeckDefinition("rusty-test", own),
                    new DeckDefinition("enemy-test", Enumerable.Repeat(enemy, 40)), seed);
                if (!state.Players[0].Hand.Any(card => card.Definition.Id == source.Id)) continue;
                while (state.Phase == GamePhase.Mulligan) state = GameEngine.Apply(state, new MulliganAction([]));
                if (state.ActivePlayer == 0) state = GameEngine.Apply(state, new EndTurnAction());
                for (var i = 0; i < enemies; i++)
                    state = GameEngine.Apply(state, GameEngine.GetLegalActions(state).OfType<PlayFollowerAction>().First());
                state = GameEngine.Apply(state, new EndTurnAction());
                while (state.Players[0].CurrentPlayPoints < source.Cost)
                {
                    state = GameEngine.Apply(state, new EndTurnAction());
                    state = GameEngine.Apply(state, new EndTurnAction());
                }
                return (state, state.Players[0].Hand.Single(card => card.Definition.Id == source.Id).InstanceId);
            }
            throw new InvalidOperationException("Could not deal the Fanfare test card.");
        }

        var recruit = CardCatalog.Get(CardIds.RustyRecruit);
        var (state, cardId) = Scenario(recruit);
        var legal = GameEngine.GetLegalActions(state);
        var plays = legal.OfType<PlayFollowerAction>().Where(action => action.CardInstanceId == cardId).ToArray();
        Check(plays.Length == 2 && plays.All(action => action.EnemyFollowerTargetInstanceIds is { Count: 1 }),
            "a unique deck must offer one targeted play per enemy, without a targetless bypass");
        var chosen = state.Players[1].Board[1].InstanceId;
        var gestures = HumanActionResolver.FromHand(legal, cardId, chosen);
        Check(gestures.Count == 1 && gestures[0] is PlayFollowerAction selected
            && selected.EnemyFollowerTargetInstanceIds!.Single() == chosen, "dragging to an enemy must select that enemy");
        Check(HumanActionResolver.FromHand(legal, cardId).Count == 2, "dragging to own board must retain the target choices");
        var fingerprint = GameEngine.StateFingerprint(state);
        var after = GameEngine.Apply(state, gestures[0]);
        Check(after.Players[1].Board.Single(follower => follower.InstanceId == chosen).CurrentDefense == 5,
            "selected enemy must receive 5 damage");
        Check(after.Players[1].Board.Single(follower => follower.InstanceId != chosen).CurrentDefense == 10,
            "unselected enemy must not receive damage");
        Check(after.Players[0].Board.Single().HasRush, "the played recruit must retain Rush");
        Check(GameEngine.StateFingerprint(state) == fingerprint, "Apply must not mutate its input snapshot");
        var rejected = false;
        try { GameEngine.Apply(state, new PlayFollowerAction(cardId)); }
        catch (InvalidOperationException) { rejected = true; }
        Check(rejected, "a required target cannot be silently omitted");
        rejected = false;
        try { GameEngine.Apply(state, new PlayFollowerAction(cardId, EnemyFollowerTargetInstanceIds: state.Players[1].Board.Select(f => f.InstanceId).ToArray())); }
        catch (InvalidOperationException) { rejected = true; }
        Check(rejected, "a single-target Fanfare cannot target both enemies");

        var (lethalState, lethalId) = Scenario(recruit, defense: 4);
        var lethal = GameEngine.GetLegalActions(lethalState).OfType<PlayFollowerAction>().First(a => a.CardInstanceId == lethalId);
        var lethalAfter = GameEngine.Apply(lethalState, lethal);
        Check(lethalAfter.Players[1].Board.Count == 1 && lethalAfter.Players[1].Graveyard.Count == 1,
            "lethal Fanfare damage must destroy its target and put it in the graveyard");

        foreach (var keywords in new[] { CardKeyword.Aura, CardKeyword.Stealth })
        {
            var (blocked, blockedId) = Scenario(recruit, protectedBy: keywords);
            var play = GameEngine.GetLegalActions(blocked).OfType<PlayFollowerAction>().Single(a => a.CardInstanceId == blockedId);
            Check(play.EnemyFollowerTargetInstanceIds is not { Count: > 0 }, "Aura/Stealth cannot be selected");
            var blockedAfter = GameEngine.Apply(blocked, play);
            Check(blockedAfter.Players[1].Board.All(f => f.CurrentDefense == 10) && blockedAfter.Players[0].Board.Count == 1,
                "no selectable target must still allow the recruit to enter play without damage");
            rejected = false;
            try { GameEngine.Apply(blocked, play with { EnemyFollowerTargetInstanceIds = [blocked.Players[1].Board[0].InstanceId] }); }
            catch (InvalidOperationException) { rejected = true; }
            Check(rejected, "protected targets must also be rejected by direct Apply");
        }

        var (duplicateState, duplicateId) = Scenario(recruit, duplicates: true);
        var duplicatePlay = GameEngine.GetLegalActions(duplicateState).OfType<PlayFollowerAction>().Single(a => a.CardInstanceId == duplicateId);
        Check(duplicatePlay.EnemyFollowerTargetInstanceIds is null, "a duplicate deck must not ask for a target");
        var duplicateAfter = GameEngine.Apply(duplicateState, duplicatePlay);
        Check(duplicateAfter.Players[1].Board.All(f => f.CurrentDefense == 10), "duplicate-deck condition must suppress damage");
        var (emptyState, emptyId) = Scenario(recruit, enemies: 0);
        var emptyPlay = GameEngine.GetLegalActions(emptyState).OfType<PlayFollowerAction>().Single(a => a.CardInstanceId == emptyId);
        Check(GameEngine.Apply(emptyState, emptyPlay).Players[0].Board.Count == 1, "empty enemy board must not prevent playing");

        // 锻磨保镖 uses the same conditional target path; retain its own damage amount.
        var (bodyguardState, bodyguardId) = Scenario(CardCatalog.Get(CardIds.TemperedBodyguard));
        var bodyguardPlay = GameEngine.GetLegalActions(bodyguardState).OfType<PlayFollowerAction>().First(a => a.CardInstanceId == bodyguardId);
        var bodyguardAfter = GameEngine.Apply(bodyguardState, bodyguardPlay);
        var bodyguardTarget = bodyguardPlay.EnemyFollowerTargetInstanceIds!.Single();
        Check(bodyguardAfter.Players[1].Board.Single(f => f.InstanceId == bodyguardTarget).CurrentDefense == 6,
            "the shared bodyguard target path must resolve 4 damage");
        Console.WriteLine("Rusty Recruit targeted Fanfare regression test passed.");
    }
}
