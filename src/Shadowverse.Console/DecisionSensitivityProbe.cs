using System.Globalization;
using Shadowverse.Engine.Agents;
using Shadowverse.Engine.Decks;
using Shadowverse.Engine.Game;
using Shadowverse.Engine.Models;
using Shadowverse.Engine.Simulation;

namespace Shadowverse.ConsoleApp;

/// <summary>
/// 【决策敏感度】一台仪器：把一组固定的局面喂给同一族牌手的**不同配置**，
/// 量"选出来的动作变了多少次"。
/// <para>
/// <b>它想回答的问题</b>：这个项目试过十几个方向、每一个都不产生强度。一个可能的统一解释是
/// **搜索的输出对自身的内部几乎不敏感** —— 换权重、换视野、换叶子、换 rollout 策略之后，
/// 它照样选同一个动作。如果这是真的，那"调参"永远不会有收益，而且**任何**改动要先证明
/// 它能改变决策，才谈得上改进决策。
/// </para>
/// <para>
/// <b>为什么这个量比"预测指标"可靠</b>：2026-09-13 实测过一个反例 ——
/// 用 AUC（搜索估值预测胜负的能力）当改动筛子，结果三份不同 rollout 配置的 AUC
/// **四位小数完全相同**，因为改动根本没改变决策，估值和标签都逐字节相同（报告 §18.4）。
/// 敏感度是**决策层面**的量，不存在那个恒等式问题：它直接数"动作变了没有"。
/// </para>
/// <para>
/// <b>口径</b>：只统计主回合、只统计第一牌手（<c>perspectivePlayer == 0</c>）的决策。
/// 每个配置都在**同一批局面**上问，且用同一个 seed —— 所以配对，差别只来自配置本身。
/// </para>
/// </summary>
public static class DecisionSensitivityProbe
{
    /// <summary>一个待测配置：名字 + 造牌手的工厂。</summary>
    public sealed record Variant(string Name, Func<LookaheadPlayerAgent> Create);

    /// <summary>一个配置的测量结果。</summary>
    public sealed record VariantResult(
        string Name,
        /// <summary>和基线选得不一样的动作数。</summary>
        int Changed,
        int Decisions,
        /// <summary>基线最优候选与次优候选的分数差（估值的"决策裕度"）。</summary>
        double BaselineTopGap,
        /// <summary>**被改动的那些决策**在基线上的裕度均值。裕度小 = 基线本来就没把握。</summary>
        double ChangedMeanMargin,
        /// <summary>**没被改动的那些决策**在基线上的裕度均值。用来对照。</summary>
        double UnchangedMeanMargin)
    {
        public double ChangedShare => Decisions == 0 ? double.NaN : (double)Changed / Decisions;

        /// <summary>
        /// 改动是否集中在"基线本来就没把握"的决策上。
        /// 正数 = 确实集中在薄弱决策（假设成立的方向）；接近 0 = 改动和裕度无关。
        /// </summary>
        public double MarginContrast => UnchangedMeanMargin - ChangedMeanMargin;
    }

    /// <summary>整份报告。</summary>
    public sealed record Report(
        int Positions,
        string BaselineName,
        IReadOnlyList<VariantResult> Variants,
        /// <summary>基线自己在同一局面上重问一次的"不变率"，用来当噪声地板。</summary>
        int BaselineSelfChanged);

