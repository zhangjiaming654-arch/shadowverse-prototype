using System.Diagnostics;
using Shadowverse.Engine.Agents;
using Shadowverse.Engine.Game;
using Shadowverse.Engine.Models;
using Shadowverse.Engine.Simulation;

namespace Shadowverse.ConsoleApp;

/// <summary>Which policy drives one seat of a benchmark match.</summary>
public enum AgentKind
{
    Random,
    Greedy,
    Baseline,
    Lookahead,

    /// <summary>
    /// The frozen 2.0 snapshot（视野循环 {1,3} × 60 推演）。3.0 的对照基准。
    /// </summary>
    LookaheadV2,

    /// <summary>
    /// The frozen 1.0 snapshot (10 rollouts, horizon 3, rail off). This is the opponent every
    /// 2.0 experiment is measured against, so the yardstick never drifts while the live
    /// <see cref="AgentKind.Lookahead"/> keeps changing.
    /// </summary>
    LookaheadV1,
    BaselineLookahead
}

/// <summary>
/// A parallel, seat-balanced benchmark for player agents.
/// <para>
/// Every match builds its own <see cref="GameState"/> and its own agent instances, and the engine
/// keeps no mutable static state, so matches are fully independent and can run on every core.
/// Each matchup is played in both orientations (first agent in seat 0, then in seat 1) over the
/// same sequence of deals, which cancels any seat or deck-order confound, and the reported win
/// rate carries a Wilson 95% confidence interval so a result can be told apart from noise.
/// </para>
/// </summary>
public static class AgentBenchmark
{
    public static string DisplayName(AgentKind kind) => kind switch
    {
        AgentKind.Random => "随机牌手",
        AgentKind.Greedy => "规则牌手",
        AgentKind.Baseline => "规则牌手·冻结基线",
        AgentKind.Lookahead => "前瞻牌手 3.0（开发中）",
        AgentKind.LookaheadV2 => "前瞻牌手 2.0（冻结）",
        AgentKind.LookaheadV1 => "前瞻牌手 1.0（冻结）",
        AgentKind.BaselineLookahead => "前瞻牌手·旧冻结基线",
        _ => kind.ToString()
    };

    public static AgentKind ParseAgent(string text) => text.Trim().ToLowerInvariant() switch
    {
        "random" or "rnd" or "r" => AgentKind.Random,
        "greedy" or "rule" or "g" => AgentKind.Greedy,
        "baseline" or "old" or "b" => AgentKind.Baseline,
        "lookahead" or "lookahead-v3" or "lookaheadv3" or "v3" => AgentKind.Lookahead,
        "lookahead-v2" or "lookaheadv2" or "v2" => AgentKind.LookaheadV2,
        "lookahead-v1" or "lookaheadv1" or "v1" => AgentKind.LookaheadV1,
        "lookahead-baseline" or "lookaheadbase" or "v1base" => AgentKind.BaselineLookahead,
        _ => throw new ArgumentException(
            $"未知牌手“{text}”。可选：random、greedy、baseline、lookahead、lookahead-v2、lookahead-v1、lookahead-baseline")
    };

    /// <summary>One deck the benchmark may deal out, with the label reports should use.</summary>
    public sealed record BenchmarkDeck(string Id, string Name, DeckDefinition Definition);

    public sealed record Options(
        AgentKind FirstAgent,
        AgentKind SecondAgent,
        int MatchCount,
        ulong SeedBase,
        int RolloutsPerAction,
        int FutureTurnHorizon,
        bool SwapOrientation,
        int MaxDegreeOfParallelism,
        bool PrintBehaviour,
        bool RandomDecks,
        /// <summary>
        /// The second seat's rollout budget, so the same agent can be played against itself at two
        /// different search budgets. That is the experiment that answers whether more search is
        /// worth paying for; giving both seats the same budget cannot answer it.
        /// </summary>
        int SecondRolloutsPerAction = 0,
        /// <summary>
        /// The second seat's planning horizon, for the same reason as the second rollout budget:
        /// it has to travel with the agent so that two identical agents configured differently
        /// actually play different games in the two orientations.
        /// </summary>
        int SecondFutureTurnHorizon = 0,
        /// <summary>回退闸下限；负数表示用牌手默认值。两边独立，才能做闸门开/关的对照。</summary>
        double RailMargin = -1,
        double SecondRailMargin = -1,
        /// <summary>
        /// 决策规则。两边独立，才能用"同一个牌手、只改选择规则"来做配对对照。
        /// </summary>
        LookaheadSelectionMode SelectionMode = LookaheadSelectionMode.RuleAgentFallback,
        LookaheadSelectionMode SecondSelectionMode = LookaheadSelectionMode.RuleAgentFallback,
        /// <summary>VariancePenalized 模式下的稳健惩罚系数 z。</summary>
        double RobustnessPenalty = 1.0,
        double SecondRobustnessPenalty = 1.0,
        /// <summary>
        /// 回退闸统计项的强度（1.0 = 一个配对标准误）。值越大，前瞻越倾向于把决定权让给规则牌手。
        /// </summary>
        double StatisticalConfidence = 1.0,
        double SecondStatisticalConfidence = 1.0,
        /// <summary>推演里由谁把剩余动作打完。两边独立，才能做 rollout 策略的配对对照。</summary>
        LookaheadRolloutPolicy RolloutPolicy = LookaheadRolloutPolicy.RuleAgent,
        LookaheadRolloutPolicy SecondRolloutPolicy = LookaheadRolloutPolicy.RuleAgent,
        /// <summary>换牌专用视野；0 表示跟随主回合视野。换牌是整局规划，短视野会系统性换错。</summary>
        int MulliganHorizon = 0,
        int SecondMulliganHorizon = 0,
        /// <summary>交替视野：奇数号推演用这个视野，和主视野对冲。0 表示不交替。</summary>
        int AlternateHorizon = 0,
        int SecondAlternateHorizon = 0,
        /// <summary>第三档视野：三档循环 [主, 交替, 第三]。0 表示不用。</summary>
        int ThirdHorizon = 0,
        int SecondThirdHorizon = 0,
        /// <summary>第二个集成轴：另一半推演由谁打完。默认与主策略相同（= 不集成）。</summary>
        LookaheadRolloutPolicy AlternateRolloutPolicy = LookaheadRolloutPolicy.RuleAgent,
        LookaheadRolloutPolicy SecondAlternateRolloutPolicy = LookaheadRolloutPolicy.RuleAgent,
        /// <summary>额外PP 这个动作的候选权归谁（Search / RuleGated / Never）。</summary>
        LookaheadExtraPlayPointPolicy ExtraPlayPointPolicy = LookaheadExtraPlayPointPolicy.Search,
        LookaheadExtraPlayPointPolicy SecondExtraPlayPointPolicy = LookaheadExtraPlayPointPolicy.Search,
        /// <summary>rollout 里【对手】用哪个策略——对手建模。默认规则牌手 = 1.0/2.0 的行为。</summary>
        LookaheadRolloutPolicy OpponentRolloutPolicy = LookaheadRolloutPolicy.RuleAgent,
        LookaheadRolloutPolicy SecondOpponentRolloutPolicy = LookaheadRolloutPolicy.RuleAgent,
        /// <summary>
        /// 嵌套对手模型（<c>--opponent-rollout nested</c>）的推演次数与视野。
        /// 刻意比本牌手小得多：嵌套搜索的代价是乘法。
        /// </summary>
        int OpponentRollouts = LookaheadPlayerAgent.DefaultNestedOpponentRollouts,
        int SecondOpponentRollouts = LookaheadPlayerAgent.DefaultNestedOpponentRollouts,
        int OpponentHorizon = 0,
        int SecondOpponentHorizon = 0,
        /// <summary>嵌套对手只搜"它怎么回应我"的第一手，之后退回规则牌手（省掉 4.3 倍成本里的大头）。</summary>
        bool OpponentFirstActionOnly = false,
        bool SecondOpponentFirstActionOnly = false,
        /// <summary>
        /// 嵌套**我方**（S2：`rolloutPolicy` 用 nested）的推演次数与视野。
        /// 和对手那份分开：两边模拟的是两件不同的事，共用一个值会串。
        /// </summary>
        int OwnNestedRollouts = LookaheadPlayerAgent.DefaultNestedOpponentRollouts,
        int SecondOwnNestedRollouts = LookaheadPlayerAgent.DefaultNestedOpponentRollouts,
        int OwnNestedHorizon = 0,
        int SecondOwnNestedHorizon = 0,
        /// <summary>
        /// 【S1 最小搜索树】候选评分深度。0 = 关闭（默认 = 基线行为）。
        /// 依据见报告 §19.8：瓶颈在候选集太窄，不在判断质量。
        /// </summary>
        int TreePly = 0,
        int SecondTreePly = 0,
        /// <summary>深层评价用"推演估计"而不是一层叶值（§19.12 证明叶值深评会崩盘）。</summary>
        bool TreeUsesSearchValue = false,
        bool SecondTreeUsesSearchValue = false,
        /// <summary>深层的后续动作用 mean 而非 max 汇总（去掉赢家诅咒，§19.12 实测偏差缩小 3 倍）。</summary>
        bool TreeUsesMeanFollowUp = false,
        bool SecondTreeUsesMeanFollowUp = false,
        /// <summary>深层评价的推演预算。</summary>
        int DeepRollouts = LookaheadPlayerAgent.DefaultDeepRollouts,
        int SecondDeepRollouts = LookaheadPlayerAgent.DefaultDeepRollouts,
        /// <summary>评估函数集成：一半推演用 --alt-weights-file 的第二套权重。两边独立才能对照。</summary>
        bool EvaluatorEnsemble = false,
        bool SecondEvaluatorEnsemble = false);

