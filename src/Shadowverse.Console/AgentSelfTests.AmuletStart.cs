using System.Text.Json;
using Shadowverse.Engine.Cards;
using Shadowverse.Engine.Game;
using Shadowverse.Engine.Models;
using Shadowverse.Engine.Simulation;

namespace Shadowverse.ConsoleApp;

internal static partial class AgentSelfTests
{
    internal static void RunAmuletStartInteractionTest()
    {
        static void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("Amulet Start interaction: " + message);
        }

        static GameState Scenario(CardDefinition? world = null, bool emptyHand = false, bool twoAmulets = false)
        {
            world ??= CardCatalog.Get(CardIds.InvadedWorld);
            var filler = emptyHand
                ? CardCatalog.Get(CardIds.Gladiator) with { Id = "start-empty-hand", Type = CardType.Spell, Cost = 0,
                    Attack = 0, Defense = 0, Keywords = CardKeyword.None,
                    SpellEffects = [new CardEffect(CardEffectKind.RestoreOwnLeaderHealth, 1)] }
                : CardCatalog.Get(CardIds.Gladiator) with { Cost = 0 };
            for (ulong seed = 1; seed <= 256; seed++)
            {
                var state = GameEngine.CreateGame(new DeckDefinition("start-ui",
                    Enumerable.Repeat(world, emptyHand ? 1 : 8).Concat(Enumerable.Repeat(filler, emptyHand ? 39 : 32))),
                    new DeckDefinition("start-opponent", Enumerable.Repeat(CardCatalog.Get(CardIds.AncientCreation), 40)), seed);
                if (state.Players[0].Hand.Count(c => c.Definition.Id == world.Id) < (twoAmulets ? 2 : 1)) continue;
                while (state.Phase == GamePhase.Mulligan) state = GameEngine.Apply(state, new MulliganAction([]));
                while (state.ActivePlayer != 0 || state.Players[0].CurrentPlayPoints < world.Cost * (twoAmulets ? 2 : 1))
                    state = GameEngine.Apply(state, new EndTurnAction());
                for (var i = 0; i < (twoAmulets ? 2 : 1); i++)
                {
                    var card = state.Players[0].Hand.First(c => c.Definition.Id == world.Id);
                    state = GameEngine.Apply(state, new PlayAmuletAction(card.InstanceId));
                }
                if (emptyHand)
                    while (GameEngine.GetLegalActions(state).OfType<PlaySpellAction>().FirstOrDefault() is { } spell)
                        state = GameEngine.Apply(state, spell);
                return state;
            }
            throw new InvalidOperationException("Could not deal the Start interaction fixture.");
        }

        var state = Scenario();
        var amuletId = state.Players[0].Amulets.Single().InstanceId;
        var legal = GameEngine.GetLegalActions(state);
        var starts = legal.OfType<UseStartAbilityAction>().ToArray();
        Check(starts.Length == state.Players[0].Hand.Count && starts.Distinct().Count() == starts.Length,
            "generate exactly one action per hand target, without repeated copies");
        var observation = GameEngine.ToObservation(state, 0);
        var reachable = HumanActionResolver.StartAbility(legal, amuletId);
        Check(reachable.SequenceEqual(starts), "clicking the amulet must expose every legal target");
        Check(HumanActionResolver.StartAbility(legal, -1).Count == 0, "another amulet cannot use these actions");
        Check(HumanActionResolver.StartAbilityBlockReason(observation, legal, amuletId) is null, "legal Start must be enabled");
        var labels = reachable.Select(action => HumanActionText.Describe(observation, action)).ToArray();
        Check(labels.All(label => label.Contains("启动 被侵略的世界") && label.Contains("选择手牌")), "readable ability and hand-target labels");
        Check(labels.Distinct().Count() == labels.Length, "same-name hand copies must remain distinguishable");
        var hiddenLabel = HumanActionText.DescribeOpponentAction(GameEngine.ToObservation(state, 1), starts[0]);
        Check(hiddenLabel.Contains("启动 被侵略的世界") && hiddenLabel.Contains("未公开") && !hiddenLabel.Contains("剑斗士"),
            "opponent report must name the public amulet without revealing the hand target");

