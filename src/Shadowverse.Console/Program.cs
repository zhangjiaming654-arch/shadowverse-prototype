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
    // 跑酷 + 两张创造物衍生卡：法术的【模式】通道（每个模式生成一个动作）、衍生卡入场曲与关键词、
    // 以及"3 种创造物"条件的可达性（当前不可达；卡池一变就要求补正例）。
    AgentSelfTests.RunParkourAndCreationTest();
    // 吉尔克：按"本次对战中破坏的创造物"加同名卡，且必须是**非公开**加入（不写进公开打出记录）。
    AgentSelfTests.RunContraptionOperatorGilqueTest();
    // 变身（诚心的尽小花）：消滅原卡而非破坏 ⇒ 不触发【谢幕曲】、不进墓地、不继承加成。
    AgentSelfTests.RunTransformFollowerTest();
    // 爱卡：与吉尔克同一效果但不限类别，且【进化时】要再发动一次。
    AgentSelfTests.RunForgottenInnocenceAikaTest();
    // 2026-09-28 批次 6 张超越者卡：条件入场曲 / 费用-3 的指名复制 / 让他人进化 / 吟唱护符 / 回合末自毁。
    AgentSelfTests.RunNemesisBatchTest();
    // 第二批：纹章·随机未发动能力 / 【奥义】槽 / 「创造物进入战场时」被动。
    AgentSelfTests.RunSlothBatchTest();
    // 第三批：使用法术时召唤 / 牌组无重复条件 / 牌组搜索过滤器 / 纹章每回合1次进化。
    AgentSelfTests.RunThirdBatchTest();
    // 第四批：召唤授予关键词 / 双方进化 / 失去所有能力 / 受到的伤害+1 / 回复超进化点 / 【模式】消失。
    AgentSelfTests.RunFourthBatchTest();
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
    // 搜索估值标签（蒸馏）：旁路文件必须逐行对齐（错位会静默配错局面），连续值必须学得进去。
    AgentSelfTests.RunNeuralSearchLabelTest();
    // 嵌套对手模型：新分支必须真的被执行到，否则"跑了但没测到"的负结果没有意义。
    AgentSelfTests.RunNestedOpponentModelTest();
    // S2：我方那一侧是**另一个座位、另一份预算**，必须单独押一遍。
    AgentSelfTests.RunOwnNestedRolloutTest();
    // 探针：rollout 策略到底有没有传导到候选动作的分数里（三份样本逐字节相同那件事）。
    AgentSelfTests.RunRolloutPolicyAffectsScoresProbe();
    // 敏感度恒等式：决策不变 ⇒ 整局逐动作相同 ⇒ 强度必然不变（本轮最硬的结论）。
    AgentSelfTests.RunDecisionSensitivityIdentityTest();
    // S1 搜索树原型：默认必须等于基线，且树的重新评分必须真的改变分数。
    AgentSelfTests.RunTreeSearchPrototypeTest();
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
// 嵌套对手模型（nested）自己的推演次数与视野。刻意比本牌手小得多——嵌套代价是乘法。
var opponentRollouts = ConsoleTools.ParseIntegerOption(
    args, "--opponent-rollouts", defaultValue: LookaheadPlayerAgent.DefaultNestedOpponentRollouts,
    minimum: 1, maximum: 500);
var secondOpponentRollouts = ConsoleTools.ParseIntegerOption(
    args, "--p2-opponent-rollouts", defaultValue: 0, minimum: 0, maximum: 500);
var opponentHorizon = ConsoleTools.ParseIntegerOption(
    args, "--opponent-horizon", defaultValue: 0, minimum: 0, maximum: 10);
var secondOpponentHorizon = ConsoleTools.ParseIntegerOption(
    args, "--p2-opponent-horizon", defaultValue: 0, minimum: 0, maximum: 10);
