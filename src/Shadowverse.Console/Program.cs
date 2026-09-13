using System.Text;
using System.Globalization;
using System.Diagnostics;
using System.Security.Cryptography;
using Shadowverse.Engine.Agents;
using Shadowverse.Engine.Cards;
using Shadowverse.Engine.Decks;
using Shadowverse.Engine.Game;
using Shadowverse.Engine.Models;
using Shadowverse.Engine.Simulation;
using Shadowverse.ConsoleApp;

Console.OutputEncoding = Encoding.UTF8;

if (args.Contains("--help", StringComparer.OrdinalIgnoreCase) ||
    args.Contains("-h", StringComparer.OrdinalIgnoreCase))
{
    ConsoleTools.PrintHelp();
    return;
}

if (args.Contains("--cards", StringComparer.OrdinalIgnoreCase))
{
    ConsoleTools.PrintCardCatalog();
    return;
}

if (args.Contains("--decks", StringComparer.OrdinalIgnoreCase))
{
    ConsoleTools.PrintDeckCatalog();
    return;
}

if (args.Contains("--deck-profile", StringComparer.OrdinalIgnoreCase))
{
    ConsoleTools.PrintDeckProfile();
    return;
}

if (args.Contains("--effect-test", StringComparer.OrdinalIgnoreCase))
{
    AgentSelfTests.RunRubyFanfareTest();
    AgentSelfTests.RunSeraphsGospelTest();
    AgentSelfTests.RunOliviaEffectTest();
    AgentSelfTests.RunApocalypseDeckReplacementTest();
    AgentSelfTests.RunPurgatoryEvilWorshipTest();
    AgentSelfTests.RunAstarothsVerdictTest();
    AgentSelfTests.RunGladiatorEnhanceTest();
    AgentSelfTests.RunStarchiumEvolutionTest();
    AgentSelfTests.RunDragonCardEffectTest();
    AgentSelfTests.RunLilimAndBatTest();
    AgentSelfTests.RunLathAndSkeletonTest();
    AgentSelfTests.RunBaruTest();
    AgentSelfTests.RunSummonerAndTokenTest();
    AgentSelfTests.RunConcertTest();
    AgentSelfTests.RunHeavenEyeTest();
    AgentSelfTests.RunTightropeWalkerTest();
    AgentSelfTests.RunDeathbedAnathemaTest();
    AgentSelfTests.RunAbyssalColonelTest();
    AgentSelfTests.RunDepartingAspirationTest();
    AgentSelfTests.RunDeathHostTest();
    AgentSelfTests.RunGalatadeTest();
    AgentSelfTests.RunIstanbulDeadTest();
    AgentSelfTests.RunNetherLieutenantTest();
    AgentSelfTests.RunStoredDeckRuleTest();
    AgentSelfTests.RunOpponentDeckInferenceTest();
    AgentSelfTests.RunGreedyStarchiumPriorityTest();
    AgentSelfTests.RunGuoLongAgentPriorityTest();
    AgentSelfTests.RunLookaheadAgentTest();
    AgentSelfTests.RunFrozenLookaheadV1Test();
    AgentSelfTests.RunFrozenLookaheadV2Test();
    AgentSelfTests.RunLookaheadDragonAccelerationTest();
    // V2（额外 PP 保留策略）已删除：实测无收益（对 V1 52.5% : 47.5%，p=0.77），
    // 机制是硬编码两个卡牌 ID 的特判，而且它的前提——"前瞻会浪费早期额外 PP"——
    // 在回退闸下限归零、前瞻改用自身判断之后已经不成立。
    AgentSelfTests.RunLookaheadMatchRunnerTest();
    // 人机对战：守"每个合法动作都能被某个拖拽/点击手势选中"，界面没法自动化验证，只能在这里守。
    AgentSelfTests.RunHumanActionResolverTest();
    // 人机对战：对手思考面板不能泄露对手手牌，渲染本身也不能抛异常（那会把整局对战卡死）。
    AgentSelfTests.RunOpponentThinkingReportTest();
    // 神经网络训练器：梯度必须真的进到权重里（默认学习率曾经大到把网络打飞）。
    AgentSelfTests.RunNeuralTrainerTest();
    return;
}

var defaultDeckId = DeckCatalog.All.FirstOrDefault()?.Id;
if (defaultDeckId is null)
{
    Console.WriteLine("当前卡组库为空，无法开始对局。请先在 Shadowverse.DeckEditor 中建立一副 40 张卡组并保存。");
    return;
}

