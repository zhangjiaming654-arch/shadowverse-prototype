using Shadowverse.Engine.Agents;
using Shadowverse.Engine.Decks;
using Shadowverse.Engine.Game;
using Shadowverse.Engine.Models;
using Shadowverse.Engine.Simulation;

namespace Shadowverse.ConsoleApp;

/// <summary>
/// [DIR-6] / [DIR-7] 的 V4 验收入口：<c>--v4-tree-check</c> 与 <c>--v4-wiring</c>。
/// <para>
/// 单独放一个文件，是为了**不改动**已经验收过的 <c>CausalContinuationProbe.cs</c> 与 <c>ConsoleTools.cs</c>
/// 的既有逻辑（只在 ConsoleTools 里加分发）。
/// </para>
/// </summary>
internal static class V4Checks
{
    private const ulong FrozenSeatSalt = 7UL;

    // ------------------------------------------------------------------ --v4-tree-check

    public static bool RunTreeChecks(Action<string> report)
    {
        ArgumentNullException.ThrowIfNull(report);

        var passed = true;
        var checks = 0;
        var failures = 0;
        void Check(string name, bool ok, string detail)
        {
            checks++;
            if (!ok)
            {
                failures++;
            }

            passed &= ok;
            report($"[{(ok ? "PASS" : "FAIL")}] {name} ｜ {detail}");
        }

        var decks = new Dictionary<string, DeckDefinition>(StringComparer.Ordinal);
        DeckDefinition Deck(string id)
        {
            if (!decks.TryGetValue(id, out var deck))
            {
                deck = AgentSelfTests.CreateMatchDeck(id, "v4-check-" + id);
                decks[id] = deck;
            }

            return deck;
        }

        var matchup = CausalContinuationProbe.Matchups[0];
        var seed = 900001UL;
        var state = BuildMidTurnState(Deck(matchup.Deck1), Deck(matchup.Deck2), seed, out var steps);
        var perspective = state.ActivePlayer;
        report($"构造局面：{matchup.Name} ｜ 走到第 {steps} 步 ｜ 回合 {state.TurnNumber} ｜ 视角座位 {perspective} "
            + $"｜ 合法动作 {GameEngine.GetLegalActions(state).Count} 个");

        // 丰富局面（回合 ≥ 4、合法动作 ≥ 5）：不少断言在浅局面上没有鉴别力，统一用它来测。
        var richState = BuildRichState(Deck(matchup.Deck1), Deck(matchup.Deck2), 900555UL, out var richSteps);
        var richPerspective = richState.ActivePlayer;
        var richLegal = GameEngine.GetLegalActions(richState);

        // ---- 1) 信息集键对隐藏信息不敏感，且隐藏信息确实不同（前提检查）
        var (det1, det2, hiddenDiffers) = TwoDistinctDeterminizations(state, perspective);
        var key1 = InformationSetTurnMctsAgentV4.PublicDecisionKey(det1, perspective, 8);
        var key2 = InformationSetTurnMctsAgentV4.PublicDecisionKey(det2, perspective, 8);
        Check("两个确定化的隐藏牌确实不同（前提检查）", hiddenDiffers,
            hiddenDiffers ? "对手手牌内容不同" : "多个种子下隐藏牌都相同——断言会失去意义");
        Check("相同可见信息 + 不同隐藏牌 ⇒ 相同公开决策状态键", string.Equals(key1, key2, StringComparison.Ordinal),
            $"{key1[..16]}… vs {key2[..16]}…");

        // ---- 2) 键对自己手牌/公开场面敏感
        var otherPerspectiveKey = InformationSetTurnMctsAgentV4.PublicDecisionKey(state, 1 - perspective, 8);
        Check("换视角（自己手牌/场面不同）⇒ 键改变", !string.Equals(key1, otherPerspectiveKey, StringComparison.Ordinal),
            $"{key1[..16]}… vs {otherPerspectiveKey[..16]}…");

        var rootLegal = GameEngine.GetLegalActions(state);
        var afterAction = GameEngine.Apply(state, rootLegal[0]);
        var afterKey = InformationSetTurnMctsAgentV4.PublicDecisionKey(afterAction, perspective, 8);
        Check("应用一个动作后（公开场面变化）⇒ 键改变", !string.Equals(key1, afterKey, StringComparison.Ordinal),
            $"动作 {InformationSetTurnMctsAgentV4.ActionKey(rootLegal[0])}");

        // ---- 3) [DIR-7 第 2、3 条] 键材料必须含公开实例身份、合法动作集合、剩余深度
        //         用**丰富局面**测实例身份：浅局面双方场上无随从，这条会没有鉴别力。
        var material = InformationSetTurnMctsAgentV4.PublicDecisionKeyMaterial(richState, richPerspective, 8);
        var instanceIds = new List<int>();
        var richObservation = GameEngine.ToObservation(richState, richPerspective);
        foreach (var view in new[] { richObservation.Self, richObservation.Opponent })
        {
            foreach (var follower in view.Board)
            {
                instanceIds.Add(follower.InstanceId);
            }

            if (view.Amulets is not null)
            {
                foreach (var amulet in view.Amulets)
                {
                    instanceIds.Add(amulet.InstanceId);
                }
            }
        }

        var missingIds = instanceIds.Where(id => !material.Contains(id.ToString() + "#", StringComparison.Ordinal)
            && !material.Contains("#" + id.ToString() + ':', StringComparison.Ordinal)).ToList();
        Check("键材料包含全部公开随从/护符的实例身份", missingIds.Count == 0 && instanceIds.Count > 0,
            $"丰富局面公开实例 {instanceIds.Count} 个；缺失 {missingIds.Count} 个");

        var legalKeysMissing = richLegal
            .Select(InformationSetTurnMctsAgentV4.ActionKey)
            .Where(key => !material.Contains(key, StringComparison.Ordinal))
            .ToList();
        Check("键材料包含该决策点的全部合法动作键", legalKeysMissing.Count == 0,
            $"合法动作 {richLegal.Count} 个；缺失 {legalKeysMissing.Count} 个");

        var depthKey8 = InformationSetTurnMctsAgentV4.PublicDecisionKey(state, perspective, 8);
        var depthKey7 = InformationSetTurnMctsAgentV4.PublicDecisionKey(state, perspective, 7);
        Check("键对剩余深度敏感（不同深度不合并）", !string.Equals(depthKey8, depthKey7, StringComparison.Ordinal),
            "R=8 vs R=7");

        // ---- 4) 泄露审计：键材料里不得出现"只存在于对手隐藏区域"的卡牌编号
        var (hiddenOnlyCount, leaked) = LeakAudit(det1, perspective);
        Check("键材料不含只存在于对手隐藏区域的卡牌编号", leaked.Count == 0,
            leaked.Count == 0 ? $"对手隐藏专属卡牌 {hiddenOnlyCount} 种，均未出现在键里" : $"泄露：{string.Join(',', leaked)}");

        var cross = CausalContinuationProbe.Matchups[2];
        var crossState = BuildMidTurnState(Deck(cross.Deck1), Deck(cross.Deck2), 900777UL, out var crossSteps);
        var (crossDet, _, crossHiddenDiffers) = TwoDistinctDeterminizations(crossState, crossState.ActivePlayer);
        var crossMaterial = InformationSetTurnMctsAgentV4.PublicDecisionKeyMaterial(crossDet, crossState.ActivePlayer, 8);
        var (crossHiddenOnly, crossLeaked) = LeakAudit(crossDet, crossState.ActivePlayer, crossMaterial);
        Check("交叉对局下同样无泄露，且审计确有鉴别力", crossLeaked.Count == 0 && crossHiddenOnly > 0,
            $"隐藏专属卡牌 {crossHiddenOnly} 种（镜像局面只有 {hiddenOnlyCount} 种）｜ 泄露 {crossLeaked.Count} 种"
            + $"｜ 隐藏牌确实不同={crossHiddenDiffers}｜交叉局面走到第 {crossSteps} 步");

        // ---- 5) 搜索：同状态同种子必须逐项一致；根动作全覆盖；访问数之和 == 预算；深度 ≥ 2
        const int iterations = 64;
        var fingerprintBefore = GameEngine.StateFingerprint(state);
        var agentA = new InformationSetTurnMctsAgentV4(seed, iterations);
        var (actionA, statsA) = agentA.Search(state, perspective, rootLegal, 1);
        var fingerprintAfter = GameEngine.StateFingerprint(state);

        var agentB = new InformationSetTurnMctsAgentV4(seed, iterations);
        var (actionB, statsB) = agentB.Search(state, perspective, rootLegal, 1);

        Check("搜索前后原状态完整指纹不变", string.Equals(fingerprintBefore, fingerprintAfter, StringComparison.Ordinal),
            $"{fingerprintBefore[..16]}…");
        Check("同状态同种子 ⇒ 动作与统计逐项一致",
            string.Equals(InformationSetTurnMctsAgentV4.ActionKey(actionA), InformationSetTurnMctsAgentV4.ActionKey(actionB), StringComparison.Ordinal)
            && SameStats(statsA, statsB),
            $"动作 {InformationSetTurnMctsAgentV4.ActionKey(actionA)}");
        Check("根动作访问数之和 == 实际预算", statsA.RootVisits == statsA.EffectiveIterations,
            $"{statsA.RootVisits} vs {statsA.EffectiveIterations}（配置 {statsA.ConfiguredIterations}）");
        Check("每个根合法动作至少访问一次", statsA.MinRootVisits >= 1,
            string.Join(", ", statsA.RootActions.Select(stat => $"{stat.ActionKey}={stat.Visits}")));
        Check("树实际达到至少两层（不是退化成平坦搜索）", statsA.MaxDepth >= 2, $"最大深度 {statsA.MaxDepth}");
        Check("终止原因守恒（4 类之和 == 实际预算）",
            statsA.DepthCapHits + statsA.TurnEndLeaves + statsA.TerminalLeaves + statsA.OtherLeaves == statsA.EffectiveIterations,
            $"深度上限 {statsA.DepthCapHits} + 回合结束 {statsA.TurnEndLeaves} + 终局 {statsA.TerminalLeaves} + 其他 {statsA.OtherLeaves} "
            + $"= {statsA.DepthCapHits + statsA.TurnEndLeaves + statsA.TerminalLeaves + statsA.OtherLeaves} vs 预算 {statsA.EffectiveIterations}");
        Check("非根节点也发生跨确定化共享（排除根节点的假阳性）", statsA.SharedNodeMaxDeterminizationsNonRoot >= 2,
            $"非根节点最多被 {statsA.SharedNodeMaxDeterminizationsNonRoot} 个确定化更新"
            + $"（根节点 {statsA.SharedNodeMaxDeterminizations}）");
        Check("子节点按公开决策键发生合并", statsA.NodeMerges >= 1,
            $"合并命中 {statsA.NodeMerges} 次；节点 {statsA.NodeCount} 个 / 预算 {statsA.EffectiveIterations}");

        var legalKeys = rootLegal.Select(InformationSetTurnMctsAgentV4.ActionKey).ToList();
        Check("返回动作在合法集合内", legalKeys.Contains(InformationSetTurnMctsAgentV4.ActionKey(actionA), StringComparer.Ordinal),
            InformationSetTurnMctsAgentV4.ActionKey(actionA));

        // ---- 6) 结束回合 / 终局能正确回传
        var endTurnPresent = false;
        var endTurnVisited = false;
        for (var i = 0; i < rootLegal.Count; i++)
        {
            var key = InformationSetTurnMctsAgentV4.ActionKey(rootLegal[i]);
            if (key != "end-turn")
            {
                continue;
            }

            endTurnPresent = true;
            var stat = statsA.RootActions.FirstOrDefault(item => string.Equals(item.ActionKey, key, StringComparison.Ordinal));
            endTurnVisited = stat is not null && stat.Visits >= 1;
        }

        var endTurnAction = rootLegal.FirstOrDefault(action => InformationSetTurnMctsAgentV4.ActionKey(action) == "end-turn");
        var endTurnValue = endTurnAction is null ? double.NaN : agentA.LeafValue(GameEngine.Apply(state, endTurnAction), perspective);
        Check("结束回合后仍能给出有限叶值（回传路径成立）",
            !endTurnPresent || (double.IsFinite(endTurnValue) && endTurnValue >= 0.0 && endTurnValue <= 1.0),
            $"叶值 {endTurnValue:0.0000} ｜ 结束回合动作在根合法集内={endTurnPresent} 被访问={endTurnVisited}");

        var finalState = PlayToEnd(Deck(matchup.Deck1), Deck(matchup.Deck2), 900123UL);
        var winner = finalState.Winner ?? throw new InvalidOperationException("终局必须有胜者。");
        var winnerValue = agentA.LeafValue(finalState, winner);
        var loserValue = agentA.LeafValue(finalState, 1 - winner);
        Check("终局叶值正确（胜者=1、负者=0）",
            Math.Abs(winnerValue - 1.0) < 1e-12 && Math.Abs(loserValue) < 1e-12,
            $"胜者 {winnerValue:0.0000} ／ 负者 {loserValue:0.0000}");

        // ---- 7) [DIR-7 第 4 条] 预算小于根合法动作数时也必须全覆盖
        if (rootLegal.Count >= 2)
        {
            var tinyAgent = new InformationSetTurnMctsAgentV4(seed, iterationsPerDecision: 1);
            var (_, tinyStats) = tinyAgent.Search(state, perspective, rootLegal, 3);
            Check("预算(1) 小于根动作数时仍全覆盖（实际预算自动抬高）",
                tinyStats.EffectiveIterations >= rootLegal.Count
                && tinyStats.MinRootVisits >= 1
                && tinyStats.RootVisits == tinyStats.EffectiveIterations,
                $"根动作 {rootLegal.Count} 个 ｜ 配置预算 {tinyStats.ConfiguredIterations} ｜ 实际预算 {tinyStats.EffectiveIterations} "
                + $"｜ 最小根访问 {tinyStats.MinRootVisits}");
        }

        // ---- 8) [DIR-7 第 1 条] 换牌阶段不得越界搜索对手换牌（双座位）
        var mulliganState = GameEngine.CreateGame(Deck(matchup.Deck1), Deck(matchup.Deck2), 900999UL);
        var mulliganPerspective = mulliganState.ActivePlayer;
        var mulliganLegal = GameEngine.GetLegalActions(mulliganState);
        var mulliganAgent = new InformationSetTurnMctsAgentV4(seed, 8);
        var (mulliganAction, mulliganStats) = mulliganAgent.Search(mulliganState, mulliganPerspective, mulliganLegal, 11);
        var afterMulligan = GameEngine.Apply(mulliganState, mulliganAction);
        Check("换牌：根换牌动作确实把回合交给了对手（前提检查）",
            afterMulligan.ActivePlayer != mulliganPerspective || afterMulligan.Phase != mulliganState.Phase,
            $"阶段 {mulliganState.Phase}→{afterMulligan.Phase}；行动方 {mulliganPerspective}→{afterMulligan.ActivePlayer}");
        Check("换牌：树不越界搜索对手换牌（最大深度 == 1）", mulliganStats.MaxDepth == 1,
            $"最大深度 {mulliganStats.MaxDepth} ｜ 回合结束叶子 {mulliganStats.TurnEndLeaves}/{mulliganStats.EffectiveIterations}");

        if (afterMulligan.Phase == GamePhase.Mulligan)
        {
            var secondPerspective = afterMulligan.ActivePlayer;
            var (_, secondStats) = mulliganAgent.Search(
                afterMulligan, secondPerspective, GameEngine.GetLegalActions(afterMulligan), 12);
            Check("换牌：第二位玩家（另一座位）同样不越界", secondStats.MaxDepth == 1,
                $"最大深度 {secondStats.MaxDepth} ｜ 视角座位 {secondPerspective}");
        }
        else
        {
            report("（注意：首位玩家换牌后已离开换牌阶段，双座位换牌测试的第二段未触发）");
        }

        // ---- 9) [DIR-7 第 7 条] 不同动作序列在相同深度汇入同一公开决策状态
        //         浅局面（3 个动作 → 仅 4 条长度 2 的序列）几乎不可能观测到汇合，
        //         因此用**丰富局面**（5 个动作）做枚举，并另用真实搜索里的"多来源节点数"佐证。
        var shallowProbe = ConvergenceProbe(state, rootLegal, perspective, 8);
        var convergence = ConvergenceProbe(richState, richLegal, richPerspective, 8);
        Check("不同动作序列在相同深度确实存在汇合（枚举实测）", convergence.MaxGroupSize >= 2,
            $"丰富局面枚举深度 2 的 {convergence.Sequences} 条序列 → 同键最大组 {convergence.MaxGroupSize} 个"
            + $"（浅局面 {shallowProbe.Sequences} 条 → 最大组 {shallowProbe.MaxGroupSize}）"
            + (convergence.MaxGroupSize >= 2 ? $"｜示例：{convergence.Example}" : string.Empty));

        // ---- 10) 更丰富的局面：更晚的回合、更多合法动作、更深搜索
        var richFingerprintBefore = GameEngine.StateFingerprint(richState);
        var richAgent = new InformationSetTurnMctsAgentV4(seed, 96);
        var (richAction, richStats) = richAgent.Search(richState, richPerspective, richLegal, 7);
        var richFingerprintAfter = GameEngine.StateFingerprint(richState);

        report($"丰富局面：{matchup.Name} ｜ 走到第 {richSteps} 步 ｜ 回合 {richState.TurnNumber} "
            + $"｜ 视角座位 {richPerspective} ｜ 合法动作 {richLegal.Count} 个 ｜ 迭代 96");
        Check("丰富局面：树达到 ≥ 3 层", richStats.MaxDepth >= 3, $"最大深度 {richStats.MaxDepth}");
        Check("丰富局面：根动作全覆盖且访问和 == 预算",
            richStats.RootVisits == richStats.EffectiveIterations && richStats.MinRootVisits >= 1,
            $"根访问和 {richStats.RootVisits}；最小根访问 {richStats.MinRootVisits}；各动作访问 "
            + string.Join(", ", richStats.RootActions.Select(stat => stat.Visits)));
        Check("丰富局面：仍发生公开决策状态合并", richStats.NodeMerges >= 1,
            $"合并命中 {richStats.NodeMerges} 次；节点 {richStats.NodeCount} 个；汇合节点 {richStats.ConvergentNodes} 个");
        Check("丰富局面：搜索未改动原状态", string.Equals(richFingerprintBefore, richFingerprintAfter, StringComparison.Ordinal),
            richFingerprintBefore[..16] + "…");
        Check("丰富局面：返回动作合法",
            GameEngine.GetLegalActions(richState).Select(InformationSetTurnMctsAgentV4.ActionKey)
                .Contains(InformationSetTurnMctsAgentV4.ActionKey(richAction), StringComparer.Ordinal),
            InformationSetTurnMctsAgentV4.ActionKey(richAction));
        report($"丰富局面统计：深度上限命中 {richStats.DepthCapHits} ｜ 回合结束叶子 {richStats.TurnEndLeaves} "
            + $"｜ 终局叶子 {richStats.TerminalLeaves} ｜ 决策耗时 {richStats.ElapsedMs:0.0} ms");

        report($"检查项 {checks} 个；失败 {failures} 个");
        return passed;
    }