// 嵌套对手只搜"它怎么回应我"的第一手：成本从 4.3 倍降到接近基线。
var opponentFirstActionOnly = ConsoleTools.HasOption(args, "--opponent-first-action-only");
var secondOpponentFirstActionOnly = ConsoleTools.HasOption(args, "--p2-opponent-first-action-only");
// 嵌套**我方**（S2：--rollout nested）自己的预算。和对手那份分开，两边模拟的是两件不同的事。
var ownNestedRollouts = ConsoleTools.ParseIntegerOption(
    args, "--own-rollouts", defaultValue: LookaheadPlayerAgent.DefaultNestedOpponentRollouts,
    minimum: 1, maximum: 500);
var secondOwnNestedRollouts = ConsoleTools.ParseIntegerOption(
    args, "--p2-own-rollouts", defaultValue: 0, minimum: 0, maximum: 500);
var ownNestedHorizon = ConsoleTools.ParseIntegerOption(
    args, "--own-horizon", defaultValue: 0, minimum: 0, maximum: 10);
var secondOwnNestedHorizon = ConsoleTools.ParseIntegerOption(
    args, "--p2-own-horizon", defaultValue: 0, minimum: 0, maximum: 10);
// 【S1 最小搜索树】候选评分深度。0 = 关闭（= 基线行为）。依据报告 §19.8。
var treePly = ConsoleTools.ParseIntegerOption(
    args, "--tree-ply", defaultValue: 0, minimum: 0, maximum: 3);
var secondTreePly = ConsoleTools.ParseIntegerOption(
    args, "--p2-tree-ply", defaultValue: 0, minimum: 0, maximum: 3);
// §19.12 实测：树崩盘的主因是"用一层叶值评价深层"和"对噪声取 max"（赢家诅咒）。
// 这两个开关把那两条分别打开，供后续实验用。
var treeUsesSearchValue = ConsoleTools.HasOption(args, "--tree-uses-search-value");
var secondTreeUsesSearchValue = ConsoleTools.HasOption(args, "--p2-tree-uses-search-value");
var treeUsesMeanFollowUp = ConsoleTools.HasOption(args, "--tree-mean-followup");
var secondTreeUsesMeanFollowUp = ConsoleTools.HasOption(args, "--p2-tree-mean-followup");
var deepRollouts = ConsoleTools.ParseIntegerOption(
    args, "--deep-rollouts", defaultValue: LookaheadPlayerAgent.DefaultDeepRollouts, minimum: 1, maximum: 200);
var secondDeepRollouts = ConsoleTools.ParseIntegerOption(
    args, "--p2-deep-rollouts", defaultValue: LookaheadPlayerAgent.DefaultDeepRollouts, minimum: 1, maximum: 200);
