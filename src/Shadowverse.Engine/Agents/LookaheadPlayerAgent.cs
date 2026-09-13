using Shadowverse.Engine.Cards;
using Shadowverse.Engine.Game;
using Shadowverse.Engine.Models;

namespace Shadowverse.Engine.Agents;

/// <summary>
/// How the planner turns its per-candidate estimates into one decision.
/// <para>
/// 1.0 hard-codes <see cref="RuleAgentFallback"/>. The 2.0 work is testing whether that rule is the
/// right one, so it is now selectable and the frozen 1.0 snapshot keeps the original behaviour.
/// </para>
/// </summary>
public enum LookaheadSelectionMode
{
    /// <summary>
    /// 1.0's rule: take the best average estimate, but hand the decision to the rule agent unless it
    /// beats the rule agent's own action by more than the required advantage.
    /// <para>
    /// Measured on the live 1.0 configuration: even with the practical floor at 0, the statistical
    /// term still takes 50.2% of the decisions where the planner disagrees with the rule agent.
    /// </para>
    /// </summary>
    RuleAgentFallback,

    /// <summary>
    /// Take the best average estimate and never defer to anyone.
    /// </summary>
    PureArgmax,

    /// <summary>
    /// Take the candidate with the best <c>mean - z * across-world standard deviation</c>.
    /// <para>
    /// Every candidate is scored on the same sampled hidden worlds, so the spread of a candidate's
    /// per-world values measures how much its outcome depends on which world was imagined. A large
    /// spread is the signature of strategy fusion: the action looks good only in some futures, and
    /// the agent will not actually know which future it is in when it has to follow up. Penalising
    /// that spread is the cheap, principled way to make the planner prefer actions that hold up
    /// across the hidden information instead of actions that win a lottery.
    /// </para>
    /// </summary>
    VariancePenalized
}

/// <summary>
/// 额外PP 这个动作的候选权归谁。额外PP 是超凡世界里后手专属、当回合 +1 PP、不跨回合结转的
/// 限时资源（第 5 回合及以前一次、第 6 回合起再一次），所以"什么时候砸下去"本身就是个决策。
/// <para>
/// 已测过的只是"用了之后有没有白用"（空转率约 10%），但**用对了时机但用在错误的回合**
/// 那种错误抓不到——一次真的多打出东西的使用，仍然可能是把资源花在了第 2 回合而不是第 5 回合。
/// 这个开关就是为正面回答"时机判断对不对"而加的。
/// </para>
/// </summary>
public enum LookaheadExtraPlayPointPolicy
{
    /// <summary>搜索自由决定（现状）。</summary>
    Search,

    /// <summary>
    /// 只有当规则牌手也愿意用额外PP 时，才把它留在候选集里。
    /// 也就是把"什么时候用"这一个动作交给规则牌手的明确规则，其余仍由搜索决定。
    /// </summary>
    RuleGated,

    /// <summary>
    /// 完全不允许使用额外PP。用来量化这个资源本身值多少分。
    /// </summary>
    Never
}

/// <summary>
/// Which policy completes the turn (and the horizon) inside each rollout.
/// <para>
/// The measured problem this exists to attack: the rollout's imagined future is systematically
/// misleading. With the rule agent playing the future, a longer horizon is strictly worse
/// (horizon 1 beats 3 beats 8, all measured), which means the simulated future is not a better look
/// at the position — it is a worse one. At horizon 1 the rollout plays only the rest of the current
/// turn, so the completion quality is the single thing that decides how the candidates rank.
/// </para>
/// </summary>
public enum LookaheadRolloutPolicy
{
    /// <summary>
    /// 1.0's policy: <see cref="GreedyPlayerAgent"/> plays every remaining action for both sides.
    /// </summary>
    RuleAgent,

    /// <summary>
    /// Every remaining action is chosen by hill-climbing the same evaluator the leaf uses, so the
    /// completion is self-consistent with the final score instead of following a different,
    /// hand-written preference order.
    /// </summary>
    EvaluatorGreedy
}

/// <summary>
/// A transparent Monte-Carlo lookahead agent. For every legal main-phase action it samples
/// several determinizations, lets the simple GreedyPlayerAgent play both sides for a short
/// horizon, and chooses the action with the strongest average evaluated position.
/// </summary>
public sealed class LookaheadPlayerAgent : IStateAwarePlayerAgent
{
    // 前瞻只有在优势超过下面两者中的较大值时，才敢用自己的判断，否则回退到规则牌手。
    // 下限：优势不到下限就不值得冒险——短推演里斜坡、谢幕曲这类延迟收益体现不出来。
    // 统计项：所有候选都在同一批采样世界上评估，所以用配对差值的标准误衡量噪声，
    // 比一个固定常数更贴合每一次决策各自的可靠程度。
    //
    // 实测警示（2026-09-13）：
    //  · 把下限归零只关掉了闸门的一半——统计项本身仍在生效，1.0 配置下分歧决策里
    //    仍有 50.2% 被它夺走。所以"回退闸关闭"这个说法以前是不准确的。
    //  · 统计项是**有功**的，不能删：同牌手 800 局只改这一项，
    //    纯 argmax 对 1.0 的规则是 65:96（p = 0.031），argmax 更差。
    // 所以现在这个问题变成"让位让多少才最优"，旋钮是下面这个强度系数。
    private const double DefaultStatisticalConfidence = 1.0;

    private static long _decisionsSeen;
    private static long _plannerMatchesRuleAgent;
    private static long _decisionsOverriddenByRail;

    // 决策信噪比与叶子方差的累计（都用千分之一为单位的整数，因为 Interlocked 不支持 double）。
    //
    // 用途：判别"视野越长反而越差"到底是**方差**（多推演两回合 → 叶子值在采样世界之间更散 →
    // 60 次平均后动作排序更糊）还是**盲区**（评估函数看不见延迟收益）。
    // 盲区假说已经失败过一次（补了 5 项待兑现特征，视野曲线没翻转），所以现在测方差。
    private static long _snrMilliSum;
    private static long _leafSpreadMilliSum;
    private static long _varianceSamples;

    /// <summary>
    /// 决策信噪比：前两名候选的估值差距 ÷ 两者的配对标准误。**越低说明搜索越分不清好坏**，
    /// 也就是决策越接近抛硬币。同时给出最优候选的叶子值跨世界标准差（绝对噪声量）。
    /// <para>
    /// 跑单一活牌手（对手用冻结牌手）时，这里的静态累计才干净；两边都是活牌手会混在一起。
    /// </para>
    /// </summary>
    public static string? VarianceReport()
    {
        var samples = Interlocked.Read(ref _varianceSamples);
        if (samples == 0)
        {
            return null;
        }

        var snr = Interlocked.Read(ref _snrMilliSum) / 1000.0 / samples;
        var spread = Interlocked.Read(ref _leafSpreadMilliSum) / 1000.0 / samples;
        return
            $"决策信噪比 {snr:F3}（前两名差距 ÷ 配对标准误）" +
            $" ｜ 叶子值跨世界标准差 {spread:F4}" +
            $" ｜ 样本 {samples} 次决策";
    }

