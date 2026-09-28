using System.Security.Cryptography;
using System.Text;
using Shadowverse.Engine.Game;
using Shadowverse.Engine.Models;

namespace Shadowverse.Engine.Agents;

/// <summary>根动作的搜索统计（用于接线报告与验收断言）。</summary>
public sealed record V4RootActionStat(string ActionKey, int Visits, double MeanValue);

/// <summary>
/// 一次决策的搜索统计。**必须可复现**：同状态同种子逐项一致。
/// </summary>
public sealed record V4SearchStats(
    int ConfiguredIterations,
    int EffectiveIterations,
    int RootVisits,
    int MinRootVisits,
    int NodeCount,
    int MaxDepth,
    int Determinizations,
    int SharedNodeMaxDeterminizations,
    int SharedNodeMaxDeterminizationsNonRoot,
    int ConvergentNodes,
    int NodeMerges,
    int DepthCapHits,
    int TurnEndLeaves,
    int TerminalLeaves,
    int OtherLeaves,
    IReadOnlyList<V4RootActionStat> RootActions,
    double ElapsedMs,
    string ChosenActionKey);

/// <summary>
/// <b>4.0 原型：回合内信息集树搜索（ISMCTS 风格）。</b>
///
/// <para><b>与 3.0（<see cref="LookaheadPlayerAgent"/>）的本质区别</b>：3.0 是"每个候选动作各做 N 次独立推演、
/// 再比平均值"的平坦做法；4.0 建一棵树，节点按<b>行动方在真实决策点上的公开决策状态</b>建键，
/// 不同确定化只要落到同一公开决策状态，就更新同一节点与同一套动作统计。</para>
///
/// <para><b>节点键（[DIR-7] 第 2、3、10 条修订）</b>：<see cref="PublicDecisionKey"/> =
/// 完整可见状态（含随从/护符的<b>公开实例身份</b>）+ <b>排序后的合法动作键</b> + <b>剩余树深度</b>。
/// 只用 <see cref="GameEngine.ToObservation"/> 能给出的信息；对手手牌内容、牌库顺序、RNG 状态都不在其中。</para>
///
/// <para><b>首版刻意不做的事</b>：不启用旧回退闸、不按对局特调、不改任何权重（改为在构造时<b>冻结副本</b>，
/// 免得运行期间全局权重变化改变同一个 V4 的身份）、不永久裁掉合法动作。</para>
/// </summary>
public sealed class InformationSetTurnMctsAgentV4 : IStateAwarePlayerAgent
{
    private const int PlayoutGuard = 600;

    /// <summary>
    /// [RES-11A] **采样器知识标记**：本实现通过 <see cref="GameEngine.CreateDeterminization"/> 采样，
    /// 而该 API **保留了对手隐藏牌的真实牌池**（只重洗"哪几张在手上 / 顺序"）。
    /// <para>
    /// 含义：V4 只能用于"**同等信息条件**下比较搜索架构"的诊断；它**不是**只依据公开信息的采样，
    /// 因此**不代表可部署的公平牌手**。冻结的 2.0 / 3.0 用的是同一个 API，所以三方条件相同。
    /// 未来若 V4 搜索架构本身出现正向证据，再另加"只依据公开信息 + 候选牌组先验"的采样器并做第二阶段复验。
    /// </para>
    /// </summary>
    public const string DeterminizationKnowledge = "legacy_true_hidden_pool";

    private readonly int _iterations;
    private readonly int _maxDepth;
    private readonly double _exploration;
    private readonly double _widening;
    private readonly int _leafHorizonTurns;
    private readonly ulong _seed;
    private readonly GreedyPlayerAgent _rule = new();

    /// <summary>[DIR-7] 构造时**冻结**叶值权重与尺度：运行期间改全局权重不得改变同一个 V4 的身份。</summary>
    private readonly double[] _leafWeights;
    private readonly double _leafScoreScale;

    private ulong _decisionNumber;

    public InformationSetTurnMctsAgentV4(
        ulong seed,
        int iterationsPerDecision = 200,
        int maxDepth = 8,
        double exploration = 0.7,
        int leafHorizonTurns = 3,
        double widening = 1.0)
    {
        if (iterationsPerDecision < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(iterationsPerDecision), "迭代预算必须 ≥ 1。");
        }

