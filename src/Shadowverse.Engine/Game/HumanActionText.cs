using Shadowverse.Engine.Cards;

namespace Shadowverse.Engine.Game;

/// <summary>
/// 把一个 <see cref="GameAction"/> 翻译成人话。
/// <para>
/// 放在引擎层（而不是界面里）有一个很具体的理由：<b>"同一组候选动作必须能被文字区分开"</b>
/// 这条性质只有在这里才能被自检覆盖。界面自己写一份翻译，自检就够不着它 ——
/// 实际后果就是【模式】卡牌弹出几条一模一样的选项，用户看着几个相同的按钮无从选起。
/// 这是实测踩过的，所以翻译逻辑和被测对象放在同一层。
/// </para>
/// <para>
/// 认不出来的动作退回 <c>ToString()</c>，<b>绝不隐藏任何选项</b>。
/// </para>
/// </summary>
public static class HumanActionText
{
    public static string Describe(GameObservation observation, GameAction action)
    {
        // 手牌/场上都可能有**多张同名卡**（卡组本来就放 3 张，场上也可能站两只一样的）。
        // 只渲染卡名的话，"弃掉 A" 和 "弃掉 A" 会变成一模一样的两条候选，
        // 界面上根本分不出该点哪个 —— 两只同名敌方随从更是必须分清，
        // 因为它们的身材和 buff 可能完全不同。所以同名时补一个副本序号。
        // （这是自检抓出来的：满面笑容的烹饪·琪米卡 的两条候选文字完全相同。）
        string CardName(int instanceId)
        {
            var card = observation.OwnHand.FirstOrDefault(hand => hand.InstanceId == instanceId);
            if (card is null)
            {
                return $"#{instanceId}";
            }

            return Numbered(
                observation.OwnHand.Where(hand => hand.Definition.Id == card.Definition.Id).ToList(),
                entry => entry.InstanceId == instanceId,
                card.Definition.Name);
        }

        string FollowerName(int instanceId) => observation.Self.Board
            .FirstOrDefault(follower => follower.InstanceId == instanceId) is { } follower
            ? Numbered(
                observation.Self.Board.Where(other => other.CardId == follower.CardId).ToList(),
                entry => entry.InstanceId == instanceId,
                CardCatalog.Get(follower.CardId).Name)
            : $"#{instanceId}";

        string OpponentFollowerName(int instanceId) => observation.Opponent.Board
            .FirstOrDefault(follower => follower.InstanceId == instanceId) is { } follower
            ? Numbered(
                observation.Opponent.Board.Where(other => other.CardId == follower.CardId).ToList(),
                entry => entry.InstanceId == instanceId,
                CardCatalog.Get(follower.CardId).Name)
            : $"#{instanceId}";

        return action switch
        {
            MulliganAction mulligan => mulligan.ReplaceInstanceIds.Count == 0
                ? "不换牌"
                : "换掉：" + string.Join("、", mulligan.ReplaceInstanceIds.Select(CardName)),
            PlayFollowerAction play =>
                $"打出随从 {CardName(play.CardInstanceId)}" + ModeSuffix(play) + DetailSuffix(play),
            PlayAmuletAction amulet => $"打出护符 {CardName(amulet.CardInstanceId)}",
            PlaySpellAction spell => $"打出法术 {CardName(spell.CardInstanceId)}" + DetailSuffix(spell),
            PlayCrystallizeAction crystallize => $"结晶 {CardName(crystallize.CardInstanceId)}",
            PlayAccelerateAction accelerate => $"加速 {CardName(accelerate.CardInstanceId)}",
            EvolveAction evolve =>
                $"进化 {FollowerName(evolve.FollowerInstanceId)}" + ModeSuffix(evolve) + DetailSuffix(evolve),
            SuperEvolveAction superEvolve =>
                $"超进化 {FollowerName(superEvolve.FollowerInstanceId)}" + ModeSuffix(superEvolve) + DetailSuffix(superEvolve),
            AttackLeaderAction attackLeader => $"{FollowerName(attackLeader.AttackerInstanceId)} 攻击对方主战者",
            AttackFollowerAction attackFollower =>
                $"{FollowerName(attackFollower.AttackerInstanceId)} 攻击 {OpponentFollowerName(attackFollower.DefenderInstanceId)}",
            UseExtraPlayPointAction => "使用额外 PP",
            EndTurnAction => "结束回合",
            _ => action.ToString() ?? "未知动作"
        };

        // 【模式】卡牌必须把模式名带出来，否则菜单里会出现几条一模一样的选项。
        // 模式名本身就是完整的能力说明（"对对手所有随从造成5点伤害并回复1点进化点"）。
        string ModeSuffix(GameAction candidate) =>
            HumanActionResolver.ModeNameOf(observation, candidate) is { } modeName
                ? $" ｜ 模式：{modeName}"
                : string.Empty;

        // 一个动作除了"打哪张牌""选哪个模式"之外，还可能同时带目标、额外代价等好几种附加信息。
        // **必须把每一种都累加出来，不能用 switch 只挑一个** ——
        // 用 switch 的话，同时带"弃一张手牌"和"弃一组手牌"的动作只会渲染出前者，
        // 后者被藏住，于是两条本来不同的候选文字一模一样，用户在界面上没法选。
        // （这正是自检抓出来的：满面笑容的烹饪·琪米卡 的两条候选文字完全相同。）
        string DetailSuffix(GameAction candidate)
        {
            var parts = new List<string>();

            switch (candidate)
            {
                case PlaySpellAction { Target: EnemyLeaderTarget }:
                    parts.Add("指定对方主战者");
                    break;
                case PlaySpellAction { Target: EnemyFollowerTarget target }:
                    parts.Add($"指定 {OpponentFollowerName(target.FollowerInstanceId)}");
                    break;
            }

            if (candidate is PlayFollowerAction { EnemyFollowerTargetInstanceIds: { Count: > 0 } enemyIds })
            {
                parts.Add("指定 " + string.Join("、", enemyIds.Select(OpponentFollowerName)));
            }

            if (candidate is EvolveAction { EnemyFollowerTargetInstanceId: int evolveTargetId })
            {
                parts.Add($"指定 {OpponentFollowerName(evolveTargetId)}");
            }

            if (candidate is SuperEvolveAction { EnemyFollowerTargetInstanceId: int superTargetId })
            {
                parts.Add($"指定 {OpponentFollowerName(superTargetId)}");
            }

            if (candidate is SuperEvolveAction { OtherFollowerTargetInstanceId: int allyId })
            {
                parts.Add($"指定己方的 {FollowerName(allyId)}");
            }

            if (candidate is PlayFollowerAction { HandCardTargetInstanceId: int handTargetId })
            {
                parts.Add($"弃掉手牌的 {CardName(handTargetId)}");
            }

            if (OwnHandTargets(candidate) is { Count: > 0 } ownHandIds)
            {
                parts.Add("弃掉 " + string.Join("、", ownHandIds.Select(CardName)));
            }

            return parts.Count == 0 ? string.Empty : "（" + string.Join("；", parts) + "）";
        }

        static IReadOnlyList<int>? OwnHandTargets(GameAction candidate) => candidate switch
        {
            PlayFollowerAction play => play.OwnHandCardTargetInstanceIds,
            PlaySpellAction spell => spell.OwnHandCardTargetInstanceIds,
            EvolveAction evolve => evolve.OwnHandCardTargetInstanceIds,
            SuperEvolveAction superEvolve => superEvolve.OwnHandCardTargetInstanceIds,
            _ => null
        };
    }

    /// <summary>
    /// 同名条目多于一个时，给指定的那一个补上"（第 N 张）"。
    /// 小于等于一个时原样返回，避免正常情况下的文字变啰嗦。
    /// </summary>
    private static string Numbered<T>(
        IReadOnlyList<T> sameName,
        Func<T, bool> isTarget,
        string name)
    {
        if (sameName.Count <= 1)
        {
            return name;
        }

        for (var index = 0; index < sameName.Count; index++)
        {
            if (isTarget(sameName[index]))
            {
                return $"{name}（第{index + 1}张）";
            }
        }

        return name;
    }
}