        var input = GameEngine.StateFingerprint(state);
        var deckIds = state.Players[1].Deck.Select(c => c.InstanceId).ToArray();
        var after = GameEngine.Apply(state, starts[0]);
        Check(GameEngine.StateFingerprint(state) == input, "activation must not mutate its source snapshot");
        Check(after.Players[1].Deck.Select(c => c.InstanceId).SequenceEqual(deckIds), "copying must not remove the opponent's deck card");
        Check(after.Players[0].Hand.Single(c => c.InstanceId == starts[0].HandCardTargetInstanceId).Definition.Id == CardIds.AncientCreation,
            "selected hand card must transform into the opponent's card");
        Check(after.Players[0].Hand.Count == state.Players[0].Hand.Count, "transformation must keep the hand count");
        var afterLegal = GameEngine.GetLegalActions(after);
        Check(HumanActionResolver.StartAbility(afterLegal, amuletId).Count == 0, "cannot activate twice in one turn");
        Check(HumanActionResolver.StartAbilityBlockReason(GameEngine.ToObservation(after, 0), afterLegal, amuletId) == "本回合已启动",
            "used ability must give a visible reason");
        var rejected = false;
        try { GameEngine.Apply(after, starts[0]); } catch (InvalidOperationException) { rejected = true; }
        Check(rejected, "direct Apply must also reject a repeated Start");
        var nextTurn = GameEngine.Apply(GameEngine.Apply(after, new EndTurnAction()), new EndTurnAction());
        Check(HumanActionResolver.StartAbility(GameEngine.GetLegalActions(nextTurn), amuletId).Count > 0,
            "next own turn must restore Start");

        var replay = JsonSerializer.SerializeToElement(MachineReplay.Build(1, "own", "human", "other", "rule", state, 0,
            [new MatchStep(state, 0, starts[0], after)]));
        var activation = replay.GetProperty("e")[0];
        Check(activation[2].GetString() == "Z" && activation[3].GetInt32() == amuletId && activation[4].GetInt32() == starts[0].HandCardTargetInstanceId,
            "machine replay must retain the source amulet and target");
        Check(activation[6].GetString() == CardIds.AncientCreation, "machine replay must retain the transformed card");

        var empty = Scenario(emptyHand: true);
        Check(empty.Players[0].Hand.Count == 0 && !GameEngine.GetLegalActions(empty).OfType<UseStartAbilityAction>().Any(),
            "targeted Start must be unavailable without a hand card");
        Check(HumanActionResolver.StartAbilityBlockReason(GameEngine.ToObservation(empty, 0), GameEngine.GetLegalActions(empty),
            empty.Players[0].Amulets.Single().InstanceId) == "没有可选择的手牌", "empty hand must have a readable reason");
        var targetless = Scenario(CardCatalog.Get(CardIds.InvadedWorld) with { StartAbility = new StartAbilityDefinition(0, []) }, emptyHand: true);
        var noTarget = GameEngine.GetLegalActions(targetless).OfType<UseStartAbilityAction>().Single();
        var noTargetAfter = GameEngine.Apply(targetless, noTarget);
        Check(GameEngine.StateFingerprint(targetless) != GameEngine.StateFingerprint(noTargetAfter),
            "used Start status must be part of the state fingerprint even when the effect changes nothing else");

        var pair = Scenario(CardCatalog.Get(CardIds.InvadedWorld) with { Cost = 0 }, twoAmulets: true);
        var pairStarts = GameEngine.GetLegalActions(pair).OfType<UseStartAbilityAction>().ToArray();
        Check(pairStarts.Length == pair.Players[0].Hand.Count * 2, "two amulets each expose their own targets");
        var used = pairStarts[0];
        var pairAfter = GameEngine.Apply(pair, used);
        Check(GameEngine.GetLegalActions(pairAfter).OfType<UseStartAbilityAction>().All(a => a.AmuletInstanceId != used.AmuletInstanceId)
            && GameEngine.GetLegalActions(pairAfter).OfType<UseStartAbilityAction>().Any(), "using one amulet must not disable the other");

        var paid = Scenario(CardCatalog.Get(CardIds.InvadedWorld) with
            { StartAbility = new StartAbilityDefinition(2, CardCatalog.Get(CardIds.InvadedWorld).StartAbility!.Effects) });
        Check(!GameEngine.GetLegalActions(paid).OfType<UseStartAbilityAction>().Any(), "insufficient PP must block paid Start");
        rejected = false;
        try { GameEngine.Apply(paid, new UseStartAbilityAction(paid.Players[0].Amulets.Single().InstanceId, paid.Players[0].Hand[0].InstanceId)); }
        catch (InvalidOperationException) { rejected = true; }
        Check(rejected, "Apply must reject insufficient PP");
        paid = GameEngine.Apply(GameEngine.Apply(paid, new EndTurnAction()), new EndTurnAction());
        var paidAction = GameEngine.GetLegalActions(paid).OfType<UseStartAbilityAction>().First();
        Check(GameEngine.Apply(paid, paidAction).Players[0].CurrentPlayPoints == paid.Players[0].CurrentPlayPoints - 2, "pay the Start fee exactly once");
        Console.WriteLine("Amulet Start interaction regression test passed (targets, labels, once per turn, PP, empty hand, copies, replay).");
    }
}