    /// <summary>
    /// How often the planner's own judgement is actually used, and how often the fallback rail
    /// takes the decision away from it.
    /// <para>
    /// This is the first thing to check when a change to the evaluator fails to move the benchmark:
    /// if the rail hands almost every decision to the rule agent, then the evaluator barely
    /// influences play and improving it cannot show up as a win-rate change. Returns null when no
    /// planner decision has been recorded.
    /// </para>
    /// </summary>
    public static string? DeferralReport()
    {
        var decisions = Interlocked.Read(ref _decisionsSeen);
        if (decisions == 0)
        {
            return null;
        }

        var matches = Interlocked.Read(ref _plannerMatchesRuleAgent);
        var overridden = Interlocked.Read(ref _decisionsOverriddenByRail);
        var contested = decisions - matches;
        var overrideShare = contested == 0 ? 0.0 : 100.0 * overridden / contested;
        return
            $"前瞻决策 {decisions} 次 ｜ 与规则牌手自然重合 {matches} 次（{100.0 * matches / decisions:F1}%）" +
            $" ｜ 分歧 {contested} 次，其中被回退闸覆盖 {overridden} 次（{overrideShare:F1}%）";
    }

    // 对手可达伤害在叶子评分里的权重。可达伤害以"点"为单位，与生命值同一量纲，
    // 所以 1.0 大致等于"对手每能多打 1 点，就相当于自己少 1 点血"。
    private const double OpponentReachWeight = 1.0;
    private readonly GreedyPlayerAgent _rolloutAgent = new();
    private readonly int _rolloutsPerAction;
    private readonly int _futureTurnHorizon;
    private readonly ulong _seed;
    private long _decisionNumber;

    /// <summary>
    /// 回退闸的默认下限。
    /// <para>
    /// 实测（同牌手 2000 局，只有这个值不同）：0.035 → 47.5%，0 → **52.5%，p = 0.013**。
    /// 也就是说这个下限**一直在挡好事**：它拦掉的正确判断比它拦掉的短视错误更多。
    /// </para>
    /// 原来设成 0.035 的理由是"短推演看不到斜坡这类延迟收益"（龙之启示那条自检）。
    /// 那个隐患是真的，但它出现的频率远低于前瞻判断本身的价值。所以下限归零，
    /// 只保留统计项——决策在统计上打平时仍然交给规则牌手，那部分才是有依据的保守。
    /// <para>修"短视"应该去改评估器，而不是用一个全局下限把前瞻的所有判断一起压掉。</para>
    /// </summary>
    public static double DefaultMinimumPracticalAdvantage { get; set; } = 0.0;

    private readonly double _minimumPracticalAdvantage;
    private readonly LookaheadSelectionMode _selectionMode;
    private readonly double _robustnessPenalty;
    private readonly double _statisticalConfidence;
    private readonly int _mulliganHorizon;
    private readonly int[] _horizonCycle;
    private readonly LookaheadRolloutPolicy[] _rolloutPolicyCycle;
    private readonly LookaheadExtraPlayPointPolicy _extraPlayPointPolicy;
    private readonly LookaheadRolloutPolicy _opponentRolloutPolicy;
    private readonly bool _useEvaluatorEnsemble;

    public LookaheadPlayerAgent(
        int rolloutsPerAction = 60,
        int futureTurnHorizon = 3,
        ulong seed = 88_210UL,
        double? minimumPracticalAdvantage = null,
        LookaheadSelectionMode selectionMode = LookaheadSelectionMode.RuleAgentFallback,
        double robustnessPenalty = 1.0,
        double statisticalConfidence = DefaultStatisticalConfidence,
        LookaheadRolloutPolicy rolloutPolicy = LookaheadRolloutPolicy.RuleAgent,
        int mulliganHorizon = 0,
        int alternateHorizon = 0,
        int thirdHorizon = 0,
        LookaheadRolloutPolicy alternateRolloutPolicy = LookaheadRolloutPolicy.RuleAgent,
        LookaheadExtraPlayPointPolicy extraPlayPointPolicy = LookaheadExtraPlayPointPolicy.Search,
        LookaheadRolloutPolicy opponentRolloutPolicy = LookaheadRolloutPolicy.RuleAgent,
        bool useEvaluatorEnsemble = false)
    {
        if (rolloutsPerAction is < 1 or > 500)
        {
            throw new ArgumentOutOfRangeException(nameof(rolloutsPerAction), "Rollouts per action must be from 1 to 500.");
        }

        if (futureTurnHorizon is < 1 or > 10)
        {
            throw new ArgumentOutOfRangeException(nameof(futureTurnHorizon), "Future turn horizon must be from 1 to 10.");
        }

        var rail = minimumPracticalAdvantage ?? DefaultMinimumPracticalAdvantage;
        if (rail < 0 || double.IsNaN(rail))
        {
            throw new ArgumentOutOfRangeException(nameof(minimumPracticalAdvantage), "回退闸下限不能为负数。");
        }

        if (robustnessPenalty < 0 || double.IsNaN(robustnessPenalty))
        {
            throw new ArgumentOutOfRangeException(nameof(robustnessPenalty), "稳健惩罚系数不能为负数。");
        }

        if (statisticalConfidence < 0 || double.IsNaN(statisticalConfidence))
        {
            throw new ArgumentOutOfRangeException(nameof(statisticalConfidence), "回退闸统计强度不能为负数。");
        }

        if (mulliganHorizon is < 0 or > 10)
        {
            throw new ArgumentOutOfRangeException(nameof(mulliganHorizon), "换牌视野必须是 0 到 10；0 表示跟随主回合视野。");
        }

        if (alternateHorizon is < 0 or > 10)
        {
            throw new ArgumentOutOfRangeException(nameof(alternateHorizon), "交替视野必须是 0 到 10；0 表示不交替。");
        }

        if (thirdHorizon is < 0 or > 10)
        {
            throw new ArgumentOutOfRangeException(nameof(thirdHorizon), "第三档视野必须是 0 到 10；0 表示不用。");
        }

        _rolloutsPerAction = rolloutsPerAction;
        _futureTurnHorizon = futureTurnHorizon;
        _seed = seed == 0 ? 1UL : seed;
        _minimumPracticalAdvantage = rail;
        _selectionMode = selectionMode;
        _robustnessPenalty = robustnessPenalty;
        _statisticalConfidence = statisticalConfidence;
        _mulliganHorizon = mulliganHorizon == 0 ? futureTurnHorizon : mulliganHorizon;

        // 视野循环。每次推演按序号取其中一档，所以一个候选动作的分数是多档视野下估计值的平均。
        // 实测两档 {1,3} 远好于任一单档（76.3% vs 65.3% / 61.9%），说明不同视野的偏差方向
        // 相反、平均后互相抵消——这是集成效应，多一档可能更强。
        _horizonCycle = thirdHorizon > 0
            ? [futureTurnHorizon, alternateHorizon, thirdHorizon]
            : alternateHorizon > 0
                ? [futureTurnHorizon, alternateHorizon]
                : [futureTurnHorizon];

        // 第二个集成轴：推演由谁打完。规则牌手和"爬山同一个评估函数"是两套完全不同的判断，
        // 偏差方向也不同——而 2.0 的经验是"偏差相反的两个视角平均起来 > 任何一个"。
        // 两者单独用都是负收益（爬山单独用是 12:21），但组合起来未知，这正是要测的。
        _rolloutPolicyCycle = alternateRolloutPolicy != rolloutPolicy
            ? [rolloutPolicy, alternateRolloutPolicy]
            : [rolloutPolicy];
        _extraPlayPointPolicy = extraPlayPointPolicy;

        // 对手建模。默认还是规则牌手（= 1.0/2.0 的行为）。
        // 换成 EvaluatorGreedy 就是"把对手当成一个会优化自己局面的家伙"，而不是按手写规则出牌——
        // 真实对手是 2.0/3.0 级的搜索牌手，所以后者才是错的模型。
        // 注意：EvaluatorGreedy **双方都用**时是负收益（实测 12:21），
        // 但"只给对手用"从没测过，两者是完全不同的实验。
        _opponentRolloutPolicy = opponentRolloutPolicy;
        _useEvaluatorEnsemble = useEvaluatorEnsemble;
    }