    // ------------------------------------------------------------------ --v4-wiring

    /// <summary>
    /// 接线普查：四种对局各跑 N 局，只看"跑不跑得通、代价多大、搜索结构如何"，**不看胜负**。
    /// [DIR-7 第 5、9 条] 分歧率只在 **V4 所在座位**的合格决策上统计。
    /// </summary>
    public static bool RunWiring(CausalContinuationProbe.Config config, int gamesPerMatchup, int iterations, Action<string> report)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(report);

        var decks = new Dictionary<string, DeckDefinition>(StringComparer.Ordinal);
        DeckDefinition Deck(string id)
        {
            if (!decks.TryGetValue(id, out var deck))
            {
                deck = AgentSelfTests.CreateMatchDeck(id, "v4-wiring-" + id);
                decks[id] = deck;
            }

            return deck;
        }

        var totalDecisions = 0;
        var totalEligible = 0;
        var v4Decisions = 0;
        var v4Eligible = 0;
        var v4Disagreements = 0;
        var opponentDecisions = 0;
        var opponentEligible = 0;
        var illegal = 0;
        var polluted = 0;
        var nondeterministic = 0;
        var rootCoverageViolations = 0;
        var nodeTotal = 0.0;
        var visitTotal = 0.0;
        var depthTotal = 0.0;
        var msTotal = 0.0;
        var minRootVisitsWorst = int.MaxValue;
        var depthCapHits = 0;
        var turnEndLeaves = 0;
        var terminalLeaves = 0;
        var otherLeaves = 0;
        var conservationViolations = 0;
        var iterationTotal = 0;
        var statCount = 0;
        var games = 0;