        if (maxDepth < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxDepth), "最大深度必须 ≥ 1。");
        }

        if (exploration < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(exploration), "探索常数不得为负。");
        }

        if (widening <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(widening), "渐进扩展系数必须为正。");
        }

        _seed = seed;
        _iterations = iterationsPerDecision;
        _maxDepth = maxDepth;
        _exploration = exploration;
        _leafHorizonTurns = Math.Max(1, leafHorizonTurns);
        _widening = widening;

        var weights = LookaheadPlayerAgent.PositionWeights;
        _leafWeights = new double[weights.Length];
        Array.Copy(weights, _leafWeights, weights.Length);
        _leafScoreScale = LookaheadPlayerAgent.ScoreScale;
    }

    /// <summary>最近一次决策的统计（供接线普查读取）。</summary>
    public V4SearchStats? LastStats { get; private set; }

    public int IterationsPerDecision => _iterations;

    public GameAction ChooseAction(GameObservation observation, IReadOnlyList<GameAction> legalActions) =>
        throw new InvalidOperationException(
            "InformationSetTurnMctsAgentV4 必须由 MatchRunner 驱动，才能通过 CreateDeterminization 安全采样隐藏世界。");

    public GameAction ChooseAction(
        GameState state,
        GameObservation observation,
        IReadOnlyList<GameAction> legalActions)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(observation);
        ArgumentNullException.ThrowIfNull(legalActions);

        var decision = ++_decisionNumber;
        var (action, stats) = Search(state, observation.PerspectivePlayer, legalActions, decision);
        LastStats = stats;
        return action;
    }

    // ------------------------------------------------------------------ 搜索主体

    /// <summary>直接驱动一次搜索（验收测试也走这里，保证测的就是生产路径）。</summary>
    public (GameAction Action, V4SearchStats Stats) Search(
        GameState state,
        int perspectivePlayer,
        IReadOnlyList<GameAction> legalActions,
        ulong decisionNumber)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(legalActions);

        if (legalActions.Count == 0)
        {
            throw new InvalidOperationException("没有合法动作时不应调用搜索。");
        }

        // [DIR-7 第 4 条] 预算至少覆盖全部根合法动作，否则"根动作全覆盖"这条要求不可能成立。
        var effectiveIterations = Math.Max(_iterations, legalActions.Count);

        var started = System.Diagnostics.Stopwatch.StartNew();
        var rootKey = PublicDecisionKey(state, perspectivePlayer, _maxDepth);
        var tree = new Tree(rootKey);
        var rootLegalKeys = legalActions.Select(ActionKey).ToList();

        var determinizations = 0;
        var depthCapHits = 0;
        var turnEndLeaves = 0;
        var terminalLeaves = 0;
        var otherLeaves = 0;

        for (var iteration = 0; iteration < effectiveIterations; iteration++)
        {
            var detSeed = DeriveSeed(_seed, decisionNumber, (ulong)iteration);
            var world = GameEngine.CreateDeterminization(state, perspectivePlayer, detSeed);
            determinizations++;

            var rootActive = world.ActivePlayer;
            var rootPhase = world.Phase;
            var startTurn = world.TurnNumber;

            var node = 0;
            var sim = world;
            var depth = 0;
            var path = new List<(int Node, int ActionIndex)>();

            // [DIR-8 第 2 条] 每次迭代**必须恰好记录一个**终止原因，否则统计不守恒：
            //   0=other 1=depthCap 2=turnEnd 3=terminal
            var exitReason = 0;

            while (true)
            {
                if (sim.IsGameOver)
                {
                    exitReason = 3;
                    break;
                }

                // [DIR-7 第 1 条] 停止条件必须**同时**看 ActivePlayer / Phase / TurnNumber。
                // 只比 TurnNumber 会漏掉换牌阶段：首位玩家换牌后 TurnNumber 不变、只有 ActivePlayer 切换，
                // 于是搜索会继续替对手换牌并按我方收益最大化（等于假设对手合作）。
                if (sim.ActivePlayer != rootActive || sim.Phase != rootPhase || sim.TurnNumber != startTurn)
                {
                    exitReason = 2;
                    break;
                }

                if (depth >= _maxDepth)
                {
                    exitReason = 1;
                    break;
                }

                var legal = GameEngine.GetLegalActions(sim);
                if (legal.Count == 0)
                {
                    exitReason = 0;
                    break;
                }

                var legalKeys = legal.Select(ActionKey).ToList();
                tree.Nodes[node].Determinizations.Add(detSeed);

                int actionIndex;
                if (depth == 0 && iteration < rootLegalKeys.Count)
                {
                    // 根节点所有合法动作至少访问一次：前 |A(root)| 次迭代逐个强制访问。
                    var forcedKey = legalKeys.Contains(rootLegalKeys[iteration], StringComparer.Ordinal)
                        ? rootLegalKeys[iteration]
                        : legalKeys[0];
                    actionIndex = tree.EnsureAction(node, forcedKey);
                }
                else
                {
                    actionIndex = tree.SelectAction(node, legalKeys, _exploration, _widening, _seed, decisionNumber, depth == 0);
                }

                var key = tree.Nodes[node].ActionKeys[actionIndex];
                var chosen = PickAction(legal, key);
                path.Add((node, actionIndex));

                var next = GameEngine.Apply(sim, chosen);
                sim = next;
                depth++;
                tree.MaxDepth = Math.Max(tree.MaxDepth, depth);

                if (next.IsGameOver
                    || next.ActivePlayer != rootActive
                    || next.Phase != rootPhase
                    || next.TurnNumber != startTurn)
                {
                    // 终局或本回合结束：标记该动作"已扩展但没有子节点"，否则渐进扩展会永远重新选中它。
                    // **同时必须记下终止原因**（[DIR-8 第 2 条]）：漏记会让"回合结束率/终局率"变成假数据。
                    tree.MarkLeaf(node, actionIndex);
                    exitReason = next.IsGameOver ? 3 : 2;
                    break;
                }

                // [DIR-7 第 3、10 条] 子节点键 = 公开决策状态（可见状态 + 合法动作集合 + 剩余深度）
                var childKey = PublicDecisionKey(next, next.ActivePlayer, _maxDepth - depth);
                var childIndex = tree.GetOrCreateNode(childKey);
                tree.LinkChild(node, actionIndex, childIndex);
                node = childIndex;
            }

            switch (exitReason)
            {
                case 1: depthCapHits++; break;
                case 2: turnEndLeaves++; break;
                case 3: terminalLeaves++; break;
                default: otherLeaves++; break;
            }

            var value = LeafValue(sim, perspectivePlayer);
            foreach (var (n, a) in path)
            {
                var target = tree.Nodes[n];
                target.Visits[a]++;
                target.Sums[a] += value;
                target.TotalVisits++;
            }
        }

        var root = tree.Nodes[0];
        var rootVisits = new List<int>();
        for (var i = 0; i < root.ActionKeys.Count; i++)
        {
            rootVisits.Add(root.Visits[i]);
        }

        var best = -1;
        for (var i = 0; i < root.ActionKeys.Count; i++)
        {
            if (root.Visits[i] == 0)
            {
                continue;
            }

            if (best < 0)
            {
                best = i;
                continue;
            }

            var betterVisits = root.Visits[i] > root.Visits[best];
            var sameVisits = root.Visits[i] == root.Visits[best];
            var meanI = root.Sums[i] / root.Visits[i];
            var meanBest = root.Sums[best] / root.Visits[best];
            var betterMean = sameVisits && meanI > meanBest + 1e-12;
            var sameMean = sameVisits && Math.Abs(meanI - meanBest) <= 1e-12;
            var betterKey = sameMean && string.CompareOrdinal(root.ActionKeys[i], root.ActionKeys[best]) < 0;
            if (betterVisits || betterMean || betterKey)
            {
                best = i;
            }
        }

        if (best < 0)
        {
            throw new InvalidOperationException("搜索没有访问任何根动作——这不应发生。");
        }

        started.Stop();
        var rootStats = new List<V4RootActionStat>();
        for (var i = 0; i < root.ActionKeys.Count; i++)
        {
            var visits = root.Visits[i];
            rootStats.Add(new V4RootActionStat(
                root.ActionKeys[i],
                visits,
                visits == 0 ? 0.0 : root.Sums[i] / visits));
        }

        var chosenKey = root.ActionKeys[best];
        var stats = new V4SearchStats(
            _iterations,
            effectiveIterations,
            rootStats.Sum(stat => stat.Visits),
            rootStats.Count == 0 ? 0 : rootStats.Min(stat => stat.Visits),
            tree.Nodes.Count,
            tree.MaxDepth,
            determinizations,
            tree.Nodes.Max(n => n.Determinizations.Count),
            tree.Nodes.Skip(1).Select(n => n.Determinizations.Count).DefaultIfEmpty(0).Max(),
            tree.Nodes.Count(n => n.Incoming.Count >= 2),
            tree.Merges,
            depthCapHits,
            turnEndLeaves,
            terminalLeaves,
            otherLeaves,
            rootStats,
            started.Elapsed.TotalMilliseconds,
            chosenKey);

        return (PickAction(legalActions, chosenKey), stats);
    }

    // ------------------------------------------------------------------ 叶值

    /// <summary>叶值：终局直接给 0/1；否则用规则牌手把局面走完到 {1,3} 两个视野再评估，取平均。</summary>
    public double LeafValue(GameState state, int perspectivePlayer)
    {
        if (state.IsGameOver)
        {
            return state.Winner == perspectivePlayer ? 1.0 : 0.0;
        }

        var shortValue = PlayoutValue(state, perspectivePlayer, 1);
        var longValue = PlayoutValue(state, perspectivePlayer, _leafHorizonTurns);
        return (shortValue + longValue) / 2.0;
    }

    private double PlayoutValue(GameState state, int perspectivePlayer, int turns)
    {
        var sim = state;
        var startTurn = sim.TurnNumber;
        var guard = 0;
        while (!sim.IsGameOver && guard++ < PlayoutGuard)
        {
            if (sim.TurnNumber >= startTurn + turns)
            {
                break;
            }

            var player = sim.ActivePlayer;
            var observation = GameEngine.ToObservation(sim, player);
            var legal = GameEngine.GetLegalActions(sim);
            if (legal.Count == 0)
            {
                break;
            }

            var action = _rule.ChooseAction(observation, legal);
            sim = GameEngine.Apply(sim, action);
        }

        if (sim.IsGameOver)
        {
            return sim.Winner == perspectivePlayer ? 1.0 : 0.0;
        }

        return LinearLeaf(sim, perspectivePlayer);
    }

    /// <summary>与 3.0 线性叶值**同一公式**，但用构造时冻结的权重副本。</summary>
    private double LinearLeaf(GameState state, int perspectivePlayer)
    {
        var features = LookaheadPlayerAgent.PositionFeatures(state, perspectivePlayer);
        var raw = 0.0;
        for (var index = 0; index < features.Length && index < _leafWeights.Length; index++)
        {
            raw += features[index] * _leafWeights[index];
        }

        return 1.0 / (1.0 + Math.Exp(-raw / _leafScoreScale));
    }

    // ------------------------------------------------------------------ 公开决策状态键

    /// <summary>
    /// [DIR-7 第 3 条] 统一的公开决策状态键：**完整可见状态 + 排序后的合法动作键 + 剩余树深度**。
    /// 用哈希值作为节点身份；调试时用 <see cref="PublicDecisionKeyMaterial"/> 看原文。
    /// </summary>
    public static string PublicDecisionKey(GameState state, int perspectivePlayer, int remainingDepth)
    {
        var material = PublicDecisionKeyMaterial(state, perspectivePlayer, remainingDepth);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material)));
    }

    /// <summary>键的原文（仅供调试与断言；键本身是它的 SHA256）。</summary>
    public static string PublicDecisionKeyMaterial(GameState state, int perspectivePlayer, int remainingDepth)
    {
        ArgumentNullException.ThrowIfNull(state);
        var observation = GameEngine.ToObservation(state, perspectivePlayer);
        var legalKeys = GameEngine.GetLegalActions(state)
            .Select(ActionKey)
            .OrderBy(key => key, StringComparer.Ordinal);
        return DescribeObservation(observation)
            + "|L=" + string.Join(',', legalKeys)
            + "|R=" + remainingDepth;
    }

    /// <summary>可读的可见信息描述（**含公开实例身份**，[DIR-7 第 2 条]）。</summary>
    public static string DescribeObservation(GameObservation observation)
    {
        ArgumentNullException.ThrowIfNull(observation);
        var sb = new StringBuilder(512);
        sb.Append("P=").Append(observation.PerspectivePlayer)
          .Append(";A=").Append(observation.ActivePlayer)
          .Append(";T=").Append(observation.TurnNumber)
          .Append(";Ph=").Append(observation.Phase);
        AppendView(sb, "S", observation.Self);
        AppendView(sb, "O", observation.Opponent);

        sb.Append(";hand=[");
        foreach (var card in observation.OwnHand)
        {
            sb.Append(card.InstanceId).Append(':').Append(card.Definition.Id).Append(',');
        }

        sb.Append("];deck=[");
        if (observation.OwnDeckCardIds is not null)
        {
            foreach (var id in observation.OwnDeckCardIds)
            {
                sb.Append(id).Append(',');
            }
        }

        sb.Append(']');
        return sb.ToString();
    }

    private static void AppendView(StringBuilder sb, string tag, PlayerView view)
    {
        sb.Append(';').Append(tag).Append('{')
          .Append("hp=").Append(view.Health).Append('/').Append(view.MaxHealth)
          .Append(",pp=").Append(view.CurrentPlayPoints).Append('/').Append(view.MaxPlayPoints)
          .Append(",ot=").Append(view.OwnTurnNumber)
          .Append(",ep=").Append(view.EvolutionPoints).Append('/').Append(view.SuperEvolutionPoints)
          .Append(",fl=").Append(view.UsedEarlyExtraPlayPoint ? 1 : 0).Append(view.UsedLateExtraPlayPoint ? 1 : 0)
          .Append(",al=").Append(view.AttackedEnemyLeaderOnPreviousTurn ? 1 : 0)
          .Append(",hc=").Append(view.HandCount).Append(",dc=").Append(view.DeckCount).Append(",gc=").Append(view.GraveyardCount)
          .Append(",board=[");
        foreach (var follower in view.Board)
        {
            // 公开实例身份必须进键：攻击 / 进化 / 指向类动作都用实例 ID 表示。
            sb.Append(follower.InstanceId).Append('#').Append(follower.CardId).Append(':')
              .Append(follower.Attack).Append(':').Append(follower.CurrentDefense)
              .Append(':').Append(follower.MaxDefense).Append(':').Append(follower.Keywords)
              .Append(':').Append(follower.EvolutionState).Append(':').Append(follower.HasAttacked ? 1 : 0)
              .Append(':').Append(follower.Name).Append('|');
        }

        sb.Append("],amulets=[");
        if (view.Amulets is not null)
        {
            foreach (var amulet in view.Amulets)
            {
                sb.Append(amulet.InstanceId).Append('#').Append(amulet.CardId).Append(':')
                  .Append(amulet.Countdown?.ToString() ?? "-").Append('|');
            }
        }

        sb.Append("],crests=[");
        if (view.Crests is not null)
        {
            foreach (var crest in view.Crests)
            {
                sb.Append(crest.Id).Append('|');
            }
        }

        sb.Append("],revealed=[");
        if (view.RevealedCardIds is not null)
        {
            foreach (var id in view.RevealedCardIds)
            {
                sb.Append(id).Append('|');
            }
        }

        sb.Append("]}");
    }

    // ------------------------------------------------------------------ 动作身份

    /// <summary>动作的**稳定规范表示**：不依赖集合引用相等，可用于跨确定化比较同一动作。</summary>
    public static string ActionKey(GameAction action)
    {
        ArgumentNullException.ThrowIfNull(action);
        return action switch
        {
            MulliganAction mulligan => $"mulligan[{string.Join('|', mulligan.ReplaceInstanceIds)}]",
            PlayFollowerAction play =>
                $"follower[{play.CardInstanceId};{play.HandCardTargetInstanceId};{Ids(play.EnemyFollowerTargetInstanceIds)};"
                + $"{play.ModeChoiceIndex};{Ids(play.OwnHandCardTargetInstanceIds)}]",
            PlayAmuletAction amulet => $"amulet[{amulet.CardInstanceId}]",
            PlayCrystallizeAction crystallize => $"crystallize[{crystallize.CardInstanceId}]",
            PlayAccelerateAction accelerate => $"accelerate[{accelerate.CardInstanceId}]",
            PlaySpellAction spell => $"spell[{spell.CardInstanceId};{spell.Target?.ToString() ?? "-"};{Ids(spell.OwnHandCardTargetInstanceIds)};{spell.ModeChoiceIndex}]",
            EvolveAction evolve =>
                $"evolve[{evolve.FollowerInstanceId};{evolve.ModeChoiceIndex};{Ids(evolve.OwnHandCardTargetInstanceIds)};"
                + $"{evolve.EnemyFollowerTargetInstanceId}]",
            SuperEvolveAction super =>
                $"super[{super.FollowerInstanceId};{super.OtherFollowerTargetInstanceId};{super.ModeChoiceIndex};"
                + $"{Ids(super.OwnHandCardTargetInstanceIds)};{super.EnemyFollowerTargetInstanceId}]",
            UseExtraPlayPointAction => "extra-pp",
            AttackLeaderAction attackLeader => $"attack-leader[{attackLeader.AttackerInstanceId}]",
            AttackFollowerAction attackFollower => $"attack-follower[{attackFollower.AttackerInstanceId}->{attackFollower.DefenderInstanceId}]",
            EndTurnAction => "end-turn",
            _ => action.GetType().Name
        };
    }

    private static string Ids(IReadOnlyList<int>? ids) =>
        ids is null || ids.Count == 0 ? "-" : string.Join('|', ids);

    private static GameAction PickAction(IReadOnlyList<GameAction> legal, string key)
    {
        foreach (var action in legal)
        {
            if (string.Equals(ActionKey(action), key, StringComparison.Ordinal))
            {
                return action;
            }
        }

        throw new InvalidOperationException($"合法动作集合里找不到键为 {key} 的动作。");
    }

    internal static ulong DeriveSeed(ulong seed, ulong decision, ulong iteration)
    {
        var value = seed ^ (decision * 0x9E3779B97F4A7C15UL) ^ (iteration * 0xBF58476D1CE4E5B9UL);
        value ^= value >> 30;
        value *= 0xBF58476D1CE4E5B9UL;
        value ^= value >> 27;
        value *= 0x94D049BB133111EBUL;
        value ^= value >> 31;
        return value;
    }

    internal static ulong MixSeed(ulong a, ulong b, ulong c)
    {
        var value = a ^ (b * 0x9E3779B97F4A7C15UL) ^ (c * 0xD1B54A32D192ED03UL);
        value ^= value >> 29;
        value *= 0xBF58476D1CE4E5B9UL;
        value ^= value >> 32;
        return value;
    }

    // ------------------------------------------------------------------ 树

    private sealed class Node
    {
        public Node(string key)
        {
            Key = key;
        }

        /// <summary>本节点的**公开决策状态键**。同键即合并。</summary>
        public string Key { get; }

        public List<string> ActionKeys { get; } = new();

        /// <summary>每个动作对应的子节点下标；<c>-1</c> = 尚未扩展，<c>-2</c> = 已扩展但无子节点（终局/回合结束），<c>≥0</c> = 子节点下标。</summary>
        public List<int> ChildIndices { get; } = new();

        public List<int> Visits { get; } = new();

        public List<double> Sums { get; } = new();

        public int TotalVisits { get; set; }

        /// <summary>到达过本节点的确定化种子集合（用于"真的共享统计"这项断言）。</summary>
        public HashSet<ulong> Determinizations { get; } = new();

        /// <summary>指向本节点的**不同 (父节点, 动作槽)** 集合：≥2 说明不同路径真的汇合到了同一公开决策状态。</summary>
        public HashSet<long> Incoming { get; } = new();
    }

    private sealed class Tree
    {
        private readonly Dictionary<string, int> _indexByKey = new(StringComparer.Ordinal);

        public Tree(string rootKey)
        {
            GetOrCreateNode(rootKey);
        }

        public List<Node> Nodes { get; } = new();

        public int MaxDepth { get; set; }

        /// <summary>子节点按公开决策键**命中已有节点**的次数。</summary>
        public int Merges { get; private set; }

        /// <summary>按公开决策键取节点；不存在则新建。**这是跨确定化共享统计的入口**。</summary>
        public int GetOrCreateNode(string key)
        {
            if (_indexByKey.TryGetValue(key, out var existing))
            {
                Merges++;
                return existing;
            }

            var index = Nodes.Count;
            _indexByKey[key] = index;
            Nodes.Add(new Node(key));
            return index;
        }

        /// <summary>确保该动作在节点上有一个槽位（子节点可仍未扩展）。</summary>
        public int EnsureAction(int nodeIndex, string actionKey)
        {
            var node = Nodes[nodeIndex];
            for (var i = 0; i < node.ActionKeys.Count; i++)
            {
                if (string.Equals(node.ActionKeys[i], actionKey, StringComparison.Ordinal))
                {
                    return i;
                }
            }

            node.ActionKeys.Add(actionKey);
            node.ChildIndices.Add(-1);
            node.Visits.Add(0);
            node.Sums.Add(0.0);
            return node.ActionKeys.Count - 1;
        }

        public void LinkChild(int nodeIndex, int actionIndex, int childIndex)
        {
            Nodes[nodeIndex].ChildIndices[actionIndex] = childIndex;
            Nodes[childIndex].Incoming.Add(((long)nodeIndex * 1_000_003) + actionIndex);
        }

        /// <summary>标记"该动作已扩展，但走完它本回合就结束了"（终局或回合结束），没有子节点。</summary>
        public void MarkLeaf(int nodeIndex, int actionIndex) =>
            Nodes[nodeIndex].ChildIndices[actionIndex] = -2;

        /// <summary>
        /// 选择要走的动作。**根节点**保持全覆盖（先把所有合法动作加进树）；
        /// **非根节点**做真正的渐进扩展：[DIR-7 第 6 条] 子节点数随 <c>ceil(widening·√N)</c> 增长，
        /// 且待扩展动作按<b>稳定种子顺序</b>挑选（不依赖引擎给出的动作顺序）。
        /// </summary>
        public int SelectAction(
            int nodeIndex,
            IReadOnlyList<string> legalKeys,
            double exploration,
            double widening,
            ulong seed,
            ulong decisionNumber,
            bool isRoot)
        {
            var node = Nodes[nodeIndex];

            var expandable = new List<string>();
            foreach (var key in legalKeys)
            {
                var slot = FindAction(node, key);
                if (slot < 0 || node.ChildIndices[slot] == -1)
                {
                    expandable.Add(key);
                }
            }

            if (expandable.Count > 0)
            {
                var expandedCount = node.ActionKeys.Count(key =>
                {
                    var slot = FindAction(node, key);
                    return slot >= 0 && node.ChildIndices[slot] != -1;
                });

                var allowed = isRoot
                    ? expandable.Count
                    : Math.Max(1, (int)Math.Ceiling(widening * Math.Sqrt(1 + node.TotalVisits)));

                if (isRoot || expandedCount < allowed)
                {
                    // 稳定顺序：同一 (节点键, 动作键, 种子, 决策号) 永远挑同一个动作
                    var ordered = expandable
                        .OrderBy(key => MixSeed(seed, decisionNumber, (ulong)StableHash(key)), Comparer<ulong>.Default)
                        .ThenBy(key => key, StringComparer.Ordinal)
                        .ToList();
                    return EnsureAction(nodeIndex, ordered[0]);
                }
            }

            // UCB1（只比已经扩展出子节点的合法动作；-2 的叶子动作同样参与）
            var logParent = Math.Log(Math.Max(1, node.TotalVisits));
            var best = -1;
            var bestScore = double.NegativeInfinity;
            for (var i = 0; i < node.ActionKeys.Count; i++)
            {
                if (node.ChildIndices[i] == -1 || !Contains(legalKeys, node.ActionKeys[i]))
                {
                    continue;
                }

                var visits = node.Visits[i];
                var mean = visits == 0 ? double.PositiveInfinity : node.Sums[i] / visits;
                var score = visits == 0
                    ? double.PositiveInfinity
                    : mean + (exploration * Math.Sqrt(logParent / visits));
                if (best < 0 || score > bestScore + 1e-12
                    || (Math.Abs(score - bestScore) <= 1e-12
                        && string.CompareOrdinal(node.ActionKeys[i], node.ActionKeys[best]) < 0))
                {
                    best = i;
                    bestScore = score;
                }
            }

            if (best < 0)
            {
                return EnsureAction(nodeIndex, legalKeys[0]);
            }

            return best;
        }

        private static int StableHash(string text)
        {
            unchecked
            {
                var hash = 17;
                foreach (var ch in text)
                {
                    hash = (hash * 31) + ch;
                }

                return hash;
            }
        }

        private static int FindAction(Node node, string actionKey)
        {
            for (var i = 0; i < node.ActionKeys.Count; i++)
            {
                if (string.Equals(node.ActionKeys[i], actionKey, StringComparison.Ordinal))
                {
                    return i;
                }
            }

            return -1;
        }

        private static bool Contains(IReadOnlyList<string> keys, string value)
        {
            for (var i = 0; i < keys.Count; i++)
            {
                if (string.Equals(keys[i], value, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