var firstDeckId = ConsoleTools.ParseDeckId(args, "--deck1", defaultDeckId);
var secondDeckId = ConsoleTools.ParseDeckId(args, "--deck2", defaultDeckId);
var useLookahead = args.Contains("--lookahead", StringComparer.OrdinalIgnoreCase);
var useRandomSeed = args.Contains("--random", StringComparer.OrdinalIgnoreCase);
var lookaheadRollouts = ConsoleTools.ParseIntegerOption(args, "--rollouts", defaultValue: 60, minimum: 1, maximum: 500);
var lookaheadHorizon = ConsoleTools.ParseIntegerOption(args, "--horizon", defaultValue: 3, minimum: 1, maximum: 10);
// 第二牌手可以单独设预算与视野，用来做"同一个牌手、两种配置对打"的对照实验。
var secondLookaheadRollouts = ConsoleTools.ParseIntegerOption(
    args,
    "--p2-rollouts",
    defaultValue: lookaheadRollouts,
    minimum: 1,
    maximum: 500);
var secondLookaheadHorizon = ConsoleTools.ParseIntegerOption(
    args,
    "--p2-horizon",
    defaultValue: lookaheadHorizon,
    minimum: 1,
    maximum: 10);
var swapOrientation = !args.Contains("--no-swap", StringComparer.OrdinalIgnoreCase);
var printBehaviour = !args.Contains("--quiet-stats", StringComparer.OrdinalIgnoreCase);
var maxDegreeOfParallelism = ConsoleTools.ParseIntegerOption(
    args,
    "--jobs",
    defaultValue: Environment.ProcessorCount,
    minimum: 1,
    maximum: 256);

// Fitted evaluator weights must be in place before any match starts, so this runs before dispatch.
var weightsFile = ConsoleTools.ReadOptionValue(args, "--weights-file");
if (weightsFile is not null)
{
    WeightTools.LoadWeights(weightsFile, Console.WriteLine);
}

// 神经网络叶子评估：给了就取代线性评估。
var neuralWeightsFile = ConsoleTools.ReadOptionValue(args, "--neural-weights");
if (neuralWeightsFile is not null)
{
    LookaheadPlayerAgent.ConfigureNeuralEvaluator(NeuralPositionEvaluator.Load(neuralWeightsFile));
    Console.WriteLine($"已载入神经网络叶子评估：{neuralWeightsFile}");
}

// 按对局切换权重：同一套特征、按（我方卡组 × 对手卡组）各用一份拟合权重。
// 实测这件事值 7.5 ~ 27.5 个 BO10 分，见 AGENT-STRENGTH-REPORT.md 第 14.8 节。
var matchupWeightsFile = ConsoleTools.ReadOptionValue(args, "--matchup-weights");
if (matchupWeightsFile is not null)
{
    ConsoleTools.LoadMatchupWeights(matchupWeightsFile);
}

// 集成用的第二套评估权重。给了就开启"评估函数集成"，否则保持单一评估函数。
var altWeightsFile = ConsoleTools.ReadOptionValue(args, "--alt-weights-file");
if (altWeightsFile is not null)
{
    WeightTools.LoadEnsembleWeights(altWeightsFile, Console.WriteLine);
}

// 评估函数集成：给 --alt-weights-file 载入的第二套权重开一个按边开关，才能做配对对照。
var evaluatorEnsemble = ConsoleTools.HasOption(args, "--evaluator-ensemble");
var secondEvaluatorEnsemble = ConsoleTools.HasOption(args, "--p2-evaluator-ensemble");
if (evaluatorEnsemble || secondEvaluatorEnsemble)
{
    Console.WriteLine($"评估函数集成：第一牌手 {(evaluatorEnsemble ? "开" : "关")} ｜ 第二牌手 {(secondEvaluatorEnsemble ? "开" : "关")}。");
}

var railMarginText = ConsoleTools.ReadOptionValue(args, "--rail-margin");
if (railMarginText is not null)
{
    LookaheadPlayerAgent.DefaultMinimumPracticalAdvantage = double.Parse(
        railMarginText,
        CultureInfo.InvariantCulture);
    Console.WriteLine(
        $"回退闸下限默认值设为 {LookaheadPlayerAgent.DefaultMinimumPracticalAdvantage:R}。");
}

// 只覆盖第二牌手的闸门，用来做"同一个牌手、闸门开/关"的对照。
var secondRailMarginText = ConsoleTools.ReadOptionValue(args, "--p2-rail-margin");
var secondRailMargin = secondRailMarginText is null
    ? -1
    : double.Parse(secondRailMarginText, CultureInfo.InvariantCulture);