    private sealed record Outcome(
        int Winner,
        int StartingPlayer,
        int FirstAgentSeat,
        int MatchIndex,
        bool IsNormalOrientation);

    /// <summary>
    /// A paired comparison over shared deals. <paramref name="FirstWinsBoth"/> and
    /// <paramref name="SecondWinsBoth"/> are the decisive deals; <paramref name="SplitPairs"/> were
    /// decided by the seat rather than by the policy. <see cref="DecisivePairs"/> is what the
    /// sign test actually runs on.
    /// </summary>
    private sealed record PairedResult(
        int PairCount,
        int FirstWinsBoth,
        int SecondWinsBoth,
        int SplitPairs,
        double PValue)
    {
        public int DecisivePairs => FirstWinsBoth + SecondWinsBoth;
    }

    public static void Run(Options options, IReadOnlyList<BenchmarkDeck> decks)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(decks);
        if (decks.Count == 0)
        {
            throw new ArgumentException("A benchmark needs at least one deck.", nameof(decks));
        }

        if (options.RandomDecks && decks.Count < 2)
        {
            throw new ArgumentException(
                "随机抽卡组至少需要两副卡组；当前只提供了 1 副。",
                nameof(decks));
        }

        var normalCount = options.SwapOrientation ? (options.MatchCount + 1) / 2 : options.MatchCount;
        var swappedCount = options.MatchCount - normalCount;

        var outcomes = new Outcome[options.MatchCount];
        var assignments = new (BenchmarkDeck A, BenchmarkDeck B)[options.MatchCount];
        var behaviour = options.PrintBehaviour ? new BatchStatisticsReporter() : null;
        var behaviourGate = new object();

        // A lookahead run can take the better part of an hour on a throttling laptop, so progress is
        // reported with the running tally. Stopping a run early then still leaves usable numbers in
        // the log instead of losing everything that was held in memory.
        var completedCount = 0;
        var progressGate = new object();
        var progressEvery = Math.Max(1, options.MatchCount / 20);

        // 额外PP 的累计（跨对局，按"第一牌手 / 第二牌手"分开）。
        var firstExtraUses = 0;
        var firstExtraWasted = 0;
        var secondExtraUses = 0;
        var secondExtraWasted = 0;

        PrintHeader(options, decks, normalCount, swappedCount);

