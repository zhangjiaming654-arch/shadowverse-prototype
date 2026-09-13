using Shadowverse.Engine.Agents;
using Shadowverse.Engine.Decks;
using Shadowverse.Engine.Game;
using Shadowverse.Engine.Models;
using Shadowverse.Engine.Simulation;

namespace Shadowverse.ConsoleApp;

/// <summary>
/// 【决策质量分层】按"决策裕度"给决策分层，看搜索的**判断正确率**怎么随裕度变化。
/// <para>
/// <b>它想回答的问题</b>：这个项目反复发现"搜索分不清好坏"（
/// `AGENT-STRENGTH-REPORT.md` §五：问题在偏差不在方差；加推演没用）。
/// 一个可测的版本是：**在裕度小的决策上，搜索选的动作到底是不是更好？**
/// </para>
/// <list type="bullet">
/// <item>如果**裕度大时判断准、裕度小时接近抛硬币** ⇒ 天花板就在"区分不开"这件事上，
/// 那么真正该做的是**降低估计噪声**（更多推演、更好的确定化），
/// 而项目实测"加推演到顶就回落"，说明这条路可能已经到头。</item>
/// <item>如果**连裕度大时都不准** ⇒ 搜索的判断本身有系统性错误，
/// 加算力没用，必须改结构。</item>
/// </list>
/// <para>
/// <b>怎么算"准"</b>：对每一个决策，取搜索选的动作，和规则牌手会选的动作比。
/// 然后用**这一局最终谁赢**当标签：
/// 如果搜索和规则牌手分歧，那么"搜索那一方最终赢了"就是它这次分歧判断**对**的证据。
/// 这只是相关性证据（一局里很多决策共享同一个胜负），所以只做**分层比较**，不做显著性。
/// </para>
/// </summary>
public static class DecisionQualityProbe
{
    public sealed record Stratum(
        double MarginLower,
        double MarginUpper,
        int Decisions,
        /// <summary>搜索与规则牌手分歧的比例。</summary>
        double DisagreementShare,
        /// <summary>分歧的决策里，搜索那一方最终赢了的比例。</summary>
        double DisagreementWinShare,
        /// <summary>
        /// 这些分歧决策上，**搜索自己给被选动作的估值**均值。
        /// 这是关键对照：把"实际胜率"和"搜索自己的预测"比，就排除了
        /// "自对弈里某一方本来就强"这个混淆（见 <see cref="DecisionQualityProbe"/> 的注释）。
        /// </summary>
        double MeanEstimate);

    public sealed record Report(
        int Decisions,
        int Games,
        double OverallDisagreementShare,
        double OverallDisagreementWinShare,
        IReadOnlyList<Stratum> Strata,
        /// <summary>搜索和规则牌手完全一致的决策里，搜索那一方最终赢的比例（对照用）。</summary>
        double AgreementWinShare);