// 决策规则。两边独立，才能做"同一个牌手、只改选择规则"的配对对照。
var selectionMode = ConsoleTools.ParseSelectionMode(args, "--selection");
var secondSelectionMode = ConsoleTools.ParseSelectionMode(args, "--p2-selection");
if (selectionMode != LookaheadSelectionMode.RuleAgentFallback ||
    secondSelectionMode != LookaheadSelectionMode.RuleAgentFallback)
{
    Console.WriteLine($"选择规则：第一牌手 {selectionMode} ｜ 第二牌手 {secondSelectionMode}。");
}

var robustnessText = ConsoleTools.ReadOptionValue(args, "--robustness");
var robustnessPenalty = robustnessText is null
    ? 1.0
    : double.Parse(robustnessText, CultureInfo.InvariantCulture);
var secondRobustnessText = ConsoleTools.ReadOptionValue(args, "--p2-robustness");
var secondRobustnessPenalty = secondRobustnessText is null
    ? robustnessPenalty
    : double.Parse(secondRobustnessText, CultureInfo.InvariantCulture);

// 回退闸统计项的强度。值越大，前瞻越倾向于把决定权让给规则牌手。
var confidenceText = ConsoleTools.ReadOptionValue(args, "--confidence");
var statisticalConfidence = confidenceText is null
    ? 1.0
    : double.Parse(confidenceText, CultureInfo.InvariantCulture);
var secondConfidenceText = ConsoleTools.ReadOptionValue(args, "--p2-confidence");
var secondStatisticalConfidence = secondConfidenceText is null
    ? statisticalConfidence
    : double.Parse(secondConfidenceText, CultureInfo.InvariantCulture);

// 推演里由谁把剩余动作打完。默认是 1.0 的行为（规则牌手）。
var rolloutPolicy = ConsoleTools.ParseRolloutPolicy(args, "--rollout");
var secondRolloutPolicy = ConsoleTools.ParseRolloutPolicy(args, "--p2-rollout");
// 第三个集成轴：另一半推演由谁打完。默认与主策略相同 = 不集成。
var alternateRolloutPolicy = ConsoleTools.ParseRolloutPolicy(args, "--alt-rollout");
var secondAlternateRolloutPolicy = ConsoleTools.ParseRolloutPolicy(args, "--p2-alt-rollout");
if (alternateRolloutPolicy != rolloutPolicy || secondAlternateRolloutPolicy != secondRolloutPolicy)
{
    Console.WriteLine($"策略循环：第一牌手 {rolloutPolicy}/{alternateRolloutPolicy} ｜ 第二牌手 {secondRolloutPolicy}/{secondAlternateRolloutPolicy}。");
}

// 额外PP 的候选权归谁。默认 search（搜索自由决定）。
var extraPlayPointPolicy = ConsoleTools.ParseExtraPlayPointPolicy(args, "--extra-pp");
var secondExtraPlayPointPolicy = ConsoleTools.ParseExtraPlayPointPolicy(args, "--p2-extra-pp");
if (extraPlayPointPolicy != LookaheadExtraPlayPointPolicy.Search ||
    secondExtraPlayPointPolicy != LookaheadExtraPlayPointPolicy.Search)
{
    Console.WriteLine($"额外PP 候选权：第一牌手 {extraPlayPointPolicy} ｜ 第二牌手 {secondExtraPlayPointPolicy}。");
}

// 对手建模：rollout 里对手用哪个策略。默认规则牌手。
var opponentRolloutPolicy = ConsoleTools.ParseRolloutPolicy(args, "--opponent-rollout");
var secondOpponentRolloutPolicy = ConsoleTools.ParseRolloutPolicy(args, "--p2-opponent-rollout");
if (opponentRolloutPolicy != LookaheadRolloutPolicy.RuleAgent ||
    secondOpponentRolloutPolicy != LookaheadRolloutPolicy.RuleAgent)
{
    Console.WriteLine($"对手建模：第一牌手 {opponentRolloutPolicy} ｜ 第二牌手 {secondOpponentRolloutPolicy}。");
}

// 换牌专用视野。0 = 跟随主回合视野（1.0 的行为）。
var mulliganHorizon = ConsoleTools.ParseIntegerOption(args, "--mulligan-horizon", defaultValue: 0, minimum: 0, maximum: 10);
var secondMulliganHorizon = ConsoleTools.ParseIntegerOption(
    args, "--p2-mulligan-horizon", defaultValue: mulliganHorizon, minimum: 0, maximum: 10);

// 交替视野：奇数号推演用这个视野，和主视野对冲。0 = 不交替。
var alternateHorizon = ConsoleTools.ParseIntegerOption(args, "--alternate-horizon", defaultValue: 0, minimum: 0, maximum: 10);
var secondAlternateHorizon = ConsoleTools.ParseIntegerOption(
    args, "--p2-alternate-horizon", defaultValue: alternateHorizon, minimum: 0, maximum: 10);