    /// <summary>
    /// Picks the action the rollout plays next. Under <see cref="LookaheadRolloutPolicy.RuleAgent"/>
    /// this is 1.0's behaviour; under <see cref="LookaheadRolloutPolicy.EvaluatorGreedy"/> it is a
    /// one-step improvement of the same evaluator the leaf uses.
    /// </summary>
    private GameAction ChooseRolloutAction(
        GameState simulation,
        int activePlayer,
        IReadOnlyList<GameAction> legalActions,
        LookaheadRolloutPolicy policy)
    {
        if (policy == LookaheadRolloutPolicy.RuleAgent)
        {
            return _rolloutAgent.ChooseAction(
                GameEngine.ToObservation(simulation, activePlayer),
                legalActions);
        }

        var best = legalActions[0];
        var bestScore = double.NegativeInfinity;
        foreach (var action in legalActions)
        {
            var next = GameEngine.Apply(simulation, action);
            var score = next.IsGameOver
                ? next.Winner == activePlayer ? 1.0 : 0.0
                : EvaluatePosition(next, activePlayer);
            if (score > bestScore)
            {
                bestScore = score;
                best = action;
            }
        }

        return best;
    }

    /// <summary>The latest main-phase comparison, exposed for reports and later advisor UI.</summary>
    public LookaheadDecision? LastDecision { get; private set; }

    public GameAction ChooseAction(GameObservation observation, IReadOnlyList<GameAction> legalActions) =>
        throw new InvalidOperationException("LookaheadPlayerAgent must be run by MatchRunner so it can create safe determinizations.");

    public GameAction ChooseAction(
        GameState state,
        GameObservation observation,
        IReadOnlyList<GameAction> legalActions)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(observation);
        ArgumentNullException.ThrowIfNull(legalActions);

        if (legalActions.Count == 0)
        {
            throw new InvalidOperationException("An agent was asked to act without any legal actions.");
        }

        // 按对局选权重。放在最前面：这一手的所有推演都要用选中的那一份。
        ResolveMatchupWeights(observation);

        // 换牌也走搜索，不再直接交给规则牌手。
        //
        // 换牌决定整局的规划和前期节奏，而且"要不要全换去赌某张关键牌"是**卡组相关**的判断，
        // 一张一张按费用打分表达不了。用搜索则不需要任何卡组特判：每个换牌子集都会被推演到
        // 主回合初期再评估，所以"这套牌该不该留这个起手"是由实际推演结果决定的。
        //
        // 代价很小：起手 4 张只有 2^4 = 16 个子集，比主回合动辄几十个动作还少。
        var decisionNumber = ++_decisionNumber;
        var ruleAgentAction = _rolloutAgent.ChooseAction(observation, legalActions);

        // 换牌和主回合用不同的视野。换牌是"整局规划"的决定，而主回合的短视野是实测最优的
        // （视野 1 > 3 > 8）。用同一个值会把换牌也压成"只看打完第 1 回合"，
        // 那会系统性偏爱低费早起手、把高费制胜牌换掉。
        var horizon = observation.Phase == GamePhase.Mulligan ? _mulliganHorizon : _futureTurnHorizon;

        // 换牌不参与循环：它是一次性的整局规划决定，对冲多种视野在那里没有意义。
        var cycle = observation.Phase == GamePhase.Mulligan ? [horizon] : _horizonCycle;

        // 额外PP 的候选权。默认 Search = 搜索自由决定；另两个模式是为了正面回答
        // "这个限时资源的时机判断对不对"，见 LookaheadExtraPlayPointPolicy 的注释。
        var candidates = legalActions;
        if (_extraPlayPointPolicy != LookaheadExtraPlayPointPolicy.Search &&
            legalActions.Any(action => action is UseExtraPlayPointAction))
        {
            var keepExtraPlayPoint =
                _extraPlayPointPolicy == LookaheadExtraPlayPointPolicy.RuleGated &&
                ruleAgentAction is UseExtraPlayPointAction;
            if (!keepExtraPlayPoint)
            {
                candidates = legalActions
                    .Where(action => action is not UseExtraPlayPointAction)
                    .ToArray();
            }
        }

        var evaluations = candidates
            .Select((action, originalIndex) => EvaluateAction(
                state,
                observation.PerspectivePlayer,
                action,
                originalIndex,
                decisionNumber,
                cycle))
            .ToArray();

        // Reporting order is always "best average first", whatever the selection rule is.
        var ranked = evaluations
            .OrderByDescending(evaluation => evaluation.EstimatedWinChance)
            .ThenByDescending(evaluation => evaluation.CompletedWins)
            .ThenBy(evaluation => evaluation.OriginalLegalActionIndex)
            .ToArray();

        LookaheadActionEvaluation plannerPreferred;
        LookaheadActionEvaluation selected;

        if (_selectionMode == LookaheadSelectionMode.RuleAgentFallback)
        {
            plannerPreferred = ranked[0];

            // 规则牌手选的动作**可能压根不在候选里**：--extra-pp never / rule 会把"使用额外PP"
            // 从候选里剔掉，而规则牌手恰好想用它。这时 evaluations.Single 会抛
            // InvalidOperationException，整局对战直接崩。
            // 没有可比的基准就只能退回"用搜索的首选"—— 那本来就是剔除额外PP 之后最好的动作，
            // 和 --extra-pp never 的语义一致。
            var ruleAgentEvaluation = evaluations.FirstOrDefault(evaluation =>
                ReferenceEquals(evaluation.Action, ruleAgentAction));
            if (ruleAgentEvaluation is null)
            {
                selected = plannerPreferred;
            }
            else
            {
                // Every candidate is evaluated on the same sampled hidden worlds, so the decision rests on
                // the standard error of the *paired* per-rollout difference, on top of a floor below which
                // an edge is not worth acting on however consistent the samples happen to look.
                var requiredAdvantage = Math.Max(
                    _minimumPracticalAdvantage,
                    _statisticalConfidence * PairedStandardError(plannerPreferred, ruleAgentEvaluation));
                selected = plannerPreferred.EstimatedWinChance - ruleAgentEvaluation.EstimatedWinChance
                           <= requiredAdvantage
                    ? ruleAgentEvaluation
                    : plannerPreferred;
            }

            if (!ReferenceEquals(plannerPreferred.Action, ruleAgentAction) &&
                ReferenceEquals(selected.Action, ruleAgentAction))
            {
                Interlocked.Increment(ref _decisionsOverriddenByRail);
            }
        }
        else
        {
            plannerPreferred = _selectionMode == LookaheadSelectionMode.PureArgmax
                ? ranked[0]
                : ranked
                    .OrderByDescending(evaluation =>
                        evaluation.EstimatedWinChance -
                        (_robustnessPenalty * WorldStandardDeviation(evaluation)))
                    .ThenByDescending(evaluation => evaluation.CompletedWins)
                    .ThenBy(evaluation => evaluation.OriginalLegalActionIndex)
                    .First();
            selected = plannerPreferred;
        }