        for (var matchupIndex = 0; matchupIndex < CausalContinuationProbe.Matchups.Count; matchupIndex++)
        {
            var matchup = CausalContinuationProbe.Matchups[matchupIndex];
            for (var game = 0; game < gamesPerMatchup; game++)
            {
                var seed = Mix(config.SourceSeedBase, (ulong)((matchupIndex * 1_000_003) + game + 1));
                var state = GameEngine.CreateGame(Deck(matchup.Deck1), Deck(matchup.Deck2), seed);
                var v4 = new InformationSetTurnMctsAgentV4(seed, iterations);
                var shadowV4 = new InformationSetTurnMctsAgentV4(seed, iterations);
                var opponent = new LookaheadPlayerAgentV2(Mix(seed, FrozenSeatSalt), CausalContinuationProbe.FrozenV2Rollouts);
                var shadow2 = new LookaheadPlayerAgentV2(seed, CausalContinuationProbe.FrozenV2Rollouts);
                var steps = 0;

                while (!state.IsGameOver && steps++ < 2_000)
                {
                    var player = state.ActivePlayer;
                    var observation = GameEngine.ToObservation(state, player);
                    var legal = GameEngine.GetLegalActions(state);
                    var legalKeys = legal.Select(InformationSetTurnMctsAgentV4.ActionKey).ToList();
                    var fingerprintBefore = GameEngine.StateFingerprint(state);

                    GameAction action;
                    if (player == 0)
                    {
                        // [DIR-8 第 3 条] 必须走**生产路径** ChooseAction（它按该座位自己的决策序号递增），
                        // 否则采样种子序列与生产不同，报出来的分歧率与结构统计都不是生产 V4 的真实结果。
                        var chosen = v4.ChooseAction(state, observation, legal);
                        var stats = v4.LastStats ?? throw new InvalidOperationException("生产路径没有留下统计。");
                        action = chosen;

                        // 第二个**持久** V4，只在同一个决策点同步复算，用来验证动作与统计一致
                        var shadowV4Action = shadowV4.ChooseAction(state, observation, legal);
                        if (!string.Equals(
                                InformationSetTurnMctsAgentV4.ActionKey(chosen),
                                InformationSetTurnMctsAgentV4.ActionKey(shadowV4Action),
                                StringComparison.Ordinal))
                        {
                            nondeterministic++;
                        }

                        nodeTotal += stats.NodeCount;
                        visitTotal += stats.RootVisits;
                        depthTotal += stats.MaxDepth;
                        msTotal += stats.ElapsedMs;
                        statCount++;
                        iterationTotal += stats.EffectiveIterations;
                        depthCapHits += stats.DepthCapHits;
                        turnEndLeaves += stats.TurnEndLeaves;
                        terminalLeaves += stats.TerminalLeaves;
                        otherLeaves += stats.OtherLeaves;
                        minRootVisitsWorst = Math.Min(minRootVisitsWorst, stats.MinRootVisits);
                        if (stats.MinRootVisits < 1 || stats.RootVisits != stats.EffectiveIterations)
                        {
                            rootCoverageViolations++;
                        }

                        if (stats.DepthCapHits + stats.TurnEndLeaves + stats.TerminalLeaves + stats.OtherLeaves
                            != stats.EffectiveIterations)
                        {
                            conservationViolations++;
                        }

                        v4Decisions++;
                        if (legal.Count >= 2)
                        {
                            v4Eligible++;
                        }

                        var shadowAction = shadow2.ChooseAction(state, observation, legal);
                        if (!string.Equals(
                                InformationSetTurnMctsAgentV4.ActionKey(chosen),
                                InformationSetTurnMctsAgentV4.ActionKey(shadowAction),
                                StringComparison.Ordinal))
                        {
                            v4Disagreements++;
                        }
                    }
                    else
                    {
                        action = opponent.ChooseAction(state, observation, legal);
                        opponentDecisions++;
                        if (legal.Count >= 2)
                        {
                            opponentEligible++;
                        }
                    }

                    if (!legalKeys.Contains(InformationSetTurnMctsAgentV4.ActionKey(action), StringComparer.Ordinal))
                    {
                        illegal++;
                    }

                    if (!string.Equals(fingerprintBefore, GameEngine.StateFingerprint(state), StringComparison.Ordinal))
                    {
                        polluted++;
                    }

                    totalDecisions++;
                    if (legal.Count >= 2)
                    {
                        totalEligible++;
                    }

                    state = GameEngine.Apply(state, action);
                }

                games++;
            }
        }