// 第三档视野：三档循环 [主, 交替, 第三]。0 = 不用。
var thirdHorizon = ConsoleTools.ParseIntegerOption(args, "--third-horizon", defaultValue: 0, minimum: 0, maximum: 10);
var secondThirdHorizon = ConsoleTools.ParseIntegerOption(
    args, "--p2-third-horizon", defaultValue: thirdHorizon, minimum: 0, maximum: 10);

if (args.Contains("--collect-selfplay", StringComparer.OrdinalIgnoreCase))
{
    ConsoleTools.RunSelfPlayCollection(args, lookaheadRollouts, lookaheadHorizon, maxDegreeOfParallelism);
    return;
}

if (ConsoleTools.HasOption(args, "--fit-weights"))
{
    ConsoleTools.RunWeightFitting(args);
    return;
}

if (ConsoleTools.HasOption(args, "--train-neural"))
{
    ConsoleTools.RunNeuralTraining(args);
    return;
}

// --bo10 是--stats 的另一种计数口径，两者都进批量分支。
if (args.Contains("--stats", StringComparer.OrdinalIgnoreCase) ||
    ConsoleTools.HasOption(args, "--bo10"))
{
    var randomDecks = ConsoleTools.HasOption(args, "--random-decks");
    var benchmarkDecks = ConsoleTools.ResolveBenchmarkDecks(
        args,
        defaultDeckId,
        firstDeckId,
        secondDeckId,
        randomDecks);

    var firstAgentKind = ConsoleTools.ParseAgentOption(args, "--p1") ?? (useLookahead ? AgentKind.Lookahead : AgentKind.Greedy);
    var secondAgentKind = ConsoleTools.ParseAgentOption(args, "--p2") ?? (useLookahead ? AgentKind.Greedy : AgentKind.Random);

    // BO10 口径：1 个 BO10 = 5 对牌局 = 10 局，所以 --bo10 100 就是 1000 局。
    var bo10Blocks = ConsoleTools.ParseIntegerOption(args, "--bo10", defaultValue: 0, minimum: 0, maximum: 1000);
    var statisticsMatchCount = bo10Blocks > 0 ? bo10Blocks * 10 : ConsoleTools.ParseStatisticsMatchCount(args);
    if (bo10Blocks > 0)
    {
        Console.WriteLine($"BO10 口径：{bo10Blocks} 个 BO10 = {statisticsMatchCount} 局。");
    }

    AgentBenchmark.Run(
        new AgentBenchmark.Options(
            firstAgentKind,
            secondAgentKind,
            statisticsMatchCount,
            useRandomSeed ? ConsoleTools.CreateRandomSeed() : 20_260_901UL,
            lookaheadRollouts,
            lookaheadHorizon,
            swapOrientation,
            maxDegreeOfParallelism,
            printBehaviour,
            randomDecks,
            secondLookaheadRollouts,
            secondLookaheadHorizon,
            -1,
            secondRailMargin,
            selectionMode,
            secondSelectionMode,
            robustnessPenalty,
            secondRobustnessPenalty,
            statisticalConfidence,
            secondStatisticalConfidence,
            rolloutPolicy,
            secondRolloutPolicy,
            mulliganHorizon,
            secondMulliganHorizon,
            alternateHorizon,
            secondAlternateHorizon,
            thirdHorizon,
            secondThirdHorizon,
            alternateRolloutPolicy,
            secondAlternateRolloutPolicy,
            extraPlayPointPolicy,
            secondExtraPlayPointPolicy,
            opponentRolloutPolicy,
            secondOpponentRolloutPolicy,
            evaluatorEnsemble,
            secondEvaluatorEnsemble),
        benchmarkDecks);
    return;
}

if (args.Contains("--smoke-test", StringComparer.OrdinalIgnoreCase))
{
    AgentSelfTests.RunSmokeTest(firstDeckId, secondDeckId);
    return;
}

if (useLookahead)
{
    ConsoleTools.RunLookaheadVsGreedyMatch(
        firstDeckId,
        secondDeckId,
        lookaheadRollouts,
        lookaheadHorizon,
        useRandomSeed ? ConsoleTools.CreateRandomSeed() : 20_260_902UL,
        useRandomSeed);
}
else
{
    ConsoleTools.RunGreedyVsRandomMatch(
        firstDeckId,
        secondDeckId,
        useRandomSeed ? ConsoleTools.CreateRandomSeed() : 20_260_901UL,
        useRandomSeed);
}