    public static Report Run(
        IReadOnlyList<DeckDefinition> decks,
        int gameCount,
        ulong seedBase,
        int rollouts,
        int horizon,
        Action<string> report)
    {
        ArgumentNullException.ThrowIfNull(decks);
        ArgumentNullException.ThrowIfNull(report);

        // 每 5 个"决策裕度"分一层（0..1 之外的全归最后一层），层界固定，便于复现。
        double[] boundaries = [0.0, 0.01, 0.02, 0.05, 0.10, 0.20, double.MaxValue];
        var perStratum = new (int Decisions, int Disagreements, int DisagreementWins, double EstimateSum)[boundaries.Length - 1];
        var totalDecisions = 0;
        var totalDisagreements = 0;
        var totalDisagreementWins = 0;
        var agreementWins = 0;
        var agreementTotal = 0;
        var games = 0;

        for (var index = 0; index < gameCount; index++)
        {
            var first = decks[index % decks.Count];
            var second = decks[(index + 1) % decks.Count];
            var seed = seedBase + (ulong)index;
            var agent = new LookaheadPlayerAgent(
                rolloutsPerAction: rollouts,
                futureTurnHorizon: horizon,
                seed: seed ^ 0x5DEECE66DUL,
                minimumPracticalAdvantage: 0.0);
            var rule = new GreedyPlayerAgent();
            var samples = new List<(double Margin, bool Disagreed, double Estimate)>();

            var result = MatchRunner.PlayToEnd(
                GameEngine.CreateGame(first, second, seed),
                agent,
                rule,
                onStep: step =>
                {
                    if (step.BeforeState.Phase != GamePhase.Main || step.ActingPlayer != 0)
                    {
                        return;
                    }

                    var decision = agent.LastDecision;
                    if (decision is null || decision.Evaluations.Count < 2)
                    {
                        return;
                    }

                    var ordered = decision.Evaluations
                        .Select(evaluation => evaluation.EstimatedWinChance)
                        .OrderByDescending(value => value)
                        .ToList();
                    var margin = ordered[0] - ordered[1];

                    var observation = GameEngine.ToObservation(step.BeforeState, 0);
                    var legal = GameEngine.GetLegalActions(step.BeforeState);
                    var ruleAction = rule.ChooseAction(observation, legal);
                    samples.Add((margin, !SameAction(ruleAction, step.Action), ordered[0]));
                });

            games++;

            // 胜负只在局末知道，所以标签在这里贴上。
            var agentWon = result.Winner == 0;
            foreach (var (margin, disagreed, estimate) in samples)
            {
                totalDecisions++;
                var stratum = StratumOf(boundaries, margin);
                if (disagreed)
                {
                    totalDisagreements++;
                    perStratum[stratum].Decisions++;
                    perStratum[stratum].Disagreements++;
                    perStratum[stratum].EstimateSum += estimate;
                    if (agentWon)
                    {
                        totalDisagreementWins++;
                        perStratum[stratum].DisagreementWins++;
                    }
                }
                else
                {
                    agreementTotal++;
                    if (agentWon)
                    {
                        agreementWins++;
                    }
                }
            }

            if ((index + 1) % Math.Max(1, gameCount / 5) == 0)
            {
                report($"  进度 {index + 1}/{gameCount} 局 ｜ 已记录 {totalDecisions} 个决策");
            }
        }

        var strata = new List<Stratum>();
        for (var index = 0; index < perStratum.Length; index++)
        {
            var entry = perStratum[index];
            strata.Add(new Stratum(
                boundaries[index],
                boundaries[index + 1],
                entry.Decisions,
                totalDisagreements == 0 ? double.NaN : (double)entry.Disagreements / totalDisagreements,
                entry.Disagreements == 0 ? double.NaN : (double)entry.DisagreementWins / entry.Disagreements,
                entry.Disagreements == 0 ? double.NaN : entry.EstimateSum / entry.Disagreements));
        }

        return new Report(
            totalDecisions,
            games,
            totalDecisions == 0 ? double.NaN : (double)totalDisagreements / totalDecisions,
            totalDisagreements == 0 ? double.NaN : (double)totalDisagreementWins / totalDisagreements,
            strata,
            agreementTotal == 0 ? double.NaN : (double)agreementWins / agreementTotal);
    }

    private static int StratumOf(double[] boundaries, double margin)
    {
        for (var index = 0; index < boundaries.Length - 1; index++)
        {
            if (margin >= boundaries[index] && margin < boundaries[index + 1])
            {
                return index;
            }
        }

        return boundaries.Length - 2;
    }

    private static bool SameAction(GameAction left, GameAction right) =>
        Render(left) == Render(right);

    /// <summary>结构比较（`GameAction` 的 record 相等对列表成员是按引用的）。</summary>
    private static string Render(GameAction action) => action switch
    {
        MulliganAction mulligan => $"mulligan[{string.Join('|', mulligan.ReplaceInstanceIds)}]",
        PlayFollowerAction play => $"follower[{play.CardInstanceId};{play.HandCardTargetInstanceId};{Ids(play.EnemyFollowerTargetInstanceIds)};{play.ModeChoiceIndex};{Ids(play.OwnHandCardTargetInstanceIds)}]",
        PlayAmuletAction amulet => $"amulet[{amulet.CardInstanceId}]",
        PlayCrystallizeAction crystallize => $"crystallize[{crystallize.CardInstanceId}]",
        PlayAccelerateAction accelerate => $"accelerate[{accelerate.CardInstanceId}]",
        PlaySpellAction spell => $"spell[{spell.CardInstanceId};{Target(spell.Target)};{Ids(spell.OwnHandCardTargetInstanceIds)}]",
        EvolveAction evolve => $"evolve[{evolve.FollowerInstanceId};{evolve.ModeChoiceIndex};{Ids(evolve.OwnHandCardTargetInstanceIds)};{evolve.EnemyFollowerTargetInstanceId}]",
        SuperEvolveAction super => $"super[{super.FollowerInstanceId};{super.OtherFollowerTargetInstanceId};{super.ModeChoiceIndex};{Ids(super.OwnHandCardTargetInstanceIds)};{super.EnemyFollowerTargetInstanceId}]",
        UseExtraPlayPointAction => "extra-pp",
        AttackLeaderAction leader => $"attack-leader[{leader.AttackerInstanceId}]",
        AttackFollowerAction follower => $"attack-follower[{follower.AttackerInstanceId}->{follower.DefenderInstanceId}]",
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