        var stopwatch = Stopwatch.StartNew();
        Parallel.For(
            0,
            options.MatchCount,
            new ParallelOptions { MaxDegreeOfParallelism = options.MaxDegreeOfParallelism },
            index =>
            {
                var isNormalOrientation = index < normalCount;
                var matchIndex = isNormalOrientation ? index : index - normalCount;
                var seed = options.SeedBase + (ulong)matchIndex;

                // Both orientations of a pair get the same two decks, so the pair still shares a
                // deal and each agent plays each deck once.
                var (deckA, deckB) = SelectDecks(options, decks, matchIndex);
                assignments[index] = (deckA, deckB);

                // 推演次数、视野、闸门、选择规则、稳健系数、统计强度——所有"随牌手走"的设置都从
                // Options 解析到 SideConfig，绝不绑在座位上。绑在座位上时，两个同种牌手配不同设置
                // 会在两个方向打出完全相同的一局（座位 0 永远一个配置、座位 1 永远另一个），
                // 配对检验就退化成"每对各赢一边"，结论恒为无差异。
                var seatZeroAgent = CreateAgent(
                    ResolveSide(options, first: isNormalOrientation),
                    MixedSeed(seed, 1));
                var seatOneAgent = CreateAgent(
                    ResolveSide(options, first: !isNormalOrientation),
                    MixedSeed(seed, 2));

                // 额外PP 诊断。额外PP 是超凡世界里【后手专属】的限时资源：第 5 回合及以前一次、
                // 第 6 回合起再一次，效果是当回合 +1 PP，且不跨回合结转。
                // 所以判定"空转"有一个很干净的判据：用了额外PP 的那个回合，结束时还剩 ≥1 点 PP
                // —— 既然 PP 不结转，那剩下的这点就说明那 +1 点没能变成任何东西。
                var extraUses = new int[2];
                var extraWasted = new int[2];
                var extraUsedThisTurn = new bool[2];

                var result = MatchRunner.PlayToEnd(
                    GameEngine.CreateGame(deckA.Definition, deckB.Definition, seed),
                    seatZeroAgent,
                    seatOneAgent,
                    onStep: step =>
                    {
                        switch (step.Action)
                        {
                            case UseExtraPlayPointAction:
                                extraUsedThisTurn[step.ActingPlayer] = true;
                                break;

                            case EndTurnAction:
                                if (extraUsedThisTurn[step.ActingPlayer])
                                {
                                    extraUses[step.ActingPlayer]++;
                                    if (step.BeforeState.Players[step.ActingPlayer].CurrentPlayPoints >= 1)
                                    {
                                        extraWasted[step.ActingPlayer]++;
                                    }

                                    extraUsedThisTurn[step.ActingPlayer] = false;
                                }

                                break;
                        }
                    });

                // 座位 0 属于哪一边取决于换边方向，所以要按方向把统计归到正确的一边。
                var firstSeat = isNormalOrientation ? 0 : 1;
                Interlocked.Add(ref firstExtraUses, extraUses[firstSeat]);
                Interlocked.Add(ref firstExtraWasted, extraWasted[firstSeat]);
                Interlocked.Add(ref secondExtraUses, extraUses[1 - firstSeat]);
                Interlocked.Add(ref secondExtraWasted, extraWasted[1 - firstSeat]);

                outcomes[index] = new Outcome(
                    result.Winner,
                    result.FinalState.StartingPlayer,
                    isNormalOrientation ? 0 : 1,
                    matchIndex,
                    isNormalOrientation);

                if (isNormalOrientation && behaviour is not null)
                {
                    lock (behaviourGate)
                    {
                        behaviour.Add(result);
                    }
                }

                var finished = Interlocked.Increment(ref completedCount);
                if (finished % progressEvery == 0)
                {
                    lock (progressGate)
                    {
                        var (firstBoth, secondBoth, split) = PartialTally(options, outcomes, normalCount);
                        Console.WriteLine(
                            $"  进度 {finished}/{options.MatchCount} ｜ 已用 {stopwatch.Elapsed.TotalSeconds:F0} 秒 ｜ " +
                            $"决定性牌局 {firstBoth}:{secondBoth}（各赢一边 {split}）");

                        // **同时写进共享进度文件**，界面（另一个进程）每秒钟读一次显示出来 ——
                        // 用户要的是"命令行跑着的时候，界面上也能看见进度"。
                        var perMatch = stopwatch.Elapsed.TotalSeconds / Math.Max(1, finished);
                        LiveProgress.Report(
                            "命令行批量对局",
                            finished,
                            options.MatchCount,
                            DisplayName(options.FirstAgent),
                            firstBoth,
                            DisplayName(options.SecondAgent),
                            secondBoth,
                            stopwatch.Elapsed,
                            TimeSpan.FromSeconds(perMatch * (options.MatchCount - finished)));

                        // BO10 是用户的主要评判口径，所以进度里直接给累计得分，不用等跑完。
                        var bo10 = PartialBo10Tally(options, outcomes, normalCount);
                        if (bo10.Counted > 0)
                        {
                            Console.WriteLine(
                                $"    BO10 已出 {bo10.Counted} 个 ｜ 第一牌手 {bo10.FirstPoints} 分 ｜ " +
                                $"第二牌手 {bo10.SecondPoints} 分 ｜ 平手 {bo10.Ties} 个" +
                                $"  →  折算 100 个 = {100.0 * bo10.FirstPoints / bo10.Counted:F0} 分");
                        }
                    }
                }
            });
        stopwatch.Stop();

        PrintDeckAssignments(options, assignments, normalCount);
        PrintResult(options, outcomes, stopwatch.Elapsed);

        PrintDeferral("3.0（开发中）", LookaheadPlayerAgent.DeferralReport());
        PrintDeferral("3.0（开发中）· 方差诊断", LookaheadPlayerAgent.VarianceReport());
        PrintDeferral("2.0（冻结）", LookaheadPlayerAgentV2.DeferralReport());
        PrintDeferral("1.0（冻结）", LookaheadPlayerAgentV1.DeferralReport());
        PrintExtraPlayPointUse(
            options,
            new ExtraPlayPointUse(firstExtraUses, firstExtraWasted),
            new ExtraPlayPointUse(secondExtraUses, secondExtraWasted));

