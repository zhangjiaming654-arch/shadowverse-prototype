using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Shadowverse.Engine.Agents;
using Shadowverse.Engine.Decks;
using Shadowverse.Engine.Game;
using Shadowverse.Engine.Models;
using Shadowverse.Engine.Simulation;

namespace Shadowverse.ConsoleApp;

/// <summary>
/// 【因果续局】§11 预注册实验的实现：3.0 选的动作，是否真的比 2.0 选的动作更好？
/// <para>
/// <b>它回答什么</b>：在冻结 2.0 产生的局面分布上，把同一个局面复制两份，一份强制走 2.0 的动作、
/// 一份强制走 3.0 的动作，后续**都**交给冻结 2.0 用**相同随机源**打完，比较哪一边赢。
/// </para>
/// <para>
/// <b>它不回答什么</b>：两条分支的后续都是 2.0，所以这只测"单步动作的质量"，
/// **不能**推出"完整 3.0 策略更强"。这是分叉诊断，不是强度验收。
/// </para>
/// <para>
/// <b>随机源为什么可以对齐</b>：<c>LookaheadPlayerAgentV2</c> 的推演种子是
/// <c>SimulationSeed(decisionNumber, rollout)</c>，只取决于牌手自己的 <c>_seed</c> 与它内部的
/// 决策计数，<b>与局面无关</b>。所以两条分支只要用相同 seed 构造续局牌手，
/// 第 k 次推演用的就是同一个种子 —— "相同随机源"是可严格实现的，不是近似。
/// </para>
/// <para>
/// <b>代码不可改动的约束</b>：冻结的 1.0/2.0（<c>LookaheadPlayerAgentV1/V2</c>）一个字都不能改，
/// 有 <c>RunFrozenLookaheadV1Test/V2Test</c> 钉住。本文件是新增的独立诊断入口，
/// 唯一对引擎的改动是新增 <see cref="GameEngine.Clone"/>（只加 API，不改行为）。
/// </para>
/// </summary>
public static class CausalContinuationProbe
{
    /// <summary>冻结 2.0 的推演次数，写死在它的构造函数调用里（与 <c>ReplayForm.FrozenV2Rollouts</c> 一致）。</summary>
    public const int FrozenV2Rollouts = 60;

    /// <summary>续局时第二牌手的种子盐。两边分支都用同一个盐，所以随机源仍然对齐。</summary>
    private const ulong FrozenSeatSalt = 7UL;

    /// <summary>四种对局。镜像局两个方向等价，所以只跑一个方向；交叉局两个方向都要跑。</summary>
    public static readonly IReadOnlyList<Matchup> Matchups =
    [
        new("中速梦镜像", "DECK-003", "DECK-003"),
        new("郭龙镜像", "DECK-002", "DECK-002"),
        new("交叉：中速梦(先) vs 郭龙", "DECK-003", "DECK-002"),
        new("交叉：郭龙(先) vs 中速梦", "DECK-002", "DECK-003")
    ];

    public sealed record Matchup(string Name, string Deck1, string Deck2);

    /// <summary>一次运行的完整参数。写进 config.json，用于"可重放"验收。</summary>
    public sealed record Config(
        int GamesPerMatchup,
        ulong SourceSeedBase,
        ulong ProbeSeed,
        IReadOnlyList<ulong> ContinuationSeeds,
        string OutputDirectory,
        bool DryRun,
        /// <summary>[DIR-4A #3] 断点续跑：校验检查点后跳过已完成的源对局。</summary>
        bool Resume);

    /// <summary>
    /// 逐 (局面, 续局种子) 一行。列名与 §11 复审要求的完全一致。
    /// </summary>
    public sealed record Row(
        string Matchup,
        ulong SourceGameSeed,
        int DecisionIndex,
        string StateHash,
        int EligibleDecisionCount,
        int SampledDecisionIndex,
        string Action2,
        string Action3,
        bool Agreed,
        ulong? ContinuationSeed,
        int? Result2,
        int? Result3,
        double Delta)
    {
        public static string Header =>
            "matchup,sourceGameSeed,decisionIndex,stateHash,eligibleDecisionCount,sampledDecisionIndex," +
            "action2,action3,agreed,continuationSeed,result2,result3,delta";

        public string ToCsv() => string.Join(',',
            Csv(Matchup),
            SourceGameSeed.ToString(CultureInfo.InvariantCulture),
            DecisionIndex.ToString(CultureInfo.InvariantCulture),
            Csv(StateHash),
            EligibleDecisionCount.ToString(CultureInfo.InvariantCulture),
            SampledDecisionIndex.ToString(CultureInfo.InvariantCulture),
            Csv(Action2),
            Csv(Action3),
            Agreed ? "1" : "0",
            ContinuationSeed?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            Result2?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            Result3?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            Delta.ToString("0.####", CultureInfo.InvariantCulture));

        private static string Csv(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";
    }

    /// <summary>
    /// 一个统计单位 = 一个源对局里抽出的那一个决策。
    /// <para>
    /// <b>统计单位必须是局面，不是 (局面, 种子)</b>：同一局面的多次续局高度相关，
    /// 把它们展开成独立样本会人为压低方差。所以先在局面内对种子求平均，再以局面为样本。
    /// </para>
    /// </summary>
    public sealed record Unit(
        string Matchup,
        ulong SourceGameSeed,
        string StateHash,
        int DecisionIndex,
        int EligibleDecisionCount,
        int SampledDecisionIndex,
        string Action2,
        string Action3,
        bool Agreed,
        double Delta,
        IReadOnlyList<Row> SeedRows);

    public sealed record Stratum(
        string Name,
        int Units,
        int Disagreements,
        double DisagreementRate,
        double DeltaAll,
        double DeltaConditional);

    public sealed record Summary(
        Config Config,
        int SourceGamesAttempted,
        int SourceGamesWithoutEligibleDecision,
        int M,
        int D,
        double DisagreementRate,
        double DeltaConditional,
        double DeltaAll,
        double ConditionalCiLow,
        double ConditionalCiHigh,
        double AllCiLow,
        double AllCiHigh,
        int PositiveUnits,
        int NegativeUnits,
        int TieUnits,
        double MeanSeedDeltaSpread,
        IReadOnlyList<Stratum> ByMatchup,
        IReadOnlyList<Unit> Units);

    // ------------------------------------------------------------------ 运行

    /// <summary>
    /// 跑完整实验（或 dry run）。<paramref name="report"/> 用于进度输出。
    /// </summary>
    public static Summary Run(Config config, Action<string> report)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(report);

        var decks = new Dictionary<string, DeckDefinition>(StringComparer.Ordinal);
        DeckDefinition Deck(string id)
        {
            if (!decks.TryGetValue(id, out var deck))
            {
                deck = AgentSelfTests.CreateMatchDeck(id, "causal-" + id);
                decks[id] = deck;
            }

            return deck;
        }

        var units = new List<Unit>();
        var attempted = 0;
        var skipped = 0;

        for (var matchupIndex = 0; matchupIndex < Matchups.Count; matchupIndex++)
        {
            var matchup = Matchups[matchupIndex];
            report($"---------- {matchup.Name} ----------");
            for (var gameIndex = 0; gameIndex < config.GamesPerMatchup; gameIndex++)
            {
                attempted++;
                var sourceSeed = Mix(config.SourceSeedBase, (ulong)((matchupIndex * 1_000_003) + gameIndex));
                var sampled = CollectSampledDecision(matchup, sourceSeed, Deck);
                if (sampled is null)
                {
                    skipped++;
                    report($"  源对局 {sourceSeed}：没有「主回合 + 第一牌手 + 合法动作≥2」的决策，跳过");
                    continue;
                }

                var unit = EvaluateUnit(sampled, config, report);
                units.Add(unit);
                report(
                    $"  源对局 {sourceSeed}：决策#{unit.DecisionIndex}（合格 {unit.EligibleDecisionCount} 个，抽中第 {unit.SampledDecisionIndex} 个）"
                    + $" ｜ {(unit.Agreed ? "动作一致 → δ=0（不跑续局）" : $"分歧 δ={unit.Delta:+0.000;-0.000}（{unit.SeedRows.Count} 个种子）")}");
            }
        }

        return Summarize(config, attempted, skipped, units);
    }

    /// <summary>一个源对局的抽样结果。</summary>
    private sealed record Sampled(
        Matchup Matchup,
        ulong SourceSeed,
        GameState State,
        int DecisionIndex,
        int EligibleDecisionCount,
        int SampledDecisionIndex);

    /// <summary>
    /// 用冻结 2.0 打完一整局，并从**所有合格决策**里均匀抽一个。
    /// <para>
    /// <b>必须先于"是否分歧"抽样</b>：如果先问 2.0/3.0、再在分歧里挑一个，
    /// 抽样就与结果相关，而"取第一个合格决策"会系统性偏向前期。用蓄水池抽样避免两者。
    /// </para>
    /// </summary>
    private static Sampled? CollectSampledDecision(
        Matchup matchup,
        ulong sourceSeed,
        Func<string, DeckDefinition> deck)
    {
        // 抽样用的随机源与牌手随机源隔离，但由源种子决定，保证可重放。
        var rng = new Random(unchecked((int)(Mix(sourceSeed, 0xA11CEUL) & 0x7FFF_FFFFUL)));
        Sampled? chosen = null;
        var eligibleSeen = 0;
        var decisionIndex = 0;

        MatchRunner.PlayToEnd(
            GameEngine.CreateGame(deck(matchup.Deck1), deck(matchup.Deck2), sourceSeed),
            new LookaheadPlayerAgentV2(sourceSeed, FrozenV2Rollouts),
            new LookaheadPlayerAgentV2(Mix(sourceSeed, FrozenSeatSalt), FrozenV2Rollouts),
            onStep: step =>
            {
                if (step.BeforeState.Phase != GamePhase.Main || step.ActingPlayer != 0)
                {
                    return;
                }

                var index = decisionIndex++;
                var legal = GameEngine.GetLegalActions(step.BeforeState);
                if (legal.Count < 2)
                {
                    return;
                }

                eligibleSeen++;
                if (rng.Next(eligibleSeen) == 0)
                {
                    chosen = new Sampled(
                        matchup, sourceSeed, step.BeforeState, index, eligibleSeen, eligibleSeen - 1);
                }
            });

        return chosen;
    }

    /// <summary>对抽中的局面问 2.0 与 3.0，分歧则做完整交叉续局。</summary>
    private static Unit EvaluateUnit(Sampled sampled, Config config, Action<string> report)
    {
        var stateHash = DecisionFingerprint(sampled.State);
        var legal = GameEngine.GetLegalActions(sampled.State);
        var action2 = AskAction(new LookaheadPlayerAgentV2(config.ProbeSeed, FrozenV2Rollouts), sampled.State);
        var action3 = AskAction(ShippingV3(config.ProbeSeed), sampled.State);
        var canonical2 = Canonical(action2);
        var canonical3 = Canonical(action3);

        // 接线自检④：两个动作都必须来自同一份合法动作集合（不读对手隐藏信息就体现在这里：
        // 两者都只拿到 ToObservation + GetLegalActions 的产物）。
        if (!legal.Any(a => Canonical(a) == canonical2) || !legal.Any(a => Canonical(a) == canonical3))
        {
            throw new InvalidOperationException(
                $"牌手返回了非法动作：{canonical2} / {canonical3}。状态 {stateHash}，回合 {sampled.State.TurnNumber}。");
        }

        if (canonical2 == canonical3)
        {
            var zeroRow = new Row(
                sampled.Matchup.Name, sampled.SourceSeed, sampled.DecisionIndex, stateHash,
                sampled.EligibleDecisionCount, sampled.SampledDecisionIndex,
                canonical2, canonical3, Agreed: true, ContinuationSeed: null,
                Result2: null, Result3: null, Delta: 0);
            return new Unit(
                sampled.Matchup.Name, sampled.SourceSeed, stateHash, sampled.DecisionIndex,
                sampled.EligibleDecisionCount, sampled.SampledDecisionIndex,
                canonical2, canonical3, Agreed: true, Delta: 0, SeedRows: [zeroRow]);
        }

        // 完整交叉：每个续局种子都同时评估两个动作。不能把 (局面,种子) 当独立样本。
        var rows = new List<Row>();
        var sum2 = 0;
        var sum3 = 0;
        foreach (var continuationSeed in config.ContinuationSeeds)
        {
            var winner2 = RunBranch(sampled.State, action2, continuationSeed).Winner;
            var winner3 = RunBranch(sampled.State, action3, continuationSeed).Winner;
            var result2 = winner2 == 0 ? 1 : 0;
            var result3 = winner3 == 0 ? 1 : 0;
            sum2 += result2;
            sum3 += result3;
            rows.Add(new Row(
                sampled.Matchup.Name, sampled.SourceSeed, sampled.DecisionIndex, stateHash,
                sampled.EligibleDecisionCount, sampled.SampledDecisionIndex,
                canonical2, canonical3, Agreed: false, continuationSeed,
                result2, result3, result3 - result2));
        }

        var count = config.ContinuationSeeds.Count;
        var delta = count == 0 ? double.NaN : ((double)sum3 / count) - ((double)sum2 / count);
        _ = report;
        return new Unit(
            sampled.Matchup.Name, sampled.SourceSeed, stateHash, sampled.DecisionIndex,
            sampled.EligibleDecisionCount, sampled.SampledDecisionIndex,
            canonical2, canonical3, Agreed: false, delta, rows);
    }