    /// <summary>
    /// 采一批局面，然后逐个配置重问。
    /// </summary>
    /// <param name="decks">用于采局面的卡组对。</param>
    /// <param name="gameCount">采几个局面（每局取一个主回合决策）。</param>
    /// <param name="baseline">基线配置（同时是"动作是否改变"的参照）。</param>
    /// <param name="variants">待测配置。</param>
    /// <param name="report">进度输出。</param>
    public static Report Run(
        IReadOnlyList<DeckDefinition> decks,
        int gameCount,
        ulong seedBase,
        Variant baseline,
        IReadOnlyList<Variant> variants,
        Action<string> report)
    {
        ArgumentNullException.ThrowIfNull(decks);
        ArgumentNullException.ThrowIfNull(report);

        report($"采局面：{gameCount} 局（每局取一个主回合决策）");
        var positions = CollectPositions(decks, gameCount, seedBase);
        report($"采到 {positions.Count} 个局面");

        var baselineAgent = baseline.Create();
        var baselineChoices = new List<string>(positions.Count);
        var gaps = new List<double>(positions.Count);
        var lastDecisionNull = 0;
        var singleCandidate = 0;
        foreach (var (state, _) in positions)
        {
            var decision = Ask(baselineAgent, state, out var candidateCount, out var hadDecision);
            if (!hadDecision)
            {
                lastDecisionNull++;
            }

            if (candidateCount < 2)
            {
                singleCandidate++;
            }

            baselineChoices.Add(Canonical(decision.Action));
            gaps.Add(TopGap(decision));
        }

        report(
            $"  诊断：LastDecision 为空 {lastDecisionNull}/{positions.Count} ｜ " +
            $"候选数 <2 的局面 {singleCandidate}/{positions.Count}");

        // 分差只在"真的有次优候选"的局面上定义：只有 1 个合法动作时没有"次优"，
        // 那个局面记 NaN。**不能直接 Average()** —— 一个 NaN 会把整体平均毒成 NaN，
        // 这个探针第一版就是这么错的（实测 8 个局面里有 2 个是单候选）。
        var definedGaps = gaps.Where(gap => !double.IsNaN(gap)).ToList();
        var meanGap = definedGaps.Count == 0 ? double.NaN : definedGaps.Average();
        report(
            $"基线 {baseline.Name}：最优/次优候选的平均分差 = {meanGap:F4}" +
            $"（{definedGaps.Count}/{positions.Count} 个局面有次优候选）");

        // 噪声地板：同一个配置重问一遍。如果连它自己都不稳定，后面的数字就不能解读。
        var repeatAgent = baseline.Create();
        var selfChanged = 0;
        for (var index = 0; index < positions.Count; index++)
        {
            if (Canonical(Ask(repeatAgent, positions[index].State).Action) != baselineChoices[index])
            {
                selfChanged++;
            }
        }

        var results = new List<VariantResult>();
        foreach (var variant in variants)
        {
            var agent = variant.Create();
            var changed = 0;
            var changedMargins = new List<double>();
            var unchangedMargins = new List<double>();
            for (var index = 0; index < positions.Count; index++)
            {
                var isChanged =
                    Canonical(Ask(agent, positions[index].State).Action) != baselineChoices[index];
                if (isChanged)
                {
                    changed++;
                }

                // 裕度必须在**基线**上取（那是被扰动的那一点），而且是 NaN 就跳过。
                var margin = gaps[index];
                if (double.IsNaN(margin))
                {
                    continue;
                }

                (isChanged ? changedMargins : unchangedMargins).Add(margin);
            }

            var result = new VariantResult(
                variant.Name,
                changed,
                positions.Count,
                meanGap,
                changedMargins.Count == 0 ? double.NaN : changedMargins.Average(),
                unchangedMargins.Count == 0 ? double.NaN : unchangedMargins.Average());
            results.Add(result);
            report(
                $"  {variant.Name,-34} 决策改变 {changed,4}/{positions.Count}（{100.0 * changed / positions.Count:F1}%）"
                + $" ｜ 改变处裕度 {result.ChangedMeanMargin:F4} vs 未变处 {result.UnchangedMeanMargin:F4}"
                + $" ｜ 对比 {result.MarginContrast:+0.0000;-0.0000}");
        }

        return new Report(positions.Count, baseline.Name, results, selfChanged);
    }

    private sealed record Decision(GameAction Action, double Best, double Second)
    {
        /// <summary>最优与次优候选的分数差 = 这次决策的"裕度"。裕度越小越容易被任何扰动翻掉。</summary>
        public double Gap =>
            double.IsNaN(Best) || double.IsNaN(Second) ? double.NaN : Best - Second;
    }

    /// <summary>让牌手在这个局面上出招，并取回它的候选分数。</summary>
    private static Decision Ask(
        LookaheadPlayerAgent agent,
        GameState state,
        out int candidateCount,
        out bool hadDecision)
    {
        var observation = GameEngine.ToObservation(state, state.ActivePlayer);
        var legal = GameEngine.GetLegalActions(state);
        var action = agent.ChooseAction(state, observation, legal);
        var evaluations = agent.LastDecision?.Evaluations ?? [];
        candidateCount = evaluations.Count;
        hadDecision = agent.LastDecision is not null;
        if (evaluations.Count == 0)
        {
            return new Decision(action, double.NaN, double.NaN);
        }

        var ordered = evaluations
            .Select(evaluation => evaluation.EstimatedWinChance)
            .OrderByDescending(value => value)
            .ToList();
        return new Decision(
            action,
            ordered[0],
            ordered.Count > 1 ? ordered[1] : double.NaN);
    }