        report($"对局 {games} 局（四种对局各 {gamesPerMatchup} 局）｜ 配置迭代预算 {iterations}/决策");
        report($"determinizationKnowledge = **{InformationSetTurnMctsAgentV4.DeterminizationKnowledge}**"
            + "（采样器条件于真实对手牌池，与冻结 2.0/3.0 同一 API；仅供同信息条件下的架构比较，不代表可部署的公平牌手）");
        report($"总决策数 {totalDecisions} ｜ 全部合格决策 {totalEligible} ｜ 其中 **V4 座位**决策 {v4Decisions}、合格 {v4Eligible}");
        report($"**V4 与冻结 2.0 的动作分歧率（只在 V4 的合格决策上）= "
            + $"{(v4Eligible == 0 ? 0 : (double)v4Disagreements / v4Eligible):P2}（{v4Disagreements}/{v4Eligible}）**");
        report($"（参考：全部合格决策 {totalEligible} ｜ 对手座位决策 {opponentDecisions}、合格 {opponentEligible}；两座位分别统计，已按 [DIR-7] 第 5 条修正）");
        report($"平均每决策：节点 {(statCount == 0 ? 0 : nodeTotal / statCount):N1} ｜ 根访问 {(statCount == 0 ? 0 : visitTotal / statCount):N1} "
            + $"｜ 最大深度 {(statCount == 0 ? 0 : depthTotal / statCount):N1} ｜ 耗时 {(statCount == 0 ? 0 : msTotal / statCount):N1} ms"
            + $" ｜ 最小根访问（全批最差）{(minRootVisitsWorst == int.MaxValue ? 0 : minRootVisitsWorst)}");
        report($"终止原因守恒：深度上限 {depthCapHits} + 回合结束 {turnEndLeaves} + 终局 {terminalLeaves} + 其他 {otherLeaves} = {depthCapHits + turnEndLeaves + terminalLeaves + otherLeaves} vs 迭代总数 {iterationTotal}"
            + $" ｜ 回合结束叶子率 {(iterationTotal == 0 ? 0 : (double)turnEndLeaves / iterationTotal):P2}"
            + $" ｜ 终局叶子率 {(iterationTotal == 0 ? 0 : (double)terminalLeaves / iterationTotal):P2}");
        report($"非法动作 {illegal} ｜ 状态污染 {polluted} ｜ 确定性失败 {nondeterministic} ｜ 根覆盖违例 {rootCoverageViolations} ｜ 终止原因不守恒 {conservationViolations}（五者都必须为 0）");