    /// <summary>强制走一个动作，然后两边都用冻结 2.0、同一组续局种子打完。</summary>
    private static (int Winner, int Actions) RunBranch(GameState decisionState, GameAction forced, ulong continuationSeed)
    {
        // 显式克隆：协议要求"克隆完整状态两份"，两份必须从逐字节相同的快照出发。
        var after = GameEngine.Apply(GameEngine.Clone(decisionState), forced);
        var result = MatchRunner.PlayToEnd(
            after,
            new LookaheadPlayerAgentV2(continuationSeed, FrozenV2Rollouts),
            new LookaheadPlayerAgentV2(Mix(continuationSeed, FrozenSeatSalt), FrozenV2Rollouts));
        return (result.Winner, result.ActionCount);
    }

    /// <summary>3.0 的出货配置（与 <c>ConsoleTools.RunDecisionSensitivity</c> 里的基线一致）。</summary>
    private static LookaheadPlayerAgent ShippingV3(ulong seed) => new(
        rolloutsPerAction: 60,
        futureTurnHorizon: 1,
        alternateHorizon: 3,
        seed: seed,
        minimumPracticalAdvantage: 0.0);

    /// <summary>
    /// 哨兵用的 3.0 变体：只改一个旋钮。用来把"2.0 与 3.0 真的从不分歧"
    /// 与"询问路径本身问不出任何分歧"这两种可能区分开。
    /// <c>statisticalConfidence</c> 默认值就是 <c>DefaultStatisticalConfidence = 1.0</c>。
    /// </summary>
    private static LookaheadPlayerAgent SentinelV3(
        ulong seed, int rollouts = 60, int alternateHorizon = 3, double statisticalConfidence = 1.0) => new(
        rolloutsPerAction: rollouts,
        futureTurnHorizon: 1,
        alternateHorizon: alternateHorizon,
        seed: seed,
        statisticalConfidence: statisticalConfidence,
        minimumPracticalAdvantage: 0.0);

    /// <summary>
    /// 让牌手出招。每次询问都新建实例，避免内部决策计数器污染后续询问的可重放性。
    /// </summary>
    private static GameAction AskAction(IPlayerAgent agent, GameState state)
    {
        var observation = GameEngine.ToObservation(state, state.ActivePlayer);
        var legal = GameEngine.GetLegalActions(state);
        return agent is IStateAwarePlayerAgent aware
            ? aware.ChooseAction(state, observation, legal)
            : agent.ChooseAction(observation, legal);
    }

    // ------------------------------------------------------------------ 统计

    private static Summary Summarize(Config config, int attempted, int skipped, IReadOnlyList<Unit> units)
    {
        var m = units.Count;
        var disagreed = units.Where(u => !u.Agreed).ToList();
        var d = disagreed.Count;

        var deltasAll = units.Select(u => u.Delta).ToList();
        var deltasConditional = disagreed.Select(u => u.Delta).ToList();

        var deltaAll = Mean(deltasAll);
        var deltaConditional = Mean(deltasConditional);

        var allCi = BootstrapCi(deltasAll, config.SourceSeedBase ^ 0x5EEDUL);
        var conditionalCi = BootstrapCi(deltasConditional, config.SourceSeedBase ^ 0xC0DEUL);

        // 局面内多续局种子的离散度：用来报告"续局方差"，**不作通过线**。
        // 注意：两次独立续局结果不同的概率是 2p(1−p)，只有 p=0.5 时才等于 50%，
        // 所以绝不能拿"≈50%"当自检判据（这正是 §11 初稿的错误）。
        var spreads = disagreed
            .Where(u => u.SeedRows.Count > 1)
            .Select(u => u.SeedRows.Max(r => r.Delta) - u.SeedRows.Min(r => r.Delta))
            .ToList();
        var meanSpread = Mean(spreads);

        var strata = Matchups
            .Select(matchup =>
            {
                var inStratum = units.Where(u => u.Matchup == matchup.Name).ToList();
                var stratumDisagreed = inStratum.Where(u => !u.Agreed).ToList();
                return new Stratum(
                    matchup.Name,
                    inStratum.Count,
                    stratumDisagreed.Count,
                    inStratum.Count == 0 ? double.NaN : (double)stratumDisagreed.Count / inStratum.Count,
                    Mean(inStratum.Select(u => u.Delta).ToList()),
                    Mean(stratumDisagreed.Select(u => u.Delta).ToList()));
            })
            .ToList();

        return new Summary(
            config,
            attempted,
            skipped,
            m,
            d,
            m == 0 ? double.NaN : (double)d / m,
            deltaConditional,
            deltaAll,
            conditionalCi.Low,
            conditionalCi.High,
            allCi.Low,
            allCi.High,
            disagreed.Count(u => u.Delta > 0),
            disagreed.Count(u => u.Delta < 0),
            disagreed.Count(u => Math.Abs(u.Delta) < 1e-12) + units.Count(u => u.Agreed),
            meanSpread,
            strata,
            units);
    }

    private static double Mean(IReadOnlyList<double> values) =>
        values.Count == 0 ? double.NaN : values.Average();

    /// <summary>
    /// 局面级（cluster）自助法置信区间。重采样单位是**局面**，不是 (局面,种子)，
    /// 因为同一局面的多次续局不独立。
    /// </summary>
    private static (double Low, double High) BootstrapCi(IReadOnlyList<double> values, ulong seed, int resamples = 10_000)
    {
        if (values.Count == 0)
        {
            return (double.NaN, double.NaN);
        }

        var rng = new Random(unchecked((int)(seed & 0x7FFF_FFFFUL)));
        var means = new double[resamples];
        for (var r = 0; r < resamples; r++)
        {
            var sum = 0.0;
            for (var i = 0; i < values.Count; i++)
            {
                sum += values[rng.Next(values.Count)];
            }

            means[r] = sum / values.Count;
        }

        Array.Sort(means);
        return (means[(int)(resamples * 0.025)], means[(int)(resamples * 0.975)]);
    }

    // ------------------------------------------------------------------ 接线自检

    /// <summary>
    /// 跑接线验收（RES-5 要求的 20 局面 dry run）。返回 true 表示全部通过。
    /// 这些检查不看强弱结论，只看"仪器接对了没有"。
    /// </summary>
    public static bool RunWiringChecks(Config config, Action<string> report)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(report);

        var decks = new Dictionary<string, DeckDefinition>(StringComparer.Ordinal);
        DeckDefinition Deck(string id)
        {
            if (!decks.TryGetValue(id, out var deck))
            {
                deck = AgentSelfTests.CreateMatchDeck(id, "causal-" + id);
                decks[id] = deck;
            }

            return deck;
        }

        var allPassed = true;
        var checks = 0;
        void Check(string name, bool ok, string detail)
        {
            checks++;
            allPassed &= ok;
            report($"[{(ok ? "PASS" : "FAIL")}] {name}");
            report($"        {detail}");
        }

        var unitsByGame = new Dictionary<ulong, int>();
        var perMatchupSeen = new Dictionary<string, int>(StringComparer.Ordinal);
        var checkedDisagreements = 0;
        var syntheticPairs = 0;
        var replayChecked = false;
        var sentinelNames = new[] { "2.0冻结", "3.0出货", "3.0推演10", "3.0纯H1", "3.0闸门关" };
        var sentinelChoices = new List<string[]>();

        foreach (var matchup in Matchups)
        {
            perMatchupSeen[matchup.Name] = 0;
        }

        var target = Math.Min(config.GamesPerMatchup * Matchups.Count, 20);
        report($"接线验收：目标 {target} 个局面（四种对局均衡）");

        for (var matchupIndex = 0; matchupIndex < Matchups.Count && unitsByGame.Count < target; matchupIndex++)
        {
            var matchup = Matchups[matchupIndex];
            for (var gameIndex = 0; gameIndex < config.GamesPerMatchup && unitsByGame.Count < target; gameIndex++)
            {
                var sourceSeed = Mix(config.SourceSeedBase, (ulong)((matchupIndex * 1_000_003) + gameIndex));
                var sampled = CollectSampledDecision(matchup, sourceSeed, Deck);
                if (sampled is null)
                {
                    continue;
                }

                // ⑤ 每个源对局最多一个统计单位
                unitsByGame.TryGetValue(sourceSeed, out var seen);
                unitsByGame[sourceSeed] = seen + 1;
                perMatchupSeen[matchup.Name]++;

                var beforeHash = DecisionFingerprint(sampled.State);
                var action2 = AskAction(new LookaheadPlayerAgentV2(config.ProbeSeed, FrozenV2Rollouts), sampled.State);
                var action3 = AskAction(ShippingV3(config.ProbeSeed), sampled.State);
                var afterHash = DecisionFingerprint(sampled.State);

                // ①a 询问动作不得改动被问的状态
                Check(
                    $"询问动作不改动原状态（{matchup.Name} #{sampled.DecisionIndex}）",
                    beforeHash == afterHash,
                    $"before={beforeHash} after={afterHash}");

                var legal = GameEngine.GetLegalActions(sampled.State).Select(Canonical).ToHashSet(StringComparer.Ordinal);
                var canonical2 = Canonical(action2);
                var canonical3 = Canonical(action3);

                // ④ 两个动作都来自同一份合法动作集合
                Check(
                    $"两个动作都在合法集合内（{matchup.Name} #{sampled.DecisionIndex}）",
                    legal.Contains(canonical2) && legal.Contains(canonical3),
                    $"合法 {legal.Count} 个；2.0={canonical2}；3.0={canonical3}");

                // 仪器灵敏度哨兵：在同批局面上多问几组"明显不同"的配置。
                // 如果 2.0 与 3.0 从不分歧，而这里也从不分歧，那就是询问路径坏了；
                // 如果这里分歧很多、2.0/3.0 却是 0，那说明"两者动作一致"是真实结论。
                sentinelChoices.Add(
                [
                    canonical2,
                    canonical3,
                    Canonical(AskAction(SentinelV3(config.ProbeSeed, rollouts: 10), sampled.State)),
                    Canonical(AskAction(SentinelV3(config.ProbeSeed, alternateHorizon: 0), sampled.State)),
                    Canonical(AskAction(SentinelV3(config.ProbeSeed, statisticalConfidence: 0.0), sampled.State))
                ]);

                // 分支接线必须在**动作确实不同**时才被走到。若 2.0/3.0 恰好一致，
                // 就用一组合成的不同动作对来测同一套接线（克隆/续局/顺序/重放），
                // 否则"续局路径"会一次都没被执行过，接线验收就是假的。
                GameAction forced2;
                GameAction forced3;
                if (canonical2 != canonical3)
                {
                    forced2 = action2;
                    forced3 = action3;
                    checkedDisagreements++;
                }
                else
                {
                    var distinct = GameEngine.GetLegalActions(sampled.State)
                        .GroupBy(Canonical, StringComparer.Ordinal)
                        .Select(group => group.First())
                        .Take(2)
                        .ToList();
                    if (distinct.Count < 2)
                    {
                        report($"  （{matchup.Name} #{sampled.DecisionIndex} 动作一致且合法动作 <2，跳过分支接线检查）");
                        continue;
                    }

                    forced2 = distinct[0];
                    forced3 = distinct[1];
                    syntheticPairs++;
                    report(
                        $"  （{matchup.Name} #{sampled.DecisionIndex} 2.0/3.0 动作一致，" +
                        $"改用合成动作对 {Canonical(forced2)} / {Canonical(forced3)} 测分支接线；不计入统计）");
                }

                var continuationSeed = config.ContinuationSeeds[0];

                // 直接断言"两个克隆分支互不污染"：跑完整整两个分支之后，
                // 被两条分支共享的**决策状态**必须逐字节不变。
                // 这比"调换顺序结果一致"更强 —— 后者只能发现影响结果的那类污染。
                var baseHashBefore = GameEngine.StateFingerprint(sampled.State);

                // ①b 同状态 + 同动作 + 同随机源 ⇒ 逐次一致
                var branchA1 = RunBranch(sampled.State, forced2, continuationSeed);
                var branchA2 = RunBranch(sampled.State, forced2, continuationSeed);
                Check(
                    $"同状态+同动作+同种子结果逐次一致（{matchup.Name} #{sampled.DecisionIndex}）",
                    branchA1 == branchA2,
                    $"第一次 winner={branchA1.Winner}/actions={branchA1.Actions}；第二次 winner={branchA2.Winner}/actions={branchA2.Actions}");

                // ③ 调换分支执行顺序不改变结果（防共享可变对象/分支污染）
                var branchB = RunBranch(sampled.State, forced3, continuationSeed);
                var branchAAgain = RunBranch(sampled.State, forced2, continuationSeed);
                Check(
                    $"调换分支顺序不改变结果（{matchup.Name} #{sampled.DecisionIndex}）",
                    branchAAgain == branchA1,
                    $"先A后B时 A=({branchA1.Winner},{branchA1.Actions})；先B后A时 A=({branchAAgain.Winner},{branchAAgain.Actions})；B=({branchB.Winner},{branchB.Actions})");

                // ⑥ CSV 可重放：同一行重跑结果一致
                if (!replayChecked)
                {
                    replayChecked = true;
                    var rerun = RunBranch(sampled.State, forced2, continuationSeed);
                    Check(
                        "CSV 可重放（同一行重跑一致）",
                        rerun == branchA1,
                        $"重跑 winner={rerun.Winner}/actions={rerun.Actions}");
                }

                var baseHashAfter = GameEngine.StateFingerprint(sampled.State);
                Check(
                    $"分支互不污染（完整状态指纹）（{matchup.Name} #{sampled.DecisionIndex}）",
                    baseHashBefore == baseHashAfter,
                    $"before={baseHashBefore[..16]} after={baseHashAfter[..16]}");
            }
        }