    private static Decision Ask(LookaheadPlayerAgent agent, GameState state) =>
        Ask(agent, state, out _, out _);

    private static double TopGap(Decision decision) =>
        double.IsNaN(decision.Second) ? double.NaN : decision.Gap;

    /// <summary>
    /// 采一批主回合局面。每局只取一个（固定取主回合里的第一个决策），
    /// 这样局面之间不至于高度重复（同一局的相邻决策几乎是同一个局面）。
    /// </summary>
    private static List<(GameState State, int Turn)> CollectPositions(
        IReadOnlyList<DeckDefinition> decks,
        int gameCount,
        ulong seedBase)
    {
        var positions = new List<(GameState, int)>();
        for (var index = 0; index < gameCount; index++)
        {
            var first = decks[index % decks.Count];
            var second = decks[(index + 1) % decks.Count];
            var seed = seedBase + (ulong)index;
            var collected = false;
            MatchRunner.PlayToEnd(
                GameEngine.CreateGame(first, second, seed),
                new GreedyPlayerAgent(),
                new GreedyPlayerAgent(),
                onStep: step =>
                {
                    if (collected ||
                        step.BeforeState.Phase != GamePhase.Main ||
                        step.BeforeState.ActivePlayer != 0)
                    {
                        return;
                    }

                    positions.Add((step.BeforeState, step.BeforeState.TurnNumber));
                    collected = true;
                });
        }

        return positions;
    }

    /// <summary>把动作渲染成规范字符串（`GameAction` 的 record 相等对列表成员是按引用的，不能直接用）。</summary>
    private static string Canonical(GameAction action) => action switch
    {
        MulliganAction mulligan => $"mulligan[{string.Join('|', mulligan.ReplaceInstanceIds)}]",
        PlayFollowerAction play => $"follower[{play.CardInstanceId};{play.HandCardTargetInstanceId};{Ids(play.EnemyFollowerTargetInstanceIds)};{play.ModeChoiceIndex};{Ids(play.OwnHandCardTargetInstanceIds)}]",
        PlayAmuletAction amulet => $"amulet[{amulet.CardInstanceId}]",
        PlayCrystallizeAction crystallize => $"crystallize[{crystallize.CardInstanceId}]",
        PlayAccelerateAction accelerate => $"accelerate[{accelerate.CardInstanceId}]",
        PlaySpellAction spell => $"spell[{spell.CardInstanceId};{Target(spell.Target)};{Ids(spell.OwnHandCardTargetInstanceIds)}]",
        EvolveAction evolve => $"evolve[{evolve.FollowerInstanceId};{evolve.ModeChoiceIndex};{Ids(evolve.OwnHandCardTargetInstanceIds)};{evolve.EnemyFollowerTargetInstanceId}]",
        SuperEvolveAction superEvolve => $"super[{superEvolve.FollowerInstanceId};{superEvolve.OtherFollowerTargetInstanceId};{superEvolve.ModeChoiceIndex};{Ids(superEvolve.OwnHandCardTargetInstanceIds)};{superEvolve.EnemyFollowerTargetInstanceId}]",
        UseExtraPlayPointAction => "extra-pp",
        AttackLeaderAction attackLeader => $"attack-leader[{attackLeader.AttackerInstanceId}]",
        AttackFollowerAction attackFollower => $"attack-follower[{attackFollower.AttackerInstanceId}->{attackFollower.DefenderInstanceId}]",
        EndTurnAction => "end-turn",
        _ => action.GetType().Name
    };

    private static string Target(SpellTarget? target) => target switch
    {
        null => "-",
        EnemyLeaderTarget => "leader",
        EnemyFollowerTarget follower => $"follower:{follower.FollowerInstanceId}",
        _ => target.GetType().Name
    };

    private static string Ids(IReadOnlyList<int>? values) =>
        values is null ? "-" : string.Join('|', values);
}