if (opponentRolloutPolicy != LookaheadRolloutPolicy.RuleAgent ||
    secondOpponentRolloutPolicy != LookaheadRolloutPolicy.RuleAgent)
{
    Console.WriteLine($"对手建模：第一牌手 {opponentRolloutPolicy} ｜ 第二牌手 {secondOpponentRolloutPolicy}。");
    if (opponentRolloutPolicy == LookaheadRolloutPolicy.NestedLookahead ||
        secondOpponentRolloutPolicy == LookaheadRolloutPolicy.NestedLookahead)
    {
        // 口径必须跟着输出走：这个模型的强度是"会搜索"，不是 2.0 的真实强度。
        var firstNested = opponentRolloutPolicy == LookaheadRolloutPolicy.NestedLookahead
            ? opponentRollouts
            : 0;
        var secondNested = secondOpponentRolloutPolicy == LookaheadRolloutPolicy.NestedLookahead
            ? (secondOpponentRollouts > 0 ? secondOpponentRollouts : opponentRollouts)
            : 0;
        Console.WriteLine(
            $"  嵌套对手模型：推演 {firstNested}/{secondNested} 次" +
            $"（本牌手是 {lookaheadRollouts} 次；嵌套搜索代价是乘法，所以这里刻意开小）" +
            $" ｜ 视野 {(opponentHorizon > 0 ? opponentHorizon : lookaheadHorizon)}" +
            (secondOpponentHorizon > 0 ? $"/{secondOpponentHorizon}" : string.Empty));
    }
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

// 诊断"搜索估值到底有没有信息"。极便宜：只读已经采好的样本，不跑任何对局。
if (ConsoleTools.HasOption(args, "--diagnose-search-value"))
{
    ConsoleTools.RunSearchValueDiagnostics(args);
    return;
}

// 【决策敏感度】量"搜索的输出对自身内部有多敏感"。几分钟，不跑 BO10。
if (ConsoleTools.HasOption(args, "--sensitivity"))
{
    ConsoleTools.RunDecisionSensitivity(args);
    return;
}

// 【决策质量分层】裕度小时搜索的判断是不是接近抛硬币？几分钟，不跑 BO10。
if (ConsoleTools.HasOption(args, "--quality"))
{
    ConsoleTools.RunDecisionQuality(args);
    return;
}

// 【因果续局】§11 预注册实验：3.0 的单步动作是否真的比 2.0 更好？
// 分叉诊断，不是强度验收。--causal-wiring 只跑接线验收，不看强弱结论。
if (ConsoleTools.HasOption(args, "--causal-continuation")
    || ConsoleTools.HasOption(args, "--causal-wiring")
    || ConsoleTools.HasOption(args, "--causal-census")
    || ConsoleTools.HasOption(args, "--causal-clone-tests")
    || ConsoleTools.HasOption(args, "--causal-formal")
    || ConsoleTools.HasOption(args, "--causal-replay")
    || ConsoleTools.HasOption(args, "--causal-bootstrap-check")
    || ConsoleTools.HasOption(args, "--causal-amulet-check")
    || ConsoleTools.HasOption(args, "--causal-entry-check")
    // [DIR-6] 4.0 原型（信息集树）的验收入口
    || ConsoleTools.HasOption(args, "--v4-tree-check")
    || ConsoleTools.HasOption(args, "--v4-wiring")
    || ConsoleTools.HasOption(args, "--v4-causal-formal")
    || ConsoleTools.HasOption(args, "--v4-causal-replay"))
{
    ConsoleTools.RunCausalContinuation(args);
    return;
}

// 采样分支放在**所有牌手参数解析完之后**：采样要和 --bo10 用同一套 rollout 配置，
// 否则"采样的牌手"和"报告的配置"会不是同一个东西。
if (args.Contains("--collect-selfplay", StringComparer.OrdinalIgnoreCase))
{
    var collectionConfig = ConsoleTools.BuildSideConfig(
        rollouts: lookaheadRollouts,
        horizon: lookaheadHorizon,
        railMargin: -1,
        selectionMode: selectionMode,
        robustnessPenalty: robustnessPenalty,
        statisticalConfidence: statisticalConfidence,
        rolloutPolicy: rolloutPolicy,
        mulliganHorizon: mulliganHorizon,
        alternateHorizon: alternateHorizon,
        thirdHorizon: thirdHorizon,
        alternateRolloutPolicy: alternateRolloutPolicy,
        extraPlayPointPolicy: extraPlayPointPolicy,
        opponentRolloutPolicy: opponentRolloutPolicy,
        opponentRollouts: opponentRollouts,
        opponentHorizon: opponentHorizon,
        opponentFirstActionOnly: opponentFirstActionOnly,
        ownNestedRollouts: ownNestedRollouts,
        ownNestedHorizon: ownNestedHorizon,
        treePly: treePly,
        treeUsesSearchValue: treeUsesSearchValue,
        deepRollouts: deepRollouts,
        treeUsesMeanFollowUp: treeUsesMeanFollowUp,
        evaluatorEnsemble: evaluatorEnsemble);
    ConsoleTools.RunSelfPlayCollection(
        args, lookaheadRollouts, lookaheadHorizon, maxDegreeOfParallelism, collectionConfig);
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
            opponentRollouts,
            secondOpponentRollouts,
            opponentHorizon,
            secondOpponentHorizon,
            opponentFirstActionOnly,
            secondOpponentFirstActionOnly,
            ownNestedRollouts,
            secondOwnNestedRollouts,
            ownNestedHorizon,
            secondOwnNestedHorizon,
            treePly,
            secondTreePly,
            treeUsesSearchValue,
            secondTreeUsesSearchValue,
            treeUsesMeanFollowUp,
            secondTreeUsesMeanFollowUp,
            deepRollouts,
            secondDeepRollouts,
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

