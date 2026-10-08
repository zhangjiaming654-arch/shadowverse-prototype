using Shadowverse.Engine.Cards;
using Shadowverse.Engine.Models;

namespace Shadowverse.Engine.Game;

public static class HumanTargetText
{
    public static string Name(GameObservation observation, HumanTarget target)
    {
        if (target.Zone == HumanTargetZone.EnemyLeader) return "敌方主战者";
        if (target.Zone == HumanTargetZone.OwnHand)
        {
            var hand = observation.OwnHand.ToList();
            var index = hand.FindIndex(c => c.InstanceId == target.InstanceId);
            return index < 0 ? "已离开手牌的卡牌" : $"手牌第{index + 1}张 · {hand[index].Definition.Name}";
        }
        var side = target.Zone == HumanTargetZone.OwnBoard ? observation.Self : observation.Opponent;
        var prefix = target.Zone == HumanTargetZone.OwnBoard ? "己方" : "敌方";
        if (side.Board.FirstOrDefault(f => f.InstanceId == target.InstanceId) is { } follower)
        {
            var copies = side.Board.Where(f => f.CardId == follower.CardId).ToList();
            var copy = copies.Count > 1 ? $"（第{copies.FindIndex(f => f.InstanceId == target.InstanceId) + 1}个）" : "";
            return $"{prefix} · {follower.Name}{copy} [{follower.Attack}/{follower.CurrentDefense}]";
        }
        if (side.Amulets?.FirstOrDefault(a => a.InstanceId == target.InstanceId) is { } amulet)
        {
            var copies = side.Amulets.Where(a => a.CardId == amulet.CardId).ToList();
            var copy = copies.Count > 1 ? $"（第{copies.FindIndex(a => a.InstanceId == target.InstanceId) + 1}个）" : "";
            return $"{prefix}护符 · {amulet.Name}{copy}";
        }
        return $"{prefix}已离场的目标";
    }

    public static CardDefinition? Source(GameObservation observation, GameAction action)
    {
        if (HumanActionResolver.PlayedCardOf(action) is int handId)
            return observation.OwnHand.FirstOrDefault(c => c.InstanceId == handId)?.Definition;
        var id = action switch { EvolveAction a => a.FollowerInstanceId, SuperEvolveAction a => a.FollowerInstanceId, _ => -1 };
        var follower = observation.Self.Board.FirstOrDefault(f => f.InstanceId == id);
        return follower is null ? null : CardCatalog.Get(follower.CardId);
    }

    public static string Title(GameObservation observation, GameAction action) =>
        (action switch { EvolveAction => "进化", SuperEvolveAction => "超进化", PlaySpellAction => "使用法术",
            PlayAmuletAction => "使用护符", PlayAccelerateAction => "使用激奏", PlayCrystallizeAction => "使用结晶", _ => "打出随从" }) +
        " · " + (Source(observation, action)?.Name ?? "卡牌");

    public static string Ability(GameObservation observation, GameAction action)
    {
        var source = Source(observation, action);
        var mode = HumanActionResolver.ModeNameOf(observation, action);
        var printed = source?.EffectText;
        return (mode is null ? "" : "所选模式：" + mode + "\n\n") +
            (string.IsNullOrWhiteSpace(printed) ? "请查看卡牌能力。" : printed.Replace("；", "；\n"));
    }

    public static string Instruction(GameObservation observation, HumanTargetSelection selection)
    {
        if (selection.IsComplete) return selection.StageCount == 0 ? "查看卡牌能力，确认后使用。\n取消或按 Esc 可返回手牌。" : "目标已选好，确认后发动。";
        var counts = string.Join(" / ", selection.RequiredCounts);
        var available = selection.Available;
        var enemyFollowers = available.All(t => t.Zone == HumanTargetZone.EnemyBoard && observation.Opponent.Board.Any(f => f.InstanceId == t.InstanceId));
        var ownFollowers = available.All(t => t.Zone == HumanTargetZone.OwnBoard && observation.Self.Board.Any(f => f.InstanceId == t.InstanceId));
        var followerOrLeader = available.All(t => t.Zone == HumanTargetZone.EnemyLeader ||
            t.Zone == HumanTargetZone.EnemyBoard && observation.Opponent.Board.Any(f => f.InstanceId == t.InstanceId));
        var subject = selection.CurrentRole switch
        {
            HumanTargetRole.ReturnHand => "要放回牌组的手牌",
            HumanTargetRole.DiscardHand => "要弃掉的手牌",
            HumanTargetRole.EvolveAlly => "要一同进化的己方随从",
            _ when enemyFollowers => "敌方随从",
            _ when ownFollowers => "己方随从",
            _ when followerOrLeader => "目标（敌方随从或主战者）",
            _ => "战场卡牌（随从或护符）"
        };
        var unit = selection.CurrentRole is HumanTargetRole.ReturnHand or HumanTargetRole.DiscardHand || subject.StartsWith("战场卡牌") ? "张" : "个";
        return $"请选择 {counts} {unit}{subject}\n已选 {selection.Selected.Count} {unit} · 再点已选对象可取消";
    }
}