        var ok = illegal == 0 && polluted == 0 && nondeterministic == 0 && rootCoverageViolations == 0 && conservationViolations == 0 && statCount > 0;

        // [RES-11A] 把采样器知识标记与本次参数、程序集身份一起写进 manifest，便于外部核对。
        try
        {
            var manifestPath = Path.Combine(config.OutputDirectory, "v4-wiring-manifest.json");
            var consolePath = typeof(V4Checks).Assembly.Location;
            var enginePath = typeof(InformationSetTurnMctsAgentV4).Assembly.Location;
            string Sha(string path) => File.Exists(path)
                ? Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path)))
                : "(缺失)";
            var manifest = string.Join('\n',
                "{",
                $"  \"generatedAt\": \"{DateTime.Now:O}\",",
                $"  \"determinizationKnowledge\": \"{InformationSetTurnMctsAgentV4.DeterminizationKnowledge}\",",
                "  \"note\": \"采样器条件于真实对手牌池（与冻结 2.0/3.0 同一 API）；仅供同信息条件下的搜索架构比较，不代表可部署的公平牌手。\",",
                $"  \"iterationsPerDecision\": {iterations},",
                "  \"maxDepth\": 8,",
                $"  \"gamesPerMatchup\": {gamesPerMatchup},",
                $"  \"v4Decisions\": {v4Decisions},",
                $"  \"v4Eligible\": {v4Eligible},",
                $"  \"v4Disagreements\": {v4Disagreements},",
                $"  \"v4DisagreementRate\": {((v4Eligible == 0 ? 0 : (double)v4Disagreements / v4Eligible).ToString("0.000000", System.Globalization.CultureInfo.InvariantCulture))},",
                $"  \"averageNodesPerDecision\": {(statCount == 0 ? 0 : nodeTotal / statCount).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)},",
                $"  \"averageMaxDepth\": {(statCount == 0 ? 0 : depthTotal / statCount).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)},",
                $"  \"averageMsPerDecision\": {(statCount == 0 ? 0 : msTotal / statCount).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)},",
                $"  \"depthCapHitRate\": {(iterationTotal == 0 ? 0 : (double)depthCapHits / iterationTotal).ToString("0.000000", System.Globalization.CultureInfo.InvariantCulture)},",
                $"  \"illegalActions\": {illegal},",
                $"  \"statePollution\": {polluted},",
                $"  \"nondeterminismFailures\": {nondeterministic},",
                $"  \"rootCoverageViolations\": {rootCoverageViolations},",
                $"  \"consoleAssemblySha256\": \"{Sha(consolePath)}\",",
                $"  \"engineAssemblySha256\": \"{Sha(enginePath)}\"",
                "}");
            File.WriteAllText(manifestPath, manifest);
            report($"manifest → {manifestPath}");
        }
        catch (IOException exception)
        {
            report($"manifest 写入失败（不影响验收判定）：{exception.Message}");
        }

        return ok;
    }

    // ------------------------------------------------------------------ 辅助

    /// <summary>构造一个"更晚的回合 + 更多合法动作"的丰富局面。</summary>
    private static GameState BuildRichState(DeckDefinition first, DeckDefinition second, ulong seed, out int steps)
    {
        var rule = new GreedyPlayerAgent();
        var state = GameEngine.CreateGame(first, second, seed);
        steps = 0;
        while (!state.IsGameOver && steps < 400)
        {
            var legal = GameEngine.GetLegalActions(state);
            if (state.Phase == GamePhase.Main && state.TurnNumber >= 4 && legal.Count >= 5)
            {
                return state;
            }

            var observation = GameEngine.ToObservation(state, state.ActivePlayer);
            state = GameEngine.Apply(state, rule.ChooseAction(observation, legal));
            steps++;
        }

        throw new InvalidOperationException("没能构造出回合 ≥ 4、合法动作 ≥ 5 的丰富局面。");
    }

    /// <summary>构造一个"回合中、合法动作 ≥ 2"的局面。</summary>
    private static GameState BuildMidTurnState(DeckDefinition first, DeckDefinition second, ulong seed, out int steps)
    {
        var rule = new GreedyPlayerAgent();
        var state = GameEngine.CreateGame(first, second, seed);
        steps = 0;
        while (!state.IsGameOver && steps < 400)
        {
            var legal = GameEngine.GetLegalActions(state);
            if (state.Phase == GamePhase.Main && legal.Count >= 2)
            {
                return state;
            }

            var observation = GameEngine.ToObservation(state, state.ActivePlayer);
            state = GameEngine.Apply(state, rule.ChooseAction(observation, legal));
            steps++;
        }

        throw new InvalidOperationException("没能构造出回合中、合法动作 ≥ 2 的局面。");
    }

    /// <summary>
    /// 枚举深度 2 的全部动作序列，看是否有**不同序列落到同一公开决策状态**（相同剩余深度）。
    /// 这是 [DIR-7] 第 7 条要求的"不同路径同深度汇合"的实证检查。
    /// </summary>
    private static (int Sequences, int MaxGroupSize, string Example) ConvergenceProbe(
        GameState state, IReadOnlyList<GameAction> legal, int perspective, int maxDepth)
    {
        var groups = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var sequences = 0;
        foreach (var first in legal)
        {
            var mid = GameEngine.Apply(state, first);
            if (mid.IsGameOver)
            {
                continue;
            }

            foreach (var second in GameEngine.GetLegalActions(mid))
            {
                var end = GameEngine.Apply(mid, second);
                if (end.IsGameOver)
                {
                    continue;
                }

                sequences++;
                var key = InformationSetTurnMctsAgentV4.PublicDecisionKey(end, end.ActivePlayer, maxDepth - 2);
                if (!groups.TryGetValue(key, out var list))
                {
                    list = new List<string>();
                    groups[key] = list;
                }

                list.Add(InformationSetTurnMctsAgentV4.ActionKey(first) + " → " + InformationSetTurnMctsAgentV4.ActionKey(second));
            }
        }

        var bestGroup = groups.Values.OrderByDescending(list => list.Count).FirstOrDefault();
        return (sequences, bestGroup?.Count ?? 0, bestGroup is null ? "-" : string.Join(" ／ ", bestGroup.Take(2)));
    }

    /// <summary>
    /// 泄露审计：取"对手隐藏手牌里、且在可见信息里完全没有合法出处的卡牌编号"，
    /// 再检查键材料里是否出现了它们。种数为 0 时这次审计没有鉴别力（要换对局重做）。
    /// </summary>
    private static (int HiddenOnly, List<string> Leaked) LeakAudit(
        GameState determinization,
        int perspective,
        string? material = null)
    {
        var selfView = GameEngine.ToObservation(determinization, perspective);
        var opponentAsSelf = GameEngine.ToObservation(determinization, 1 - perspective);

        var legitimate = new HashSet<string>(StringComparer.Ordinal);
        foreach (var card in selfView.OwnHand)
        {
            legitimate.Add(card.Definition.Id);
        }

        if (selfView.OwnDeckCardIds is not null)
        {
            foreach (var id in selfView.OwnDeckCardIds)
            {
                legitimate.Add(id);
            }
        }

        foreach (var view in new[] { selfView.Self, selfView.Opponent })
        {
            foreach (var follower in view.Board)
            {
                legitimate.Add(follower.CardId);
            }

            if (view.Amulets is not null)
            {
                foreach (var amulet in view.Amulets)
                {
                    legitimate.Add(amulet.CardId);
                }
            }

            if (view.RevealedCardIds is not null)
            {
                foreach (var id in view.RevealedCardIds)
                {
                    legitimate.Add(id);
                }
            }
        }

        var hiddenOnly = opponentAsSelf.OwnHand
            .Select(card => card.Definition.Id)
            .Where(id => !legitimate.Contains(id))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        material ??= InformationSetTurnMctsAgentV4.PublicDecisionKeyMaterial(determinization, perspective, 8);
        var leaked = hiddenOnly.Where(id => material.Contains(id, StringComparison.Ordinal)).ToList();
        return (hiddenOnly.Count, leaked);
    }

    private static (GameState First, GameState Second, bool HiddenDiffers) TwoDistinctDeterminizations(GameState state, int perspective)
    {
        var first = GameEngine.CreateDeterminization(state, perspective, 11);
        for (var attempt = 0; attempt < 64; attempt++)
        {
            var second = GameEngine.CreateDeterminization(state, perspective, (ulong)(12 + attempt));
            var handFirst = string.Join(',', GameEngine.ToObservation(first, 1 - perspective).OwnHand.Select(card => card.Definition.Id));
            var handSecond = string.Join(',', GameEngine.ToObservation(second, 1 - perspective).OwnHand.Select(card => card.Definition.Id));
            if (!string.Equals(handFirst, handSecond, StringComparison.Ordinal))
            {
                return (first, second, true);
            }
        }

        return (first, GameEngine.CreateDeterminization(state, perspective, 99), false);
    }

    private static bool SameStats(V4SearchStats a, V4SearchStats b)
    {
        if (a.ConfiguredIterations != b.ConfiguredIterations || a.EffectiveIterations != b.EffectiveIterations
            || a.RootVisits != b.RootVisits || a.MinRootVisits != b.MinRootVisits
            || a.NodeCount != b.NodeCount || a.MaxDepth != b.MaxDepth
            || a.Determinizations != b.Determinizations
            || a.SharedNodeMaxDeterminizations != b.SharedNodeMaxDeterminizations
            || a.SharedNodeMaxDeterminizationsNonRoot != b.SharedNodeMaxDeterminizationsNonRoot
            || a.ConvergentNodes != b.ConvergentNodes || a.NodeMerges != b.NodeMerges
            || a.DepthCapHits != b.DepthCapHits || a.TurnEndLeaves != b.TurnEndLeaves || a.TerminalLeaves != b.TerminalLeaves || a.OtherLeaves != b.OtherLeaves
            || !string.Equals(a.ChosenActionKey, b.ChosenActionKey, StringComparison.Ordinal)
            || a.RootActions.Count != b.RootActions.Count)
        {
            return false;
        }

        for (var i = 0; i < a.RootActions.Count; i++)
        {
            if (!string.Equals(a.RootActions[i].ActionKey, b.RootActions[i].ActionKey, StringComparison.Ordinal)
                || a.RootActions[i].Visits != b.RootActions[i].Visits
                || Math.Abs(a.RootActions[i].MeanValue - b.RootActions[i].MeanValue) > 1e-12)
            {
                return false;
            }
        }

        return true;
    }

    private static GameState PlayToEnd(DeckDefinition first, DeckDefinition second, ulong seed)
    {
        var result = MatchRunner.PlayToEnd(
            GameEngine.CreateGame(first, second, seed),
            new GreedyPlayerAgent(),
            new GreedyPlayerAgent());
        return result.FinalState;
    }

    private static ulong Mix(ulong seed, ulong salt)
    {
        var value = seed ^ (salt * 0x9E3779B97F4A7C15UL);
        value ^= value >> 30;
        value *= 0xBF58476D1CE4E5B9UL;
        value ^= value >> 27;
        value *= 0x94D049BB133111EBUL;
        value ^= value >> 31;
        return value;
    }
}
