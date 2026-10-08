using System.Reflection;
using System.Text.Json;
using Shadowverse.Engine.Cards;
using Shadowverse.Engine.Game;
using Shadowverse.Engine.Models;
using Shadowverse.Engine.Simulation;

namespace Shadowverse.ConsoleApp;

internal static partial class AgentSelfTests
{
    internal static void RunCardReadoutAndWhiteFangTest()
    {
        static void Check(bool condition, string text)
        { if (!condition) throw new InvalidOperationException("Card readout / White Fang: " + text); }
        static void Set(object obj, string name, object value) => obj.GetType().GetProperty(name,
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(obj, value);
        static GameState Scenario(CardDefinition source, bool singleton, CardKeyword keywords = CardKeyword.None)
        {
            var filler = CardCatalog.Get(CardIds.Gladiator) with { Cost = 0, Keywords = keywords };
            var own = Enumerable.Range(0, 39).Select(i => singleton ? filler with { Id = $"singleton-{i}" } : filler).Append(source).ToArray();
            for (ulong seed = 1; seed <= 512; seed++)
            {
                var state = GameEngine.CreateGame(new DeckDefinition("fixture", own),
                    new DeckDefinition("enemy", Enumerable.Repeat(filler, 40)), seed);
                if (!state.Players[0].Hand.Any(c => c.Definition.Id == source.Id)) continue;
                while (state.Phase == GamePhase.Mulligan) state = GameEngine.Apply(state, new MulliganAction([]));
                while (state.ActivePlayer != 1) state = GameEngine.Apply(state, new EndTurnAction());
                for (var i = 0; i < 2; i++) state = GameEngine.Apply(state, GameEngine.GetLegalActions(state).OfType<PlayFollowerAction>().First());
                state = GameEngine.Apply(state, new EndTurnAction());
                Set(state.Players[0], nameof(PlayerState.MaxPlayPoints), 10);
                Set(state.Players[0], nameof(PlayerState.CurrentPlayPoints), 10);
                return state;
            }
            throw new InvalidOperationException("Unable to deal fixture");
        }
        var white = CardCatalog.Get(CardIds.WhiteFangPhosphorescence);
        foreach (var singleton in new[] { false, true })
        foreach (var keywords in new[] { CardKeyword.None, CardKeyword.Aura | CardKeyword.Stealth })
        {
            var state = Scenario(white, singleton, keywords);
            var card = state.Players[0].Hand.Single(c => c.Definition.Id == white.Id);
            var plays = GameEngine.GetLegalActions(state).OfType<PlaySpellAction>().Where(a => a.CardInstanceId == card.InstanceId).ToArray();
            if (singleton)
            {
                Check(plays.Length == 1 && plays[0].Target is null, "singleton must offer one targetless spell");
                var selection = new HumanTargetSelection(GameEngine.ToObservation(state, 0), plays);
                Check(selection.IsComplete, "all-destroy must not ask for a target");
                var fingerprint = GameEngine.StateFingerprint(state);
                var after = GameEngine.Apply(state, plays.Single());
                Check(after.Players[1].Board.Count == 0 && after.Players[1].Graveyard.Count == 2, "destroy all, including Aura/Stealth");
                Check(fingerprint == GameEngine.StateFingerprint(state), "source snapshot stays immutable");
                var rejected = false;
                try { GameEngine.Apply(state, new PlaySpellAction(card.InstanceId, new EnemyFollowerTarget(state.Players[1].Board[0].InstanceId))); }
                catch (InvalidOperationException) { rejected = true; }
                Check(rejected, "all-destroy must reject an invented choice");
            }
            else if (keywords == CardKeyword.None)
            {
                Check(plays.Length == 2 && plays.All(p => p.Target is EnemyFollowerTarget), "duplicates must choose one enemy");
                var after = GameEngine.Apply(state, plays[0]);
                Check(after.Players[1].Board.Count == 1, "chosen enemy must actually be destroyed");
            }
            else
            {
                Check(plays.Length == 0, "single-target spell cannot choose Aura/Stealth");
                var rejected = false;
                try { GameEngine.Apply(state, new PlaySpellAction(card.InstanceId, new EnemyFollowerTarget(state.Players[1].Board[0].InstanceId))); }
                catch (InvalidOperationException) { rejected = true; }
                Check(rejected, "direct single-target Apply must also reject Aura/Stealth");
            }
        }
        foreach (var definition in CardCatalog.All.Where(d => d.Accelerate is not null || d.Crystallize is not null || d.EnhanceEffects is { Count: > 0 }))
        {
            var state = Scenario(definition, true);
            var card = state.Players[0].Hand.Single(c => c.Definition.Id == definition.Id);
            foreach (var pp in Enumerable.Range(0, 11))
            {
                Set(state.Players[0], nameof(PlayerState.CurrentPlayPoints), pp);
                var readout = GameEngine.GetHandCardReadout(state.Players[0], card);
                var plays = GameEngine.GetLegalActions(state).Where(a => a switch
                {
                    PlayFollowerAction a1 => a1.CardInstanceId == card.InstanceId,
                    PlayAmuletAction a1 => a1.CardInstanceId == card.InstanceId,
                    PlaySpellAction a1 => a1.CardInstanceId == card.InstanceId,
                    PlayAccelerateAction a1 => a1.CardInstanceId == card.InstanceId,
                    PlayCrystallizeAction a1 => a1.CardInstanceId == card.InstanceId, _ => false
                }).ToArray();
                foreach (var action in plays)
                {
                    var after = GameEngine.Apply(state, action);
                    var enhance = definition.EnhanceEffects?.Where(e => e.Cost <= pp).OrderByDescending(e => e.Cost).FirstOrDefault();
                    var refund = (enhance?.Effects ?? []).Concat(definition.FanfareEffects ?? [])
                        .Where(e => e.Kind == CardEffectKind.RestoreOwnPlayPoints).Sum(e => e.Amount);
                    Check(after.Players[0].CurrentPlayPoints == Math.Min(10, pp - readout.Cost + refund), $"{definition.Name} PP{pp}: displayed cost must equal actual payment");
                    Check((action is PlayAccelerateAction) == (readout.Form == HandPlayForm.Accelerate), "Accelerate form");
                    Check((action is PlayCrystallizeAction) == (readout.Form == HandPlayForm.Crystallize), "Crystallize form");
                }
            }
        }
        var oathCards = CardCatalog.All.Where(d => d.OathEffects is { Count: > 0 } || d.SuperOathEffects is { Count: > 0 } ||
            d.Effect?.Kind == CardEffectKind.DealDamageToAllEnemyFollowersAndLeaderWithSuperOathUpgrade).ToArray();
        foreach (var definition in oathCards)
        {
            var state = Scenario(definition, true);
            var player = state.Players[0]; var card = player.Hand.Single(c => c.Definition.Id == definition.Id);
            Set(player, nameof(PlayerState.OwnTurnNumber), 7);
            Set(player, nameof(PlayerState.OwnFollowersEvolvedThisBattle), 8);
            Set(card, "HandEntryEvolvedCountInternal", 6);
            var readout = GameEngine.GetHandCardReadout(player, card);
            Check(readout.OathGauge == 9 && !readout.OathReady && !readout.SuperOathReady, "count only evolutions while this card was in hand");
            Set(player, nameof(PlayerState.OwnTurnNumber), 8);
            readout = GameEngine.GetHandCardReadout(player, card);
            Check(readout.OathGauge == 10 && (readout.OathThreshold is null || readout.OathReady), "Oath threshold 10");
            Set(player, nameof(PlayerState.OwnTurnNumber), 13);
            readout = GameEngine.GetHandCardReadout(player, card);
            Check(readout.OathGauge == 15 && (readout.SuperOathThreshold is null || readout.SuperOathReady), "Super Oath threshold 15");
            Set(card, nameof(CardInstance.CostReduction), 2);
            Set(card, nameof(CardInstance.TemporaryCostReduction), 1);
            Check(GameEngine.GetHandCardReadout(player, card).Cost == Math.Max(0, definition.Cost - 3), "permanent and temporary cost reduction");
            using var json = JsonDocument.Parse(MachineReplay.Serialize(1, "fixture", "真人", "fixture", "AI", state, -1, []));
            var hand = json.RootElement.GetProperty("init")[0][12].EnumerateArray().Single(h => h[0].GetInt32() == card.InstanceId);
            Check(hand[1].GetInt32() == 2 && hand[2].GetInt32() == 1 && hand[6].GetInt32() == 15, "export retains hand costs and per-card Oath gauge");
        }
        System.Console.WriteLine("Card readout / White Fang checks passed: fees, modes, per-card Oath and targetless all-destroy.");
    }
}
