using Shadowverse.Engine.Cards;
using Shadowverse.Engine.Game;
using Shadowverse.Engine.Models;

namespace Shadowverse.Engine.Agents;
/// <summary>
/// FROZEN 2.0 BASELINE SNAPSHOT，2026-09-13 冻结。
/// <para>
/// 这是 3.0 的对照基准。它的配置**写死在构造函数里**：视野循环 {1, 3}、rollout 用规则牌手、
/// 选择规则 RuleAgentFallback、回退闸统计强度 1.0 与下限 0、手调评估权重、分数尺度 12，
/// 每次候选 60 次推演。<b>本文件不得修改。</b>
/// </para>
/// <para>
/// 实测成绩（100 个 BO10 = 1000 局，对冻结 1.0）：<b>2.0 得 71 分，1.0 得 9 分，平手 20 个</b>，
/// 决定性牌局 182:50，总胜率 63.2%，先手 332/500、后手 300/500。
/// </para>
/// <para>
/// 它依赖共享的 <see cref="LookaheadSelectionMode"/> / <see cref="LookaheadRolloutPolicy"/> 枚举
/// 和 <see cref="LookaheadActionEvaluation"/> / <see cref="LookaheadDecision"/> 记录，
/// 这些留在活文件里；也依赖活的 <see cref="GreedyPlayerAgent"/> 作为 rollout 策略——
/// 那个类一旦改动，本快照的强度会跟着变，2.0 就必须重新冻结并重跑整条对照阶梯。
/// </para>
/// </summary>
public sealed class LookaheadPlayerAgentV2 : IStateAwarePlayerAgent
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
    private const double DefaultMinimumPracticalAdvantage = 0.0;

    private readonly double _minimumPracticalAdvantage;
    private readonly LookaheadSelectionMode _selectionMode;
    private readonly double _robustnessPenalty;
    private readonly double _statisticalConfidence;
    private readonly int _mulliganHorizon;
    private readonly int[] _horizonCycle;
    private readonly LookaheadRolloutPolicy[] _rolloutPolicyCycle;

    /// <summary>
    /// 2.0 的视野循环，写死。暴露成 public 是为了让冻结护栏自检（<c>RunFrozenLookaheadV2Test</c>）
    /// 能直接钉住它，而不是靠行为间接推断。
    /// </summary>
    public static readonly int[] FrozenHorizonCycle = [1, 3];

    /// <summary>
    /// 2.0 的配置在这里是**写死**的，不接受参数——这样"2.0"永远只指一个东西：
    /// 视野循环 {1, 3}、rollout 用规则牌手、选择规则 RuleAgentFallback、回退闸统计强度 1.0、
    /// 下限 0、手调评估权重、分数尺度 12。
    /// <para>只留 seed 和推演次数两个参数：seed 必须能变（每局要独立），推演次数留出来是为了
    /// 需要便宜筛选时能降预算，但**任何降预算的结果都必须标注"这不是完整 2.0"**。</para>
    /// </summary>
    public LookaheadPlayerAgentV2(ulong seed = 88_210UL, int rolloutsPerAction = 60)
    {
        if (rolloutsPerAction is < 1 or > 500)
        {
            throw new ArgumentOutOfRangeException(nameof(rolloutsPerAction), "Rollouts per action must be from 1 to 500.");
        }

        _rolloutsPerAction = rolloutsPerAction;
        _futureTurnHorizon = FrozenHorizonCycle[0];
        _seed = seed == 0 ? 1UL : seed;
        _minimumPracticalAdvantage = DefaultMinimumPracticalAdvantage;
        _selectionMode = LookaheadSelectionMode.RuleAgentFallback;
        _robustnessPenalty = 1.0;
        _statisticalConfidence = DefaultStatisticalConfidence;
        _mulliganHorizon = FrozenHorizonCycle[0];
        _horizonCycle = FrozenHorizonCycle;
        _rolloutPolicyCycle = [LookaheadRolloutPolicy.RuleAgent];
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
        throw new InvalidOperationException("LookaheadPlayerAgentV2 must be run by MatchRunner so it can create safe determinizations.");

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
        var evaluations = legalActions
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
            var ruleAgentEvaluation = evaluations.Single(evaluation =>
                ReferenceEquals(evaluation.Action, ruleAgentAction));

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
                var policy = _rolloutPolicyCycle[
                    (rollout / horizonCycle.Count) % _rolloutPolicyCycle.Length];
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

            var positionValue = EvaluatePosition(simulation, perspectivePlayer);
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

        // 2026-10-02 由用户裁掉 8 项"历史补丁"（20 → 12 维）：
        //   护符差 / 当前能量点差 / 能量点上限差 / 龙族虹卡场面
        //   白吃数差 / 场面总攻击力差 / 已站住随从数差 / 场面宽度差
        // 理由：它们或是某一代卡组的特例硬编码（"龙族虹卡"用的是收藏属性、与规则无关），
        // 或长期权重为 0 白占维度；留着会在拟合时污染结果。
        return
        [
            self.Health - opponent.Health,
            // 「场面差」与「纹章引擎价值」已删除（2026-10-02 用户裁定）：
            // 两者都在给"我留下的东西值多少"估值，与下面的「回合结束威胁差」职责重叠，
            // 分开写会互相污染。现在**统一由「回合结束威胁差」衡量**。
            CrestBurden(opponent.Crests) - CrestBurden(self.Crests),
            self.Hand.Count - opponent.Hand.Count,
            self.Deck.Count - opponent.Deck.Count,
            self.EvolutionPoints + self.SuperEvolutionPoints -
                opponent.EvolutionPoints - opponent.SuperEvolutionPoints,
            EndOfOwnTurnThreat(self, opponent) - EndOfOwnTurnThreat(opponent, self),
            -OpponentReach(opponent),
            // ── 2026-10-02 补的"引擎类"特征 ──
            // 起因：宇宙鱼（引擎型卡组）比中速梦差 10 个 BO10 分。查证发现评估函数
            // 对"引擎"几乎是瞎的：纹章只认一枚自伤纹章（其余恒为 0）、手牌与牌组只数张数、
            // 累计进化次数（瞬念召唤/解放奥义的充能进度）根本没有这一项。
            AccumulatedEvolutions(self) - AccumulatedEvolutions(opponent),
            HandThreat(self) - HandThreat(opponent),
            DeckSummonValue(self.Deck) - DeckSummonValue(opponent.Deck)
        ];
    }

    /// <summary>本次对战中累计进化过多少次 —— 【瞬念召唤】与【解放奥义】的充能进度。</summary>
    private static int AccumulatedEvolutions(PlayerState player) => player.OwnFollowersEvolvedThisBattle;

    /// <summary>
    /// 纹章作为**引擎**的价值：按纹章**实际带的效果**估值，而不是只认某一枚。
    /// 原实现只在 <see cref="CrestBurden"/> 里认「焦灰的安纳提玛」一枚、其余恒为 0，
    /// 于是「束刃的罪人：自己使用随从时每回合1次使其进化」这种引擎价值算出来是 0。
    /// </summary>
    private static int CrestEngineValue(IReadOnlyList<CrestInstance>? crests)
    {
        if (crests is null)
        {
            return 0;
        }

        var value = 0;
        foreach (var crest in crests)
        {
            var definition = crest.Definition;
            value += (definition.PassiveEffects?.Count ?? 0) * 4;          // 持续被动最值钱
            value += (definition.StartOfOwnTurnEffects?.Count ?? 0) * 3;   // 每回合白拿
            value += (definition.EndOfOwnTurnEffects?.Count ?? 0) * 2;
            value += (definition.OwnLeaderRestoredEffects?.Count ?? 0);
            value += (definition.LastWordsEffects?.Count ?? 0);
            value -= CountSelfDamage(definition) * 3;                      // 自伤是负担
        }

        return value;
    }

    private static int CountSelfDamage(CrestDefinition definition) =>
        (definition.StartOfOwnTurnEffects ?? [])
            .Concat(definition.OwnLeaderRestoredEffects ?? [])
            .Count(effect => effect.Kind == CardEffectKind.DealDamageToOwnLeader);

    /// <summary>
    /// 手牌的**威胁**（而不只是张数）。手里攥着一张【解放奥义】能打 10 点的斩杀牌，
    /// 和攥着一张废牌，`Hand.Count` 完全看不出区别。
    /// </summary>
    private static int HandThreat(PlayerState player) =>
        player.Hand.Sum(card => CardThreat(card.Definition));

    /// <summary>一张牌所有效果里能造成的伤害总量（覆盖入场曲/奥义/解放奥义/法术本体/谢幕曲等）。</summary>
    private static int CardThreat(CardDefinition definition)
    {
        var total = 0;
        foreach (var effect in AllEffectsOf(definition))
        {
            total += effect.Kind switch
            {
                CardEffectKind.DealDamageToEnemyFollowerOrLeader => effect.Amount,
                CardEffectKind.DealDamageToEnemyLeader => effect.Amount,
                CardEffectKind.DealDamageToRandomEnemyFollower => effect.Amount,
                CardEffectKind.DealDamageToAllEnemyFollowers => effect.Amount,
                CardEffectKind.DealDamageToAllEnemyFollowersAndLeader => effect.Amount,
                CardEffectKind.DealDamageToEnemyFollower => effect.Amount,
                // 「发动 N 次、每次 M 点」——总量是两者相乘。
                CardEffectKind.DealRandomDamageToEnemyFollowerOrLeaderRepeatedly => effect.Amount * effect.SecondaryAmount,
                CardEffectKind.DealDamageToRandomEnemyFollowerRepeatedly => effect.Amount * effect.SecondaryAmount,
                CardEffectKind.DealDamageToRandomEnemyFollowerCount => effect.Amount * effect.SecondaryAmount,
                _ => 0
            };
        }

        return total;
    }

    /// <summary>一张牌上所有可能造成伤害的效果槽。</summary>
    private static IEnumerable<CardEffect> AllEffectsOf(CardDefinition definition)
    {
        if (definition.Effect is not null)
        {
            yield return definition.Effect;
        }

        foreach (var group in new[]
                 {
                     definition.SpellEffects,
                     definition.FanfareEffects,
                     definition.OathEffects,
                     definition.SuperOathEffects,
                     definition.LastWordsEffects,
                     definition.OnEvolveEffects,
                     definition.EvolutionEffects,
                     definition.PassiveEffects
                 })
        {
            if (group is null)
            {
                continue;
            }

            foreach (var effect in group)
            {
                yield return effect;
            }
        }
    }

    /// <summary>牌组里"会自动跳出来"的卡（【瞬念召唤】）的价值 —— 也是张数看不出来的。</summary>
    private static int DeckSummonValue(IReadOnlyList<CardInstance>? deck) =>
        deck?.Count(card => card.Definition.TranscendentSummon is not null) * 3 ?? 0;

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
    public static readonly double[] PositionWeights =
    [
        // 2026-10-02 随特征表同步删到 12 项（用户裁定）。
        2.0,   // 生命差
        0.5,   // 对手纹章负担
        0.6,   // 手牌差
        0.15,  // 牌库差
        0.4,   // 进化点差
        0.45,  // 回合结束威胁差
        OpponentReachWeight, // 对手可达伤害
        // 以下四项是 2026-10-02 补的"引擎类"特征。
        // ⚠️ 这里用的是**手调值，不是拟合值** —— 实测拟合值明显更差，故意不采用：
        //   拟合（--fit-weights，宇宙鱼 200 局自对弈、15389 条样本，验证集 0.63960）：
        //     宇宙鱼 2.0 vs 1.0 = **51.0%**（BO10 50）—— 比手调差 16 个百分点
        //   手调（本组）：
        //     宇宙鱼 2.0 vs 1.0 = **67.0%**（决定性 39:13，BO10 80）
        // 原因：拟合目标是"预测自对弈的最终胜负"（最小化对数损失），
        // 与"在对局中赢下 1.0"不是同一个目标 —— 与项目既有记录一致
        // （第 620-625 行记的"中速梦专精拟合"同样没换来胜率）。
        0.30,  // 累计进化次数差（瞬念召唤/解放奥义的充能进度）
        0.10,  // 手牌威胁差
        0.15   // 牌组瞬念召唤价值差
    ];

    /// <summary>Divides the weighted feature sum before the logistic squash.</summary>
    public const double ScoreScale = 12.0;

    private static double EvaluatePosition(GameState state, int perspectivePlayer)
    {
        var features = PositionFeatures(state, perspectivePlayer);
        var weights = PositionWeights;
        var rawScore = 0.0;
        for (var index = 0; index < features.Length; index++)
        {
            rawScore += features[index] * weights[index];
        }

        // 可学习价值表（纹章 / 自己的手牌）。
        // 关键点：**对手手牌是暗牌，不参与**；对手的纹章是公开信息，参与。
        rawScore += LearnedValueOf(state, perspectivePlayer)
                    - LearnedValueOf(state, perspectivePlayer == 0 ? 1 : 0);

        // The logistic conversion turns a readable material/health score into a stable
        // 0..1 short-horizon win-chance estimate, without claiming it is an exact full-game win rate.
        return 1.0 / (1.0 + Math.Exp(-rawScore / ScoreScale));
    }

    /// <summary>
    /// 一方的"可学习价值"= 它持有的纹章价值之和 + 它**手牌**的卡价值之和。
    /// 手牌只算自己那一方 —— 对手手牌看不到，算进去就是作弊。
    /// </summary>
    private static double LearnedValueOf(GameState state, int playerIndex)
    {
        LearnedValues.EnsureLoaded();
        var player = state.Players[playerIndex];
        var value = 0.0;
        foreach (var crest in player.Crests)
        {
            value += LearnedValues.Crest(crest.Definition.Id);
        }

        foreach (var card in player.Hand)
        {
            value += LearnedValues.HandCard(card.Definition.Id);
        }

        return value;
    }

    private static int BoardValue(IReadOnlyList<FollowerInstance> board) => board.Sum(follower =>
        (follower.Attack * 2) +
        follower.CurrentDefense +
        RarityBoardValue(follower.Definition.Rarity) +
        (follower.HasWard ? 2 : 0) +
        (follower.HasStorm ? 1 : 0) +
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

    /// <summary>引擎随从/纹章按"预计还能活几个回合"折算成残值。可标定，初值 2。</summary>
    private const double EngineLifespanTurns = 2.0;

    /// <summary>
    /// **回合结束时的局面价值 = 剩余场面 + 每回合产出 × 存活期。**
    /// <para>
    /// 这一项**吸收**了原来的「场面差」与「纹章引擎价值」（2026-10-02 用户裁定）：
    /// 三者本来都在给"我留下的东西值多少"估值，分开写会互相污染。
    /// </para>
    /// <para>
    /// 产出按**实际能打到的量**折算，而不是名义数字 ——
    /// 「回合结束扫3」打在 5 个随从上和打在 1 个随从上，威胁当然不同；
    /// 「光环召唤 3/3」和「召唤 1/1」也差 3 倍。这是用户要求的量化口径。
    /// </para>
    /// </summary>
    private static double EndOfOwnTurnThreat(PlayerState source, PlayerState target)
    {
        // ① 剩余场面
        var attack = source.Board.Sum(follower => follower.Attack);
        var defence = source.Board.Sum(follower => follower.CurrentDefense);
        var wardDefence = source.Board.Where(follower => follower.HasWard)
            .Sum(follower => follower.CurrentDefense);
        // 守护的价值**上限是它实际能挡住的伤害** —— 给 10 个守护随从也不能超过对手能打出的总量。
        var wardValue = Math.Min(wardDefence, OpponentReach(target));
        // 对手难以处理的随从（【灵气】不能被指定 /【威慑】）额外加成。
        var sticky = source.Board.Count(follower => follower.HasAura || follower.HasIntimidate) * 6.0;

        // ② 每回合产出：场上引擎随从 + 纹章，按同一套折算（纹章不再"只认一枚"）
        var engine = source.Board.Sum(follower =>
                PerTurnOutput(source, target, follower.IsEvolved
                    ? follower.Definition.EvolvedEndOfOwnTurnEffects ?? []
                    : follower.Definition.UnevolvedEndOfOwnTurnEffects ?? [])
                + PerTurnOutput(source, target, follower.Definition.PassiveEffects ?? []))
            + source.Crests.Sum(crest =>
                PerTurnOutput(source, target, crest.Definition.StartOfOwnTurnEffects ?? [])
                + PerTurnOutput(source, target, crest.Definition.EndOfOwnTurnEffects ?? [])
                + PerTurnOutput(source, target, crest.Definition.PassiveEffects ?? []));

        return attack + defence + wardValue + sticky + (engine * EngineLifespanTurns);
    }

    /// <summary>
    /// 一组效果"每回合能产出多少" —— **按实际能打到的量折算**。
    /// 「毁灭创造物γ：回合结束扫3」这种光环，对手不处理它就会被持续清场，
    /// 所以它的价值 = 每回合真实产出 × 存活期，而不是一个拍出来的系数。
    /// </summary>
    private static double PerTurnOutput(
        PlayerState source,
        PlayerState target,
        IReadOnlyList<CardEffect> effects)
    {
        var value = 0.0;
        foreach (var effect in effects)
        {
            value += effect.Kind switch
            {
                // 扫场：只算真能打到的随从数（最多 5 个位置）。
                CardEffectKind.DealDamageToAllEnemyFollowers =>
                    effect.Amount * Math.Min(target.Board.Count, 5),
                CardEffectKind.DealDamageToAllEnemyFollowersAndLeader =>
                    (effect.Amount * Math.Min(target.Board.Count, 5)) + effect.Amount,
                CardEffectKind.DealDamageToUpToTwoRandomEnemyFollowers =>
                    Math.Min(2, target.Board.Count) * (double)effect.Amount,
                // 分配伤害：上限是"对手场上防御总量能吃掉的"。
                CardEffectKind.DistributeDamageAmongEnemyFollowersByEntryOrder =>
                    Math.Min(effect.Amount, target.Board.Sum(follower => follower.CurrentDefense)),
                // 召唤：按**召唤体的攻+防**算（3/3 与 1/1 差 3 倍）。
                CardEffectKind.SummonFollower => SummonedBodyValue(effect) * effect.Amount,
                // 打主战者 / 回血：按实际值，回血受"还缺多少血"封顶。
                CardEffectKind.DealDamageToEnemyLeader =>
                    Math.Min(effect.Amount, target.Health),
                CardEffectKind.RestoreOwnLeaderHealth =>
                    Math.Min(effect.Amount, source.MaxHealth - source.Health),
                CardEffectKind.EvolveOtherFollowerEnteringWithPrintedCostAtLeast => 6.0,
                CardEffectKind.EvolvePlayedFollowerOncePerTurn => 4.0,
                _ => 0
            };
        }

        return value;
    }

    /// <summary>被召唤出来的那个随从值多少（攻 + 防）。取不到就按 0。</summary>
    private static double SummonedBodyValue(CardEffect effect)
    {
        if (effect.ReferencedCardId is null)
        {
            return 0;
        }

        var summoned = CardCatalog.Get(effect.ReferencedCardId);
        return summoned.Attack + summoned.Defense;
    }

    private static int RarityBoardValue(CardRarity rarity) => rarity switch
    {
        CardRarity.Rainbow => 5,
        CardRarity.Gold => 2,
        _ => 0
    };

    private static int AmuletValue(IReadOnlyList<AmuletInstance> amulets) =>
        amulets?.Sum(amulet => amulet.Countdown ?? 1) ?? 0;

    private static int CrestBurden(IReadOnlyList<CrestInstance> crests) =>
        crests.Count(crest => crest.Definition.Id == CrestIds.AshenAnathemaBanderst) * 3;

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