        Interlocked.Increment(ref _decisionsSeen);
        if (ReferenceEquals(plannerPreferred.Action, ruleAgentAction))
        {
            Interlocked.Increment(ref _plannerMatchesRuleAgent);
        }

        // 方差诊断：只在前两名都有逐次推演值、且标准误可用时记录。
        if (ranked.Length >= 2 && ranked[0].RolloutValues is not null && ranked[1].RolloutValues is not null)
        {
            var standardError = PairedStandardError(ranked[0], ranked[1]);
            if (standardError > 0 && !double.IsInfinity(standardError) && standardError < double.MaxValue)
            {
                var margin = ranked[0].EstimatedWinChance - ranked[1].EstimatedWinChance;
                Interlocked.Add(ref _snrMilliSum, (long)Math.Round(1000.0 * margin / standardError));
                Interlocked.Add(
                    ref _leafSpreadMilliSum,
                    (long)Math.Round(1000.0 * WorldStandardDeviation(ranked[0])));
                Interlocked.Increment(ref _varianceSamples);
            }
        }

        LastDecision = new LookaheadDecision(
            observation.PerspectivePlayer,
            observation.TurnNumber,
            selected.Action,
            ranked);
        return selected.Action;
    }

    /// <summary>
    /// How much one candidate's outcome depends on which hidden world was sampled.
    /// <para>
    /// All candidates are scored on the same worlds, so this spread is not evaluation noise that more
    /// rollouts would remove — it is a real property of the action: a large value means the action is
    /// only good in some imagined futures, which is what the agent cannot actually rely on. Returns 0
    /// when there is nothing to measure.
    /// </para>
    /// </summary>
    private static double WorldStandardDeviation(LookaheadActionEvaluation evaluation)
    {
        var values = evaluation.RolloutValues;
        if (values is null || values.Count < 2)
        {
            return 0.0;
        }

        var total = 0.0;
        var totalSquares = 0.0;
        for (var index = 0; index < values.Count; index++)
        {
            total += values[index];
            totalSquares += values[index] * values[index];
        }

        var mean = total / values.Count;
        return Math.Sqrt(Math.Max(0.0, (totalSquares / values.Count) - (mean * mean)));
    }

    private LookaheadActionEvaluation EvaluateAction(
        GameState liveState,
        int perspectivePlayer,
        GameAction candidate,
        int originalLegalActionIndex,
        long decisionNumber,
        IReadOnlyList<int> horizonCycle)
    {
        var totalValue = 0.0;
        var completedWins = 0;
        var completedLosses = 0;
        var rolloutValues = new double[_rolloutsPerAction];

        for (var rollout = 0; rollout < _rolloutsPerAction; rollout++)
        {
            // 视野循环：按推演序号轮换视野档位，所以候选动作的分数是多档视野估计值的平均。
            // 配对性不受影响——每个序号在两侧仍然共享同一个采样世界。
            var horizonUsed = horizonCycle[rollout % horizonCycle.Count];

            // The deadline belongs to the live position, not to the candidate action. In the
            // old order an EndTurnAction incremented TurnNumber before this was calculated,
            // so passing was judged from a later, higher-PP future than playing a card.
            var targetTurnNumber = liveState.TurnNumber + horizonUsed;

            // Every candidate receives the same sequence of sampled hidden worlds, making
            // comparison less noisy than giving each action unrelated random draws.
            var simulationSeed = SimulationSeed(decisionNumber, rollout);
            var simulation = GameEngine.CreateDeterminization(liveState, perspectivePlayer, simulationSeed);
            simulation = GameEngine.Apply(simulation, candidate);

            var simulatedActions = 0;
            while (!simulation.IsGameOver &&
                   simulation.TurnNumber < targetTurnNumber &&
                   simulatedActions < 400)
            {
                var activePlayer = simulation.ActivePlayer;
                var legalActions = GameEngine.GetLegalActions(simulation);

                // 两个循环用不同的除数，否则"交替视野"和"交替 rollout 策略"会同相位，
                // 第 1、3、5… 次推演同时拿到两个交替值，两个因素就分不开了。
                //
                // 对手用单独的策略：它模拟的是"别人怎么回应我"，那是另一个模型，
                // 和"我自己怎么把这回合打完"不该共用一个策略。
                var policy = activePlayer == perspectivePlayer
                    ? _rolloutPolicyCycle[(rollout / horizonCycle.Count) % _rolloutPolicyCycle.Length]
                    : _opponentRolloutPolicy;
                var rolloutAction = ChooseRolloutAction(simulation, activePlayer, legalActions, policy);
                simulation = GameEngine.Apply(simulation, rolloutAction);
                simulatedActions++;
            }

            if (simulation.IsGameOver)
            {
                if (simulation.Winner == perspectivePlayer)
                {
                    completedWins++;
                    totalValue += 1.0;
                    rolloutValues[rollout] = 1.0;
                }
                else
                {
                    completedLosses++;
                    rolloutValues[rollout] = 0.0;
                }

                continue;
            }

            // 评估函数集成：和视野循环用不同的除数，避免同相位。
            var useEnsembleWeights = _useEvaluatorEnsemble
                                     && EnsembleWeights is not null
                                     && (rollout / horizonCycle.Count) % 2 == 1;
            var positionValue = useEnsembleWeights
                ? EvaluatePosition(simulation, perspectivePlayer, EnsembleWeights!, EnsembleScoreScale)
                : EvaluatePosition(simulation, perspectivePlayer);
            totalValue += positionValue;
            rolloutValues[rollout] = positionValue;
        }

        return new LookaheadActionEvaluation(
            candidate,
            originalLegalActionIndex,
            totalValue / _rolloutsPerAction,
            _rolloutsPerAction,
            completedWins,
            completedLosses,
            rolloutValues);
    }

    /// <summary>
    /// The standard error of the paired per-rollout difference between two candidates. Both were
    /// evaluated on the same sampled hidden worlds, so the noise they share cancels and only the
    /// genuinely different part of the comparison is left.
    /// </summary>
    private static double PairedStandardError(
        LookaheadActionEvaluation first,
        LookaheadActionEvaluation second)
    {
        // Without per-rollout values there is nothing to pair, so the comparison cannot be made and
        // the reliable rule-agent action is kept.
        if (first.RolloutValues is null || second.RolloutValues is null)
        {
            return double.MaxValue;
        }

        var count = Math.Min(first.RolloutValues.Count, second.RolloutValues.Count);
        if (count < 2)
        {
            return double.MaxValue;
        }

        var total = 0.0;
        var totalSquares = 0.0;
        for (var index = 0; index < count; index++)
        {
            var difference = first.RolloutValues[index] - second.RolloutValues[index];
            total += difference;
            totalSquares += difference * difference;
        }

        var mean = total / count;
        var variance = Math.Max(0.0, (totalSquares / count) - (mean * mean));
        return Math.Sqrt(variance / count);
    }

    /// <summary>
    /// The evaluator's raw feature vector for one position, seen from one player's side.
    /// <para>
    /// Every entry is a difference between the two sides, so the vector is antisymmetric: negating
    /// the perspective negates every feature. That is what lets a single weight vector describe
    /// both players. The vector is exposed so self-play collection can label positions with the
    /// eventual winner and fit the weights, instead of leaving them hand-tuned.
    /// </para>
    /// </summary>
    public static double[] PositionFeatures(GameState state, int perspectivePlayer)
    {
        ArgumentNullException.ThrowIfNull(state);
        var self = state.Players[perspectivePlayer];
        var opponent = state.Players[perspectivePlayer == 0 ? 1 : 0];

        return
        [
            self.Health - opponent.Health,
            BoardValue(self.Board) - BoardValue(opponent.Board),
            AmuletValue(self.Amulets) - AmuletValue(opponent.Amulets),
            CrestBurden(opponent.Crests) - CrestBurden(self.Crests),
            self.Hand.Count - opponent.Hand.Count,
            self.Deck.Count - opponent.Deck.Count,
            self.CurrentPlayPoints - opponent.CurrentPlayPoints,
            self.MaxPlayPoints - opponent.MaxPlayPoints,
            self.EvolutionPoints + self.SuperEvolutionPoints -
                opponent.EvolutionPoints - opponent.SuperEvolutionPoints,
            DragonRainbowBoardValue(self) - DragonRainbowBoardValue(opponent),
            EndOfOwnTurnThreat(self, opponent) - EndOfOwnTurnThreat(opponent, self),
            -OpponentReach(opponent),
            // 以下四项专门刻画"场面交换"能力。原来的 BoardValue 只是把攻击力和防御力加总，
            // 表达不了中速梦的核心问题："我能不能吃你的场面，你能不能吃我的。"
            FreeKillAdvantage(self, opponent),
            AttackAdvantage(self, opponent),
            StandingAdvantage(state, self, opponent),
            WidthAdvantage(self, opponent),

            // ── 以下五项是"待兑现的账" ──
            //
            // 前面所有特征都只描述"现在有什么"。但本作里有大量价值是**挂在场上还没兑现**的：
            // 谢幕曲、被动效果、护符到期结算。这些东西写在卡面上、也写在状态里，
            // 而原来的评估函数**一个字都没读**（核对过：只读了护符倒数和一张纹章的特判）。
            //
            // 这正是"视野越长反而越差"的根因：把推演往前推，只会看到更多**自己算不出价值**的局面。
            // 加视野不会让看不见的东西变得看得见。
            //
            // 分类是客观的（按 CardEffectKind 归类），量级交给拟合的权重，
            // 所以这里不需要手调"一个谢幕曲值几分"这种可疑常数。
            PendingValue(self, PendingDamage) - PendingValue(opponent, PendingDamage),
            PendingValue(self, PendingHeal) - PendingValue(opponent, PendingHeal),
            PendingValue(self, PendingCards) - PendingValue(opponent, PendingCards),
            PendingValue(self, PendingDevelopment) - PendingValue(opponent, PendingDevelopment),
            PendingValue(self, PendingResource) - PendingValue(opponent, PendingResource)
        ];
    }

    /// <summary>
    /// 白吃数差：我方能用某个随从击杀、且不会被反杀的目标个数，减去对手对我方能做到同样事情的个数。
    /// </summary>
    private static int FreeKillAdvantage(PlayerState self, PlayerState opponent) =>
        FreeKills(self.Board, opponent.Board) - FreeKills(opponent.Board, self.Board);

    private static int FreeKills(
        IReadOnlyList<FollowerInstance> attackers,
        IReadOnlyList<FollowerInstance> targets) =>
        targets.Count(target => attackers.Any(attacker =>
            attacker.Attack > 0 &&
            attacker.Attack >= target.CurrentDefense &&
            target.Attack < attacker.CurrentDefense));

    /// <summary>双方场面总攻击力之差：我下回合能打出多少点。</summary>
    private static int AttackAdvantage(PlayerState self, PlayerState opponent) =>
        self.Board.Sum(follower => follower.Attack) - opponent.Board.Sum(follower => follower.Attack);

    /// <summary>
    /// 已站住的随从数之差（非本回合召唤）。这决定下一个回合谁先动手，
    /// 比"总攻击力"更能反映节奏归属。
    /// </summary>
    private static int StandingAdvantage(GameState state, PlayerState self, PlayerState opponent) =>
        self.Board.Count(follower => follower.SummonedOnTurn != state.TurnNumber) -
        opponent.Board.Count(follower => follower.SummonedOnTurn != state.TurnNumber);

    /// <summary>场面宽度之差。中速梦很吃站场数量，五个格子占几个是实打实的优势。</summary>
    private static int WidthAdvantage(PlayerState self, PlayerState opponent) =>
        self.Board.Count - opponent.Board.Count;

    /// <summary>
    /// One weight per <see cref="PositionFeatures"/> entry. These started as hand-tuned values;
    /// <c>--fit-weights</c> replaces them with weights fitted on self-play outcomes. The health
    /// term is the only one whose scale is anchored: in a health-difference unit, 2.0 means "one
    /// point of health is worth half of what one point of the raw score is".
    /// </summary>
    public static double[] PositionWeights { get; private set; } =
    [
        2.0,   // 生命差
        0.7,   // 场面差
        0.4,   // 护符差
        0.5,   // 对手纹章负担
        0.6,   // 手牌差
        0.15,  // 牌库差
        0.05,  // 当前能量点差
        0.45,  // 能量点上限差
        0.4,   // 进化点差
        0.35,  // 龙族虹卡场面
        0.45,  // 回合结束威胁差
        OpponentReachWeight, // 对手可达伤害
        // 以下四项是"场面交换"特征。实测（中速梦镜像 1500 局、闸门关、10 次推演）：
        //   12 项手调 → 决定性牌局 292:91（76.2%）
        //   16 项手调 → 272:102（72.7%）
        //   16 项中速梦专精拟合 → 271:112（70.8%）
        // 也就是说"加这些特征"和"把这些权重拟合得更准"都没有换来胜率，
        // 所以默认权重先归零（等价于原来的 12 项），特征本身保留供以后用别的目标拟合。
        0.0,   // 白吃数差（交换效率）
        0.0,   // 场面总攻击力差
        0.0,   // 已站住随从数差（节奏归属）
        0.0,   // 场面宽度差
        // 待兑现的账。初值是"每 1 点效果量值 0.6 分"的粗估，**必须用拟合替换**
        // （`--collect-selfplay` + `--fit-weights`）；这里只是给个不倒向零的起点。
        0.6,   // 待兑现伤害
        0.5,   // 待兑现回复
        0.4,   // 待兑现手牌
        0.4,   // 待兑现铺场/强化
        0.5    // 待兑现资源
    ];

    /// <summary>Divides the weighted feature sum before the logistic squash.</summary>
    public static double ScoreScale { get; private set; } = 12.0;

    /// <summary>
    /// Replaces the hand-tuned evaluator with fitted values. A logistic fit produces a single
    /// weight vector that already absorbs the scale, so callers pass a scale of 1.
    /// <para>
    /// This sets process-wide state, so it must run before any match starts — in practice right
    /// after the command line is parsed and before the parallel benchmark loop.
    /// </para>
    /// </summary>
    public static void ConfigureWeights(double[] weights, double scoreScale)
    {
        ArgumentNullException.ThrowIfNull(weights);
        if (weights.Length != PositionWeights.Length)
        {
            throw new ArgumentException(
                $"权重个数必须是 {PositionWeights.Length}，收到 {weights.Length}。",
                nameof(weights));
        }

        if (scoreScale <= 0 || double.IsNaN(scoreScale))
        {
            throw new ArgumentOutOfRangeException(nameof(scoreScale), "分数尺度必须为正数。");
        }

        PositionWeights = weights;
        ScoreScale = scoreScale;
    }

    // ───────────────────────── 按对局切换权重 ─────────────────────────
    //
    // 实测（各 40 个 BO10，同一批牌局、只换"权重是在哪种对局的数据上拟合的"）：
    //   权重来源        中速梦镜像    交叉对局
    //   镜像数据拟合    65.0 分      37.5 分
    //   交叉数据拟合    57.5 分      65.0 分
    // **每一列都是"在本对局上拟合的那一套"赢** —— 用对的一份值 7.5 ~ 27.5 个 BO10 分。
    // 而且镜像那一列里交叉拟合的输给镜像拟合的，排除了"交叉权重只是普遍更好"这个解释。
    //
    // 所以：同一套 21 项特征、按对局各拟合一份权重、开局选一份。
    // 键用**卡组编号**（稳定、可读，拟合本来就是按卡组分组的），不用特征。

    private static IReadOnlyDictionary<(string Own, string Opponent), (double[] Weights, double Scale)>?
        _matchupWeights;

    /// <summary>本局当前生效的权重。null = 用全局默认 <see cref="PositionWeights"/>。</summary>
    private double[]? _activeWeights;
    private double _activeScale = 1.0;
    private bool _matchupResolved;

    /// <summary>上一次看到的自己牌库长度，用来识别"新的一局"。</summary>
    private int _lastOwnDeckCount;

    public static void ConfigureMatchupWeights(
        IReadOnlyDictionary<(string Own, string Opponent), (double[] Weights, double Scale)>? matchupWeights) =>
        _matchupWeights = matchupWeights;

    /// <summary>
    /// 分辨本局双方各是哪套牌，并在权重表里选一份。
    /// <para>
    /// **只选一次。** 卡组在一局里不会变，而 <c>OwnDeckCardIds</c> 会随着抽牌缩短 ——
    /// 每手重算会让选中的权重中途跳变，那比不切换还糟。
    /// 对手要等第一张牌亮出来才认得出，所以认不出来就先用默认权重，下一手再试。
    /// </para>
    /// <para>
    /// <b>认不唯一就返回 null（不猜）</b>：猜错对局比不切换更糟。
    /// </para>
    /// </summary>
    private void ResolveMatchupWeights(GameObservation observation)
    {
        if (_matchupWeights is null)
        {
            return;
        }

        // 自己牌库突然变长 = 换了一局（牌手实例可能被多局复用）。
        // 阈值取 3：局内牌库只会因为抽牌变短，只有"洗回牌库"会小幅增加，不会一下多出 3 张以上。
        var ownDeckCount = observation.OwnDeckCardIds?.Count ?? 0;
        if (ownDeckCount > _lastOwnDeckCount + 3)
        {
            _matchupResolved = false;
            _activeWeights = null;
        }

        _lastOwnDeckCount = ownDeckCount;

        if (_matchupResolved)
        {
            return;
        }

        var own = IdentifySingleDeck(observation.OwnDeckCardIds);
        var opponent = IdentifySingleDeck(observation.Opponent.RevealedCardIds);
        if (own is null || opponent is null)
        {
            return;
        }

        _matchupResolved = true;
        if (_matchupWeights.TryGetValue((own, opponent), out var found))
        {
            _activeWeights = found.Weights;
            _activeScale = found.Scale;
        }
    }

    /// <summary>把一组卡牌编号认成牌库里的一副。候选不唯一就返回 null。</summary>
    public static string? IdentifySingleDeck(IReadOnlyList<string>? cardIds)
    {
        if (cardIds is null || cardIds.Count == 0)
        {
            return null;
        }

        var candidates = OpponentDeckInference.Identify(cardIds);
        return candidates.Count == 1 ? candidates[0].DeckId : null;
    }

    /// <summary>
    /// 叶子评估。<b>用这一局当前生效的权重</b>：默认是全局 <see cref="PositionWeights"/>，
    /// 按对局切换之后就是那一份（<c>EvaluatePosition</c> 本来就有一个接收 weights 的重载）。
    /// </summary>
    private double EvaluatePosition(GameState state, int perspectivePlayer) =>
        _activeWeights is null
            ? EvaluatePosition(state, perspectivePlayer, PositionWeights, ScoreScale)
            : EvaluatePosition(state, perspectivePlayer, _activeWeights, _activeScale);

    private static double EvaluatePosition(
        GameState state,
        int perspectivePlayer,
        double[] weights,
        double scoreScale)
    {
        var features = PositionFeatures(state, perspectivePlayer);

        // 装了神经网络就用它替代线性评估。见 NeuralPositionEvaluator 的注释：
        // 线性模型连续失败不是调参问题，是表达力问题。
        //
        // **只对 3.0 生效**：2.0（V2 类）有自己的权重（从它自己的只读引用传进来），
        // 所以这里必须用 ReferenceEquals 判断"这套权重是不是 3.0 的基准权重"，
        // 否则把网络的叶子塞给冻结锚点，验收就变成了"网络对网络" —— 那个数字没有意义。
        if (NeuralEvaluator is not null && ReferenceEquals(weights, PositionWeights))
        {
            return NeuralEvaluator.Evaluate(features);
        }

        var rawScore = 0.0;
        for (var index = 0; index < features.Length; index++)
        {
            rawScore += features[index] * weights[index];
        }

        // The logistic conversion turns a readable material/health score into a stable
        // 0..1 short-horizon win-chance estimate, without claiming it is an exact full-game win rate.
        return 1.0 / (1.0 + Math.Exp(-rawScore / scoreScale));
    }

    /// <summary>
    /// 可选的神经网络叶子评估。非 null 时**取代**线性评估（集成槽位仍走线性，方便做 A/B）。
    /// </summary>
    public static NeuralPositionEvaluator? NeuralEvaluator { get; private set; }

    public static void ConfigureNeuralEvaluator(NeuralPositionEvaluator? evaluator) =>
        NeuralEvaluator = evaluator;

    /// <summary>
    /// 集成用的第二套评估权重（null = 不集成）。第一个集成轴是视野，这个轴是**评估函数**：
    /// 手调权重和拟合权重是两个偏差方向不同的估计量，而 2.0 的经验正是
    /// "偏差相反的两个视角平均起来远好于任何一个"。单独用拟合权重是更差的（对 1.0 只有 64 分），
    /// 但那正是集成成员该有的样子 —— 视野 1 单独用也比集成差。
    /// </summary>
    public static double[]? EnsembleWeights { get; private set; }

    public static double EnsembleScoreScale { get; private set; } = 1.0;

    public static void ConfigureEnsembleWeights(double[] weights, double scoreScale)
    {
        ArgumentNullException.ThrowIfNull(weights);
        if (weights.Length != PositionWeights.Length)
        {
            throw new ArgumentException(
                $"集成权重个数必须是 {PositionWeights.Length}，收到 {weights.Length}。",
                nameof(weights));
        }

        if (scoreScale <= 0 || double.IsNaN(scoreScale))
        {
            throw new ArgumentOutOfRangeException(nameof(scoreScale), "分数尺度必须为正数。");
        }

        EnsembleWeights = weights;
        EnsembleScoreScale = scoreScale;
    }

    private static int BoardValue(IReadOnlyList<FollowerInstance> board) => board.Sum(follower =>
        (follower.Attack * 2) +
        follower.CurrentDefense +
        RarityBoardValue(follower.Definition.Rarity) +
        (follower.HasWard ? 2 : 0) +
        (follower.HasStorm ? 1 : 0) +
        // 突进原来**漏了**。它和疾驰一样让随从当回合就能攻击（只是不能打脸），
        // 而徒姬的被动正是"进场梦魇随从获得突进"——漏掉它等于把那条收益判成零。
        (follower.HasRush ? 1 : 0) +
        (follower.HasBane ? 3 : 0) +
        (follower.HasIntimidate ? 3 : 0) +
        (follower.HasBarrier ? 2 : 0) +
        (follower.HasAura ? 3 : 0));

    /// <summary>
    /// How much damage the opponent could still deliver on their next turn: their visible board
    /// plus an estimate of the burst hidden in their hand.
    /// <para>
    /// The hidden part comes from identifying which saved deck the opponent is on by the cards they
    /// have revealed, then asking what that deck could still be holding. It never reads their hand,
    /// so the planner stays honest about what it is allowed to know.
    /// </para>
    /// </summary>
    private static double OpponentReach(PlayerState opponent)
    {
        var boardAttack = opponent.Board.Sum(follower => follower.Attack);
        var burst = OpponentDeckInference.EstimatedHandBurst(
            opponent.RevealedCardIds,
            opponent.Hand.Count,
            opponent.MaxPlayPoints,
            opponent.Deck.Count);
        return boardAttack + burst;
    }

    private static double DragonRainbowBoardValue(PlayerState player)
    {
        // A rainbow follower that has reached the board is a concrete threat. A rainbow
        // still in hand is not automatically value: rewarding it made the old evaluator
        // prefer hoarding expensive cards and passing instead of using its PP.
        return player.Board
            .Where(follower => follower.Definition.Profession == CardProfession.Dragon &&
                               follower.Definition.Rarity == CardRarity.Rainbow)
            .Sum(follower => 8.0 + (follower.Definition.Cost * 0.5));
    }

    private static double EndOfOwnTurnThreat(PlayerState source, PlayerState target)
    {
        var value = 0.0;
        foreach (var follower in source.Board)
        {
            var effects = follower.IsEvolved
                ? follower.Definition.EvolvedEndOfOwnTurnEffects ?? []
                : follower.Definition.UnevolvedEndOfOwnTurnEffects ?? [];
            foreach (var effect in effects)
            {
                value += effect.Kind switch
                {
                    CardEffectKind.DealDamageToUpToTwoRandomEnemyFollowers =>
                        Math.Min(2, target.Board.Count) * effect.Amount * 0.45,
                    CardEffectKind.RestoreOwnLeaderHealth =>
                        Math.Min(effect.Amount, source.MaxHealth - source.Health) * 0.55,
                    CardEffectKind.DealDamageToEnemyLeader =>
                        Math.Min(effect.Amount, target.Health) * 0.9,
                    _ => 0
                };
            }
        }

        return value;
    }

    private static int RarityBoardValue(CardRarity rarity) => rarity switch
    {
        CardRarity.Rainbow => 5,
        CardRarity.Gold => 2,
        _ => 0
    };

    /// <summary>
    /// 护符的紧迫度。原来写成 <c>Sum(Countdown)</c>：倒数 4 的护符打 4 分、倒数 1（下回合就兑现）
    /// 打 1 分 —— **方向是反的**，越接近兑现反而分越低。而且视野 1 下倒数根本还没轮到减
    /// （递减发生在"我方下个回合开始"），所以 payoff 永远在推演之外。
    /// 这里只管"离兑现多近"，兑现出来值多少交给待兑现特征分类。
    /// </summary>
    private static int AmuletValue(IReadOnlyList<AmuletInstance> amulets) =>
        amulets?.Sum(amulet => amulet.Countdown is not { } countdown
            ? 2
            : Math.Max(1, 6 - countdown)) ?? 0;

    /// <summary>
    /// 任意纹章的负担。原来是硬编码只认"焦灰的安纳提玛·班德奈特"一张
    /// （单卡特判，和当初剑斗士换牌 bug 同一类隐患）。改成按纹章实际挂载的触发点计数，
    /// 这样**所有**纹章都被看见，而不是只有一张。
    /// </summary>
    private static int CrestBurden(IReadOnlyList<CrestInstance> crests) =>
        crests.Sum(crest =>
            (crest.Definition.StartOfOwnTurnEffects?.Count ?? 0) +
            (crest.Definition.OwnLeaderRestoredEffects?.Count ?? 0) +
            (crest.Definition.EndOfOwnTurnEffects?.Count ?? 0));

    // 待兑现效果的五个类别。分类客观、量级交给拟合权重。
    private const int PendingDamage = 0;
    private const int PendingHeal = 1;
    private const int PendingCards = 2;
    private const int PendingDevelopment = 3;
    private const int PendingResource = 4;

    /// <summary>
    /// 某一类"待兑现效果"的总量。来源：场上随从的谢幕曲与被动效果、护符到期结算的效果
    /// （按离兑现的远近加权）、以及护符自己的被动效果。
    /// <para>
    /// 这是评估函数里唯一一处解释"卡面上写了什么"的地方，也是让搜索能看见延迟收益的关键：
    /// 一只带谢幕曲的随从站在场上时，旧的评估函数只看到它的攻击力和体力。
    /// </para>
    /// </summary>
    private static double PendingValue(PlayerState player, int category)
    {
        var total = 0.0;

        foreach (var follower in player.Board)
        {
            total += SumPending(follower.Definition.LastWordsEffects, category);
            total += SumPending(follower.Definition.PassiveEffects, category);
            if (follower.IsEvolved)
            {
                total += SumPending(follower.Definition.EvolvedEndOfOwnTurnEffects, category);
            }
        }

        foreach (var amulet in player.Amulets)
        {
            // 离兑现越近，这笔账越"实"。
            var imminence = amulet.Countdown is not { } countdown
                ? 1.0
                : 1.0 / (1.0 + Math.Max(0, countdown));
            total += imminence * SumPending(amulet.LastWordsEffects, category);
            total += SumPending(amulet.Definition.PassiveEffects, category);
        }

        return total;
    }

    private static double SumPending(IReadOnlyList<CardEffect>? effects, int category) =>
        effects is null
            ? 0.0
            : effects.Where(effect => PendingCategoryOf(effect.Kind) == category)
                .Sum(effect => (double)effect.Amount);

    /// <summary>
    /// 把一个效果归到五个待兑现类别之一。故意只做**归类**、不做量级判断 ——
    /// "一个谢幕曲值 3 分还是 8 分"应该是拟合出来的权重，不是手调的常数。
    /// </summary>
    private static int PendingCategoryOf(CardEffectKind kind) => kind switch
    {
        CardEffectKind.DealDamageToEnemyFollowerOrLeader or
        CardEffectKind.DealDamageToUpToTwoEnemyFollowersAndLeader or
        CardEffectKind.SetEnemyLeaderMaxHealth or
        CardEffectKind.DealDamageToRandomEnemyFollower or
        CardEffectKind.DealDamageToRandomEnemyFollowerAndLeader or
        CardEffectKind.DealDamageToRandomEnemyFollowerIfOwnFollowersAttackedEnemyLeaderPreviousTurn or
        CardEffectKind.DistributeDamageAmongEnemyFollowersByEntryOrder or
        CardEffectKind.DecreaseAllEnemyFollowersDefense or
        CardEffectKind.DealDamageToAllEnemyFollowersAndLeader or
        CardEffectKind.DestroyRandomEnemyFollowerWithHighestAttack or
        CardEffectKind.DealDamageToEnemyLeader or
        CardEffectKind.DealDamageToEnemyLeaderIfAwakened or
        CardEffectKind.DealDamageToAllFollowersWithoutTrait or
        CardEffectKind.DealDamageToOwnLeader or
        CardEffectKind.DealDamageToAllEnemyFollowers or
        CardEffectKind.DestroyEnemyFollower or
        CardEffectKind.DealDamageToAllFollowersByFollowerCount or
        CardEffectKind.DealDamageToUpToTwoRandomEnemyFollowers or
        CardEffectKind.DealDamageToAllLeaders or
        CardEffectKind.DealDamageToEnemyFollower or
        CardEffectKind.DestroyRandomEnemyFollower or
        CardEffectKind.ShatterRandomLastWordsCardAndEnemyFollower => PendingDamage,

        CardEffectKind.RestoreOwnLeaderHealth or
        CardEffectKind.SetOwnLeaderMaxHealth or
        CardEffectKind.GrantOwnLeaderDamageImmunityUntilEndOfOpponentTurn => PendingHeal,

        CardEffectKind.DrawCards or
        CardEffectKind.ReturnOwnHandCardToDeckThenDrawCards or
        CardEffectKind.ReplaceOwnDeckWithApocalypseDeck or
        CardEffectKind.AddCopyToHandWithoutLastWords or
        CardEffectKind.DiscardOwnHandCards or
        CardEffectKind.DiscardOwnHandCardsUpTo or
        CardEffectKind.SearchDeckForFollowerWithMinimumCostToHand or
        CardEffectKind.AddCopyToHand or
        CardEffectKind.RecallFollowerFromGraveyard => PendingCards,

        CardEffectKind.SuperEvolveAnotherUnevolvedFollower or
        CardEffectKind.GainStats or
        CardEffectKind.SummonFollower or
        CardEffectKind.GainStatsToOtherAlliedFollowers or
        CardEffectKind.GainStormIfOwnFollowersAttackedEnemyLeaderPreviousTurn or
        CardEffectKind.GainTemporaryAttackIfOwnFollowersAttackedEnemyLeaderPreviousTurn or
        CardEffectKind.GiveEnemyCrest or
        CardEffectKind.GainStorm or
        CardEffectKind.GainStatsToRandomOtherAlliedFollower or
        CardEffectKind.SummonFollowerWithoutLastWords or
        CardEffectKind.BanishSelf or
        CardEffectKind.EvolveSelf or
        CardEffectKind.SummonRandomDistinctDeckFollowers or
        CardEffectKind.GrantRushToEnteringAlliedFollowers or
        CardEffectKind.EmpowerEnteringAlliedTraitFollowers or
        CardEffectKind.GainWard or
        CardEffectKind.GiveSelfCrest or
        CardEffectKind.SummonFollowerWithBonusWithoutLastWords => PendingDevelopment,

        CardEffectKind.RestoreOwnPlayPoints or
        CardEffectKind.IncreaseOwnMaxPlayPointsAndDrawIfAtTen or
        CardEffectKind.IncreaseOwnMaxPlayPoints or
        CardEffectKind.RestoreOwnEvolutionPoints => PendingResource,

        // 未知效果也归到"铺场/强化"里给一次被拟合的机会，而不是直接判零。
        _ => PendingDevelopment
    };

    private ulong SimulationSeed(long decisionNumber, int rollout)
    {
        var value = _seed +
                    (ulong)decisionNumber * 0x9E3779B97F4A7C15UL +
                    (ulong)(rollout + 1) * 0xBF58476D1CE4E5B9UL;
        value ^= value >> 30;
        value *= 0xBF58476D1CE4E5B9UL;
        value ^= value >> 27;
        value *= 0x94D049BB133111EBUL;
        return value ^ (value >> 31);
    }
}

/// <summary>One candidate-action estimate from the latest LookaheadPlayerAgent decision.</summary>
public sealed record LookaheadActionEvaluation(
    GameAction Action,
    int OriginalLegalActionIndex,
    double EstimatedWinChance,
    int Simulations,
    int CompletedWins,
    int CompletedLosses,
    /// <summary>
    /// The value of each individual rollout, in rollout order. Two candidates share their hidden
    /// worlds rollout by rollout, so these are what a paired comparison between candidates needs.
    /// Optional so that the frozen 1.0 baseline, which predates it, still compiles unchanged.
    /// </summary>
    IReadOnlyList<double>? RolloutValues = null);

/// <summary>Public decision information suitable for an advisor report without exposing hidden cards.</summary>
public sealed record LookaheadDecision(
    int Player,
    int TurnNumber,
    GameAction SelectedAction,
    IReadOnlyList<LookaheadActionEvaluation> Evaluations);
