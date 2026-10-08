using Shadowverse.Engine.Cards;
using Shadowverse.Engine.Game;
using Shadowverse.Engine.Models;

namespace Shadowverse.ConsoleApp;

internal static partial class AgentSelfTests
{
    internal static void RunHumanTargetSelectionTest()
    {
        static void Check(bool ok, string message)
        {
            if (!ok) throw new InvalidOperationException("Guided targets: " + message);
        }
        var card = CardCatalog.Get(CardIds.Gladiator);
        var spell = CardCatalog.All.First(c => c.Type == CardType.Spell);
        var amulet = CardCatalog.Get(CardIds.InvadedWorld);
        VisibleFollower Follower(int id, int attack = 2) => new(id, card.Id, card.Name, attack, 5, 5,
            CardKeyword.None, EvolutionState.Unevolved, false);
        var self = new PlayerView(20, 20, 10, 10, 5, 2, 2, false, false, false, 6, 30, 0,
            [Follower(10), Follower(11)], [new(12, amulet.Id, amulet.Name, null)]);
        var enemy = self with { Board = [Follower(20), Follower(21, 4), Follower(22)], Amulets = [new(23, amulet.Id, amulet.Name, null)] };
        var observation = new GameObservation(0, 0, 9, GamePhase.Main, self, enemy,
            [new(1, card), new(2, spell), new(3, card), new(4, card), new(5, spell), new(6, card)]);
        var traversed = 0;
        void ReachAll(IReadOnlyList<GameAction> actions)
        {
            foreach (var wanted in actions)
            {
                var selection = new HumanTargetSelection(observation, actions);
                while (!selection.IsComplete)
                {
                    foreach (var target in HumanTargetSelection.Targets(observation, wanted, selection.CurrentRole!.Value))
                    {
                        Check(selection.Available.Contains(target), "required target must glow");
                        Check(selection.Toggle(target), "required target must be selectable");
                    }
                    Check(selection.CanAdvance && selection.Advance(), "exact legal selection must advance");
                }
                Check(selection.Candidates.Any(a => ReferenceEquals(a, wanted)), "return the exact original engine action");
                traversed++;
            }
        }
        IReadOnlyList<GameAction> damage = [new PlaySpellAction(2, new EnemyFollowerTarget(20)),
            new PlaySpellAction(2, new EnemyFollowerTarget(21)), new PlaySpellAction(2, new EnemyLeaderTarget())];
        ReachAll(damage);
        var single = new HumanTargetSelection(observation, damage);
        Check(!single.CanAdvance && !single.Toggle(new(HumanTargetZone.OwnBoard, 10)), "invalid click cannot submit or select");
        Check(single.Toggle(new(HumanTargetZone.EnemyBoard, 20)) && single.Toggle(new(HumanTargetZone.EnemyBoard, 21)) &&
            single.Selected.Single().InstanceId == 21 && single.Available.Count == 3, "single target can be changed before confirming");
        Check(single.Toggle(new(HumanTargetZone.EnemyBoard, 21)) && !single.CanAdvance, "click again deselects");
        IReadOnlyList<GameAction> transform = [new PlaySpellAction(2, new FollowerTarget(10)), new PlaySpellAction(2, new FollowerTarget(20)),
            new PlaySpellAction(2, new AmuletTarget(12)), new PlaySpellAction(2, new AmuletTarget(23))];
        ReachAll(transform);
        var labels = transform.Select(a => HumanActionText.Describe(observation, a)).ToArray();
        Check(labels.Distinct().Count() == 4 && labels.Any(t => t.Contains("己方护符")) && labels.Any(t => t.Contains("敌方护符")), "all board-target sides and types must be named");
        Check(HumanActionResolver.FromHand(transform, 2, 12).Single() == transform[2], "drag can point at an allied amulet");
        var multi = new List<GameAction>();
        foreach (var pair in new[] { new[] { 20, 21 }, new[] { 20, 22 }, new[] { 21, 22 } })
            foreach (var handId in new[] { 3, 4 })
                multi.Add(new PlayFollowerAction(1, handId, pair, OwnHandCardTargetInstanceIds: [5, 6]));
        ReachAll(multi);
        var two = new HumanTargetSelection(observation, multi);
        Check(two.Toggle(new(HumanTargetZone.EnemyBoard, 20)) && !two.CanAdvance, "two targets cannot confirm early");
        Check(two.Toggle(new(HumanTargetZone.EnemyBoard, 21)) && two.CanAdvance && !two.Toggle(new(HumanTargetZone.EnemyBoard, 22)), "cannot add an illegal third target");
        Check(two.Toggle(new(HumanTargetZone.EnemyBoard, 20)) && two.Toggle(new(HumanTargetZone.EnemyBoard, 22)), "multi targets can be revised");
        Check(!two.CanGoBack && two.StageIndex == 0 && two.StageCount == 3 && !two.Back(), "first stage cannot go back");
        Check(two.Advance() && two.StageIndex == 1 && two.CanGoBack && two.Completed.Count == 2, "advancing records history");
        Check(two.Toggle(new(HumanTargetZone.OwnHand, 3)) && two.Back() && two.StageIndex == 0 && two.Completed.Count == 0 &&
            two.Selected.Select(t => t.InstanceId).Order().SequenceEqual(new int?[] { 21, 22 }) && two.Candidates.Count == multi.Count,
            "back restores the original legal pool and previous selected targets");
        Check(HumanActionResolver.DropHand(observation, damage, 2, new(HumanTargetZone.EnemyBoard, 22)).Count == 0,
            "invalid enemy drop must not fall back to a different target");
        Check(HumanActionResolver.DropHand(observation, damage, 2, new(HumanTargetZone.EnemyLeader)).Single() == damage[2], "legal leader drop remains exact");
        Check(HumanActionResolver.DropHand(observation, damage, 2, null).Count == 3, "own-board drop retains all guided targets");
        Check(HumanActionText.Describe(observation, multi[0]).Contains("放回牌组") && HumanActionText.Describe(observation, multi[0]).Contains("弃掉"), "returning and discarding are separate operations");
        ReachAll([new EvolveAction(10, OwnHandCardTargetInstanceIds: [3, 4], EnemyFollowerTargetInstanceId: 20),
            new EvolveAction(10, OwnHandCardTargetInstanceIds: [3, 5], EnemyFollowerTargetInstanceId: 21)]);
        ReachAll([new SuperEvolveAction(10, 11, OwnHandCardTargetInstanceIds: [3], EnemyFollowerTargetInstanceId: 20),
            new SuperEvolveAction(10, 11, OwnHandCardTargetInstanceIds: [4], EnemyFollowerTargetInstanceId: 21)]);
        // An empty target group remains reachable when the engine provides it (e.g. no enemy follower).
        ReachAll([new PlayFollowerAction(1, EnemyFollowerTargetInstanceIds: []), new PlayFollowerAction(1, EnemyFollowerTargetInstanceIds: [20])]);
        Check(HumanTargetText.Name(observation, new(HumanTargetZone.EnemyBoard, 21)).Contains("第2个") &&
            HumanTargetText.Name(observation, new(HumanTargetZone.EnemyBoard, 21)).Contains("4/5"), "same-name followers need a copy number and current stats");
        Check(HumanTargetText.Name(observation, new(HumanTargetZone.OwnHand, 4)).Contains("第4张"), "hand target must match visible hand order");
        var modeSpell = CardCatalog.Get(CardIds.Parkour);
        var modeObservation = observation with { OwnHand = [new(2, modeSpell)] };
        var modeLabels = new[] { new PlaySpellAction(2, null, ModeChoiceIndex: 0), new PlaySpellAction(2, null, ModeChoiceIndex: 1) }
            .Select(a => HumanActionText.Describe(modeObservation, a)).ToArray();
        Check(modeLabels.Distinct().Count() == 2 && modeLabels.All(s => s.Contains("模式")), "spell modes must remain readable and distinct");
        Console.WriteLine($"Guided target selection test passed ({traversed} actions; followers, spells, both-side amulets, leaders, return/discard, evolve and multi-stage selection).");
    }
}