        behaviour?.Print(
            $"A 位·{DisplayName(options.FirstAgent)}（未换边方向）",
            $"B 位·{DisplayName(options.SecondAgent)}（未换边方向）");
    }

    /// <summary>
    /// Names one side for the report. When both sides are the same agent kind, the display name alone
    /// cannot tell the reader which result belongs to which configuration, so the differing settings
    /// are appended. Most 2.0 experiments have exactly that shape: one agent, two settings.
    /// </summary>
    private static (string First, string Second) SideLabels(Options options)
    {
        var first = DisplayName(options.FirstAgent);
        var second = DisplayName(options.SecondAgent);
        if (!string.Equals(first, second, StringComparison.Ordinal))
        {
            return (first, second);
        }

        return ($"{first}〔{DescribeSide(options, first: true)}〕",
                $"{second}〔{DescribeSide(options, first: false)}〕");
    }

    private static string DescribeSide(Options options, bool first)
    {
        var rollouts = first
            ? options.RolloutsPerAction
            : options.SecondRolloutsPerAction > 0 ? options.SecondRolloutsPerAction : options.RolloutsPerAction;
        var horizon = first
            ? options.FutureTurnHorizon
            : options.SecondFutureTurnHorizon > 0 ? options.SecondFutureTurnHorizon : options.FutureTurnHorizon;
        var selection = first ? options.SelectionMode : options.SecondSelectionMode;
        var rail = first ? options.RailMargin : options.SecondRailMargin;
        var confidence = first ? options.StatisticalConfidence : options.SecondStatisticalConfidence;
        var rolloutPolicy = first ? options.RolloutPolicy : options.SecondRolloutPolicy;

        var parts = new List<string> { $"推演 {rollouts}", $"视野 {horizon}", selection.ToString() };
        if (rolloutPolicy != LookaheadRolloutPolicy.RuleAgent)
        {
            parts.Add($"rollout {rolloutPolicy}");
        }
        if (selection == LookaheadSelectionMode.VariancePenalized)
        {
            parts.Add($"z={(first ? options.RobustnessPenalty : options.SecondRobustnessPenalty):0.###}");
        }

        if (selection == LookaheadSelectionMode.RuleAgentFallback && Math.Abs(confidence - 1.0) > 1e-12)
        {
            parts.Add($"统计强度 {confidence:0.###}");
        }

        if (rail >= 0)
        {
            parts.Add($"闸门 {rail:0.###}");
        }

        var mulliganHorizon = first ? options.MulliganHorizon : options.SecondMulliganHorizon;
        if (mulliganHorizon > 0 && mulliganHorizon != horizon)
        {
            parts.Add($"换牌视野 {mulliganHorizon}");
        }

        var alternateHorizon = first ? options.AlternateHorizon : options.SecondAlternateHorizon;
        var thirdHorizon = first ? options.ThirdHorizon : options.SecondThirdHorizon;
        if (alternateHorizon > 0 || thirdHorizon > 0)
        {
            var cycle = thirdHorizon > 0
                ? $"{horizon},{alternateHorizon},{thirdHorizon}"
                : $"{horizon},{alternateHorizon}";
            parts.Add($"视野循环 {cycle}");
        }

        var alternateRollout = first ? options.AlternateRolloutPolicy : options.SecondAlternateRolloutPolicy;
        if (alternateRollout != (first ? options.RolloutPolicy : options.SecondRolloutPolicy))
        {
            parts.Add($"策略循环 {rolloutPolicy}/{alternateRollout}");
        }

        var extraPlayPoint = first ? options.ExtraPlayPointPolicy : options.SecondExtraPlayPointPolicy;
        if (extraPlayPoint != LookaheadExtraPlayPointPolicy.Search)
        {
            parts.Add($"额外PP {extraPlayPoint}");
        }

        var opponentRollout = first ? options.OpponentRolloutPolicy : options.SecondOpponentRolloutPolicy;
        if (opponentRollout != LookaheadRolloutPolicy.RuleAgent)
        {
            parts.Add($"对手建模 {opponentRollout}");
        }

        if (first ? options.EvaluatorEnsemble : options.SecondEvaluatorEnsemble)
        {
            parts.Add("评估集成");
        }

        return string.Join(" / ", parts);
    }

    /// <summary>
    /// 额外PP 使用情况。<see cref="WasteRate"/> 高说明这个后手专属资源在被打水漂。
    /// </summary>
    private sealed record ExtraPlayPointUse(int Uses, int Wasted)
    {
        public double WasteRate => Uses == 0 ? 0.0 : (double)Wasted / Uses;
    }

    private static void PrintExtraPlayPointUse(
        Options options,
        ExtraPlayPointUse first,
        ExtraPlayPointUse second)
    {
        if (first.Uses + second.Uses == 0)
        {
            return;
        }

        var labels = SideLabels(options);
        Console.WriteLine("额外PP 使用（超凡世界规则：后手专属、当回合 +1 PP、不跨回合结转）：");
        Console.WriteLine(
            $"  {labels.First}：用 {first.Uses} 次 ｜ 空转 {first.Wasted} 次（{first.WasteRate:P1}）");
        Console.WriteLine(
            $"  {labels.Second}：用 {second.Uses} 次 ｜ 空转 {second.Wasted} 次（{second.WasteRate:P1}）");
        Console.WriteLine();
    }

    private static void PrintDeferral(string label, string? report)    {
        if (report is null)
        {
            return;
        }

        Console.WriteLine($"[{label}] {report}");
        Console.WriteLine();
    }

    /// <summary>
    /// Picks the two decks for one deal. Both orientations of the deal use the same pair, so the
    /// choice is a pure function of the match index and the base seed.
    /// </summary>
    private static (BenchmarkDeck A, BenchmarkDeck B) SelectDecks(
        Options options,
        IReadOnlyList<BenchmarkDeck> decks,
        int matchIndex)
    {
        if (!options.RandomDecks || decks.Count < 2)
        {
            return (decks[0], decks[^1]);
        }

        var draw = MixedSeed(options.SeedBase + (ulong)matchIndex, 17);
        var first = (int)(draw % (ulong)decks.Count);
        // Draw the second deck from the remaining decks so a pair is a mirror only when the pool
        // itself holds a single list.
        var offset = 1 + (int)((draw >> 32) % (ulong)(decks.Count - 1));
        return (decks[first], decks[(first + offset) % decks.Count]);
    }

    /// <summary>
    /// The decisive-pair tally over the matches finished so far. A pair counts only once both of
    /// its halves are complete, so an interrupted run still reads honestly instead of showing a
    /// lopsided subset.
    /// </summary>
    private static (int FirstBoth, int SecondBoth, int Split) PartialTally(
        Options options,
        Outcome[] outcomes,
        int normalCount)
    {
        var pairCount = options.MatchCount - normalCount;
        var firstBoth = 0;
        var secondBoth = 0;
        var split = 0;
        for (var index = 0; index < pairCount; index++)
        {
            var normal = outcomes[index];
            var swapped = outcomes[normalCount + index];
            if (normal is null || swapped is null)
            {
                continue;
            }

            var firstWonNormal = normal.Winner == normal.FirstAgentSeat;
            var firstWonSwapped = swapped.Winner == swapped.FirstAgentSeat;
            if (firstWonNormal && firstWonSwapped)
            {
                firstBoth++;
            }
            else if (!firstWonNormal && !firstWonSwapped)
            {
                secondBoth++;
            }
            else
            {
                split++;
            }
        }

        return (firstBoth, secondBoth, split);
    }

    /// <summary>
    /// 进度行里的 BO10 累计得分。只统计已经跑完的完整 5 对一组，
    /// 这样盯着日志就能直接看到"第一牌手现在多少分"，而不用等跑完的汇总——
    /// 中途叫停也留得下 BO10 口径的数字。
    /// </summary>
    private static (int Counted, int FirstPoints, int SecondPoints, int Ties) PartialBo10Tally(
        Options options,
        Outcome[] outcomes,
        int normalCount)
    {
        var pairCount = options.MatchCount - normalCount;
        var possibleBlocks = pairCount / Bo10PairsPerMatch;
        var counted = 0;
        var firstPoints = 0;
        var secondPoints = 0;
        var ties = 0;

        for (var block = 0; block < possibleBlocks; block++)
        {
            var complete = true;
            var firstGameWins = 0;
            for (var offset = 0; offset < Bo10PairsPerMatch; offset++)
            {
                var index = (block * Bo10PairsPerMatch) + offset;
                var normal = outcomes[index];
                var swapped = outcomes[normalCount + index];
                if (normal is null || swapped is null)
                {
                    complete = false;
                    break;
                }

                if (normal.Winner == normal.FirstAgentSeat)
                {
                    firstGameWins++;
                }

                if (swapped.Winner == swapped.FirstAgentSeat)
                {
                    firstGameWins++;
                }
            }

            // 遇到还没跑完的组要**跳过而不是中断**：并行调度下各组的完成顺序不严格，
            // 中断会让累计只覆盖一个很短的前缀，把 BO10 得分显示得很不稳。
            if (!complete)
            {
                continue;
            }

            counted++;
            if (firstGameWins > Bo10PairsPerMatch)
            {
                firstPoints++;
            }
            else if (firstGameWins < Bo10PairsPerMatch)
            {
                secondPoints++;
            }
            else
            {
                ties++;
            }
        }

        return (counted, firstPoints, secondPoints, ties);
    }

    private static void PrintDeckAssignments(
        Options options,
        (BenchmarkDeck A, BenchmarkDeck B)[] assignments,
        int normalCount)
    {
        if (!options.RandomDecks)
        {
            return;
        }

        Console.WriteLine("随机抽卡组（每对牌局两副不同卡组，两边方向各用一次）：");
        foreach (var group in assignments
                     .Take(normalCount)
                     .GroupBy(assignment => $"{assignment.A.Name}（A 位） vs {assignment.B.Name}（B 位）")
                     .OrderByDescending(group => group.Count()))
        {
            Console.WriteLine($"  {group.Key}：{group.Count()} 对");
        }

        Console.WriteLine();
    }

    private static void PrintHeader(
        Options options,
        IReadOnlyList<BenchmarkDeck> decks,
        int normalCount,
        int swappedCount)
    {
        Console.WriteLine("========== 牌手基准 ==========");
        if (options.RandomDecks)
        {
            Console.WriteLine(
                $"卡组：每对牌局从 {decks.Count} 副中随机抽两副 —— {string.Join("、", decks.Select(deck => deck.Name))}");
        }
        else
        {
            Console.WriteLine(decks.Count == 1 || decks[0].Id == decks[^1].Id
                ? $"卡组：双方均为 {decks[0].Name}"
                : $"卡组：A 位 {decks[0].Name} ｜ B 位 {decks[^1].Name}");
        }

        Console.WriteLine(
            $"对阵：{DisplayName(options.FirstAgent)}（第一牌手） vs {DisplayName(options.SecondAgent)}（第二牌手）");
        Console.WriteLine(options.SwapOrientation
            ? $"局数：{options.MatchCount}（{normalCount} 局原方向 + {swappedCount} 局换边对开）"
            : $"局数：{options.MatchCount}（未换边）");
        var headerSecondRollouts = options.SecondRolloutsPerAction > 0
            ? options.SecondRolloutsPerAction
            : options.RolloutsPerAction;
        var headerSecondHorizon = options.SecondFutureTurnHorizon > 0
            ? options.SecondFutureTurnHorizon
            : options.FutureTurnHorizon;
        var samePlanningConfig = headerSecondRollouts == options.RolloutsPerAction
                                 && headerSecondHorizon == options.FutureTurnHorizon;
        Console.WriteLine(samePlanningConfig
            ? $"种子：固定，基准 {options.SeedBase} ｜ 前瞻参数：{options.RolloutsPerAction} 次推演 × {options.FutureTurnHorizon} 回合"
            : $"种子：固定，基准 {options.SeedBase} ｜ 前瞻参数：" +
              $"第一牌手 {options.RolloutsPerAction} 次推演 × {options.FutureTurnHorizon} 回合 ／ " +
              $"第二牌手 {headerSecondRollouts} 次推演 × {headerSecondHorizon} 回合");
        Console.WriteLine(
            $"并行度：{options.MaxDegreeOfParallelism}（处理器 {Environment.ProcessorCount} 核）");
        Console.WriteLine();
    }

    private static void PrintResult(Options options, Outcome[] outcomes, TimeSpan elapsed)
    {
        var matchCount = outcomes.Length;
        var firstWins = 0;
        var firstAsStartingWins = 0;
        var firstAsStartingTotal = 0;
        var firstAsSecondWins = 0;
        var firstAsSecondTotal = 0;
        var startingPlayerWins = 0;

        foreach (var outcome in outcomes)
        {
            var firstAgentWon = outcome.Winner == outcome.FirstAgentSeat;
            if (firstAgentWon)
            {
                firstWins++;
            }

            if (outcome.FirstAgentSeat == outcome.StartingPlayer)
            {
                firstAsStartingTotal++;
                if (firstAgentWon)
                {
                    firstAsStartingWins++;
                }
            }
            else
            {
                firstAsSecondTotal++;
                if (firstAgentWon)
                {
                    firstAsSecondWins++;
                }
            }

            if (outcome.Winner == outcome.StartingPlayer)
            {
                startingPlayerWins++;
            }
        }

        var secondWins = matchCount - firstWins;
        var firstInterval = WilsonInterval(firstWins, matchCount);
        var secondInterval = WilsonInterval(secondWins, matchCount);

        var labels = SideLabels(options);

        Console.WriteLine("牌手                          胜局        胜率      95% 置信区间");
        PrintAgentRow(labels.First, firstWins, matchCount, firstInterval);
        PrintAgentRow(labels.Second, secondWins, matchCount, secondInterval);
        Console.WriteLine();

        Console.WriteLine(
            $"座位：先手胜 {startingPlayerWins}/{matchCount}（{Percent(startingPlayerWins, matchCount)}）" +
            $" ｜ 后手胜 {matchCount - startingPlayerWins}/{matchCount}（{Percent(matchCount - startingPlayerWins, matchCount)}）");
        Console.WriteLine(
            $"  {labels.First}：先手 {firstAsStartingWins}/{firstAsStartingTotal}" +
            $" ｜ 后手 {firstAsSecondWins}/{firstAsSecondTotal}");
        Console.WriteLine();

        var paired = options.SwapOrientation ? BuildPairedResult(options, outcomes) : null;
        if (paired is not null)
        {
            PrintPairedResult(options, paired);

            // BO10 是用户指定的主要评判口径，所以只要有足够的完整牌局就一并给出。
            var bo10 = BuildBo10Result(options, outcomes, paired);
            if (bo10 is not null)
            {
                PrintBo10Result(options, bo10);
            }
        }

        if (paired is { DecisivePairs: > 0 })
        {
            // The sign test runs on the decisive pairs, so the leader has to be read off the same
            // counts. Using overall wins here could name one side while the test was decided by the
            // other, which is exactly the kind of reporting mistake this project has already paid for.
            var leader = paired.FirstWinsBoth >= paired.SecondWinsBoth ? labels.First : labels.Second;
            var high = Math.Max(paired.FirstWinsBoth, paired.SecondWinsBoth);
            var low = Math.Min(paired.FirstWinsBoth, paired.SecondWinsBoth);
            Console.WriteLine(paired.PValue < 0.05
                ? $"结论：{leader} 的优势显著（配对检验 p = {paired.PValue:F4}，决定性牌局 {high}:{low}）。"
                : $"结论：没有统计显著差异（配对检验 p = {paired.PValue:F4}，决定性牌局 {high}:{low}）。");
        }
        else if (paired is { PairCount: > 0 })
        {
            Console.WriteLine("结论：没有统计显著差异——每一对牌局都由座位决定，牌手强弱没有体现出差异。");
        }
        else if (firstInterval.Low <= 0.5 && firstInterval.High >= 0.5)
        {
            Console.WriteLine("结论：第一牌手的胜率与五五开没有统计差异（置信区间跨过 50%）。");
        }
        else if (firstInterval.Low > secondInterval.High || secondInterval.Low > firstInterval.High)
        {
            var winner = firstWins > secondWins ? labels.First : labels.Second;
            Console.WriteLine($"结论：{winner} 的优势在 95% 置信水平上显著（未换边，牌局之间不独立，偏乐观）。");
        }
        else
        {
            Console.WriteLine("结论：置信区间重叠，差距尚不显著——请增加局数。");
        }

        var seconds = Math.Max(elapsed.TotalSeconds, 0.001);
        Console.WriteLine(
            $"耗时：{seconds:F1} 秒（{matchCount / seconds:F1} 局/秒）");
        Console.WriteLine("说明：胜率按牌手统计，已跨两个座位方向合并；换边对开用的是同一批牌局种子。");
        Console.WriteLine("      上面的置信区间把每一局当独立样本，因此偏乐观；判断差异请看配对检验的 p 值。");
        Console.WriteLine();
    }

    /// <summary>
    /// The paired comparison over the shared deals.
    /// <para>
    /// Every deal is played twice, once with the first agent in seat 0 and once with it in seat 1,
    /// so each agent is the starting player exactly once per deal. A deal the first agent wins from
    /// <em>both</em> seats is therefore evidence about the policy, while a deal each agent wins once
    /// was decided by the seat and says nothing about relative strength. This matters: on the
    /// 2026-09-12 Greedy comparison the raw win rate read 54.1% while 9163 of 10000 deals were
    /// seat-decided splits, so the headline number was carried entirely by 831 decisive deals
    /// against 6. The sign test (McNemar) on the decisive deals is the test that answers the
    /// question this benchmark exists to answer.
    /// </para>
    /// </summary>
    private static PairedResult BuildPairedResult(Options options, Outcome[] outcomes)
    {
        var normalCount = NormalCount(options);
        var pairCount = options.MatchCount - normalCount;
        if (!options.SwapOrientation || pairCount == 0)
        {
            return new PairedResult(0, 0, 0, 0, 1.0);
        }

        var firstWinsBoth = 0;
        var secondWinsBoth = 0;
        var splitPairs = 0;
        for (var index = 0; index < pairCount; index++)
        {
            var normal = outcomes[index];
            var swapped = outcomes[normalCount + index];
            var firstWonNormal = normal.Winner == normal.FirstAgentSeat;
            var firstWonSwapped = swapped.Winner == swapped.FirstAgentSeat;

            if (firstWonNormal && firstWonSwapped)
            {
                firstWinsBoth++;
            }
            else if (!firstWonNormal && !firstWonSwapped)
            {
                secondWinsBoth++;
            }
            else
            {
                splitPairs++;
            }
        }

        return new PairedResult(
            pairCount,
            firstWinsBoth,
            secondWinsBoth,
            splitPairs,
            McNemarPValue(firstWinsBoth, secondWinsBoth));
    }

    private static void PrintPairedResult(Options options, PairedResult paired)
    {
        var labels = SideLabels(options);
        Console.WriteLine();
        Console.WriteLine($"配对检验（同种子换边，共 {paired.PairCount} 对牌局）：");
        Console.WriteLine(
            $"  {labels.First}：两个座位都赢 {paired.FirstWinsBoth} 对");
        Console.WriteLine(
            $"  {labels.Second}：两个座位都赢 {paired.SecondWinsBoth} 对");
        Console.WriteLine(
            $"  各赢一边 {paired.SplitPairs} 对（由座位决定，不含强弱信息）");
        Console.WriteLine(
            $"  p = {paired.PValue:F4}（基于 {paired.DecisivePairs} 对决定性牌局；p < 0.05 才算有显著差异）");
        Console.WriteLine();
    }

    /// <summary>Five seat-swapped pairs make one BO10, i.e. ten games with five each way.</summary>
    private const int Bo10PairsPerMatch = 5;

    private static int NormalCount(Options options) =>
        options.SwapOrientation ? (options.MatchCount + 1) / 2 : options.MatchCount;

    /// <summary>
    /// The BO10 summary. Five seat-swapped pairs are one BO10; whoever wins more of the ten games
    /// takes the point, and a 5-5 split scores for nobody.
    /// <para>
    /// <see cref="ExpectedPoints"/> is the score an exactly equal pair of agents would average,
    /// computed from this run's own split rate. It is deliberately not 50: a 5-5 split scores for
    /// nobody, so the more pairs the seat decides, the lower the even-match baseline falls. With a
    /// 53.5% split rate it is about 37, and two literally identical agents would score 0 for both.
    /// </para>
    /// </summary>
    private sealed record Bo10Result(
        int Bo10Count,
        int FirstPoints,
        int SecondPoints,
        int Ties,
        double WinProbability,
        double ExpectedPoints,
        double PValue,
        bool FirstLeads);

    private static Bo10Result? BuildBo10Result(Options options, Outcome[] outcomes, PairedResult paired)
    {
        if (!options.SwapOrientation || paired.PairCount < Bo10PairsPerMatch)
        {
            return null;
        }

        var normalCount = NormalCount(options);
        var bo10Count = paired.PairCount / Bo10PairsPerMatch;
        var firstPoints = 0;
        var secondPoints = 0;
        var ties = 0;

        for (var block = 0; block < bo10Count; block++)
        {
            var firstGameWins = 0;
            for (var offset = 0; offset < Bo10PairsPerMatch; offset++)
            {
                var pairIndex = (block * Bo10PairsPerMatch) + offset;
                var normal = outcomes[pairIndex];
                var swapped = outcomes[normalCount + pairIndex];
                if (normal.Winner == normal.FirstAgentSeat)
                {
                    firstGameWins++;
                }

                if (swapped.Winner == swapped.FirstAgentSeat)
                {
                    firstGameWins++;
                }
            }

            if (firstGameWins > Bo10PairsPerMatch)
            {
                firstPoints++;
            }
            else if (firstGameWins < Bo10PairsPerMatch)
            {
                secondPoints++;
            }
            else
            {
                ties++;
            }
        }

        // 等强零假设：保持本次实测的"各赢一边"比例不变，把决定性牌局对半分。
        var splitRate = (double)paired.SplitPairs / paired.PairCount;
        var bothRate = (1.0 - splitRate) / 2.0;
        var winProbability = Bo10WinProbability(bothRate, splitRate, bothRate);
        var firstLeads = firstPoints >= secondPoints;
        var leaderPoints = Math.Max(firstPoints, secondPoints);

        return new Bo10Result(
            bo10Count,
            firstPoints,
            secondPoints,
            ties,
            winProbability,
            100.0 * winProbability,
            BinomialTailProbability(bo10Count, leaderPoints, winProbability),
            firstLeads);
    }

    /// <summary>
    /// Probability that one side takes a BO10, given the per-pair outcome distribution. Five pairs,
    /// each contributing 2, 1 or 0 game wins; the BO10 is won on six or more of the ten games.
    /// </summary>
    private static double Bo10WinProbability(double pWinBoth, double pSplit, double pLoseBoth)
    {
        var distribution = new double[11];
        distribution[0] = 1.0;
        for (var pair = 0; pair < Bo10PairsPerMatch; pair++)
        {
            var next = new double[11];
            for (var sum = 0; sum <= 10; sum++)
            {
                var weight = distribution[sum];
                if (weight == 0)
                {
                    continue;
                }

                next[sum] += weight * pLoseBoth;
                next[sum + 1] += weight * pSplit;
                next[sum + 2] += weight * pWinBoth;
            }

            distribution = next;
        }

        var total = 0.0;
        for (var sum = Bo10PairsPerMatch + 1; sum <= 10; sum++)
        {
            total += distribution[sum];
        }

        return total;
    }

    /// <summary>Exact one-sided binomial tail P(X &gt;= successes), by recurrence so no factorials are needed.</summary>
    private static double BinomialTailProbability(int trials, int successes, double probability)
    {
        if (successes <= 0)
        {
            return 1.0;
        }

        if (successes > trials || probability <= 0)
        {
            return 0.0;
        }

        if (probability >= 1)
        {
            return 1.0;
        }

        var complement = 1.0 - probability;
        var term = Math.Pow(complement, trials);
        var total = 0.0;
        for (var index = 0; index <= trials; index++)
        {
            if (index >= successes)
            {
                total += term;
            }

            term *= (double)(trials - index) / (index + 1) * probability / complement;
        }

        return Math.Min(1.0, total);
    }

    private static void PrintBo10Result(Options options, Bo10Result bo10)
    {
        var labels = SideLabels(options);
        var leader = bo10.FirstLeads ? labels.First : labels.Second;
        var scoreRate = (double)bo10.FirstPoints / bo10.Bo10Count;
        Console.WriteLine($"BO10 战绩（每 {Bo10PairsPerMatch} 对牌局合成 1 个 BO10，共 {bo10.Bo10Count} 个）：");
        Console.WriteLine(
            $"  {labels.First} {bo10.FirstPoints} 分 ｜ {labels.Second} {bo10.SecondPoints} 分 ｜ 平手 {bo10.Ties} 个");
        Console.WriteLine($"  第一牌手得分率 {scoreRate:P1}（这就是 BO10 口径的最终数字）");
        Console.WriteLine(
            $"  等强零假设：单个 BO10 得分概率 {bo10.WinProbability:P1}、平手 {1.0 - (2.0 * bo10.WinProbability):P1}" +
            $"，期望 {bo10.ExpectedPoints:F1} 分 —— 不是 50，平手双方都不得分");
        Console.WriteLine(
            $"  单侧检验 p {FormatPValue(bo10.PValue)}（" +
            (bo10.PValue < 0.001
                ? "差异极其明显"
                : bo10.PValue < 0.05 ? "有显著差异" : "看不出差异") +
            "；判定门槛见 BO10-JUDGEMENT.md）");
        Console.WriteLine($"  领先方：{leader}");
        Console.WriteLine();
    }

    /// <summary>p 值常常小到 double 直接打印成 0，所以极小值改用科学计数法，别丢掉量级。</summary>
    private static string FormatPValue(double pValue) => pValue switch
    {
        0 => "< 1e-300",
        < 1e-4 => $"= {pValue:E2}",
        _ => $"= {pValue:0.#####}"
    };

    /// <summary>
    /// Two-sided McNemar p-value with the continuity correction, using the chi-square form. For
    /// one degree of freedom the chi-square tail is exactly the complementary error function.
    /// </summary>
    private static double McNemarPValue(int firstOnly, int secondOnly)
    {
        var discordant = firstOnly + secondOnly;
        if (discordant == 0)
        {
            return 1.0;
        }

        var chiSquare = Math.Pow(Math.Abs(firstOnly - secondOnly) - 1.0, 2) / discordant;
        // The chi-square tail with one degree of freedom is exactly erfc(sqrt(x / 2)).
        return chiSquare <= 0 ? 1.0 : ComplementaryErrorFunction(Math.Sqrt(chiSquare / 2.0));
    }

    /// <summary>
    /// Complementary error function. Abramowitz &amp; Stegun 7.1.26, accurate to about 1.5e-7,
    /// which is far finer than any p-value decision this benchmark makes. It is written out here
    /// rather than taken from the framework so the harness stays independent of the runtime's
    /// math surface.
    /// </summary>
    private static double ComplementaryErrorFunction(double x)
    {
        var z = Math.Abs(x);
        var t = 1.0 / (1.0 + (0.5 * z));
        var polynomial = 0.0;
        for (var index = ErfcCoefficients.Length - 1; index >= 0; index--)
        {
            polynomial = (polynomial * t) + ErfcCoefficients[index];
        }

        var value = t * Math.Exp((-z * z) - 1.26551223 + polynomial);
        return x >= 0 ? value : 2.0 - value;
    }

    private static readonly double[] ErfcCoefficients =
    [
        1.00002368, 0.37409196, 0.09678418, -0.18628806, 0.27886807,
        -1.13520398, 1.48851587, -0.82215223, 0.17087277
    ];

    private static void PrintAgentRow(
        string name,
        int wins,
        int total,
        (double Low, double High) interval)
    {
        Console.WriteLine(
            $"{PadDisplay(name, 22)}{wins,5}/{total,-6}{Percent(wins, total),8}      [{interval.Low * 100:F1}%, {interval.High * 100:F1}%]");
    }

    /// <summary>
    /// Pads to a terminal column rather than a character count, so the CJK agent names line up
    /// with the numeric columns instead of drifting left.
    /// </summary>
    private static string PadDisplay(string text, int width)
    {
        var displayWidth = text.Sum(character => character > 0x2E80 ? 2 : 1);
        return text + new string(' ', Math.Max(1, width - displayWidth));
    }

    /// <summary>
    /// The Wilson score interval. It stays inside 0..1 and behaves sensibly for the small win
    /// counts an agent comparison produces, unlike the normal approximation.
    /// </summary>
    private static (double Low, double High) WilsonInterval(int wins, int total)
    {
        if (total <= 0)
        {
            return (0.0, 1.0);
        }

        const double z = 1.96;
        var proportion = (double)wins / total;
        var denominator = 1.0 + (z * z / total);
        var centre = (proportion + (z * z / (2.0 * total))) / denominator;
        var margin = z * Math.Sqrt(
            (proportion * (1.0 - proportion) / total) + (z * z / (4.0 * total * total))) / denominator;
        return (Math.Max(0.0, centre - margin), Math.Min(1.0, centre + margin));
    }

    private static string Percent(int wins, int total) =>
        total == 0 ? "0.0%" : $"{100.0 * wins / total:F1}%";

    /// <summary>
    /// One seat's complete agent configuration, with every per-side default already resolved.
    /// <para>
    /// This exists so that adding a new per-agent knob cannot silently bind it to a seat: everything
    /// that must travel with the agent lives here, and <see cref="ResolveSide"/> is the single place
    /// that reads the first/second side of <see cref="Options"/>.
    /// </para>
    /// </summary>
    public sealed record SideConfig(
        AgentKind Kind,
        int RolloutsPerAction,
        int FutureTurnHorizon,
        double RailMargin,
        LookaheadSelectionMode SelectionMode,
        double RobustnessPenalty,
        double StatisticalConfidence,
        LookaheadRolloutPolicy RolloutPolicy,
        int MulliganHorizon,
        int AlternateHorizon,
        int ThirdHorizon,
        LookaheadRolloutPolicy AlternateRolloutPolicy,
        LookaheadExtraPlayPointPolicy ExtraPlayPointPolicy,
        LookaheadRolloutPolicy OpponentRolloutPolicy,
        int OpponentRollouts,
        int OpponentHorizon,
        bool OpponentFirstActionOnly,
        int OwnNestedRollouts,
        int OwnNestedHorizon,
        int TreePly,
        bool TreeUsesSearchValue,
        bool TreeUsesMeanFollowUp,
        int DeepRollouts,
        bool EvaluatorEnsemble);

    private static SideConfig ResolveSide(Options options, bool first) => new(
        first ? options.FirstAgent : options.SecondAgent,
        first
            ? options.RolloutsPerAction
            : options.SecondRolloutsPerAction > 0 ? options.SecondRolloutsPerAction : options.RolloutsPerAction,
        first
            ? options.FutureTurnHorizon
            : options.SecondFutureTurnHorizon > 0 ? options.SecondFutureTurnHorizon : options.FutureTurnHorizon,
        first ? options.RailMargin : options.SecondRailMargin,
        first ? options.SelectionMode : options.SecondSelectionMode,
        first ? options.RobustnessPenalty : options.SecondRobustnessPenalty,
        first ? options.StatisticalConfidence : options.SecondStatisticalConfidence,
        first ? options.RolloutPolicy : options.SecondRolloutPolicy,
        first
            ? options.MulliganHorizon
            : options.SecondMulliganHorizon > 0 ? options.SecondMulliganHorizon : options.MulliganHorizon,
        first
            ? options.AlternateHorizon
            : options.SecondAlternateHorizon > 0 ? options.SecondAlternateHorizon : options.AlternateHorizon,
        first
            ? options.ThirdHorizon
            : options.SecondThirdHorizon > 0 ? options.SecondThirdHorizon : options.ThirdHorizon,
        first ? options.AlternateRolloutPolicy : options.SecondAlternateRolloutPolicy,
        first ? options.ExtraPlayPointPolicy : options.SecondExtraPlayPointPolicy,
        first ? options.OpponentRolloutPolicy : options.SecondOpponentRolloutPolicy,
        first
            ? options.OpponentRollouts
            : options.SecondOpponentRollouts > 0 ? options.SecondOpponentRollouts : options.OpponentRollouts,
        first
            ? options.OpponentHorizon
            : options.SecondOpponentHorizon > 0 ? options.SecondOpponentHorizon : options.OpponentHorizon,
        first ? options.OpponentFirstActionOnly : options.SecondOpponentFirstActionOnly,
        first
            ? options.OwnNestedRollouts
            : options.SecondOwnNestedRollouts > 0 ? options.SecondOwnNestedRollouts : options.OwnNestedRollouts,
        first
            ? options.OwnNestedHorizon
            : options.SecondOwnNestedHorizon > 0 ? options.SecondOwnNestedHorizon : options.OwnNestedHorizon,
        first ? options.TreePly : options.SecondTreePly,
        first ? options.TreeUsesSearchValue : options.SecondTreeUsesSearchValue,
        first ? options.TreeUsesMeanFollowUp : options.SecondTreeUsesMeanFollowUp,
        first ? options.DeepRollouts : options.SecondDeepRollouts,
        first ? options.EvaluatorEnsemble : options.SecondEvaluatorEnsemble);

    public static IPlayerAgent CreateAgent(SideConfig config, ulong seed)
    {
        // 负数表示"用牌手自己的默认值"，这样只有明确指定的一边才会被覆盖。
        var rail = config.RailMargin < 0 ? (double?)null : config.RailMargin;
        return config.Kind switch
        {
            AgentKind.Random => new RandomPlayerAgent(unchecked((int)(seed & 0x7FFF_FFFFUL))),
            AgentKind.Greedy => new GreedyPlayerAgent(),
            AgentKind.Baseline => new BaselineGreedyPlayerAgent(),
            AgentKind.Lookahead => new LookaheadPlayerAgent(
                rolloutsPerAction: config.RolloutsPerAction,
                futureTurnHorizon: config.FutureTurnHorizon,
                seed: seed,
                minimumPracticalAdvantage: rail,
                selectionMode: config.SelectionMode,
                robustnessPenalty: config.RobustnessPenalty,
                statisticalConfidence: config.StatisticalConfidence,
                rolloutPolicy: config.RolloutPolicy,
                mulliganHorizon: config.MulliganHorizon,
                alternateHorizon: config.AlternateHorizon,
                thirdHorizon: config.ThirdHorizon,
                alternateRolloutPolicy: config.AlternateRolloutPolicy,
                extraPlayPointPolicy: config.ExtraPlayPointPolicy,
                opponentRolloutPolicy: config.OpponentRolloutPolicy,
                useEvaluatorEnsemble: config.EvaluatorEnsemble,
                opponentRollouts: config.OpponentRollouts,
                opponentHorizon: config.OpponentHorizon,
                opponentFirstActionOnly: config.OpponentFirstActionOnly,
                ownNestedRollouts: config.OwnNestedRollouts,
                ownNestedHorizon: config.OwnNestedHorizon,
                treePly: config.TreePly,
                treeUsesSearchValue: config.TreeUsesSearchValue,
                deepRollouts: config.DeepRollouts,
                treeUsesMeanFollowUp: config.TreeUsesMeanFollowUp),
            AgentKind.LookaheadV2 => new LookaheadPlayerAgentV2(seed, config.RolloutsPerAction),
            AgentKind.LookaheadV1 => new LookaheadPlayerAgentV1(
                config.RolloutsPerAction, config.FutureTurnHorizon, seed, rail),
            AgentKind.BaselineLookahead => new BaselineLookaheadPlayerAgent(
                config.RolloutsPerAction, config.FutureTurnHorizon, seed),
            _ => throw new ArgumentOutOfRangeException(nameof(config), config.Kind, "Unsupported agent kind.")
        };
    }

    public static IPlayerAgent CreateAgent(
        AgentKind kind,
        ulong seed,
        int rollouts,
        int horizon,
        double railMargin = -1,
        LookaheadSelectionMode selectionMode = LookaheadSelectionMode.RuleAgentFallback,
        double robustnessPenalty = 1.0)
    {
        // 负数表示"用牌手自己的默认值"，这样只有明确指定的一边才会被覆盖。
        var rail = railMargin < 0 ? (double?)null : railMargin;
        return kind switch
        {
            AgentKind.Random => new RandomPlayerAgent(unchecked((int)(seed & 0x7FFF_FFFFUL))),
            AgentKind.Greedy => new GreedyPlayerAgent(),
            AgentKind.Baseline => new BaselineGreedyPlayerAgent(),
            AgentKind.Lookahead => new LookaheadPlayerAgent(
                rollouts, horizon, seed, rail, selectionMode, robustnessPenalty),
            AgentKind.LookaheadV1 => new LookaheadPlayerAgentV1(rollouts, horizon, seed, rail),
            AgentKind.BaselineLookahead => new BaselineLookaheadPlayerAgent(rollouts, horizon, seed),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unsupported agent kind.")
        };
    }

    /// <summary>
    /// SplitMix64. It keeps each agent's own randomness independent of the game seed while staying
    /// a pure function of it, so a benchmark run is reproducible from its base seed alone.
    /// </summary>
    private static ulong MixedSeed(ulong seed, ulong salt)
    {
        var value = seed + (salt * 0x9E37_79B9_7F4A_7C15UL);
        value ^= value >> 30;
        value *= 0xBF58_476D_1CE4_E5B9UL;
        value ^= value >> 27;
        value *= 0x94D0_49BB_1331_11EBUL;
        return value ^ (value >> 31);
    }
}
