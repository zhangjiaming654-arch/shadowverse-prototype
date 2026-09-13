using Shadowverse.Engine.Agents;

namespace Shadowverse.Engine.Game;

/// <summary>
/// 把对手一次决策的思考过程渲染成给人看的几行字。
/// <para>
/// 放在引擎层（而不是界面里）的理由和 <see cref="HumanActionText"/> 一样：
/// 这段拼装里有真逻辑 —— "有几个候选""第 2 名存不存在""要不要提回退闸"——
/// 界面自己写一份，自检就够不着，而这些地方正是最容易出错、
/// 又最不容易在界面上被发现的地方（一个越界异常会直接卡死整局对战）。
/// </para>
/// <para>
/// <b>只使用公开信息</b>：候选动作里涉及对手手牌的只报类型，见
/// <see cref="HumanActionText.DescribeOpponentAction"/>。
/// </para>
/// </summary>
public static class OpponentThinkingReport
{
    public static IReadOnlyList<string> Build(
        GameObservation observation,
        LookaheadDecision decision,
        string playedDescription,
        int maxRows = 6)
    {
        ArgumentNullException.ThrowIfNull(observation);
        ArgumentNullException.ThrowIfNull(decision);

        var evaluations = decision.Evaluations;
        if (evaluations is null || evaluations.Count == 0)
        {
            return [$"第 {decision.TurnNumber} 回合 · 没有可供比较的候选动作"];
        }

        var plannerTop = evaluations[0];
        var deferred = !ReferenceEquals(decision.SelectedAction, plannerTop.Action);
        var lines = new List<string>
        {
            $"第 {decision.TurnNumber} 回合 · {(deferred ? "让位给规则牌手" : "他自己判断")}",
            "实际打出：" + playedDescription
        };

        // 候选只有一个 —— 没有"第 2 名"可谈。
        // **这里必须提前返回**：下面那句"第 1 名领先第 2 名"会直接越界。
        // 注意原因是"引擎只给了一个合法动作"（典型是没牌可打、也没人能攻击，只能结束回合），
        // **不是**"牌手跳过了搜索"—— LookaheadPlayerAgent 没有那种短路。
        if (evaluations.Count == 1)
        {
            lines.Add("这一次只有这一个合法动作，他没得选：");
            lines.Add("  " + HumanActionText.DescribeOpponentAction(observation, plannerTop.Action));
            return lines;
        }

        var rows = Math.Clamp(maxRows, 1, evaluations.Count);
        lines.Add($"考虑了 {evaluations.Count} 个动作，最看好的前 {rows} 个：");

        for (var index = 0; index < rows; index++)
        {
            var evaluation = evaluations[index];
            var mark = ReferenceEquals(evaluation.Action, decision.SelectedAction) ? "  ← 选中" : string.Empty;
            lines.Add(
                $"  {index + 1}. {HumanActionText.DescribeOpponentAction(observation, evaluation.Action)}" +
                $"  {evaluation.EstimatedWinChance * 100:F1}%{mark}");
        }

        if (evaluations.Count > rows)
        {
            lines.Add($"  …还有 {evaluations.Count - rows} 个");
        }

        var margin = (evaluations[0].EstimatedWinChance - evaluations[1].EstimatedWinChance) * 100.0;
        lines.Add($"第 1 名领先第 2 名 {margin:F1} 个百分点");

        if (deferred)
        {
            lines.Add("他自己的首选：" + HumanActionText.DescribeOpponentAction(observation, plannerTop.Action));
            lines.Add("（领先幅度没超过判定线，所以这一手让位给规则牌手）");
        }

        return lines;
    }
}