        // ⑤ 统计单位唯一性
        var duplicated = unitsByGame.Where(pair => pair.Value > 1).ToList();
        Check(
            "每个源对局最多一个统计单位",
            duplicated.Count == 0 && unitsByGame.Count > 0,
            $"局面数 {unitsByGame.Count}；重复的源对局 {duplicated.Count} 个");

        // ⑦ 四种对局都有样本
        var missing = perMatchupSeen.Where(pair => pair.Value == 0).Select(pair => pair.Key).ToList();
        Check(
            "四种对局都有样本",
            missing.Count == 0,
            missing.Count == 0
                ? string.Join(" ｜ ", perMatchupSeen.Select(pair => $"{pair.Key}={pair.Value}"))
                : $"缺样本：{string.Join('、', missing)}");

        report($"检查项 {checks} 个；自然分歧局面 {checkedDisagreements} 个；用合成动作对补测分支接线 {syntheticPairs} 个");

        // 仪器灵敏度哨兵：把"2.0 与 3.0 从不分歧"与"仪器问不出任何分歧"区分开。
        if (sentinelChoices.Count > 0)
        {
            report("仪器灵敏度哨兵（同批局面上，不同配置两两之间的动作分歧数）：");
            for (var i = 0; i < sentinelNames.Length; i++)
            {
                for (var j = i + 1; j < sentinelNames.Length; j++)
                {
                    var differing = sentinelChoices.Count(choices => choices[i] != choices[j]);
                    report($"  {sentinelNames[i]} vs {sentinelNames[j]}：{differing}/{sentinelChoices.Count}");
                }
            }
        }
        report(allPassed ? "接线验收：全部通过" : "接线验收：有失败项，不得进入正式实验");
        return allPassed;
    }

    // ------------------------------------------------------------------ 正式运行路径（DIR-3）

    /// <summary>
    /// 正式实验的 CSV 行：**每个决策一行**（不是每个分歧一行）。
    /// <para>
    /// 这样 <c>T</c> / <c>M</c> / <c>D</c> 与三个 Δ 都能**从逐行数据独立复算**，
    /// 而不是只能在程序内部算出来。K 固定为 2，所以两个续局种子的结果各占一组列。
    /// </para>
    /// </summary>
    public sealed record CausalRow(
        string Matchup,
        ulong SourceGameSeed,
        int Seat,
        int TurnNumber,
        string Phase,
        int SeatDecisionOrdinal,
        int DecisionIndex,
        bool Eligible,
        int TPerGame,
        int MPerGame,
        /// <summary>该决策点的**完整状态指纹**（用于单行重放时核对重建的是同一个局面）。</summary>
        string StateFingerprint,
        string V2RuleAction,
        string V2PlannerAction,
        string V2FinalAction,
        bool V2RailOverrode,
        string V3RuleAction,
        string V3PlannerAction,
        string V3FinalAction,
        bool V3RailOverrode,
        bool Agreed,
        ulong? S1Seed,
        ulong? S2Seed,
        int? S1Result2,
        int? S1Result3,
        double? S1Delta,
        int? S2Result2,
        int? S2Result3,
        double? S2Delta)
    {
        public static string Header =>
            "matchup,sourceGameSeed,seat,turnNumber,phase,seatDecisionOrdinal,decisionIndex,eligible,"
            + "T_perGame,M_perGame,stateFingerprint,"
            + "v2RuleAction,v2PlannerAction,v2FinalAction,v2RailOverrode,"
            + "v3RuleAction,v3PlannerAction,v3FinalAction,v3RailOverrode,agreed,"
            + "s1_seed,s2_seed,s1_result2,s1_result3,s1_delta,s2_result2,s2_result3,s2_delta";

        /// <summary>该决策的收益 δᵢ：一致记 0，分歧取两个续局种子的均值。</summary>
        public double DecisionDelta =>
            Agreed ? 0 : ((S1Delta ?? 0) + (S2Delta ?? 0)) / 2.0;

        public string ToCsv() => string.Join(',',
            Q(Matchup), SourceGameSeed, Seat, TurnNumber, Q(Phase), SeatDecisionOrdinal, DecisionIndex,
            Eligible ? 1 : 0, TPerGame, MPerGame, Q(StateFingerprint),
            Q(V2RuleAction), Q(V2PlannerAction), Q(V2FinalAction), V2RailOverrode ? 1 : 0,
            Q(V3RuleAction), Q(V3PlannerAction), Q(V3FinalAction), V3RailOverrode ? 1 : 0,
            Agreed ? 1 : 0,
            U(S1Seed), U(S2Seed),
            N(S1Result2), N(S1Result3), F(S1Delta), N(S2Result2), N(S2Result3), F(S2Delta));

        private static string Q(string value) => "\"" + (value ?? string.Empty).Replace("\"", "\"\"") + "\"";
        private static string U(ulong? value) =>
            value?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        private static string N(int? value) =>
            value?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        private static string F(double? value) =>
            value?.ToString("0.####", CultureInfo.InvariantCulture) ?? string.Empty;
    }

    public sealed record MatchupStat(
        string Matchup, int T, int M, int D, double DeltaCond, double CiLow, double CiHigh);

    public sealed record FormalSummary(
        int T,
        int M,
        int D,
        double DeltaCall,
        double DeltaEligible,
        double DeltaCond,
        double CondCiLow,
        double CondCiHigh,
        IReadOnlyList<MatchupStat> ByMatchup,
        string CsvPath);

    /// <summary>
    /// **正式运行路径**。复用与 <see cref="Measure"/> 相同的持久影子同步方式
    /// （两个座位在每个决策点都询问影子 2.0 与影子 3.0），收集**全部**真实分歧，
    /// 每个分歧做 K=2 的完整交叉续局；统计以**源对局**为重采样单位，四种对局内分别有放回抽样再合并。
    /// </summary>
    public static FormalSummary RunFormal(Config config, Action<string> report)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(report);

        // 严格等于 2：原来用 Take(2) 会静默接受 3 个以上种子，与"恰好 K=2"不符
        if (config.ContinuationSeeds.Count != 2)
        {
            throw new InvalidOperationException(
                $"正式路径要求恰好 2 个续局种子（K=2），当前 {config.ContinuationSeeds.Count} 个。");
        }

        var continuationSeeds = config.ContinuationSeeds.ToList();

        // [DIR-5 #5] + [RES-9C] 冻结种子护栏（**双向**）：
        //      gamesPerMatchup == 125   <=>   四个冻结参数全部精确匹配
        // 单向护栏只挡住"冻结种子配小规模"，挡不住反过来的情况：
        // 125 局配错种子 —— 那会跑出"规模看起来像正式实验、种子却不是预注册种子"的数据。
        // 两个方向都必须拒绝：小规模测试请显式换一次性种子；125 局只允许预注册配置。
        var usesFrozenSeeds = config.SourceSeedBase == 1_469_598_103UL
            && config.ProbeSeed == 104_729UL
            && continuationSeeds.SequenceEqual(new ulong[] { 32_416_190_071UL, 32_416_187_567UL });
        var isFormalScale = config.GamesPerMatchup == 125;
        if (usesFrozenSeeds && !isFormalScale)
        {
            throw new InvalidOperationException(
                "正式冻结种子（1469598103 / 104729 / 32416190071 / 32416187567）只能用于 "
                + "gamesPerMatchup=125 的正式运行；小规模运行请显式换一组一次性种子。");
        }

        if (isFormalScale && !usesFrozenSeeds)
        {
            throw new InvalidOperationException(
                "gamesPerMatchup=125 只允许用于预注册的正式配置"
                + "（--causal-seed 1469598103 --causal-probe-seed 104729 "
                + "--causal-continuation-seeds 32416190071,32416187567）；"
                + $"当前收到 seed={config.SourceSeedBase} probe={config.ProbeSeed} "
                + $"continuation=[{string.Join(",", continuationSeeds)}]。"
                + "125 局是正式规模，换种子必须先改预注册文档并经用户批准，不得临时改用一次性种子。");
        }

        // 供 [DIR-4] 要求的"影子同步失败注入测试"使用：置 1 时强制第一处比对失败
        var forceSyncFail = Environment.GetEnvironmentVariable("DSH_CAUSAL_FORCE_SYNC_FAIL") == "1";
        // [DIR-4A] 第 4 条：影子同步**首次**不一致就必须中止，
        // 否则 500 局正式运行可能在第一步就不同步、却白跑几小时才失败。
        var abortRequested = false;

        var rows = new List<CausalRow>();
        var games = new List<(string Matchup, List<double> Deltas, List<bool> Disagreed)>();

        // ---------------- [DIR-4A #3] 逐源对局增量落盘 + 检查点 + 严格校验的续跑 ----------------
        var partialCsvPath = Path.Combine(config.OutputDirectory, "causal-formal.partial.csv");
        var checkpointPath = Path.Combine(config.OutputDirectory, "causal-formal.checkpoint.json");
        var configHash = ConfigHash(config);
        var consoleHash = AssemblyHash(typeof(CausalContinuationProbe).Assembly.Location);
        var engineHash = AssemblyHash(typeof(GameEngine).Assembly.Location);
        var completedSeeds = new HashSet<ulong>();
        var resumedGames = 0;

        // [DIR-5 #4] 新运行开始前先归档上次的**最终**产物，避免旧结果被误认成本次结果。
        // （续跑时不归档：那些文件正是要继续的数据。）
        if (!config.Resume)
        {
            var runId = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            var archive = Path.Combine(config.OutputDirectory, "archive", runId);
            var moved = 0;
            foreach (var name in new[]
                     {
                         "causal-formal.csv",
                         "causal-formal-manifest.json",
                         "causal-formal.checkpoint.json"
                     })
            {
                var source = Path.Combine(config.OutputDirectory, name);
                if (!File.Exists(source))
                {
                    continue;
                }

                Directory.CreateDirectory(archive);
                File.Move(source, Path.Combine(archive, name), overwrite: true);
                moved++;
            }

            if (moved > 0)
            {
                report($"[DIR-5 #4] 已归档上次运行的 {moved} 个最终产物 → {archive}");
            }
        }

        if (config.Resume)
        {
            if (!File.Exists(checkpointPath) || !File.Exists(partialCsvPath))
            {
                throw new InvalidOperationException(
                    "断点续跑被拒绝：找不到检查点或 partial CSV，无法确认已完成范围。");
            }

            var checkpoint = File.ReadAllText(checkpointPath);
            foreach (var (label, expected) in new[]
                     {
                         ("configHash", configHash),
                         ("consoleAssemblySha256", consoleHash),
                         ("engineAssemblySha256", engineHash)
                     })
            {
                if (!checkpoint.Contains($"\"{label}\": \"{expected}\"", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"断点续跑被拒绝：{label} 与检查点不一致（配置或代码已变），不得续跑旧数据。");
                }
            }

            // [DIR-5 #1] 严格校验每个源对局，而不是"partial 里出现过的种子就算完成"。
            // 崩溃窗口：程序先追加整局 CSV、后写检查点 —— 若追加中途崩溃，partial 尾部
            // 会留下半局。半局必须**整组丢弃重跑**，绝不能当成已完成（那会永久少算后半局）。
            var loaded = LoadRows(partialCsvPath);
            var groups = loaded.GroupBy(row => (row.Matchup, row.SourceGameSeed)).ToList();
            var validRows = new List<CausalRow>();
            for (var index = 0; index < groups.Count; index++)
            {
                var group = groups[index];
                var tValues = group.Select(row => row.TPerGame).Distinct().ToList();
                var mValues = group.Select(row => row.MPerGame).Distinct().ToList();
                var eligibleCount = group.Count(row => row.Eligible);
                var rowCount = group.Count();
                var selfConsistent =
                    tValues.Count == 1
                    && mValues.Count == 1
                    && rowCount == tValues[0]
                    && eligibleCount == mValues[0]
                    && group.Select(row => row.DecisionIndex).Distinct().Count() == rowCount
                    && group.Select(row => row.Seat).All(seat => seat is 0 or 1);

                if (selfConsistent)
                {
                    validRows.AddRange(group);
                    completedSeeds.Add(group.Key.SourceGameSeed);
                    continue;
                }

                var isTail = index == groups.Count - 1;
                if (!isTail)
                {
                    throw new InvalidOperationException(
                        $"断点续跑被拒绝：对局 {group.Key.Matchup}/{group.Key.SourceGameSeed} 数据不自洽"
                        + $"（行数 {rowCount} vs T_perGame={string.Join('/', tValues)}；合格 {eligibleCount} vs M_perGame={string.Join('/', mValues)}），"
                        + "且它不在文件尾部 —— 非尾部损坏一律拒绝恢复。");
                }

                report($"丢弃尾部残缺对局 {group.Key.Matchup}/{group.Key.SourceGameSeed}"
                    + $"（行数 {rowCount}，T_perGame={string.Join('/', tValues)}）—— 将整组重跑");
            }

            // CSV 是"哪些已完成"的权威来源；checkpoint 只作交叉核对，差异一律**报告**而不是拒绝：
            //  - checkpoint 落后（整局已写、检查点未更新）→ 该局有效，直接采纳；
            //  - checkpoint 超前（检查点已更新、CSV 尾行被截断）→ 该局无效，会被整组重跑。
            var checkpointRowCount = ReadCheckpointRowCount(checkpoint);
            if (checkpointRowCount != validRows.Count)
            {
                report($"注意：checkpoint 的 rowCount={checkpointRowCount} 与 partial 有效行数 {validRows.Count} 不一致"
                    + "（崩溃窗口的正常残留）；以 partial 为准，缺失的对局将整组重跑");
            }

            // [DIR-6 #1] 被丢弃的尾部残缺组必须**从 partial 里真正清掉**（只留表头 + validRows），
            // 否则补跑后续对局之后，这半局就不再处于文件尾部 —— 二次中断恢复会把它判成
            // "非尾部损坏"而直接拒绝，运行就此卡死。
            if (groups.Count != completedSeeds.Count)
            {
                var rewritten = new List<string> { CausalRow.Header };
                rewritten.AddRange(validRows.Select(row => row.ToCsv()));
                var temporaryPartial = partialCsvPath + ".tmp";
                File.WriteAllLines(temporaryPartial, rewritten);
                File.Move(temporaryPartial, partialCsvPath, overwrite: true);
                WriteCheckpoint(checkpointPath, configHash, consoleHash, engineHash, completedSeeds, validRows.Count);
                report($"[DIR-6 #1] 已原子重写 partial：保留表头 + {validRows.Count} 有效行，"
                    + $"并同步检查点（cleared {groups.Count - completedSeeds.Count} 组残缺）");
            }

            rows.AddRange(validRows);
            resumedGames = completedSeeds.Count;
            report($"断点续跑：校验通过，载入 {validRows.Count} 行 / {resumedGames} 个已完成源对局（丢弃尾部残缺 {groups.Count - resumedGames} 组），跳过它们");
        }
        else
        {
            File.WriteAllText(partialCsvPath, CausalRow.Header + Environment.NewLine);
        }

        // 载入的行必须先构建 bootstrap 分组，否则统计只覆盖新跑的对局
        foreach (var group in rows.GroupBy(row => (row.Matchup, row.SourceGameSeed)))
        {
            games.Add((
                group.Key.Matchup,
                group.Select(row => row.DecisionDelta).ToList(),
                group.Select(row => !row.Agreed).ToList()));
        }

        var firstNewRowIndex = rows.Count;

        var decks = new Dictionary<string, DeckDefinition>(StringComparer.Ordinal);
        DeckDefinition Deck(string id)
        {
            if (!decks.TryGetValue(id, out var deck))
            {
                deck = AgentSelfTests.CreateMatchDeck(id, "causal-formal-" + id);
                decks[id] = deck;
            }

            return deck;
        }

        var ruleAgent = new GreedyPlayerAgent();
        var syncChecks = 0;
        var syncMismatch = 0;

        // [DIR-4 #2 / DIR-4A #1] 四对局**轮转**：先跑每种对局的第一局，再跑各自的第二局……
        // 这样中断时四种对局的数据尽量均衡，而不是"前三种跑完、第四种一局都没有"。
        // 种子仍由 (matchupIndex, gameIndex) 决定，所以种子集合与改动前完全一致。
        for (var roundIndex = 0; roundIndex < config.GamesPerMatchup; roundIndex++)
        {
            for (var matchupIndex = 0; matchupIndex < Matchups.Count; matchupIndex++)
            {
                var matchup = Matchups[matchupIndex];
                var gameIndex = roundIndex;
                var sourceSeed = Mix(config.SourceSeedBase, (ulong)((matchupIndex * 1_000_003) + gameIndex));
                var seatSeeds = new[] { sourceSeed, Mix(sourceSeed, FrozenSeatSalt) };
                var real = new[] { new LookaheadPlayerAgentV2(seatSeeds[0], FrozenV2Rollouts), new LookaheadPlayerAgentV2(seatSeeds[1], FrozenV2Rollouts) };
                var shadow2 = new[] { new LookaheadPlayerAgentV2(seatSeeds[0], FrozenV2Rollouts), new LookaheadPlayerAgentV2(seatSeeds[1], FrozenV2Rollouts) };
                var shadow3 = new[] { ShadowV3(seatSeeds[0]), ShadowV3(seatSeeds[1]) };

                var seatOrdinals = new[] { 0, 0 };
                var raw = new List<(int Seat, int Turn, string Phase, int SeatOrdinal, int DecisionIndex, bool Eligible,
                    string V2Rule, string V2Planner, string V2Final, bool V2Rail,
                    string V3Rule, string V3Planner, string V3Final, bool V3Rail,
                    GameState State, GameAction V2Action, GameAction V3Action)>();
                var decisionIndex = 0;
                var eligibleTotal = 0;

                if (completedSeeds.Contains(sourceSeed))
                {
                    continue;   // [DIR-4A #3] 断点续跑：该源对局已完成，跳过
                }

                MatchRunner.PlayToEnd(
                    GameEngine.CreateGame(Deck(matchup.Deck1), Deck(matchup.Deck2), sourceSeed),
                    real[0],
                    real[1],
                    onStep: step =>
                    {
                        if (abortRequested)
                        {
                            return;   // 已判定无效：不再询问影子牌手，不再做任何测量工作
                        }

                        var seat = step.ActingPlayer;
                        var state = step.BeforeState;
                        var observation = GameEngine.ToObservation(state, state.ActivePlayer);
                        var legal = GameEngine.GetLegalActions(state);

                        var shadow2Action = AskAction(shadow2[seat], state);
                        syncChecks++;
                        if (Canonical(shadow2Action) != Canonical(step.Action)
                            || (forceSyncFail && syncChecks == 1))
                        {
                            syncMismatch++;
                            abortRequested = true;   // 立即中止：不再继续后续对局与续局
                        }

                        var v3Action = AskAction(shadow3[seat], state);
                        var v2Gate = GateInfo(real[seat].LastDecision, observation, legal, ruleAgent);
                        var v3Gate = GateInfo(shadow3[seat].LastDecision, observation, legal, ruleAgent);
                        var v2Final = Canonical(step.Action);
                        var v3Final = Canonical(v3Action);
                        var eligible = legal.Count >= 2;
                        if (eligible)
                        {
                            eligibleTotal++;
                        }

                        raw.Add((
                            seat, state.TurnNumber, state.Phase.ToString(), seatOrdinals[seat]++, decisionIndex++,
                            eligible,
                            v2Gate.Rule, v2Gate.Planner, v2Final, v2Gate.RailOverrode,
                            v3Gate.Rule, v3Gate.Planner, v3Final, v3Gate.RailOverrode,
                            state, step.Action, v3Action));
                    });

                var deltas = new List<double>();
                var disagreed = new List<bool>();
                foreach (var item in raw)
                {
                    if (abortRequested)
                    {
                        break;   // 无效运行：不跑任何续局
                    }
                    var agreed = string.Equals(item.V2Final, item.V3Final, StringComparison.Ordinal);
                    int? s1R2 = null, s1R3 = null, s2R2 = null, s2R3 = null;
                    double? s1D = null, s2D = null;
                    ulong? seedA = null, seedB = null;
                    if (!agreed)
                    {
                        // K=2 完整交叉：每个续局种子同时评估两个动作。
                        // 种子按局面派生（同局面两动作共享，不同局面不复用）。
                        seedA = DeriveContinuationSeed(continuationSeeds[0], sourceSeed, item.Seat, item.SeatOrdinal, 0);
                        seedB = DeriveContinuationSeed(continuationSeeds[1], sourceSeed, item.Seat, item.SeatOrdinal, 1);
                        var w2A = RunBranch(item.State, item.V2Action, seedA.Value).Winner;
                        var w3A = RunBranch(item.State, item.V3Action, seedA.Value).Winner;
                        var w2B = RunBranch(item.State, item.V2Action, seedB.Value).Winner;
                        var w3B = RunBranch(item.State, item.V3Action, seedB.Value).Winner;
                        s1R2 = w2A == item.Seat ? 1 : 0;
                        s1R3 = w3A == item.Seat ? 1 : 0;
                        s1D = s1R3 - s1R2;
                        s2R2 = w2B == item.Seat ? 1 : 0;
                        s2R3 = w3B == item.Seat ? 1 : 0;
                        s2D = s2R3 - s2R2;
                    }

                    var row = new CausalRow(
                        matchup.Name, sourceSeed, item.Seat, item.Turn, item.Phase, item.SeatOrdinal,
                        item.DecisionIndex, item.Eligible, raw.Count, eligibleTotal,
                        GameEngine.StateFingerprint(item.State),
                        item.V2Rule, item.V2Planner, item.V2Final, item.V2Rail,
                        item.V3Rule, item.V3Planner, item.V3Final, item.V3Rail,
                        agreed, seedA, seedB, s1R2, s1R3, s1D, s2R2, s2R3, s2D);
                    rows.Add(row);
                    deltas.Add(row.DecisionDelta);
                    disagreed.Add(!agreed);
                }

                games.Add((matchup.Name, deltas, disagreed));
                report(
                    $"  {matchup.Name} 局 {sourceSeed}：决策 {raw.Count}，合格 {eligibleTotal}，"
                    + $"分歧 {disagreed.Count(value => value)}");
                // [DIR-4A #3] 逐源对局增量落盘：写完即刷新，进程中断只损失当前这一局
                var gameRows = rows.Skip(firstNewRowIndex).ToList();
                firstNewRowIndex = rows.Count;
                if (gameRows.Count > 0)
                {
                    File.AppendAllLines(partialCsvPath, gameRows.Select(row => row.ToCsv()));
                }

                completedSeeds.Add(sourceSeed);
                WriteCheckpoint(checkpointPath, configHash, consoleHash, engineHash, completedSeeds, rows.Count);

                if (abortRequested)
                {
                    break;   // 无效运行：不再进入下一个源对局
                }
            }

            // [DIR-4A #4 修正] 上面的 break 只跳出**对局轮转的内层循环**；外层还按
            // GamesPerMatchup 继续 125 轮，每轮都会再空跑一局（onStep 立刻 return、
            // 0 行落盘、却把该种子记成"已完成"）。实测：强制同步失败时跑出 108 个
            // "已完成"种子、rowCount=0，白烧约 8 分钟。必须同时退出外层循环。
            if (abortRequested)
            {
                break;   // 整批作废：不再进入下一轮
            }
        }

        // [DIR-4] 第 6 条：影子同步任一失败必须使正式命令**退出非零**。
        // 无效原始数据保留成 INVALID 诊断文件，但不得生成正常的正式结论。
        if (syncMismatch > 0)
        {
            var invalidPath = Path.Combine(config.OutputDirectory, "causal-formal-INVALID.csv");
            var invalidLines = new List<string> { CausalRow.Header };
            invalidLines.AddRange(rows.Select(row => row.ToCsv()));
            File.WriteAllLines(invalidPath, invalidLines);
            report($"!! 影子同步失败 {syncMismatch}/{syncChecks}：结果标记 invalid，不生成正式结论");
            throw new InvalidOperationException(
                $"影子同步失败 {syncMismatch}/{syncChecks}，正式结果无效（诊断已写入 {invalidPath}）。");
        }

        var t = rows.Count;
        var m = rows.Count(row => row.Eligible);
        var d = rows.Count(row => !row.Agreed);
        var deltaCall = t == 0 ? double.NaN : rows.Sum(row => row.DecisionDelta) / t;
        var deltaEligible = m == 0 ? double.NaN
            : rows.Where(row => row.Eligible).Sum(row => row.DecisionDelta) / m;
        var deltaCond = d == 0 ? double.NaN
            : rows.Where(row => !row.Agreed).Sum(row => row.DecisionDelta) / d;
        var ci = ClusterBootstrap(games, config.SourceSeedBase ^ 0xB007UL);

        var byMatchup = Matchups.Select(matchup =>
        {
            var inStratum = rows.Where(row => row.Matchup == matchup.Name).ToList();
            var stratumDisagreed = inStratum.Where(row => !row.Agreed).ToList();
            var stratumCi = ClusterBootstrap(
                games.Where(game => game.Matchup == matchup.Name).ToList(), config.SourceSeedBase ^ 0xB007UL);
            return new MatchupStat(
                matchup.Name,
                inStratum.Count,
                inStratum.Count(row => row.Eligible),
                stratumDisagreed.Count,
                stratumDisagreed.Count == 0 ? double.NaN : stratumDisagreed.Average(row => row.DecisionDelta),
                stratumCi.Low,
                stratumCi.High);
        }).ToList();

        var csvPath = Path.Combine(config.OutputDirectory, "causal-formal.csv");
        var lines = new List<string> { CausalRow.Header };
        lines.AddRange(rows.Select(row => row.ToCsv()));
        File.WriteAllLines(csvPath, lines);
        WriteManifest(config, rows, t, m, d, csvPath);

        report($"影子同步 {syncChecks - syncMismatch}/{syncChecks}；T={t} M={m} D={d}");
        return new FormalSummary(t, m, d, deltaCall, deltaEligible, deltaCond, ci.Low, ci.High, byMatchup, csvPath);
    }

    /// <summary>配置身份哈希：这些字段任何一个变了，就不能续跑旧数据。</summary>
    private static string ConfigHash(Config config) =>
        Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(string.Join('|',
            "causal-formal-v1",
            config.GamesPerMatchup,
            config.SourceSeedBase,
            config.ProbeSeed,
            string.Join(',', config.ContinuationSeeds),
            FrozenV2Rollouts,
            string.Join(';', Matchups.Select(matchup => $"{matchup.Name}:{matchup.Deck1}:{matchup.Deck2}"))))));

    private static void WriteCheckpoint(
        string path,
        string configHash,
        string consoleHash,
        string engineHash,
        IReadOnlySet<ulong> completedSeeds,
        int rowCount)
    {
        var json = string.Join('\n',
            "{",
            $"  \"configHash\": \"{configHash}\",",
            $"  \"consoleAssemblySha256\": \"{consoleHash}\",",
            $"  \"engineAssemblySha256\": \"{engineHash}\",",
            $"  \"rowCount\": {rowCount},",
            $"  \"completedSourceGameCount\": {completedSeeds.Count},",
            $"  \"completedSourceGameSeeds\": [{string.Join(", ", completedSeeds.OrderBy(seed => seed))}]",
            "}");
        // [DIR-5 #2] 原子替换：先写临时文件再改名，避免"写到一半的检查点"被当成有效检查点
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, json);
        File.Move(temporary, path, overwrite: true);
    }

    /// <summary>从检查点 JSON 里读出 rowCount（缺失或损坏时返回 -1）。</summary>
    private static int ReadCheckpointRowCount(string checkpoint)
    {
        var match = System.Text.RegularExpressions.Regex.Match(checkpoint, "\"rowCount\":\\s*(\\d+)");
        return match.Success && int.TryParse(match.Groups[1].Value, out var value) ? value : -1;
    }

    /// <summary>把已落盘的 partial CSV 读回成行，供续跑复用（下游统计逻辑因此不需要改动）。</summary>
    private static List<CausalRow> LoadRows(string path)
    {
        var loaded = new List<CausalRow>();
        foreach (var line in File.ReadLines(path).Skip(1))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var f = SplitCsvLine(line);
            if (f.Count != 28)
            {
                // [DIR-5 #1] 解析错误**不得静默跳过** —— 那会把损坏藏起来
                throw new InvalidOperationException(
                    $"partial CSV 解析失败：字段数 {f.Count}（期望 28），行首：{line[..Math.Min(80, line.Length)]}");
            }

            loaded.Add(new CausalRow(
                f[0],
                ulong.Parse(f[1], CultureInfo.InvariantCulture),
                int.Parse(f[2], CultureInfo.InvariantCulture),
                int.Parse(f[3], CultureInfo.InvariantCulture),
                f[4],
                int.Parse(f[5], CultureInfo.InvariantCulture),
                int.Parse(f[6], CultureInfo.InvariantCulture),
                f[7] == "1",
                int.Parse(f[8], CultureInfo.InvariantCulture),
                int.Parse(f[9], CultureInfo.InvariantCulture),
                f[10],
                f[11], f[12], f[13], f[14] == "1",
                f[15], f[16], f[17], f[18] == "1",
                f[19] == "1",
                Nullable<ulong>(f[20]), Nullable<ulong>(f[21]),
                Nullable<int>(f[22]), Nullable<int>(f[23]), Nullable<double>(f[24]),
                Nullable<int>(f[25]), Nullable<int>(f[26]), Nullable<double>(f[27])));
        }

        return loaded;
    }

    private static T? Nullable<T>(string text) where T : struct =>
        text.Length == 0 ? null : (T)Convert.ChangeType(text, typeof(T), CultureInfo.InvariantCulture);

    private static List<string> SplitCsvLine(string line)
    {
        var fields = new List<string>();
        var builder = new System.Text.StringBuilder();
        var inQuotes = false;
        for (var index = 0; index < line.Length; index++)
        {
            var ch = line[index];
            if (inQuotes)
            {
                if (ch == '"')
                {
                    if (index + 1 < line.Length && line[index + 1] == '"')
                    {
                        builder.Append('"');
                        index++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    builder.Append(ch);
                }
            }
            else if (ch == '"')
            {
                inQuotes = true;
            }
            else if (ch == ',')
            {
                fields.Add(builder.ToString());
                builder.Clear();
            }
            else
            {
                builder.Append(ch);
            }
        }

        fields.Add(builder.ToString());
        return fields;
    }

    /// <summary>
    /// [DIR-4 #4] 伴随 manifest：记录实验配置、**代码身份**与已完成源对局清单。
    /// <para>
    /// 代码身份用两个程序集的 SHA256 —— 不需要 git plumbing，且续跑时必须严格比对：
    /// 程序集变了就说明代码变了，旧的 partial 数据不得续跑。
    /// </para>
    /// </summary>
    private static void WriteManifest(
        Config config, IReadOnlyList<CausalRow> rows, int t, int m, int d, string csvPath)
    {
        var completed = rows.Select(row => row.SourceGameSeed).Distinct().OrderBy(seed => seed).ToList();
        var json = string.Join('\n',
            "{",
            $"  \"generatedAt\": \"{DateTime.Now:o}\",",
            $"  \"csvPath\": \"{csvPath.Replace("\\", "\\\\")}\",",
            $"  \"csvHeader\": \"{CausalRow.Header}\",",
            $"  \"T\": {t},",
            $"  \"M\": {m},",
            $"  \"D\": {d},",
            $"  \"gamesPerMatchup\": {config.GamesPerMatchup},",
            $"  \"sourceSeedBase\": {config.SourceSeedBase},",
            $"  \"probeSeed\": {config.ProbeSeed},",
            $"  \"continuationBaseSeeds\": [{string.Join(", ", config.ContinuationSeeds)}],",
            $"  \"K\": {config.ContinuationSeeds.Count},",
            $"  \"consoleAssemblySha256\": \"{AssemblyHash(typeof(CausalContinuationProbe).Assembly.Location)}\",",
            $"  \"engineAssemblySha256\": \"{AssemblyHash(typeof(GameEngine).Assembly.Location)}\",",
            $"  \"completedSourceGameCount\": {completed.Count},",
            $"  \"completedSourceGameSeeds\": [{string.Join(", ", completed)}]",
            "}");
        File.WriteAllText(Path.Combine(config.OutputDirectory, "causal-formal-manifest.json"), json);
    }

    private static string AssemblyHash(string path)
    {
        if (!File.Exists(path))
        {
            return "missing";
        }

        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    /// <summary>[DIR-4A #7] 零分歧 bootstrap 专项测试的公开入口（生产路径不变）。</summary>
    public static (double Low, double High) BootstrapForTest(
        IReadOnlyList<(string Matchup, List<double> Deltas, List<bool> Disagreed)> games) =>
        ClusterBootstrap(games, 12_345UL);

    /// <summary>
    /// **零分歧 bootstrap 专项测试**。
    /// <para>
    /// 旧实现把"抽到的 replicate 一个分歧都没含"的 `Δ_cond` 当成 0，会把区间人为拉窄、
    /// 把下界拉向 0；正确做法是丢弃并重抽，抽不满就报**不可估计**。
    /// </para>
    /// </summary>
    public static bool RunBootstrapChecks(Action<string> report)
    {
        ArgumentNullException.ThrowIfNull(report);

        var passed = true;
        var checks = 0;
        void Check(string name, bool ok, string detail)
        {
            checks++;
            passed &= ok;
            report($"[{(ok ? "PASS" : "FAIL")}] {name} ｜ {detail}");
        }

        // A. 全部源对局都没有分歧 → Δ_cond 未定义 → 必须报不可估计，不得给出 [0,0]
        var allZero = new List<(string Matchup, List<double> Deltas, List<bool> Disagreed)>
        {
            ("m", [0.0, 0.0, 0.0], [false, false, false]),
            ("m", [0.0, 0.0], [false, false])
        };
        var caseA = BootstrapForTest(allZero);
        Check(
            "全部零分歧时不得给出 [0,0]，必须不可估计",
            double.IsNaN(caseA.Low) && double.IsNaN(caseA.High),
            $"CI=[{caseA.Low}, {caseA.High}]（期望 NaN）");

        // B. 两个源对局：一个含分歧 δ=+1，一个零分歧。
        //    正确实现只取分歧 → 每次重采样只要抽到有分歧那局就是 1.0，CI=[1,1]。
        //    填 0 的实现在"两次都抽到零分歧局"（概率 25%）时给出 0.0，下界会被拉到 0。
        var mixed = new List<(string Matchup, List<double> Deltas, List<bool> Disagreed)>
        {
            ("m", [1.0], [true]),
            ("m", [0.0], [false])
        };
        var caseB = BootstrapForTest(mixed);
        Check(
            "零分歧对局不得把 Δ_cond 区间拉向 0",
            Math.Abs(caseB.Low - 1.0) < 1e-9 && Math.Abs(caseB.High - 1.0) < 1e-9,
            $"CI=[{caseB.Low}, {caseB.High}]（期望 [1, 1]；填 0 的实现下界会是 0）");

        report($"检查项 {checks} 个");
        return passed;
    }

    /// <summary>
    /// 按**源对局**聚类的自助法：四种对局内分别有放回抽样、再合并，
    /// 每次重采样携带该源对局的全部决策（含零分歧的对局）。固定 10,000 次，报 percentile 95% CI。
    /// <para>正态近似只允许出现在"运行前产能估算"里，不得用于正式结论。</para>
    /// </summary>
    private static (double Low, double High) ClusterBootstrap(
        IReadOnlyList<(string Matchup, List<double> Deltas, List<bool> Disagreed)> games,
        ulong seed, int resamples = 10_000)
    {
        var byMatchup = games.GroupBy(game => game.Matchup).ToList();
        if (games.Count == 0 || byMatchup.Count == 0)
        {
            return (double.NaN, double.NaN);
        }

        var rng = new Random(unchecked((int)(seed & 0x7FFF_FFFFUL)));
        // Δ_cond 只在分歧上定义。抽到的 replicate 若一个分歧都没含，它**未定义**，
        // 不能当成 0（那会人为收窄区间）。丢弃并继续抽，直到凑满 resamples 个有效 replicate。
        var means = new List<double>(resamples);
        var maxAttempts = resamples * 100;
        var attempts = 0;
        while (means.Count < resamples && attempts < maxAttempts)
        {
            attempts++;
            var sum = 0.0;
            var count = 0;
            foreach (var stratum in byMatchup)
            {
                var pool = stratum.ToList();
                for (var pick = 0; pick < pool.Count; pick++)
                {
                    var chosen = pool[rng.Next(pool.Count)];
                    for (var index = 0; index < chosen.Deltas.Count; index++)
                    {
                        if (!chosen.Disagreed[index])
                        {
                            continue;   // Δ_cond 只在分歧上定义
                        }

                        sum += chosen.Deltas[index];
                        count++;
                    }
                }
            }

            if (count == 0)
            {
                continue;
            }

            means.Add(sum / count);
        }

        if (means.Count < resamples)
        {
            // 有效率不足：报不可估计，而不是编一个数出来
            return (double.NaN, double.NaN);
        }

        var sorted = means.ToArray();
        Array.Sort(sorted);
        return (sorted[(int)(resamples * 0.025)], sorted[(int)(resamples * 0.975)]);
    }

    // ------------------------------------------------------------------ 单行重放（DIR-4A #5）

    /// <summary>
    /// **真正的单行重放**：仅凭 manifest 与 CSV 里的一条分歧行，重建源对局、定位到那个决策点、
    /// 核对状态指纹，再用该行记录的两个种子重跑，逐项核对六个结果字段。
    /// <para>
    /// 它与"统计可从逐行复算"是两件事：后者只证明算术自洽，前者才证明**重建的是同一个局面**。
    /// </para>
    /// </summary>
    public static bool ReplaySingleRow(Config config, Action<string> report)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(report);

        var csvPath = Path.Combine(config.OutputDirectory, "causal-formal.csv");
        var manifestPath = Path.Combine(config.OutputDirectory, "causal-formal-manifest.json");
        if (!File.Exists(csvPath) || !File.Exists(manifestPath))
        {
            throw new InvalidOperationException("单行重放需要 causal-formal.csv 与 causal-formal-manifest.json。");
        }

        // 代码身份必须与产生数据时一致
        var manifest = File.ReadAllText(manifestPath);
        var consoleHash = AssemblyHash(typeof(CausalContinuationProbe).Assembly.Location);
        var engineHash = AssemblyHash(typeof(GameEngine).Assembly.Location);
        if (!manifest.Contains(consoleHash, StringComparison.Ordinal)
            || !manifest.Contains(engineHash, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("单行重放被拒绝：代码身份与 manifest 不一致。");
        }

        var rows = LoadRows(csvPath);
        var target = rows.FirstOrDefault(row => !row.Agreed)
            ?? throw new InvalidOperationException("CSV 里没有分歧行，无法做单行重放。");

        report($"重放目标：{target.Matchup} 源种子 {target.SourceGameSeed} 座位 {target.Seat} "
            + $"座内序号 {target.SeatDecisionOrdinal} 指纹 {target.StateFingerprint[..16]}…");

        var decks = new Dictionary<string, DeckDefinition>(StringComparer.Ordinal);
        DeckDefinition Deck(string id)
        {
            if (!decks.TryGetValue(id, out var deck))
            {
                // 标签必须与 RunFormal 完全一致：DeckName 是指纹的一部分，
                // 用不同标签重建会得到不同指纹（实测踩到过）。
                deck = AgentSelfTests.CreateMatchDeck(id, "causal-formal-" + id);
                decks[id] = deck;
            }

            return deck;
        }

        var matchup = Matchups.First(item => item.Name == target.Matchup);
        var seatSeeds = new[] { target.SourceGameSeed, Mix(target.SourceGameSeed, FrozenSeatSalt) };
        var real = new[]
        {
            new LookaheadPlayerAgentV2(seatSeeds[0], FrozenV2Rollouts),
            new LookaheadPlayerAgentV2(seatSeeds[1], FrozenV2Rollouts)
        };
        var shadow3 = new[] { ShadowV3(seatSeeds[0]), ShadowV3(seatSeeds[1]) };
        var seatOrdinals = new[] { 0, 0 };
        GameState? located = null;
        GameAction locatedV2 = null!;
        GameAction locatedV3 = null!;

        MatchRunner.PlayToEnd(
            GameEngine.CreateGame(Deck(matchup.Deck1), Deck(matchup.Deck2), target.SourceGameSeed),
            real[0],
            real[1],
            onStep: step =>
            {
                var seat = step.ActingPlayer;
                var ordinal = seatOrdinals[seat]++;
                // 影子 3.0 必须在**每个**决策点被询问，否则它的决策序号与实战不同步
                var v3Action = AskAction(shadow3[seat], step.BeforeState);
                if (seat != target.Seat || ordinal != target.SeatDecisionOrdinal)
                {
                    return;
                }

                located = step.BeforeState;
                locatedV2 = step.Action;
                locatedV3 = v3Action;
            });

        if (located is null)
        {
            throw new InvalidOperationException("单行重放失败：无法在重建的源对局里定位到该决策点。");
        }

        var checks = new List<(string Name, bool Pass, string Detail)>
        {
            ("状态指纹一致（重建的是同一个局面）",
                GameEngine.StateFingerprint(located) == target.StateFingerprint,
                $"重放 {GameEngine.StateFingerprint(located)[..16]}… vs CSV {target.StateFingerprint[..16]}…"),
            ("2.0 动作一致", Canonical(locatedV2) == target.V2FinalAction,
                $"{Canonical(locatedV2)} vs {target.V2FinalAction}"),
            ("3.0 动作一致", Canonical(locatedV3) == target.V3FinalAction,
                $"{Canonical(locatedV3)} vs {target.V3FinalAction}")
        };

        var seedA = target.S1Seed!.Value;
        var seedB = target.S2Seed!.Value;
        var w2A = RunBranch(located, locatedV2, seedA).Winner;
        var w3A = RunBranch(located, locatedV3, seedA).Winner;
        var w2B = RunBranch(located, locatedV2, seedB).Winner;
        var w3B = RunBranch(located, locatedV3, seedB).Winner;
        var r2A = w2A == target.Seat ? 1 : 0;
        var r3A = w3A == target.Seat ? 1 : 0;
        var r2B = w2B == target.Seat ? 1 : 0;
        var r3B = w3B == target.Seat ? 1 : 0;

        checks.Add(("s1_result2 一致", r2A == target.S1Result2, $"{r2A} vs {target.S1Result2}"));
        checks.Add(("s1_result3 一致", r3A == target.S1Result3, $"{r3A} vs {target.S1Result3}"));
        checks.Add(("s1_delta 一致", r3A - r2A == target.S1Delta, $"{r3A - r2A} vs {target.S1Delta}"));
        checks.Add(("s2_result2 一致", r2B == target.S2Result2, $"{r2B} vs {target.S2Result2}"));
        checks.Add(("s2_result3 一致", r3B == target.S2Result3, $"{r3B} vs {target.S2Result3}"));
        checks.Add(("s2_delta 一致", r3B - r2B == target.S2Delta, $"{r3B - r2B} vs {target.S2Delta}"));

        var allPassed = true;
        foreach (var (name, pass, detail) in checks)
        {
            allPassed &= pass;
            report($"[{(pass ? "PASS" : "FAIL")}] {name} ｜ {detail}");
        }

        report(allPassed
            ? "单行重放：全部一致 —— 重建的是同一局面，六个结果字段逐项相符。"
            : "单行重放：有不一致项，重放能力未达标。");
        return allPassed;
    }

    // ------------------------------------------------------------------ 两入口一致性（DIR-4A #1）

    /// <summary>
    /// **两入口逐行一致性断言**（GPT 允许的"抽出共用扫描核心"的替代方案）。
    /// <para>
    /// 对同一份配置分别跑 <see cref="Measure"/> 与 <see cref="RunFormal"/>，
    /// 按 `(对局, 源种子, 决策序号)` 配对，逐字段比较两个入口对每个决策的判定。
    /// 如果哪天有人在其中一个入口里动了决策序号或影子调用顺序，这条会立刻失败 ——
    /// 这正是"两份扫描骨架"最大的复发风险。
    /// </para>
    /// </summary>
    public static bool RunEntryConsistencyCheck(Config config, Action<string> report)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(report);

        var measure = Measure(config, _ => { });
        var formal = RunFormal(config, _ => { });
        var formalRows = LoadRows(formal.CsvPath);

        var mismatches = new List<string>();
        var compared = 0;

        var measureIndex = measure.Records.ToDictionary(
            record => (record.Matchup, record.SourceGameSeed, record.DecisionIndex));
        var formalIndex = formalRows.ToDictionary(
            row => (row.Matchup, row.SourceGameSeed, row.DecisionIndex));

        foreach (var key in measureIndex.Keys)
        {
            if (!formalIndex.TryGetValue(key, out var row))
            {
                mismatches.Add($"RunFormal 缺少决策 {key}");
                continue;
            }

            var record = measureIndex[key];
            compared++;
            void Compare(string field, string left, string right)
            {
                if (!string.Equals(left, right, StringComparison.Ordinal) && mismatches.Count < 20)
                {
                    mismatches.Add($"{key} {field}：Measure={left} ｜ RunFormal={right}");
                }
            }

            Compare("seat", record.Seat.ToString(), row.Seat.ToString());
            Compare("turnNumber", record.TurnNumber.ToString(), row.TurnNumber.ToString());
            Compare("phase", record.Phase, row.Phase);
            Compare("eligible", record.Eligible ? "1" : "0", row.Eligible ? "1" : "0");
            Compare("v2Final", record.V2Final, row.V2FinalAction);
            Compare("v2Rule", record.V2Rule, row.V2RuleAction);
            Compare("v2Planner", record.V2Planner, row.V2PlannerAction);
            Compare("v2Rail", record.V2RailOverrode ? "1" : "0", row.V2RailOverrode ? "1" : "0");
            Compare("v3Final", record.V3Final, row.V3FinalAction);
            Compare("v3Rule", record.V3Rule, row.V3RuleAction);
            Compare("v3Planner", record.V3Planner, row.V3PlannerAction);
            Compare("v3Rail", record.V3RailOverrode ? "1" : "0", row.V3RailOverrode ? "1" : "0");
        }

        foreach (var key in formalIndex.Keys)
        {
            if (!measureIndex.ContainsKey(key))
            {
                mismatches.Add($"Measure 缺少决策 {key}");
            }
        }

        report($"Measure 决策 {measure.Records.Count} 个 ｜ RunFormal 决策 {formalRows.Count} 个 ｜ 配对比较 {compared} 个");
        foreach (var mismatch in mismatches.Take(8))
        {
            report($"  差异：{mismatch}");
        }

        var passed = mismatches.Count == 0 && compared > 0;
        report(passed
            ? "两入口逐行一致：每个决策的座位/回合/阶段/合格性与两个牌手的规则·规划器·最终动作·闸门覆盖全部相同。"
            : $"两入口存在 {mismatches.Count} 处差异，不得视作同一实现。");
        return passed;
    }

    // ------------------------------------------------------------------ 护符确定性测试（DIR-4A #6）

    /// <summary>护符身份串，与 <c>StateFingerprint</c> 里编码护符的公式保持一致。</summary>
    private static List<string> AmuletIdentities(GameState state) => state.Players
        .SelectMany(player => player.Amulets)
        .Select(amulet => $"{amulet.InstanceId}:"
            + (amulet.Crystallized is null ? "-" : $"c{amulet.Crystallized.Cost}x{amulet.Crystallized.Countdown}"))
        .ToList();

    /// <summary>
    /// **护符确定性测试**（不依赖随机对局里恰好出现护符）：在确定的种子序列上主动搜索
    /// 能打出**普通护符**与**结晶护符**的决策点，`Apply` 出真实护符局面后验证克隆与指纹。
    /// </summary>
    public static bool RunAmuletChecks(Config config, Action<string> report)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(report);

        var decks = new Dictionary<string, DeckDefinition>(StringComparer.Ordinal);
        DeckDefinition Deck(string id)
        {
            if (!decks.TryGetValue(id, out var deck))
            {
                deck = AgentSelfTests.CreateMatchDeck(id, "causal-formal-" + id);
                decks[id] = deck;
            }

            return deck;
        }

        GameState? plain = null;
        GameState? crystallized = null;
        var plainWhere = string.Empty;
        var crystalWhere = string.Empty;

        foreach (var matchup in Matchups)
        {
            for (var index = 0; index < 60 && (plain is null || crystallized is null); index++)
            {
                var seed = Mix(config.SourceSeedBase, (ulong)(700_000 + index));
                MatchRunner.PlayToEnd(
                    GameEngine.CreateGame(Deck(matchup.Deck1), Deck(matchup.Deck2), seed),
                    new GreedyPlayerAgent(),
                    new GreedyPlayerAgent(),
                    onStep: step =>
                    {
                        if (step.BeforeState.Phase != GamePhase.Main)
                        {
                            return;
                        }

                        foreach (var action in GameEngine.GetLegalActions(step.BeforeState))
                        {
                            if (action is PlayAmuletAction && plain is null)
                            {
                                plain = GameEngine.Apply(step.BeforeState, action);
                                plainWhere = $"{matchup.Name} 种子 {seed} 回合 {step.BeforeState.TurnNumber}";
                            }
                            else if (action is PlayCrystallizeAction && crystallized is null)
                            {
                                crystallized = GameEngine.Apply(step.BeforeState, action);
                                crystalWhere = $"{matchup.Name} 种子 {seed} 回合 {step.BeforeState.TurnNumber}";
                            }
                        }
                    });
            }

            if (plain is not null && crystallized is not null)
            {
                break;
            }
        }

        var passed = true;
        var checks = 0;
        void Check(string name, bool ok, string detail)
        {
            checks++;
            passed &= ok;
            report($"[{(ok ? "PASS" : "FAIL")}] {name} ｜ {detail}");
        }

        Check("构造出普通护符局面", plain is not null, plain is null ? "未找到" : plainWhere);
        Check("构造出结晶护符局面", crystallized is not null, crystallized is null ? "未找到" : crystalWhere);

        var plainIds = plain is null ? [] : AmuletIdentities(plain);
        var crystalIds = crystallized is null ? [] : AmuletIdentities(crystallized);
        Check(
            "普通护符的身份编码为未结晶",
            plainIds.Count > 0 && plainIds.All(id => id.EndsWith(":-", StringComparison.Ordinal)),
            $"护符 {plainIds.Count} 个：{string.Join(' ', plainIds)}");
        Check(
            "结晶护符的身份编码带结晶信息（与普通护符可区分）",
            crystalIds.Count > 0 && crystalIds.Any(id => id.Contains(":c", StringComparison.Ordinal)),
            $"护符 {crystalIds.Count} 个：{string.Join(' ', crystalIds)}");

        foreach (var (label, state) in new[] { ("普通护符", plain), ("结晶护符", crystallized) })
        {
            if (state is null)
            {
                continue;
            }

            var clone = GameEngine.Clone(state);
            var before = GameEngine.StateFingerprint(state);
            Check($"{label}：克隆后指纹相同", before == GameEngine.StateFingerprint(clone),
                $"{before[..16]}… vs {GameEngine.StateFingerprint(clone)[..16]}…");
            Check($"{label}：克隆不共享任何可变 CardInstance", !GameEngine.ShareAnyCardInstance(state, clone),
                $"原 {GameEngine.CollectCardInstances(state).Count} 张 / 克隆 {GameEngine.CollectCardInstances(clone).Count} 张");

            // 改动克隆（推进一回合，护符倒计时会变）不得影响原状态
            var legal = GameEngine.GetLegalActions(clone);
            if (legal.Count > 0)
            {
                _ = GameEngine.Apply(clone, legal[0]);
                Check($"{label}：改动克隆不改变原状态", GameEngine.StateFingerprint(state) == before,
                    "原状态指纹必须保持不变");
            }
        }

        Check("两种护符局面的指纹互不相同",
            plain is not null && crystallized is not null
            && GameEngine.StateFingerprint(plain) != GameEngine.StateFingerprint(crystallized),
            "普通 vs 结晶的整个局面指纹不同（本项只证明两局面不同，不单独证明差异来自结晶标记）");

        report($"检查项 {checks} 个");
        return passed;
    }

    // ------------------------------------------------------------------ 克隆回归测试

    /// <summary>
    /// 克隆完整性回归测试（DIR-2 追加项）。核心是**结构性别名审计**：
    /// 克隆后两个状态之间不得共享**任何**可变的 <see cref="CardInstance"/> 引用。
    /// <para>
    /// 这比只检查 <c>FollowerInstance.Card</c> / <c>AmuletInstance.Card</c> 两个点更强，
    /// 而且不需要依赖某张具体卡去构造"回手后触发手牌减费"的动作链 ——
    /// 只要不存在共享引用，那条泄漏路径在构造上就不可能成立。
    /// 同时验证完整状态指纹对改动敏感（否则污染检查本身就是瞎的）。
    /// </para>
    /// </summary>
    public static bool RunCloneTests(Config config, Action<string> report)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(report);

        var decks = new Dictionary<string, DeckDefinition>(StringComparer.Ordinal);
        DeckDefinition Deck(string id)
        {
            if (!decks.TryGetValue(id, out var deck))
            {
                deck = AgentSelfTests.CreateMatchDeck(id, "causal-clone-" + id);
                decks[id] = deck;
            }

            return deck;
        }

        var passed = true;
        var checks = 0;
        void Check(string name, bool ok, string detail)
        {
            checks++;
            passed &= ok;
            report($"[{(ok ? "PASS" : "FAIL")}] {name}");
            report($"        {detail}");
        }

        // 收集若干"已经有场面"的主回合状态，这样随从/护符的卡引用才有覆盖
        var states = new List<GameState>();
        for (var index = 0; index < Matchups.Count && states.Count < 4; index++)
        {
            var matchup = Matchups[index];
            var seed = Mix(config.SourceSeedBase, (ulong)(90_000 + index));
            var collected = false;
            MatchRunner.PlayToEnd(
                GameEngine.CreateGame(Deck(matchup.Deck1), Deck(matchup.Deck2), seed),
                new LookaheadPlayerAgentV2(seed, FrozenV2Rollouts),
                new LookaheadPlayerAgentV2(Mix(seed, FrozenSeatSalt), FrozenV2Rollouts),
                onStep: step =>
                {
                    if (collected || step.BeforeState.Phase != GamePhase.Main)
                    {
                        return;
                    }

                    var state = step.BeforeState;
                    if (state.Players[0].Board.Count + state.Players[1].Board.Count == 0)
                    {
                        return;
                    }

                    states.Add(state);
                    collected = true;
                });
        }

        Check("采到有场面的状态样本", states.Count > 0, $"样本 {states.Count} 个（四种对局各 1 个）");

        var totalFollowers = 0;
        var totalAmulets = 0;
        foreach (var state in states)
        {
            var clone = GameEngine.Clone(state);
            var beforeFingerprint = GameEngine.StateFingerprint(state);
            totalFollowers += state.Players.Sum(player => player.Board.Count);
            totalAmulets += state.Players.Sum(player => player.Amulets.Count);

            // ① 结构性别名审计
            Check(
                $"克隆不共享任何可变 CardInstance（回合 {state.TurnNumber}）",
                !GameEngine.ShareAnyCardInstance(state, clone),
                $"原状态卡牌 {GameEngine.CollectCardInstances(state).Count} 个，克隆 {GameEngine.CollectCardInstances(clone).Count} 个");

            // ② 克隆必须逐字节等价
            Check(
                $"克隆后完整状态指纹相同（回合 {state.TurnNumber}）",
                beforeFingerprint == GameEngine.StateFingerprint(clone),
                $"{beforeFingerprint[..16]} vs {GameEngine.StateFingerprint(clone)[..16]}");

            // ③ 指纹必须对改动敏感 —— 否则污染检查是瞎的
            var legal = GameEngine.GetLegalActions(state);
            if (legal.Count > 0)
            {
                var mutated = GameEngine.Apply(clone, legal[0]);
                Check(
                    $"完整状态指纹对改动敏感（回合 {state.TurnNumber}）",
                    GameEngine.StateFingerprint(mutated) != beforeFingerprint,
                    "施加一个动作后指纹必须变化");

                // ④ 改动克隆不得影响原状态
                Check(
                    $"改动克隆不改变原状态（回合 {state.TurnNumber}）",
                    GameEngine.StateFingerprint(state) == beforeFingerprint,
                    "原状态指纹必须保持不变");
            }
        }

        report($"检查项 {checks} 个；覆盖随从 {totalFollowers} 个、护符 {totalAmulets} 个");
        return passed;
    }

    // ------------------------------------------------------------------ 直接测量（DIR-2 §1–§3）

    /// <summary>一个真实决策的完整记录，含两个牌手各自的决策来源分解。</summary>
    public sealed record DecisionRecord(
        string Matchup,
        /// <summary>源对局种子。统计单位是**源对局**，cluster 自助法要按它重采样。</summary>
        ulong SourceGameSeed,
        int Seat,
        int TurnNumber,
        string Phase,
        int DecisionIndex,
        bool Eligible,
        string V2Final,
        string V2Rule,
        string V2Planner,
        bool V2RailOverrode,
        string V3Final,
        string V3Rule,
        string V3Planner,
        bool V3RailOverrode)
    {
        /// <summary>该源对局最终的合格决策总数（整局结束后回填，不是抽中当时的已见数）。</summary>
        public int EligibleDecisionTotal { get; set; }

        public bool Agreed => string.Equals(V2Final, V3Final, StringComparison.Ordinal);
    }

    /// <summary>
    /// 影子同步校验：一个**另建**的持久 2.0，沿源对局每一步被询问，
    /// 它的动作必须与实战牌手实际走出的动作逐动作一致。
    /// 只要有一次不一致，就说明计数 / 种子 / 状态 / 调用顺序还没有同步，测量结果不得使用。
    /// </summary>
    public sealed record SyncStats(int Decisions, int Mismatches, IReadOnlyList<string> Examples)
    {
        public double Agreement => Decisions == 0 ? double.NaN : 1.0 - (double)Mismatches / Decisions;
    }

    /// <summary>一个牌手的决策来源分解。</summary>
    public sealed record GateStats(
        string Name,
        int Decisions,
        int WithPlanner,
        int PlannerMatchesRule,
        int RailOverrode,
        int FinalEqualsRule,
        int FinalEqualsPlanner)
    {
        public double PlannerMatchesRuleRate => WithPlanner == 0 ? double.NaN : (double)PlannerMatchesRule / WithPlanner;
        public double RailOverrideRate => WithPlanner == 0 ? double.NaN : (double)RailOverrode / WithPlanner;
        public double FinalEqualsRuleRate => Decisions == 0 ? double.NaN : (double)FinalEqualsRule / Decisions;
    }

    public sealed record CensusSlice2(string Key, int Decisions, int Disagreements)
    {
        public double Rate => Decisions == 0 ? double.NaN : (double)Disagreements / Decisions;
    }

    public sealed record MeasureResult(
        SyncStats Sync,
        int Decisions,
        int Eligible,
        int Disagreements,
        int Games,
        int GamesWithAtLeastOneDisagreement,
        IReadOnlyList<int> DisagreementsPerGame,
        GateStats V2,
        GateStats V3,
        IReadOnlyList<CensusSlice2> BySeat,
        IReadOnlyList<CensusSlice2> ByMatchup,
        IReadOnlyList<CensusSlice2> ByTurnBucket,
        /// <summary>分歧原因交叉表：评估差异 / 闸门差异 / 两者共同 / 无法归因。</summary>
        IReadOnlyList<(string Cause, int Count)> DisagreementCauses,
        IReadOnlyList<DecisionRecord> Records)
    {
        public double DisagreementRate => Decisions == 0 ? double.NaN : (double)Disagreements / Decisions;
        public double EligibleDisagreementRate => Eligible == 0 ? double.NaN : (double)Disagreements / Eligible;
        public double GameWithDisagreementRate => Games == 0 ? double.NaN : (double)GamesWithAtLeastOneDisagreement / Games;
    }

    /// <summary>
    /// **修正后的直接测量**（取代此前那个会重置决策计数的普查）。
    /// <para>
    /// 关键修正：源对局的每个座位都持有**持久**的
    /// 实战 2.0、影子 2.0、影子 3.0 三个实例，并在**每一个**决策点
    /// （换牌、只有一个合法动作的决策、非主阶段决策都算）都询问影子牌手。
    /// 这样影子牌手内部的 <c>decisionNumber</c> 与实战牌手同步 ——
    /// 而推演种子正是 <c>decisionNumber</c> 的函数，不同步就等于在比较别的策略。
    /// </para>
    /// </summary>
    public static MeasureResult Measure(Config config, Action<string> report)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(report);

        var decks = new Dictionary<string, DeckDefinition>(StringComparer.Ordinal);
        DeckDefinition Deck(string id)
        {
            if (!decks.TryGetValue(id, out var deck))
            {
                deck = AgentSelfTests.CreateMatchDeck(id, "causal-" + id);
                decks[id] = deck;
            }

            return deck;
        }

        var records = new List<DecisionRecord>();
        var syncMismatches = new List<string>();
        var syncDecisions = 0;
        var disagreementsPerGame = new List<int>();
        var games = 0;
        var gamesWithDisagreement = 0;
        var ruleAgent = new GreedyPlayerAgent();

        for (var matchupIndex = 0; matchupIndex < Matchups.Count; matchupIndex++)
        {
            var matchup = Matchups[matchupIndex];
            for (var gameIndex = 0; gameIndex < config.GamesPerMatchup; gameIndex++)
            {
                var sourceSeed = Mix(config.SourceSeedBase, (ulong)((matchupIndex * 1_000_003) + gameIndex));
                var seatSeeds = new[] { sourceSeed, Mix(sourceSeed, FrozenSeatSalt) };

                // 持久实例：实战 2.0（驱动对局）、影子 2.0（同步校验）、影子 3.0（被测策略）
                var real = new[] { new LookaheadPlayerAgentV2(seatSeeds[0], FrozenV2Rollouts), new LookaheadPlayerAgentV2(seatSeeds[1], FrozenV2Rollouts) };
                var shadow2 = new[] { new LookaheadPlayerAgentV2(seatSeeds[0], FrozenV2Rollouts), new LookaheadPlayerAgentV2(seatSeeds[1], FrozenV2Rollouts) };
                var shadow3 = new[] { ShadowV3(seatSeeds[0]), ShadowV3(seatSeeds[1]) };

                var gameDecisions = 0;
                var gameDisagreements = 0;
                var decisionIndex = 0;
                var gameEligible = 0;
                var firstRecordIndex = records.Count;

                MatchRunner.PlayToEnd(
                    GameEngine.CreateGame(Deck(matchup.Deck1), Deck(matchup.Deck2), sourceSeed),
                    real[0],
                    real[1],
                    onStep: step =>
                    {
                        var seat = step.ActingPlayer;
                        var state = step.BeforeState;
                        var observation = GameEngine.ToObservation(state, state.ActivePlayer);
                        var legal = GameEngine.GetLegalActions(state);
                        if (legal.Count >= 2)
                        {
                            gameEligible++;
                        }

                        // 影子 2.0：必须与实战动作逐动作一致
                        var shadow2Action = AskAction(shadow2[seat], state);
                        syncDecisions++;
                        if (Canonical(shadow2Action) != Canonical(step.Action) && syncMismatches.Count < 5)
                        {
                            syncMismatches.Add(
                                $"局 {sourceSeed} 回合 {state.TurnNumber} 座位 {seat}：实战 {Canonical(step.Action)} ｜ 影子 {Canonical(shadow2Action)}");
                        }

                        // 影子 3.0：与实战相同的决策序号
                        var v3Action = AskAction(shadow3[seat], state);

                        var v2Gate = GateInfo(real[seat].LastDecision, observation, legal, ruleAgent);
                        var v3Gate = GateInfo(shadow3[seat].LastDecision, observation, legal, ruleAgent);
                        var v2Final = Canonical(step.Action);
                        var v3Final = Canonical(v3Action);

                        records.Add(new DecisionRecord(
                            matchup.Name,
                            sourceSeed,
                            seat,
                            state.TurnNumber,
                            state.Phase.ToString(),
                            decisionIndex++,
                            legal.Count >= 2,
                            v2Final,
                            v2Gate.Rule,
                            v2Gate.Planner,
                            v2Gate.RailOverrode,
                            v3Final,
                            v3Gate.Rule,
                            v3Gate.Planner,
                            v3Gate.RailOverrode));

                        gameDecisions++;
                        if (v2Final != v3Final)
                        {
                            gameDisagreements++;
                        }
                    });

                // 整局结束才知道最终合格决策总数，在这里回填（而不是抽中当时的已见数）
                for (var index = firstRecordIndex; index < records.Count; index++)
                {
                    records[index] = records[index] with { EligibleDecisionTotal = gameEligible };
                }

                games++;
                disagreementsPerGame.Add(gameDisagreements);
                if (gameDisagreements > 0)
                {
                    gamesWithDisagreement++;
                }

                report(
                    $"  {matchup.Name} 局 {sourceSeed}：决策 {gameDecisions}，分歧 {gameDisagreements}"
                    + (gameDisagreements > 0 ? " ← 有分歧" : string.Empty));
            }
        }

        var eligible = records.Count(record => record.Eligible);
        var disagreements = records.Count(record => !record.Agreed);

        var bySeat = records
            .GroupBy(record => $"座位{record.Seat}")
            .Select(group => new CensusSlice2(group.Key, group.Count(), group.Count(record => !record.Agreed)))
            .OrderBy(slice => slice.Key, StringComparer.Ordinal)
            .ToList();
        var byMatchup = records
            .GroupBy(record => record.Matchup)
            .Select(group => new CensusSlice2(group.Key, group.Count(), group.Count(record => !record.Agreed)))
            .ToList();
        var byTurn = records
            .GroupBy(record => record.TurnNumber <= 3 ? "回合1-3"
                : record.TurnNumber <= 6 ? "回合4-6"
                : record.TurnNumber <= 10 ? "回合7-10"
                : "回合11+")
            .Select(group => new CensusSlice2(group.Key, group.Count(), group.Count(record => !record.Agreed)))
            .OrderBy(slice => slice.Key, StringComparer.Ordinal)
            .ToList();

        // 分歧原因：评估差异（planner 不同）还是闸门差异（planner 相同但覆盖状态不同）
        var causes = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["规划器不同（评估差异）"] = 0,
            ["规划器相同、闸门覆盖不同"] = 0,
            ["规划器不同且闸门覆盖不同"] = 0,
            ["无法归因（缺少规划器信息）"] = 0
        };
        foreach (var record in records.Where(record => !record.Agreed))
        {
            var plannerDiffers = !string.Equals(record.V2Planner, record.V3Planner, StringComparison.Ordinal);
            var railDiffers = record.V2RailOverrode != record.V3RailOverrode;
            var key = record.V2Planner.Length == 0 || record.V3Planner.Length == 0
                ? "无法归因（缺少规划器信息）"
                : plannerDiffers && railDiffers ? "规划器不同且闸门覆盖不同"
                : plannerDiffers ? "规划器不同（评估差异）"
                : railDiffers ? "规划器相同、闸门覆盖不同"
                : "无法归因（缺少规划器信息）";
            causes[key]++;
        }

        return new MeasureResult(
            new SyncStats(syncDecisions, syncMismatches.Count, syncMismatches),
            records.Count,
            eligible,
            disagreements,
            games,
            gamesWithDisagreement,
            disagreementsPerGame,
            BuildGateStats("2.0（实战）", records.Select(record => (record.V2Final, record.V2Rule, record.V2Planner, record.V2RailOverrode))),
            BuildGateStats("3.0（影子）", records.Select(record => (record.V3Final, record.V3Rule, record.V3Planner, record.V3RailOverrode))),
            bySeat,
            byMatchup,
            byTurn,
            causes.Select(pair => (pair.Key, pair.Value)).ToList(),
            records);
    }

    private sealed record GateTriple(string Rule, string Planner, bool RailOverrode);

    /// <summary>
    /// 从公开数据重建决策来源：规则牌手动作用我们自己构造的 <see cref="GreedyPlayerAgent"/>
    /// （引擎内部用的就是它，且它无状态）；规划器首选按引擎的排序规则
    /// （估计胜率降序 → 完成胜场降序 → 原始动作序号升序）取第一；
    /// 闸门覆盖 = 规划器首选不是规则动作、但最终动作是规则动作。
    /// 这样**不需要修改冻结的 V1/V2**。
    /// </summary>
    private static GateTriple GateInfo(
        LookaheadDecision? decision, GameObservation observation, IReadOnlyList<GameAction> legal, GreedyPlayerAgent ruleAgent)
    {
        var rule = Canonical(ruleAgent.ChooseAction(observation, legal));
        if (decision is null || decision.Evaluations.Count == 0)
        {
            return new GateTriple(rule, string.Empty, false);
        }

        var planner = decision.Evaluations
            .OrderByDescending(evaluation => evaluation.EstimatedWinChance)
            .ThenByDescending(evaluation => evaluation.CompletedWins)
            .ThenBy(evaluation => evaluation.OriginalLegalActionIndex)
            .First();
        var plannerCanonical = Canonical(planner.Action);
        var final = Canonical(decision.SelectedAction);
        var railOverrode =
            !string.Equals(plannerCanonical, rule, StringComparison.Ordinal) &&
            string.Equals(final, rule, StringComparison.Ordinal);
        return new GateTriple(rule, plannerCanonical, railOverrode);
    }

    private static GateStats BuildGateStats(
        string name, IEnumerable<(string Final, string Rule, string Planner, bool RailOverrode)> rows)
    {
        var list = rows.ToList();
        var withPlanner = list.Where(row => row.Planner.Length > 0).ToList();
        return new GateStats(
            name,
            list.Count,
            withPlanner.Count,
            withPlanner.Count(row => string.Equals(row.Planner, row.Rule, StringComparison.Ordinal)),
            withPlanner.Count(row => row.RailOverrode),
            list.Count(row => string.Equals(row.Final, row.Rule, StringComparison.Ordinal)),
            list.Count(row => string.Equals(row.Final, row.Planner, StringComparison.Ordinal) && row.Planner.Length > 0));
    }

    /// <summary>影子 3.0：与出货配置完全相同，只是持久化以便决策序号与实战同步。</summary>
    private static LookaheadPlayerAgent ShadowV3(ulong seed) => new(
        rolloutsPerAction: 60,
        futureTurnHorizon: 1,
        alternateHorizon: 3,
        seed: seed,
        minimumPracticalAdvantage: 0.0);

    // ------------------------------------------------------------------ 普查（诊断，不属于正式实验）

    public sealed record CensusRow(int TurnNumber, int Seat, string Matchup, bool Agreed);

    public sealed record CensusSlice(string Key, int Decisions, int Disagreements)
    {
        public double Rate => Decisions == 0 ? double.NaN : (double)Disagreements / Decisions;
    }

    public sealed record Census(
        int Decisions,
        int Disagreements,
        IReadOnlyList<CensusSlice> BySeat,
        IReadOnlyList<CensusSlice> ByMatchup,
        IReadOnlyList<CensusSlice> ByTurnBucket);

    /// <summary>
    /// **普查**：在源对局里问**每一个**「主回合 + 合法动作 ≥ 2」的决策，两个座位都问，
    /// 统计 2.0 与 3.0 的动作分歧率。
    /// <para>
    /// <b>为什么需要它</b>：正式实验每局只抽 1 个决策（避免同一局的决策高度相关）。
    /// 但如果那个抽样框里的分歧率是 0，就必须先分清两种可能 ——
    /// 是"2.0 与 3.0 在整个空间里从不分歧"（假设已死），
    /// 还是"分歧存在于抽样框之外"（后期回合 / 第二牌手座位，抽样框太窄）。
    /// 普查不进入统计，只用来看分歧在哪里。
    /// </para>
    /// </summary>
    public static Census RunCensus(Config config, Action<string> report)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(report);

        var decks = new Dictionary<string, DeckDefinition>(StringComparer.Ordinal);
        DeckDefinition Deck(string id)
        {
            if (!decks.TryGetValue(id, out var deck))
            {
                deck = AgentSelfTests.CreateMatchDeck(id, "causal-" + id);
                decks[id] = deck;
            }

            return deck;
        }

        var rows = new List<CensusRow>();
        for (var matchupIndex = 0; matchupIndex < Matchups.Count; matchupIndex++)
        {
            var matchup = Matchups[matchupIndex];
            for (var gameIndex = 0; gameIndex < config.GamesPerMatchup; gameIndex++)
            {
                var sourceSeed = Mix(config.SourceSeedBase, (ulong)((matchupIndex * 1_000_003) + gameIndex));
                MatchRunner.PlayToEnd(
                    GameEngine.CreateGame(Deck(matchup.Deck1), Deck(matchup.Deck2), sourceSeed),
                    new LookaheadPlayerAgentV2(sourceSeed, FrozenV2Rollouts),
                    new LookaheadPlayerAgentV2(Mix(sourceSeed, FrozenSeatSalt), FrozenV2Rollouts),
                    onStep: step =>
                    {
                        if (step.BeforeState.Phase != GamePhase.Main)
                        {
                            return;
                        }

                        if (GameEngine.GetLegalActions(step.BeforeState).Count < 2)
                        {
                            return;
                        }

                        var canonical2 = Canonical(AskAction(
                            new LookaheadPlayerAgentV2(config.ProbeSeed, FrozenV2Rollouts), step.BeforeState));
                        var canonical3 = Canonical(AskAction(ShippingV3(config.ProbeSeed), step.BeforeState));
                        rows.Add(new CensusRow(
                            step.BeforeState.TurnNumber, step.ActingPlayer, matchup.Name, canonical2 == canonical3));
                    });
            }

            report($"  普查完成：{matchup.Name}（累计决策 {rows.Count}）");
        }

        var bySeat = rows
            .GroupBy(row => $"座位{row.Seat}")
            .Select(group => new CensusSlice(group.Key, group.Count(), group.Count(row => !row.Agreed)))
            .OrderBy(slice => slice.Key, StringComparer.Ordinal)
            .ToList();
        var byMatchup = rows
            .GroupBy(row => row.Matchup)
            .Select(group => new CensusSlice(group.Key, group.Count(), group.Count(row => !row.Agreed)))
            .ToList();
        var byTurn = rows
            .GroupBy(row => row.TurnNumber <= 3 ? "回合1-3"
                : row.TurnNumber <= 6 ? "回合4-6"
                : row.TurnNumber <= 10 ? "回合7-10"
                : "回合11+")
            .Select(group => new CensusSlice(group.Key, group.Count(), group.Count(row => !row.Agreed)))
            .OrderBy(slice => slice.Key, StringComparer.Ordinal)
            .ToList();

        return new Census(
            rows.Count,
            rows.Count(row => !row.Agreed),
            bySeat,
            byMatchup,
            byTurn);
    }

    // ------------------------------------------------------------------ 工具

    /// <summary>
    /// **决策指纹**：只覆盖回合 + 行动方 + 合法动作。
    /// <para>
    /// ⚠️ 它**不是**全状态哈希。生命、PP、手牌、牌库、场面属性、减费、墓地、随机状态它都看不到，
    /// 所以**不能**用来证明"状态未被污染"——共享引用被改写时它可能完全不变。
    /// 污染检查必须用 <see cref="GameEngine.StateFingerprint"/>。
    /// </para>
    /// </summary>
    private static string DecisionFingerprint(GameState state)
    {
        var builder = new StringBuilder();
        builder.Append(state.TurnNumber).Append('|')
            .Append(state.ActivePlayer).Append('|')
            .Append(state.Phase).Append('|');
        foreach (var action in GameEngine.GetLegalActions(state))
        {
            builder.Append(Canonical(action)).Append(';');
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())))[..16];
    }

    /// <summary>
    /// [DIR-4A #2] 按**局面**派生续局种子：同一局面的两个动作共享同一对派生种子，
    /// 不同局面**不得**复用同一对固定种子 —— 否则引入跨源对局的共同随机扰动，
    /// 而 §4 的聚类重采样假定源对局之间独立。
    /// </summary>
    private static ulong DeriveContinuationSeed(
        ulong baseSeed, ulong sourceGameSeed, int seat, int seatOrdinal, int which) =>
        Mix(baseSeed, Mix(sourceGameSeed ^ (ulong)(seat * 1_000_003), (ulong)((seatOrdinal * 31) + (which * 7_919) + 1)));

    /// <summary>SplitMix64。和 <c>AgentBenchmark.MixedSeed</c> 同一个族，保证"种子是纯函数"。</summary>
    private static ulong Mix(ulong seed, ulong salt)
    {
        var value = seed + (salt * 0x9E37_79B9_7F4A_7C15UL);
        value ^= value >> 30;
        value *= 0xBF58_476D_1CE4_E5B9UL;
        value ^= value >> 27;
        value *= 0x94D0_49BB_1331_11EBUL;
        value ^= value >> 31;
        return value;
    }

    /// <summary>
    /// 把动作渲染成规范字符串。与 <c>DecisionSensitivityProbe.Canonical</c> 同口径
    /// （<c>GameAction</c> 是 record，但列表成员的相等是按引用的，不能直接比）。
    /// </summary>
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
