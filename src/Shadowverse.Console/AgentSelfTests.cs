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
namespace Shadowverse.ConsoleApp;

/// <summary>
/// 全部自检（--effect-test 跑的就是这里）。共 101 个函数，
/// 由调用图闭包从 33 个 Run*Test 入口算出来的，所以它和 ConsoleTools 的职责边界是机械划分的、不是拍脑袋。
/// </summary>
internal static class AgentSelfTests
{
internal static void RunSmokeTest(string firstDeckId, string secondDeckId)
{
    var deckA = CreateMatchDeck(firstDeckId, "A");
    var deckB = CreateMatchDeck(secondDeckId, "B");

    VerifyMulliganDoesNotRedrawSelectedCard(deckA, deckB);

    var firstMatch = MatchRunner.PlayToEnd(
        GameEngine.CreateGame(deckA, deckB, seed: 20260901),
        new GreedyPlayerAgent(),
        new RandomPlayerAgent(seed: 202));

    var replayMatch = MatchRunner.PlayToEnd(
        GameEngine.CreateGame(deckA, deckB, seed: 20260901),
        new GreedyPlayerAgent(),
        new RandomPlayerAgent(seed: 202));

    var replayMatches = firstMatch.Winner == replayMatch.Winner &&
                        firstMatch.Actions.Select(ConsoleTools.ActionSignature)
                            .SequenceEqual(replayMatch.Actions.Select(ConsoleTools.ActionSignature));

    if (!replayMatches)
    {
        throw new InvalidOperationException("Deterministic replay check failed.");
    }

    Console.WriteLine("Smoke test passed.");
    Console.WriteLine("Mulligan protection: selected card was not redrawn.");
    Console.WriteLine($"Winner: Player {firstMatch.Winner + 1}");
    Console.WriteLine($"Actions: {firstMatch.ActionCount}");
    Console.WriteLine($"Final health: P1={firstMatch.FinalState.Players[0].Health}, P2={firstMatch.FinalState.Players[1].Health}");
}

/// <summary>
/// 人机对战手势解析自检。
/// <para>
/// 守住的性质：**引擎给出的每一个合法动作，都必须能被某个拖拽/点击手势选中**。
/// 界面本身没法自动化验证，所以"真人不会遇到点不到的动作"这条保证只能在这里守。
/// </para>
/// <para>
/// 做法：让真人牌手接管整局，在每一个决策点上把所有可能的手势都跑一遍取并集，
/// 再看有没有合法动作落在并集外面。
/// </para>
/// </summary>
internal static void RunHumanActionResolverTest()
{
    const int gameCount = 6;
    string[] deckPool = ["DECK-002", "DECK-003"];
    var failures = new List<string>();
    var tally = new GestureTally();
    var decisions = 0;
    var legalActionTotal = 0;

    for (var game = 0; game < gameCount; game++)
    {
        var firstDeck = CreateMatchDeck(deckPool[game % deckPool.Length], "A");
        var secondDeck = CreateMatchDeck(deckPool[(game + 1) % deckPool.Length], "B");
        var rule = new GreedyPlayerAgent();

        // 真人牌手在这里只是"接管 + 记录"，真正出什么招交给规则牌手，
        // 这样这局能正常打完，从而覆盖到开局、中盘、进化、攻击、法术各种决策点。
        var human = new HumanPlayerAgent((observation, legalActions) =>
        {
            decisions++;
            legalActionTotal += legalActions.Count;
            CollectGestureReach(observation, legalActions, failures, tally);
            return rule.ChooseAction(observation, legalActions);
        });

        MatchRunner.PlayToEnd(
            GameEngine.CreateGame(firstDeck, secondDeck, seed: (ulong)(9_100 + game)),
            human,
            new GreedyPlayerAgent());
    }

    if (failures.Count > 0)
    {
        foreach (var failure in failures.Take(10))
        {
            Console.WriteLine("  ✗ " + failure);
        }

        throw new InvalidOperationException(
            $"人机对战手势自检失败：{failures.Count} 项。可能是合法动作点不到，" +
            "也可能是某组候选的文字无法区分（等于让用户选不了）。");
    }

    Console.WriteLine("Human action resolver test passed.");
    Console.WriteLine(
        $"  {gameCount} 局里共 {decisions} 个真人决策点、{legalActionTotal} 个合法动作，全部能被某个手势选中。");
    Console.WriteLine("  各类手势中筛出多个变体（界面要弹菜单让用户选）的比例：");
    tally.Report();
    Console.WriteLine(
        $"  其中【模式】卡牌的多模式选择出现 {tally.ModeChoicesSeen} 次；" +
        "所有候选的文字都两两不同（否则用户看着重复按钮无从选起）。");
}

/// <summary>
/// 统计各类手势"筛出多个变体"的比例。
/// <para>
/// 这个数字直接对应手感：比例越高，玩的时候弹菜单越多。
/// 分开按手势类型统计很重要 —— 混在一起算会被"每一个手牌×每个敌人的组合都探一遍"灌水，
/// 而真正影响体感的是"用户做那个动作时会不会弹菜单"。
/// </para>
/// </summary>
private sealed class GestureTally
{
    private readonly Dictionary<string, int> _total = [];
    private readonly Dictionary<string, int> _multi = [];
    private readonly List<string> _order = [];

    public void Add(string kind, int candidateCount)
    {
        if (!_total.ContainsKey(kind))
        {
            _total[kind] = 0;
            _multi[kind] = 0;
            _order.Add(kind);
        }

        _total[kind]++;
        if (candidateCount > 1)
        {
            _multi[kind]++;
        }
    }

    public void NoteModeChoice() => ModeChoicesSeen++;

    public int ModeChoicesSeen { get; private set; }

    public void Report()
    {
        foreach (var kind in _order)
        {
            var total = _total[kind];
            var multi = _multi[kind];
            var rate = total == 0 ? 0 : 100.0 * multi / total;
            Console.WriteLine($"    {kind}：{multi}/{total} = {rate:F1}%");
        }
    }
}

/// <summary>
/// 【对手思考面板】隐私自检。
/// <para>
/// 人机对战是靠<b>体感</b>判断牌手强弱的。一旦能看到对手手牌，"我赢了他"就说明不了任何事，
/// 整个对战的意义就没了。所以这个自检守住一条：
/// <see cref="HumanActionText.DescribeOpponentAction"/> 渲染出来的文字里，
/// <b>绝不能出现只存在于对手手牌、而场上看不到的卡名</b>。
/// </para>
/// <para>
/// 允许的例外：同一张卡如果对手场上也有一只（打出一张、手里还留一张），
/// 那它的名字本来就是公开的，出现在文字里不算泄露。
/// </para>
/// </summary>
internal static void RunOpponentThinkingPrivacyTest()
{
    var firstDeck = CreateMatchDeck("DECK-003", "我");
    var secondDeck = CreateMatchDeck("DECK-002", "对手");
    var opponentAgent = new LookaheadPlayerAgent(rolloutsPerAction: 4, seed: 4_242UL);

    var leaks = new List<string>();
    var described = 0;
    var redacted = 0;
    var publicOnly = 0;
    var decisions = 0;

    MatchRunner.PlayToEnd(
        GameEngine.CreateGame(firstDeck, secondDeck, seed: 8_800),
        new GreedyPlayerAgent(),
        opponentAgent,
        onStep: step =>
        {
            // onStep 是在对局线程上同步调用的，所以此刻的 LastDecision 就是这一步的决策。
            if (step.ActingPlayer != 1 || opponentAgent.LastDecision is not { } decision)
            {
                return;
            }

            decisions++;
            var observation = GameEngine.ToObservation(step.BeforeState, perspectivePlayer: 0);

            // 场上看得见的卡名 —— 这些出现在文字里是公开信息，允许。
            var visible = observation.Self.Board.Select(follower => CardCatalog.Get(follower.CardId).Name)
                .Concat(observation.Opponent.Board.Select(follower => CardCatalog.Get(follower.CardId).Name))
                .ToHashSet(StringComparer.Ordinal);

            var hiddenNames = step.BeforeState.Players[1].Hand
                .Select(card => card.Definition.Name)
                .Distinct(StringComparer.Ordinal)
                .Where(name => !visible.Contains(name))
                .ToList();

            foreach (var evaluation in decision.Evaluations)
            {
                var text = HumanActionText.DescribeOpponentAction(observation, evaluation.Action);
                described++;
                if (text.Contains("未公开", StringComparison.Ordinal))
                {
                    redacted++;
                }
                else
                {
                    publicOnly++;
                }

                foreach (var hidden in hiddenNames)
                {
                    if (text.Contains(hidden, StringComparison.Ordinal))
                    {
                        leaks.Add(
                            $"第 {decision.TurnNumber} 回合：文字里出现了只在他手牌里的「{hidden}」—— {text}");
                    }
                }
            }
        });

    if (leaks.Count > 0)
    {
        foreach (var leak in leaks.Take(5))
        {
            Console.WriteLine("  ✗ " + leak);
        }

        throw new InvalidOperationException(
            $"对手思考面板泄露了手牌信息（{leaks.Count} 处）：这样打人机对战，" +
            "赢了也说明不了牌手强弱。");
    }

    Console.WriteLine("Opponent thinking privacy test passed.");
    Console.WriteLine(
        $"  检查了 {decisions} 次对手决策、{described} 条候选描述，没有一条泄露只在他手牌里的卡名。");
    Console.WriteLine(
        $"  其中 {publicOnly} 条给出具体卡名（攻击/进化等只涉及场上随从的动作），" +
        $"{redacted} 条只报类型（涉及他的手牌）。");
}

/// <summary>
/// 一组候选动作必须<b>两两可区分</b>。
/// <para>
/// 界面在"需要用户从多个变体里挑"时，就是把候选的文字列出来（菜单或列表）。
/// 两条一模一样的文字等于没有选择 —— 用户看着几个相同的按钮，无从选起。
/// </para>
/// <para>
/// 【模式】卡牌最容易踩这个：同一张牌、同一个目标，只有模式不同，
/// 文字里不带模式名就完全一样。实测就是这么发现的（一张牌弹出 4 条重复选项）。
/// </para>
/// <para>
/// 这个检查之所以能成立，是因为翻译逻辑 <see cref="HumanActionText"/> 和它同在引擎层 ——
/// 界面自己写一份翻译，这里就够不着了。
/// </para>
/// </summary>
private static void VerifyDistinguishable(
    GameObservation observation,
    IReadOnlyList<GameAction> candidates,
    List<string> failures,
    GestureTally tally)
{
    if (candidates.Count <= 1)
    {
        return;
    }

    if (HumanActionResolver.IsModeOnlyChoice(candidates))
    {
        tally.NoteModeChoice();
    }

    var duplicates = candidates
        .Select(candidate => HumanActionText.Describe(observation, candidate))
        .GroupBy(label => label, StringComparer.Ordinal)
        .Where(group => group.Count() > 1)
        .Select(group => group.Key)
        .ToList();

    if (duplicates.Count > 0)
    {
        failures.Add(
            $"回合 {observation.TurnNumber}：有 {duplicates.Count} 组候选的文字完全相同，界面上无法区分：" +
            string.Join(" ｜ ", duplicates));
    }
}

/// <summary>
/// 把一个决策点上所有可能的手势跑一遍，并把"没有任何手势能选中"的合法动作记进
/// <paramref name="failures"/>。
/// </summary>
private static void CollectGestureReach(
    GameObservation observation,
    IReadOnlyList<GameAction> legalActions,
    List<string> failures,
    GestureTally tally)
{
    // 这里只放引擎给出的动作实例本身，所以 record 的默认相等性是可靠的 ——
    // record 里的 IReadOnlyList 字段走引用相等，而两侧就是同一个对象。
    var reachable = new HashSet<GameAction>();

    void Take(string kind, IReadOnlyList<GameAction> candidates)
    {
        tally.Add(kind, candidates.Count);
        VerifyDistinguishable(observation, candidates, failures, tally);
        foreach (var candidate in candidates)
        {
            reachable.Add(candidate);
        }
    }

    // 常驻按钮：结束回合、使用额外 PP。
    Take("常驻按钮", HumanActionResolver.Direct(legalActions));

    if (observation.Phase == GamePhase.Mulligan)
    {
        // 换牌是"点子集"，用户逐个点，所以所有子集都算可达。换牌永远不会弹菜单。
        var ids = observation.OwnHand.Select(card => card.InstanceId).ToArray();
        for (var mask = 0; mask < 1 << ids.Length; mask++)
        {
            var subset = Enumerable.Range(0, ids.Length)
                .Where(bit => (mask & (1 << bit)) != 0)
                .Select(bit => ids[bit])
                .ToArray();
            if (HumanActionResolver.Mulligan(legalActions, subset) is { } action)
            {
                reachable.Add(action);
            }
        }
    }
    else
    {
        foreach (var card in observation.OwnHand)
        {
            // 拖到己方场上（不指定目标）—— 这是最常见的出牌动作
            Take("手牌→己方场上", HumanActionResolver.FromHand(legalActions, card.InstanceId));
            // 拖到敌方主战者上
            Take(
                "手牌→敌方主战者",
                HumanActionResolver.FromHand(legalActions, card.InstanceId, null, targetLeader: true));
            // 拖到每一个敌方随从上
            foreach (var enemy in observation.Opponent.Board)
            {
                Take(
                    "手牌→敌方随从",
                    HumanActionResolver.FromHand(legalActions, card.InstanceId, enemy.InstanceId));
            }
        }

        foreach (var follower in observation.Self.Board)
        {
            // 拖到敌方主战者上
            Take(
                "随从→敌方主战者",
                HumanActionResolver.Attack(legalActions, follower.InstanceId, null, targetLeader: true));
            // 拖到每一个敌方随从上
            foreach (var enemy in observation.Opponent.Board)
            {
                Take(
                    "随从→敌方随从",
                    HumanActionResolver.Attack(legalActions, follower.InstanceId, enemy.InstanceId));
            }

            // 右键：进化 / 超进化
            Take("右键进化", HumanActionResolver.Evolve(legalActions, follower.InstanceId));
        }
    }

    foreach (var action in legalActions)
    {
        if (!reachable.Contains(action))
        {
            failures.Add(
                $"回合 {observation.TurnNumber} P{observation.PerspectivePlayer + 1}：{action}");
        }
    }
}

internal static void RunRubyFanfareTest()
{
    var ruby = CardCatalog.Get(CardIds.GreedyArchangelRuby);
    var testDeck = new DeckDefinition("Ruby effect test", Enumerable.Repeat(ruby, DeckDefinition.RequiredCardCount));
    var state = GameEngine.CreateGame(testDeck, testDeck, seed: 7_777);

    while (state.Phase == GamePhase.Mulligan)
    {
        state = GameEngine.Apply(state, new MulliganAction([]));
    }

    while (state.Players[state.ActivePlayer].CurrentPlayPoints < ruby.Cost)
    {
        state = GameEngine.Apply(state, new EndTurnAction());
    }

    var player = state.ActivePlayer;
    var before = state.Players[player];
    var play = GameEngine.GetLegalActions(state)
        .OfType<PlayFollowerAction>()
        .First(action => action.CardInstanceId == before.Hand[0].InstanceId);

    if (play.HandCardTargetInstanceId is null)
    {
        throw new InvalidOperationException("Ruby should choose another card in hand when one is available.");
    }

    var afterState = GameEngine.Apply(state, play);
    var after = afterState.Players[player];
    if (after.Board.Count != before.Board.Count + 1 ||
        after.Hand.Count != before.Hand.Count - 1 ||
        after.Deck.Count != before.Deck.Count)
    {
        throw new InvalidOperationException("Ruby's Fanfare did not return one hand card to the deck and draw one card.");
    }

    Console.WriteLine("Ruby Fanfare test passed.");
    Console.WriteLine("A different hand card was returned to the deck, the deck was shuffled, and 1 card was drawn.");
}

internal static void RunSeraphsGospelTest()
{
    var gospel = CardCatalog.Get(CardIds.SeraphsGospel);
    var testDeck = new DeckDefinition("Gospel effect test", Enumerable.Repeat(gospel, DeckDefinition.RequiredCardCount));
    var state = GameEngine.CreateGame(testDeck, testDeck, seed: 8_888);

    while (state.Phase == GamePhase.Mulligan)
    {
        state = GameEngine.Apply(state, new MulliganAction([]));
    }

    while (state.Players[state.ActivePlayer].CurrentPlayPoints < gospel.Cost)
    {
        state = GameEngine.Apply(state, new EndTurnAction());
    }

    var player = state.ActivePlayer;
    var before = state.Players[player];
    var spell = GameEngine.GetLegalActions(state).OfType<PlaySpellAction>().First();
    var afterState = GameEngine.Apply(state, spell);
    var after = afterState.Players[player];
    if (spell.Target is not null ||
        after.Hand.Count != before.Hand.Count + 1 ||
        after.Deck.Count != before.Deck.Count - 2 ||
        after.Graveyard.Count != before.Graveyard.Count + 1)
    {
        throw new InvalidOperationException("Seraph's Gospel did not draw two cards correctly.");
    }

    Console.WriteLine("Seraph's Gospel test passed.");
    Console.WriteLine("The spell entered the graveyard and 2 cards were drawn.");
}

internal static void RunOliviaEffectTest()
{
    var olivia = CardCatalog.Get(CardIds.ValiantFallenAngelOlivia);
    var testDeck = new DeckDefinition("Olivia effect test", Enumerable.Repeat(olivia, DeckDefinition.RequiredCardCount));
    var state = GameEngine.CreateGame(testDeck, testDeck, seed: 9_999);

    while (state.Phase == GamePhase.Mulligan)
    {
        state = GameEngine.Apply(state, new MulliganAction([]));
    }

    var player = state.ActivePlayer;
    while (state.ActivePlayer != player || state.Players[player].OwnTurnNumber < 7)
    {
        state = GameEngine.Apply(state, new EndTurnAction());
    }

    var firstPlay = GameEngine.GetLegalActions(state).OfType<PlayFollowerAction>().First();
    state = GameEngine.Apply(state, firstPlay);
    if (state.Players[player].CurrentPlayPoints != 2)
    {
        throw new InvalidOperationException("Olivia should restore 2 PP after being played with 7 PP.");
    }

    while (state.ActivePlayer != player || state.Players[player].OwnTurnNumber < 8)
    {
        state = GameEngine.Apply(state, new EndTurnAction());
    }

    var secondPlay = GameEngine.GetLegalActions(state).OfType<PlayFollowerAction>().First();
    state = GameEngine.Apply(state, secondPlay);
    var firstOliviaId = firstPlay.CardInstanceId;
    var secondOliviaId = secondPlay.CardInstanceId;
    var beforeSuperEvolve = state.Players[player];
    var superEvolve = GameEngine.GetLegalActions(state)
        .OfType<SuperEvolveAction>()
        .Single(action => action.FollowerInstanceId == secondOliviaId &&
                          action.OtherFollowerTargetInstanceId == firstOliviaId);
    var afterState = GameEngine.Apply(state, superEvolve);
    var after = afterState.Players[player];
    var source = after.Board.Single(follower => follower.InstanceId == secondOliviaId);
    var target = after.Board.Single(follower => follower.InstanceId == firstOliviaId);

    if (after.SuperEvolutionPoints != beforeSuperEvolve.SuperEvolutionPoints - 1 ||
        source.EvolutionState != EvolutionState.SuperEvolved || source.Attack != 7 || source.CurrentDefense != 7 ||
        target.EvolutionState != EvolutionState.SuperEvolved || target.Attack != 7 || target.CurrentDefense != 7)
    {
        throw new InvalidOperationException("Olivia's super-evolution effect did not super evolve the other follower correctly.");
    }

    Console.WriteLine("Olivia effect test passed.");
    Console.WriteLine("Fanfare drew cards, restored 2 PP, and super evolution granted another follower +3/+3 without spending SEP.");
}

internal static void RunApocalypseDeckReplacementTest()
{
    var abyssLord = CardCatalog.Get(CardIds.UltimateSinLordOfAbyss);
    var testDeck = new DeckDefinition(
        "Apocalypse replacement test",
        Enumerable.Repeat(abyssLord, DeckDefinition.RequiredCardCount));
    var state = CompleteMulligan(GameEngine.CreateGame(testDeck, testDeck, seed: 10_004));

    while (state.Players[state.ActivePlayer].CurrentPlayPoints < abyssLord.Cost)
    {
        state = GameEngine.Apply(state, new EndTurnAction());
    }

    var player = state.ActivePlayer;
    var beforeDeckCount = state.Players[player].Deck.Count;
    var play = GameEngine.GetLegalActions(state)
        .OfType<PlayFollowerAction>()
        .First(action => action.CardInstanceId == state.Players[player].Hand
            .First(card => card.Definition.Id == CardIds.UltimateSinLordOfAbyss).InstanceId);
    var afterState = GameEngine.Apply(state, play);
    var afterDeck = afterState.Players[player].Deck;

    var correctApocalypseDeck = afterDeck.Count == 10 &&
                               afterDeck.Count(card => card.Definition.Id == CardIds.SilentDemonGeneral) == 3 &&
                               afterDeck.Count(card => card.Definition.Id == CardIds.ServantOfTheAbyssLord) == 3 &&
                               afterDeck.Count(card => card.Definition.Id == CardIds.PurgatoryEvilWorship) == 3 &&
                               afterDeck.Count(card => card.Definition.Id == CardIds.AstarothsVerdict) == 1;
    if (!correctApocalypseDeck || beforeDeckCount == afterDeck.Count)
    {
        throw new InvalidOperationException("Lord of the Abyss did not replace the remaining deck with the 10-card Apocalypse deck.");
    }

    var rejectedGeneratedDeck = false;
    try
    {
        _ = CardCatalog.CreateDeck(
            "Invalid generated-card deck",
            [new DeckCardEntry(CardIds.SilentDemonGeneral, DeckDefinition.RequiredCardCount)]);
    }
    catch (ArgumentException)
    {
        rejectedGeneratedDeck = true;
    }

    if (!rejectedGeneratedDeck)
    {
        throw new InvalidOperationException("A generated Apocalypse card was incorrectly accepted in a normal deck.");
    }

    Console.WriteLine("Apocalypse deck replacement test passed.");
    Console.WriteLine("Lord of the Abyss replaced the remaining deck with 3/3/3/1 generated cards, and generated cards cannot be deck-built directly.");
}

internal static void RunPurgatoryEvilWorshipTest()
{
    VerifyPurgatoryEvilWorshipTargetCount(2);
    VerifyPurgatoryEvilWorshipTargetCount(1);
    VerifyPurgatoryEvilWorshipTargetCount(0);

    Console.WriteLine("Purgatory Evil Worship test passed.");
    Console.WriteLine("It damages up to 2 selected followers, while always dealing 6 damage to the opposing leader.");
}

internal static void VerifyPurgatoryEvilWorshipTargetCount(int expectedTargetCount)
{
    var purgatoryEvilWorship = CardCatalog.Get(CardIds.PurgatoryEvilWorship);
    var servant = CardCatalog.Get(CardIds.ServantOfTheAbyssLord);
    var attackingDeck = new DeckDefinition(
        "Purgatory Fanfare test",
        Enumerable.Repeat(purgatoryEvilWorship, DeckDefinition.RequiredCardCount));
    var defendingDeck = new DeckDefinition(
        "Servant target test",
        Enumerable.Repeat(servant, DeckDefinition.RequiredCardCount));
    var state = CompleteMulligan(GameEngine.CreateGame(attackingDeck, defendingDeck, seed: 10_007));

    for (var step = 0; step < 60; step++)
    {
        var activePlayer = state.ActivePlayer;
        var active = state.Players[activePlayer];
        var opponent = state.Players[activePlayer == 0 ? 1 : 0];

        if (activePlayer == 0 && active.CurrentPlayPoints >= purgatoryEvilWorship.Cost &&
            opponent.Board.Count == expectedTargetCount)
        {
            var action = GameEngine.GetLegalActions(state)
                .OfType<PlayFollowerAction>()
                .First(candidate =>
                    state.Players[activePlayer].Hand.Any(card =>
                        card.InstanceId == candidate.CardInstanceId &&
                        card.Definition.Id == CardIds.PurgatoryEvilWorship) &&
                    candidate.EnemyFollowerTargetInstanceIds is { Count: var count } &&
                    count == expectedTargetCount);
            var beforeOpponent = state.Players[1];
            var afterState = GameEngine.Apply(state, action);
            var afterOpponent = afterState.Players[1];

            if (afterOpponent.Health != beforeOpponent.Health - 6 ||
                action.EnemyFollowerTargetInstanceIds!.Any(targetId =>
                    afterOpponent.Board.Single(follower => follower.InstanceId == targetId).CurrentDefense != 7))
            {
                throw new InvalidOperationException(
                    "Purgatory Evil Worship did not damage the selected followers and enemy leader correctly.");
            }

            return;
        }

        if (activePlayer == 1 && active.CurrentPlayPoints >= servant.Cost && active.Board.Count < expectedTargetCount)
        {
            var servantAction = GameEngine.GetLegalActions(state)
                .OfType<PlayFollowerAction>()
                .First(candidate => active.Hand.Any(card =>
                    card.InstanceId == candidate.CardInstanceId &&
                    card.Definition.Id == CardIds.ServantOfTheAbyssLord));
            state = GameEngine.Apply(state, servantAction);
            continue;
        }

        state = GameEngine.Apply(state, new EndTurnAction());
    }

    throw new InvalidOperationException(
        $"Purgatory Evil Worship test could not create {expectedTargetCount} required target(s).");
}

internal static void RunAstarothsVerdictTest()
{
    var verdict = CardCatalog.Get(CardIds.AstarothsVerdict);
    var olivia = CardCatalog.Get(CardIds.ValiantFallenAngelOlivia);
    var verdictDeck = new DeckDefinition(
        "Astaroth's Verdict test",
        Enumerable.Repeat(verdict, DeckDefinition.RequiredCardCount));
    var healingDeck = new DeckDefinition(
        "Maximum-health healing test",
        Enumerable.Repeat(olivia, DeckDefinition.RequiredCardCount));
    var state = CompleteMulligan(GameEngine.CreateGame(verdictDeck, healingDeck, seed: 10_008));

    while (state.ActivePlayer != 0 || state.Players[0].CurrentPlayPoints < verdict.Cost)
    {
        state = GameEngine.Apply(state, new EndTurnAction());
    }

    var play = GameEngine.GetLegalActions(state).OfType<PlaySpellAction>().First();
    var afterState = GameEngine.Apply(state, play);
    var opponent = afterState.Players[1];
    if (opponent.MaxHealth != 1 || opponent.Health != 1)
    {
        throw new InvalidOperationException("Astaroth's Verdict did not reduce both maximum and current leader health to 1.");
    }

    state = afterState;
    while (state.ActivePlayer != 1 || state.Players[1].CurrentPlayPoints < olivia.Cost)
    {
        state = GameEngine.Apply(state, new EndTurnAction());
    }

    var healingPlay = GameEngine.GetLegalActions(state).OfType<PlayFollowerAction>().First();
    afterState = GameEngine.Apply(state, healingPlay);
    opponent = afterState.Players[1];
    if (opponent.MaxHealth != 1 || opponent.Health != 1)
    {
        throw new InvalidOperationException("Leader healing exceeded the maximum health set by Astaroth's Verdict.");
    }

    Console.WriteLine("Astaroth's Verdict test passed.");
    Console.WriteLine("The opposing leader's maximum and current health both became 1, and later healing could not exceed that maximum.");
}

internal static void RunGladiatorEnhanceTest()
{
    var gladiator = CardCatalog.Get(CardIds.Gladiator);
    var testDeck = new DeckDefinition(
        "Gladiator Enhance test",
        Enumerable.Repeat(gladiator, DeckDefinition.RequiredCardCount));

    var normalState = CompleteMulligan(GameEngine.CreateGame(testDeck, testDeck, seed: 10_010));
    while (normalState.Players[normalState.ActivePlayer].CurrentPlayPoints < gladiator.Cost ||
           normalState.Players[normalState.ActivePlayer].CurrentPlayPoints >= 4)
    {
        normalState = GameEngine.Apply(normalState, new EndTurnAction());
    }

    var normalPlayer = normalState.ActivePlayer;
    var normalPlay = GameEngine.GetLegalActions(normalState).OfType<PlayFollowerAction>().First();
    var normalAfter = GameEngine.Apply(normalState, normalPlay);
    var normalFollower = normalAfter.Players[normalPlayer].Board.Single(follower =>
        follower.InstanceId == normalPlay.CardInstanceId);
    if (normalAfter.Players[normalPlayer].CurrentPlayPoints !=
            normalState.Players[normalPlayer].CurrentPlayPoints - 2 ||
        normalFollower.Attack != 2 || normalFollower.CurrentDefense != 2)
    {
        throw new InvalidOperationException("Gladiator did not use its normal 2 PP play correctly below the Enhance threshold.");
    }

    var enhancedState = CompleteMulligan(GameEngine.CreateGame(testDeck, testDeck, seed: 20_010));
    while (enhancedState.Players[enhancedState.ActivePlayer].CurrentPlayPoints < 4)
    {
        enhancedState = GameEngine.Apply(enhancedState, new EndTurnAction());
    }

    var enhancedPlayer = enhancedState.ActivePlayer;
    var enhancedPlay = GameEngine.GetLegalActions(enhancedState).OfType<PlayFollowerAction>().First();
    var enhancedAfter = GameEngine.Apply(enhancedState, enhancedPlay);
    var enhancedFollower = enhancedAfter.Players[enhancedPlayer].Board.Single(follower =>
        follower.InstanceId == enhancedPlay.CardInstanceId);
    if (enhancedAfter.Players[enhancedPlayer].CurrentPlayPoints !=
            enhancedState.Players[enhancedPlayer].CurrentPlayPoints - 4 ||
        enhancedFollower.Attack != 5 || enhancedFollower.CurrentDefense != 5)
    {
        throw new InvalidOperationException("Gladiator did not pay 4 PP and gain +3/+3 through Enhance.");
    }

    Console.WriteLine("Gladiator Enhance test passed.");
    Console.WriteLine("Below 4 PP it is a 2 PP 2/2; at 4 or more PP it automatically spends 4 PP and becomes 5/5.");
}

internal static void RunStarchiumEvolutionTest()
{
    var starchium = CardCatalog.Get(CardIds.RoyalSeveringHeavenStarchium);
    var knight = CardCatalog.Get(CardIds.Knight);
    var testDeck = new DeckDefinition(
        "Starchium evolution test",
        Enumerable.Repeat(starchium, DeckDefinition.RequiredCardCount));
    var state = CompleteMulligan(GameEngine.CreateGame(testDeck, testDeck, seed: 10_011));

    while (state.Players[state.ActivePlayer].CurrentPlayPoints < starchium.Cost)
    {
        state = GameEngine.Apply(state, new EndTurnAction());
    }

    var player = state.ActivePlayer;
    var play = GameEngine.GetLegalActions(state).OfType<PlayFollowerAction>().First();
    state = GameEngine.Apply(state, play);

    while (!GameEngine.GetLegalActions(state).OfType<EvolveAction>().Any())
    {
        state = GameEngine.Apply(state, new EndTurnAction());
    }

    player = state.ActivePlayer;
    var evolve = GameEngine.GetLegalActions(state).OfType<EvolveAction>().First();
    var after = GameEngine.Apply(state, evolve);
    var source = after.Players[player].Board.Single(follower => follower.InstanceId == evolve.FollowerInstanceId);
    var knights = after.Players[player].Board.Where(follower => follower.Definition.Id == knight.Id).ToArray();

    if (source.Attack != 6 || source.CurrentDefense != 6 ||
        knights.Length != 2 || knights.Any(follower => follower.Attack != 2 || follower.CurrentDefense != 2) ||
        knights.Any(follower => follower.SummonedOnTurn != after.TurnNumber))
    {
        throw new InvalidOperationException("Starchium's evolution effect did not summon and strengthen Knights correctly.");
    }

    var rejectedGeneratedDeck = false;
    try
    {
        _ = CardCatalog.CreateDeck("Invalid Knight deck", [new DeckCardEntry(CardIds.Knight, 40)]);
    }
    catch (ArgumentException)
    {
        rejectedGeneratedDeck = true;
    }

    if (!rejectedGeneratedDeck)
    {
        throw new InvalidOperationException("Knight was incorrectly accepted in a normal deck.");
    }

    var fullBoardState = CompleteMulligan(GameEngine.CreateGame(testDeck, testDeck, seed: 10_012));
    var fullBoardPlayer = fullBoardState.StartingPlayer;
    while (fullBoardState.Players[fullBoardPlayer].Board.Count < PlayerState.BoardLimit)
    {
        if (fullBoardState.ActivePlayer == fullBoardPlayer &&
            fullBoardState.Players[fullBoardPlayer].CurrentPlayPoints >= starchium.Cost)
        {
            var fullBoardPlay = GameEngine.GetLegalActions(fullBoardState)
                .OfType<PlayFollowerAction>()
                .First();
            fullBoardState = GameEngine.Apply(fullBoardState, fullBoardPlay);
        }

        if (fullBoardState.Players[fullBoardPlayer].Board.Count < PlayerState.BoardLimit)
        {
            fullBoardState = GameEngine.Apply(fullBoardState, new EndTurnAction());
        }
    }

    var fullBoardEvolve = GameEngine.GetLegalActions(fullBoardState).OfType<EvolveAction>().First();
    var fullBoardAfter = GameEngine.Apply(fullBoardState, fullBoardEvolve);
    var fullBoardFollowers = fullBoardAfter.Players[fullBoardPlayer].Board;
    var fullBoardSource = fullBoardFollowers.Single(follower => follower.InstanceId == fullBoardEvolve.FollowerInstanceId);
    if (fullBoardFollowers.Count != PlayerState.BoardLimit ||
        fullBoardFollowers.Any(follower => follower.Definition.Id == knight.Id) ||
        fullBoardSource.Attack != 6 || fullBoardSource.CurrentDefense != 6 ||
        fullBoardFollowers.Where(follower => follower.InstanceId != fullBoardSource.InstanceId)
            .Any(follower => follower.Attack != 5 || follower.CurrentDefense != 5))
    {
        throw new InvalidOperationException("Starchium incorrectly displaced followers when the board was full.");
    }

    Console.WriteLine("Starchium evolution test passed.");
    Console.WriteLine("Evolution summoned two shared Knight tokens and strengthened every other allied follower; a full board kept all existing followers and summoned no Knights.");
}

internal static void RunDragonCardEffectTest()
{
    VerifyShatteredBanditEffects();
    VerifyWhelpTemperTantrumEffects();
    VerifyOverwhelmingAssailantEffects();
    VerifyBlazingWhelpIntimidate();
    VerifyJawsPartingEffects();
    VerifyDustLawbreakerEffects();
    VerifyFangDistributedDamageEffects();
    VerifyPiercingSinnerAntimariaEffects();
    VerifyFangedTransfigurationNormagdalaModes();
    VerifyDragonOracleEffects();
    VerifyGoldenSilverThroneRumioreAndAlberteEffects();
    VerifyMasterOfSkyFateLuriaEffects();
    VerifyShangfengjinRedCurrentAndAuraEffects();
    VerifySolarFlareRoarAndIlantzaEffects();
    VerifyNewDragonAndGeneratedCardEffects();
    VerifyAwakeningAndAmuletEffects();
    VerifyLeaderEffectsAndCrests();

    Console.WriteLine("Dragon and Neutral card effect test passed.");
    Console.WriteLine("Previous-turn leader attacks, Fanfare Storm, Last Words copies, discard triggers, random and distributed damage, Awakening, Amulets, Countdown, leader effects, Crests, evolution / super-evolution effects, Bane, Intimidate, Aura, Rush, Ward bypass, modes, PP acceleration, Accelerate, Barrier, and Enhance card search all resolved correctly.");
}

internal static void VerifyShatteredBanditEffects()
{
    var bandit = CardCatalog.Get(CardIds.ShatteredBandit);
    var deck = new DeckDefinition("Bandit effect test", Enumerable.Repeat(bandit, DeckDefinition.RequiredCardCount));
    var state = CompleteMulligan(GameEngine.CreateGame(deck, deck, seed: 10_013));
    var player = state.ActivePlayer;
    var opponent = player == 0 ? 1 : 0;

    var firstPlay = GameEngine.GetLegalActions(state).OfType<PlayFollowerAction>().First();
    state = GameEngine.Apply(state, firstPlay);
    var firstBanditId = firstPlay.CardInstanceId;
    state = GameEngine.Apply(state, new EndTurnAction());

    var opponentPlay = GameEngine.GetLegalActions(state).OfType<PlayFollowerAction>().First();
    state = GameEngine.Apply(state, opponentPlay);
    state = GameEngine.Apply(state, new EndTurnAction());

    state = GameEngine.Apply(state, new AttackLeaderAction(firstBanditId));
    state = GameEngine.Apply(state, new EndTurnAction());
    state = GameEngine.Apply(state, new EndTurnAction());
    if (!state.Players[player].AttackedEnemyLeaderOnPreviousTurn)
    {
        throw new InvalidOperationException("A leader attack was not retained for the player's next turn.");
    }

    var stormPlay = GameEngine.GetLegalActions(state).OfType<PlayFollowerAction>().First();
    state = GameEngine.Apply(state, stormPlay);
    var stormBandit = state.Players[player].Board.Single(follower => follower.InstanceId == stormPlay.CardInstanceId);
    if (!stormBandit.HasStorm)
    {
        throw new InvalidOperationException("Shattered Bandit did not gain Storm after the previous-turn leader attack.");
    }

    var handBeforeTrade = state.Players[player].Hand.Select(card => card.InstanceId).ToHashSet();
    var targetId = state.Players[opponent].Board.Single().InstanceId;
    state = GameEngine.Apply(state, new AttackFollowerAction(stormBandit.InstanceId, targetId));
    var returnedBandit = state.Players[player].Hand.SingleOrDefault(card =>
        !handBeforeTrade.Contains(card.InstanceId) && card.Definition.Id == CardIds.ShatteredBandit);
    if (returnedBandit is null || !returnedBandit.HasSuppressedLastWords)
    {
        throw new InvalidOperationException("Shattered Bandit's Last Words did not add a copy without Last Words.");
    }
}

internal static void VerifyWhelpTemperTantrumEffects()
{
    var tantrum = CardCatalog.Get(CardIds.WhelpTemperTantrum);
    var deck = new DeckDefinition("Whelp spell test", Enumerable.Repeat(tantrum, DeckDefinition.RequiredCardCount));
    var state = CompleteMulligan(GameEngine.CreateGame(deck, deck, seed: 10_014));
    var player = state.ActivePlayer;
    var opponent = player == 0 ? 1 : 0;

    var firstSpell = GameEngine.GetLegalActions(state).OfType<PlaySpellAction>().First();
    state = GameEngine.Apply(state, firstSpell);
    if (!state.Players[player].Board.Single().HasIntimidate)
    {
        throw new InvalidOperationException("Whelp Temper Tantrum did not summon a Blazing Whelp with Intimidate.");
    }

    state = GameEngine.Apply(state, new EndTurnAction());
    state = GameEngine.Apply(state, GameEngine.GetLegalActions(state).OfType<PlaySpellAction>().First());
    state = GameEngine.Apply(state, new EndTurnAction());
    state = GameEngine.Apply(state, new EndTurnAction());
    state = GameEngine.Apply(state, new EndTurnAction());

    var enhanceSpell = GameEngine.GetLegalActions(state).OfType<PlaySpellAction>().First();
    var beforePp = state.Players[player].CurrentPlayPoints;
    state = GameEngine.Apply(state, enhanceSpell);
    if (beforePp != 3 || state.Players[player].CurrentPlayPoints != 0 ||
        state.Players[opponent].Board.Count != 0)
    {
        throw new InvalidOperationException("Whelp Temper Tantrum's 3 PP Enhance did not deal 3 random damage correctly.");
    }
}

internal static void VerifyOverwhelmingAssailantEffects()
{
    var assailant = CardCatalog.Get(CardIds.OverwhelmingAssailant);
    var deck = new DeckDefinition("Assailant effect test", Enumerable.Repeat(assailant, DeckDefinition.RequiredCardCount));
    var state = CompleteMulligan(GameEngine.CreateGame(deck, deck, seed: 10_015));
    var player = state.ActivePlayer;
    var opponent = player == 0 ? 1 : 0;

    state = GameEngine.Apply(state, new EndTurnAction());
    state = GameEngine.Apply(state, new EndTurnAction());
    var firstPlay = GameEngine.GetLegalActions(state).OfType<PlayFollowerAction>().First();
    state = GameEngine.Apply(state, firstPlay);
    state = GameEngine.Apply(state, new AttackLeaderAction(firstPlay.CardInstanceId));
    state = GameEngine.Apply(state, new EndTurnAction());
    state = GameEngine.Apply(state, new EndTurnAction());

    var secondPlay = GameEngine.GetLegalActions(state).OfType<PlayFollowerAction>().First();
    state = GameEngine.Apply(state, secondPlay);
    var healthBeforeAttack = state.Players[opponent].Health;
    state = GameEngine.Apply(state, new AttackLeaderAction(secondPlay.CardInstanceId));
    var attacker = state.Players[player].Board.Single(follower => follower.InstanceId == secondPlay.CardInstanceId);
    if (state.Players[opponent].Health != healthBeforeAttack - 2 || attacker.Attack != 2)
    {
        throw new InvalidOperationException("Overwhelming Assailant did not gain temporary attack before combat.");
    }

    state = GameEngine.Apply(state, new EndTurnAction());
    var resetAttacker = state.Players[player].Board.Single(follower => follower.InstanceId == secondPlay.CardInstanceId);
    if (resetAttacker.Attack != 1)
    {
        throw new InvalidOperationException("Overwhelming Assailant's temporary attack did not expire at end of turn.");
    }
}

internal static void VerifyBlazingWhelpIntimidate()
{
    var tantrum = CardCatalog.Get(CardIds.WhelpTemperTantrum);
    var goliath = CardCatalog.Get(CardIds.Goliath);
    var attackerDeck = new DeckDefinition("Whelp Intimidate test", Enumerable.Repeat(tantrum, DeckDefinition.RequiredCardCount));
    var defenderDeck = new DeckDefinition("Intimidate target test", Enumerable.Repeat(goliath, DeckDefinition.RequiredCardCount));
    var state = CompleteMulligan(GameEngine.CreateGame(attackerDeck, defenderDeck, seed: 10_016));
    const int player = 0;
    const int opponent = 1;
    if (state.ActivePlayer != player)
    {
        state = GameEngine.Apply(state, new EndTurnAction());
    }

    state = GameEngine.Apply(state, GameEngine.GetLegalActions(state).OfType<PlaySpellAction>().First());
    state = GameEngine.Apply(state, new EndTurnAction());
    while (state.ActivePlayer != opponent || state.Players[opponent].CurrentPlayPoints < goliath.Cost)
    {
        state = GameEngine.Apply(state, new EndTurnAction());
    }

    state = GameEngine.Apply(state, GameEngine.GetLegalActions(state).OfType<PlayFollowerAction>().First());
    state = GameEngine.Apply(state, new EndTurnAction());
    var whelp = state.Players[player].Board.Single(follower => follower.Definition.Id == CardIds.BlazingWhelp);
    var goliathOnBoard = state.Players[opponent].Board.Single(follower => follower.Definition.Id == CardIds.Goliath);
    var legalAttackTargets = GameEngine.GetLegalActions(state)
        .OfType<AttackFollowerAction>()
        .Where(action => action.AttackerInstanceId == goliathOnBoard.InstanceId)
        .Select(action => action.DefenderInstanceId);
    if (legalAttackTargets.Contains(whelp.InstanceId))
    {
        throw new InvalidOperationException("Blazing Whelp's Intimidate was incorrectly offered as a follower-attack target.");
    }
}

internal static void VerifyJawsPartingEffects()
{
    var jawsParting = CardCatalog.Get(CardIds.JawsParting);
    var bandit = CardCatalog.Get(CardIds.ShatteredBandit);
    var casterDeck = new DeckDefinition(
        "Jaws Parting test",
        Enumerable.Repeat(jawsParting, DeckDefinition.RequiredCardCount));
    var opponentDeck = new DeckDefinition(
        "Jaws Parting target test",
        Enumerable.Repeat(bandit, DeckDefinition.RequiredCardCount));
    var state = CompleteMulligan(GameEngine.CreateGame(casterDeck, opponentDeck, seed: 10_017));
    const int caster = 0;
    const int opponent = 1;

    while (state.ActivePlayer != caster || state.Players[caster].CurrentPlayPoints < jawsParting.Cost)
    {
        if (state.ActivePlayer == opponent && state.Players[opponent].Board.Count == 0)
        {
            var banditPlay = GameEngine.GetLegalActions(state).OfType<PlayFollowerAction>().FirstOrDefault();
            if (banditPlay is not null)
            {
                state = GameEngine.Apply(state, banditPlay);
                continue;
            }
        }

        state = GameEngine.Apply(state, new EndTurnAction());
    }

    var spell = GameEngine.GetLegalActions(state)
        .OfType<PlaySpellAction>()
        .First();
    if (spell.OwnHandCardTargetInstanceIds is not { Count: 2 })
    {
        throw new InvalidOperationException("Jaws Parting did not require selecting exactly two hand cards to discard.");
    }

    var beforeHandCount = state.Players[caster].Hand.Count;
    var beforeGraveyardCount = state.Players[caster].Graveyard.Count;
    var beforeLeaderHealth = state.Players[opponent].Health;
    var beforeTargetCount = state.Players[opponent].Board.Count;
    state = GameEngine.Apply(state, spell);

    if (state.Players[caster].Hand.Count != beforeHandCount - 3 ||
        state.Players[caster].Graveyard.Count != beforeGraveyardCount + 3 ||
        state.Players[opponent].Health != beforeLeaderHealth - 3 ||
        state.Players[opponent].Board.Count != beforeTargetCount - 1)
    {
        throw new InvalidOperationException("Jaws Parting did not discard two cards and resolve both damage portions correctly.");
    }
}

internal static void VerifyDustLawbreakerEffects()
{
    var dustLawbreaker = CardCatalog.Get(CardIds.DustLawbreaker);
    var assailant = CardCatalog.Get(CardIds.OverwhelmingAssailant);
    var bandit = CardCatalog.Get(CardIds.ShatteredBandit);
    var casterDeck = new DeckDefinition(
        "Dust Lawbreaker condition test",
        [
            .. Enumerable.Repeat(dustLawbreaker, 20),
            .. Enumerable.Repeat(assailant, 20)
        ]);
    var opponentDeck = new DeckDefinition(
        "Dust Lawbreaker target test",
        Enumerable.Repeat(bandit, DeckDefinition.RequiredCardCount));

    for (ulong seed = 10_100; seed < 10_200; seed++)
    {
        var state = CompleteMulligan(GameEngine.CreateGame(casterDeck, opponentDeck, seed));
        const int caster = 0;
        const int opponent = 1;
        state = AdvanceWithBanditOpponent(state, caster, opponent, targetOwnTurn: 4);

        var assailantPlay = GameEngine.GetLegalActions(state)
            .OfType<PlayFollowerAction>()
            .FirstOrDefault(action => state.Players[caster].Hand
                .Single(card => card.InstanceId == action.CardInstanceId)
                .Definition.Id == CardIds.OverwhelmingAssailant);
        var hasDustLawbreaker = state.Players[caster].Hand.Any(card =>
            card.Definition.Id == CardIds.DustLawbreaker);
        if (assailantPlay is null || !hasDustLawbreaker)
        {
            continue;
        }

        state = GameEngine.Apply(state, assailantPlay);
        state = GameEngine.Apply(state, new AttackLeaderAction(assailantPlay.CardInstanceId));
        state = GameEngine.Apply(state, new EndTurnAction());

        if (state.Players[opponent].Board.Count < PlayerState.BoardLimit)
        {
            var banditPlay = GameEngine.GetLegalActions(state).OfType<PlayFollowerAction>().FirstOrDefault();
            if (banditPlay is not null)
            {
                state = GameEngine.Apply(state, banditPlay);
            }
        }

        state = GameEngine.Apply(state, new EndTurnAction());
        var dustPlay = GameEngine.GetLegalActions(state)
            .OfType<PlayFollowerAction>()
            .FirstOrDefault(action => state.Players[caster].Hand
                .Single(card => card.InstanceId == action.CardInstanceId)
                .Definition.Id == CardIds.DustLawbreaker);
        if (dustPlay is null || !state.Players[caster].AttackedEnemyLeaderOnPreviousTurn ||
            state.Players[opponent].Board.Count < 2)
        {
            continue;
        }

        var beforeTargetCount = state.Players[opponent].Board.Count;
        state = GameEngine.Apply(state, dustPlay);
        if (state.Players[opponent].Board.Count != beforeTargetCount - 2)
        {
            throw new InvalidOperationException("Dust Lawbreaker did not resolve its conditional second random damage.");
        }

        var evolve = GameEngine.GetLegalActions(state).OfType<EvolveAction>()
            .Single(action => action.FollowerInstanceId == dustPlay.CardInstanceId);
        state = GameEngine.Apply(state, evolve);
        if (!state.Players[caster].Board.Any(follower => follower.Definition.Id == assailant.Id))
        {
            throw new InvalidOperationException("Dust Lawbreaker's evolution did not summon an Overwhelming Assailant.");
        }

        return;
    }

    throw new InvalidOperationException("The deterministic Dust Lawbreaker test could not find a usable opening hand.");
}

internal static void VerifyFangDistributedDamageEffects()
{
    var fang = CardCatalog.Get(CardIds.Fang);
    var bandit = CardCatalog.Get(CardIds.ShatteredBandit);
    var basicCasterDeck = new DeckDefinition(
        "Fang basic distribution test",
        Enumerable.Repeat(fang, DeckDefinition.RequiredCardCount));
    var opponentDeck = new DeckDefinition(
        "Fang target test",
        Enumerable.Repeat(bandit, DeckDefinition.RequiredCardCount));
    var basicState = CompleteMulligan(GameEngine.CreateGame(basicCasterDeck, opponentDeck, seed: 10_018));
    const int caster = 0;
    const int opponent = 1;

    while (basicState.Players[opponent].Board.Count < 3 || basicState.ActivePlayer != caster)
    {
        if (basicState.ActivePlayer == opponent && basicState.Players[opponent].Board.Count < 3)
        {
            var banditPlay = GameEngine.GetLegalActions(basicState).OfType<PlayFollowerAction>().FirstOrDefault();
            if (banditPlay is not null)
            {
                basicState = GameEngine.Apply(basicState, banditPlay);
                continue;
            }
        }

        basicState = GameEngine.Apply(basicState, new EndTurnAction());
    }

    var basicFang = GameEngine.GetLegalActions(basicState).OfType<PlaySpellAction>().First();
    basicState = GameEngine.Apply(basicState, basicFang);
    if (basicState.Players[opponent].Board.Count != 0)
    {
        throw new InvalidOperationException("Fang did not distribute its base 3 damage through the oldest followers.");
    }

    var conditionalCasterDeck = new DeckDefinition(
        "Fang condition test",
        [
            .. Enumerable.Repeat(fang, 20),
            .. Enumerable.Repeat(CardCatalog.Get(CardIds.OverwhelmingAssailant), 20)
        ]);
    for (ulong seed = 10_200; seed < 10_300; seed++)
    {
        var state = CompleteMulligan(GameEngine.CreateGame(conditionalCasterDeck, opponentDeck, seed));
        state = AdvanceWithBanditOpponent(state, caster, opponent, targetOwnTurn: 4);

        var assailantPlay = GameEngine.GetLegalActions(state)
            .OfType<PlayFollowerAction>()
            .FirstOrDefault(action => state.Players[caster].Hand
                .Single(card => card.InstanceId == action.CardInstanceId)
                .Definition.Id == CardIds.OverwhelmingAssailant);
        if (assailantPlay is null || !state.Players[caster].Hand.Any(card => card.Definition.Id == CardIds.Fang))
        {
            continue;
        }

        state = GameEngine.Apply(state, assailantPlay);
        state = GameEngine.Apply(state, new AttackLeaderAction(assailantPlay.CardInstanceId));
        state = GameEngine.Apply(state, new EndTurnAction());
        if (state.Players[opponent].Board.Count < PlayerState.BoardLimit)
        {
            var banditPlay = GameEngine.GetLegalActions(state).OfType<PlayFollowerAction>().FirstOrDefault();
            if (banditPlay is not null)
            {
                state = GameEngine.Apply(state, banditPlay);
            }
        }

        state = GameEngine.Apply(state, new EndTurnAction());
        if (!state.Players[caster].AttackedEnemyLeaderOnPreviousTurn ||
            state.Players[opponent].Board.Count != PlayerState.BoardLimit)
        {
            continue;
        }

        var fangPlay = GameEngine.GetLegalActions(state).OfType<PlaySpellAction>()
            .FirstOrDefault(action => state.Players[caster].Hand
                .Single(card => card.InstanceId == action.CardInstanceId)
                .Definition.Id == CardIds.Fang);
        if (fangPlay is null)
        {
            continue;
        }

        state = GameEngine.Apply(state, fangPlay);
        if (state.Players[opponent].Board.Count != 0)
        {
            throw new InvalidOperationException("Fang did not increase its distributed damage from 3 to 6 after a previous-turn leader attack.");
        }

        return;
    }

    throw new InvalidOperationException("The deterministic Fang condition test could not find a usable opening hand.");
}

internal static void VerifyPiercingSinnerAntimariaEffects()
{
    var antimaria = CardCatalog.Get(CardIds.PiercingSinnerAntimaria);
    var assailant = CardCatalog.Get(CardIds.OverwhelmingAssailant);
    var goliath = CardCatalog.Get(CardIds.Goliath);
    var casterDeck = new DeckDefinition(
        "Antimaria effect test",
        [
            .. Enumerable.Repeat(antimaria, 20),
            .. Enumerable.Repeat(assailant, 20)
        ]);
    var opponentDeck = new DeckDefinition(
        "Antimaria Ward test",
        Enumerable.Repeat(goliath, DeckDefinition.RequiredCardCount));
    const int caster = 0;
    const int opponent = 1;

    for (ulong seed = 10_300; seed < 10_500; seed++)
    {
        var state = CompleteMulligan(GameEngine.CreateGame(casterDeck, opponentDeck, seed));
        for (var step = 0; step < 100 &&
             (state.ActivePlayer != caster || state.Players[caster].OwnTurnNumber < 7); step++)
        {
            state = GameEngine.Apply(state, new EndTurnAction());
        }

        if (state.ActivePlayer != caster || state.Players[caster].OwnTurnNumber < 7)
        {
            throw new InvalidOperationException("The Antimaria test setup did not reach the required turn.");
        }

        var assailantPlay = GameEngine.GetLegalActions(state)
            .OfType<PlayFollowerAction>()
            .FirstOrDefault(action => state.Players[caster].Hand
                .Single(card => card.InstanceId == action.CardInstanceId)
                .Definition.Id == CardIds.OverwhelmingAssailant);
        if (assailantPlay is null || !state.Players[caster].Hand.Any(card =>
                card.Definition.Id == CardIds.PiercingSinnerAntimaria))
        {
            continue;
        }

        state = GameEngine.Apply(state, assailantPlay);
        state = GameEngine.Apply(state, new AttackLeaderAction(assailantPlay.CardInstanceId));
        state = GameEngine.Apply(state, new EndTurnAction());

        var wardPlay = GameEngine.GetLegalActions(state).OfType<PlayFollowerAction>().FirstOrDefault();
        if (wardPlay is null)
        {
            throw new InvalidOperationException("The opponent could not play a Ward follower for the Antimaria test.");
        }

        state = GameEngine.Apply(state, wardPlay);
        state = GameEngine.Apply(state, new EndTurnAction());
        var antimariaPlay = GameEngine.GetLegalActions(state)
            .OfType<PlayFollowerAction>()
            .First(action => state.Players[caster].Hand
                .Single(card => card.InstanceId == action.CardInstanceId)
                .Definition.Id == CardIds.PiercingSinnerAntimaria);
        state = GameEngine.Apply(state, antimariaPlay);

        var antimariaOnBoard = state.Players[caster].Board.Single(follower =>
            follower.InstanceId == antimariaPlay.CardInstanceId);
        if (!antimariaOnBoard.HasRush || !antimariaOnBoard.HasStorm || !antimariaOnBoard.CanIgnoreWard)
        {
            throw new InvalidOperationException("Antimaria did not retain Rush and Ward bypass or gain conditional Storm.");
        }

        var leaderAttack = GameEngine.GetLegalActions(state).OfType<AttackLeaderAction>()
            .SingleOrDefault(action => action.AttackerInstanceId == antimariaOnBoard.InstanceId);
        if (leaderAttack is null)
        {
            throw new InvalidOperationException("Antimaria could not attack the enemy leader through Ward after gaining Storm.");
        }

        var healthBeforeAttack = state.Players[opponent].Health;
        state = GameEngine.Apply(state, leaderAttack);
        if (state.Players[opponent].Health != healthBeforeAttack - antimaria.Attack)
        {
            throw new InvalidOperationException("Antimaria's leader attack did not resolve correctly.");
        }

        return;
    }

    throw new InvalidOperationException("The deterministic Antimaria test could not find a usable opening hand.");
}

internal static void VerifyFangedTransfigurationNormagdalaModes()
{
    var normagdala = CardCatalog.Get(CardIds.FangedTransfigurationNormagdala);
    var goliath = CardCatalog.Get(CardIds.Goliath);
    var casterDeck = new DeckDefinition(
        "Normagdala mode test",
        Enumerable.Repeat(normagdala, DeckDefinition.RequiredCardCount));
    var opponentDeck = new DeckDefinition(
        "Normagdala target test",
        Enumerable.Repeat(goliath, DeckDefinition.RequiredCardCount));
    const int caster = 0;
    const int opponent = 1;
    var state = CompleteMulligan(GameEngine.CreateGame(casterDeck, opponentDeck, seed: 10_021));

    for (var step = 0; step < 100 &&
         (state.ActivePlayer != caster || state.Players[caster].CurrentPlayPoints < normagdala.Cost); step++)
    {
        if (state.ActivePlayer == opponent && state.Players[opponent].Board.Count == 0)
        {
            var wardPlay = GameEngine.GetLegalActions(state).OfType<PlayFollowerAction>().FirstOrDefault();
            if (wardPlay is not null)
            {
                state = GameEngine.Apply(state, wardPlay);
                continue;
            }
        }

        state = GameEngine.Apply(state, new EndTurnAction());
    }

    if (state.ActivePlayer != caster || state.Players[caster].CurrentPlayPoints < normagdala.Cost ||
        state.Players[opponent].Board.Count == 0)
    {
        throw new InvalidOperationException("The Normagdala test setup did not reach a usable board state.");
    }

    var handCountBeforeFanfare = state.Players[caster].Hand.Count;
    var drawAndHealPlay = GameEngine.GetLegalActions(state)
        .OfType<PlayFollowerAction>()
        .First(action => action.ModeChoiceIndex == 0);
    state = GameEngine.Apply(state, drawAndHealPlay);
    if (state.Players[caster].Hand.Count != handCountBeforeFanfare)
    {
        throw new InvalidOperationException("Normagdala's draw-and-heal mode did not draw one card after it was played.");
    }

    var normagdalaOnBoard = state.Players[caster].Board.Single(follower =>
        follower.InstanceId == drawAndHealPlay.CardInstanceId);
    var defenseBeforeDecrease = state.Players[opponent].Board
        .ToDictionary(follower => follower.InstanceId, follower => (follower.MaxDefense, follower.CurrentDefense));
    var decreaseModeEvolution = GameEngine.GetLegalActions(state).OfType<EvolveAction>()
        .Single(action => action.FollowerInstanceId == normagdalaOnBoard.InstanceId && action.ModeChoiceIndex == 1);
    state = GameEngine.Apply(state, decreaseModeEvolution);

    foreach (var follower in state.Players[opponent].Board)
    {
        var before = defenseBeforeDecrease[follower.InstanceId];
        if (follower.MaxDefense != before.MaxDefense - 4 ||
            follower.CurrentDefense != before.CurrentDefense - 4)
        {
            throw new InvalidOperationException("Normagdala's -0/-4 mode did not reduce both maximum and current defense.");
        }
    }
}

internal static void VerifyDragonOracleEffects()
{
    var dragonOracle = CardCatalog.Get(CardIds.DragonOracle);
    var deck = new DeckDefinition(
        "Dragon Oracle test",
        Enumerable.Repeat(dragonOracle, DeckDefinition.RequiredCardCount));
    const int caster = 0;
    var state = CompleteMulligan(GameEngine.CreateGame(deck, deck, seed: 10_022));

    for (var step = 0; step < 100 &&
         (state.ActivePlayer != caster || state.Players[caster].CurrentPlayPoints < dragonOracle.Cost); step++)
    {
        state = GameEngine.Apply(state, new EndTurnAction());
    }

    var firstOracle = GameEngine.GetLegalActions(state).OfType<PlaySpellAction>().First();
    var firstMaxPp = state.Players[caster].MaxPlayPoints;
    var firstCurrentPp = state.Players[caster].CurrentPlayPoints;
    var firstHandCount = state.Players[caster].Hand.Count;
    state = GameEngine.Apply(state, firstOracle);
    if (state.Players[caster].MaxPlayPoints != firstMaxPp + 1 ||
        state.Players[caster].CurrentPlayPoints != firstCurrentPp - dragonOracle.Cost ||
        state.Players[caster].Hand.Count != firstHandCount - 1)
    {
        throw new InvalidOperationException("Dragon Oracle did not increase only maximum PP by 1 without drawing before 10 PP.");
    }

    for (var step = 0; step < 100 &&
         (state.ActivePlayer != caster || state.Players[caster].MaxPlayPoints < 10); step++)
    {
        state = GameEngine.Apply(state, new EndTurnAction());
    }

    if (state.ActivePlayer != caster || state.Players[caster].MaxPlayPoints != 10)
    {
        throw new InvalidOperationException("The Dragon Oracle test setup did not reach 10 maximum PP.");
    }

    var secondOracle = GameEngine.GetLegalActions(state).OfType<PlaySpellAction>().First();
    var secondHandCount = state.Players[caster].Hand.Count;
    state = GameEngine.Apply(state, secondOracle);
    if (state.Players[caster].MaxPlayPoints != 10 ||
        state.Players[caster].Hand.Count != secondHandCount)
    {
        throw new InvalidOperationException("Dragon Oracle did not draw one card when maximum PP was 10.");
    }
}

internal static void VerifyGoldenSilverThroneRumioreAndAlberteEffects()
{
    var rumioreAndAlberte = CardCatalog.Get(CardIds.GoldenSilverThroneRumioreAndAlberte);
    var bandit = CardCatalog.Get(CardIds.ShatteredBandit);
    const int caster = 0;
    const int opponent = 1;

    var accelerateDeck = new DeckDefinition(
        "Rumiore and Alberte Accelerate test",
        Enumerable.Repeat(rumioreAndAlberte, DeckDefinition.RequiredCardCount));
    var accelerateState = CompleteMulligan(GameEngine.CreateGame(accelerateDeck, accelerateDeck, seed: 10_023));
    for (var step = 0; step < 100 &&
         (accelerateState.ActivePlayer != caster || accelerateState.Players[caster].CurrentPlayPoints < 3); step++)
    {
        accelerateState = GameEngine.Apply(accelerateState, new EndTurnAction());
    }

    var accelerate = GameEngine.GetLegalActions(accelerateState).OfType<PlayAccelerateAction>().First();
    var accelerateMaxPp = accelerateState.Players[caster].MaxPlayPoints;
    var accelerateCurrentPp = accelerateState.Players[caster].CurrentPlayPoints;
    var accelerateGraveyardCount = accelerateState.Players[caster].Graveyard.Count;
    accelerateState = GameEngine.Apply(accelerateState, accelerate);
    if (accelerateState.Players[caster].MaxPlayPoints != accelerateMaxPp + 1 ||
        accelerateState.Players[caster].CurrentPlayPoints != accelerateCurrentPp - 3 ||
        accelerateState.Players[caster].Board.Count != 0 ||
        accelerateState.Players[caster].Graveyard.Count != accelerateGraveyardCount + 1)
    {
        throw new InvalidOperationException("Rumiore and Alberte's Accelerate did not act as a 3 PP spell that only increases maximum PP.");
    }

    var followerDeck = new DeckDefinition(
        "Rumiore and Alberte Fanfare test",
        Enumerable.Repeat(rumioreAndAlberte, DeckDefinition.RequiredCardCount));
    var opponentDeck = new DeckDefinition(
        "Rumiore and Alberte target test",
        Enumerable.Repeat(bandit, DeckDefinition.RequiredCardCount));
    var state = CompleteMulligan(GameEngine.CreateGame(followerDeck, opponentDeck, seed: 10_024));
    for (var step = 0; step < 100 &&
         (state.ActivePlayer != caster || state.Players[caster].CurrentPlayPoints < rumioreAndAlberte.Cost); step++)
    {
        if (state.ActivePlayer == opponent && state.Players[opponent].Board.Count < PlayerState.BoardLimit)
        {
            var banditPlay = GameEngine.GetLegalActions(state).OfType<PlayFollowerAction>().FirstOrDefault();
            if (banditPlay is not null)
            {
                state = GameEngine.Apply(state, banditPlay);
                continue;
            }
        }

        state = GameEngine.Apply(state, new EndTurnAction());
    }

    if (state.ActivePlayer != caster || state.Players[caster].Hand.Count < 3 ||
        state.Players[opponent].Board.Count == 0)
    {
        throw new InvalidOperationException("The Rumiore and Alberte Fanfare test setup did not reach a usable state.");
    }

    if (GameEngine.GetLegalActions(state).OfType<PlayAccelerateAction>().Any())
    {
        throw new InvalidOperationException("Accelerate was incorrectly available when the normal 8 PP card could be played.");
    }

    var fanfarePlay = GameEngine.GetLegalActions(state).OfType<PlayFollowerAction>()
        .First(action => action.OwnHandCardTargetInstanceIds is { Count: 2 });
    var handCountBeforeFanfare = state.Players[caster].Hand.Count;
    var graveyardCountBeforeFanfare = state.Players[caster].Graveyard.Count;
    var opponentHealthBeforeFanfare = state.Players[opponent].Health;
    state = GameEngine.Apply(state, fanfarePlay);
    if (state.Players[caster].Hand.Count != handCountBeforeFanfare - 3 ||
        state.Players[caster].Graveyard.Count != graveyardCountBeforeFanfare + 2 ||
        state.Players[opponent].Board.Count != 0 ||
        state.Players[opponent].Health != opponentHealthBeforeFanfare - 4)
    {
        throw new InvalidOperationException("Rumiore and Alberte's Fanfare did not discard two cards and damage every enemy follower and leader.");
    }

    var superEvolve = GameEngine.GetLegalActions(state).OfType<SuperEvolveAction>()
        .Single(action => action.FollowerInstanceId == fanfarePlay.CardInstanceId);
    var handCountBeforeSuperEvolution = state.Players[caster].Hand.Count;
    state = GameEngine.Apply(state, superEvolve);
    if (state.Players[caster].Hand.Count != handCountBeforeSuperEvolution + 3 ||
        !state.Players[caster].Board.Single(follower => follower.InstanceId == fanfarePlay.CardInstanceId).IsSuperEvolved)
    {
        throw new InvalidOperationException("Rumiore and Alberte's super evolution did not draw three cards.");
    }

    var shortageDeck = new DeckDefinition(
        "Rumiore and Alberte insufficient-hand test",
        Enumerable.Repeat(rumioreAndAlberte, DeckDefinition.RequiredCardCount));
    var shortageState = CompleteMulligan(GameEngine.CreateGame(shortageDeck, shortageDeck, seed: 10_025));
    for (var step = 0; step < 100 &&
         (shortageState.ActivePlayer != caster || shortageState.Players[caster].CurrentPlayPoints < rumioreAndAlberte.Cost); step++)
    {
        shortageState = GameEngine.Apply(shortageState, new EndTurnAction());
    }

    for (var playCount = 0; playCount < PlayerState.BoardLimit; playCount++)
    {
        var handBeforePlay = shortageState.Players[caster].Hand.Count;
        var expectedDiscardCount = Math.Min(2, handBeforePlay - 1);
        var leaderHealthBeforePlay = shortageState.Players[opponent].Health;
        var play = GameEngine.GetLegalActions(shortageState).OfType<PlayFollowerAction>()
            .First(action => action.OwnHandCardTargetInstanceIds?.Count == expectedDiscardCount);
        shortageState = GameEngine.Apply(shortageState, play);

        if (shortageState.Players[caster].Hand.Count != handBeforePlay - 1 - expectedDiscardCount ||
            shortageState.Players[opponent].Health != leaderHealthBeforePlay - 4)
        {
            throw new InvalidOperationException("Rumiore and Alberte did not discard every remaining hand card when fewer than two were available.");
        }

        if (playCount == PlayerState.BoardLimit - 1)
        {
            if (expectedDiscardCount != 0 || !shortageState.IsGameOver)
            {
                throw new InvalidOperationException("Rumiore and Alberte could not resolve normally with no other hand cards to discard.");
            }

            break;
        }

        shortageState = GameEngine.Apply(shortageState, new EndTurnAction());
        shortageState = GameEngine.Apply(shortageState, new EndTurnAction());
    }
}

internal static void VerifyMasterOfSkyFateLuriaEffects()
{
    var luria = CardCatalog.Get(CardIds.MasterOfSkyFateLuria);
    var fang = CardCatalog.Get(CardIds.Fang);
    var antimaria = CardCatalog.Get(CardIds.PiercingSinnerAntimaria);
    var goliath = CardCatalog.Get(CardIds.Goliath);
    const int caster = 0;
    const int opponent = 1;

    var barrierDeck = new DeckDefinition(
        "Luria Barrier test",
        Enumerable.Repeat(luria, DeckDefinition.RequiredCardCount));
    var fangDeck = new DeckDefinition(
        "Luria Barrier damage test",
        Enumerable.Repeat(fang, DeckDefinition.RequiredCardCount));
    var barrierState = CompleteMulligan(GameEngine.CreateGame(barrierDeck, fangDeck, seed: 10_024));
    barrierState = AdvanceUntilActivePlayerHasPp(barrierState, caster, luria.Cost);
    var playLuria = GameEngine.GetLegalActions(barrierState).OfType<PlayFollowerAction>().First();
    barrierState = GameEngine.Apply(barrierState, playLuria);

    barrierState = AdvanceUntilActivePlayerHasPp(barrierState, opponent, fang.Cost);
    var firstFang = GameEngine.GetLegalActions(barrierState).OfType<PlaySpellAction>().First();
    barrierState = GameEngine.Apply(barrierState, firstFang);
    var protectedLuria = barrierState.Players[caster].Board.Single(follower => follower.InstanceId == playLuria.CardInstanceId);
    if (protectedLuria.CurrentDefense != 1 || protectedLuria.HasBarrier)
    {
        throw new InvalidOperationException("Luria's Barrier did not prevent exactly one instance of damage and then disappear.");
    }

    barrierState = AdvanceUntilActivePlayerHasPp(barrierState, opponent, fang.Cost);
    var secondFang = GameEngine.GetLegalActions(barrierState).OfType<PlaySpellAction>().First();
    barrierState = GameEngine.Apply(barrierState, secondFang);
    if (barrierState.Players[caster].Board.Any(follower => follower.InstanceId == playLuria.CardInstanceId))
    {
        throw new InvalidOperationException("Luria survived damage after her one-use Barrier had already been consumed.");
    }

    var enhanceDeck = new DeckDefinition(
        "Luria Enhance test",
        [
            .. Enumerable.Repeat(luria, 20),
            .. Enumerable.Repeat(antimaria, 20)
        ]);
    var passiveOpponentDeck = new DeckDefinition(
        "Luria Enhance opponent test",
        Enumerable.Repeat(luria, DeckDefinition.RequiredCardCount));
    var enhanceState = CompleteMulligan(GameEngine.CreateGame(enhanceDeck, passiveOpponentDeck, seed: 10_025));
    enhanceState = AdvanceUntilActivePlayerHasPp(enhanceState, caster, 8);
    var enhancePlay = GameEngine.GetLegalActions(enhanceState).OfType<PlayFollowerAction>()
        .First(action => enhanceState.Players[caster].Hand
            .Single(card => card.InstanceId == action.CardInstanceId)
            .Definition.Id == luria.Id);
    var handIdsBeforeEnhance = enhanceState.Players[caster].Hand.Select(card => card.InstanceId).ToHashSet();
    var ppBeforeEnhance = enhanceState.Players[caster].CurrentPlayPoints;
    enhanceState = GameEngine.Apply(enhanceState, enhancePlay);
    var searchedCard = enhanceState.Players[caster].Hand.Single(card => !handIdsBeforeEnhance.Contains(card.InstanceId));
    if (searchedCard.Definition.Type != CardType.Follower || searchedCard.Definition.Cost < 7 ||
        enhanceState.Players[caster].CurrentPlayPoints != Math.Min(
            enhanceState.Players[caster].MaxPlayPoints,
            ppBeforeEnhance - 8 + 7) ||
        !enhanceState.Players[caster].Board.Single(follower => follower.InstanceId == enhancePlay.CardInstanceId).HasBarrier)
    {
        throw new InvalidOperationException("Luria's 8 PP Enhance did not search a 7+ follower, restore current PP, and enter with Barrier.");
    }

    var superBarrierDeck = new DeckDefinition(
        "Luria super-evolution Barrier test",
        Enumerable.Repeat(luria, DeckDefinition.RequiredCardCount));
    var goliathDeck = new DeckDefinition(
        "Luria super-evolution attacker test",
        Enumerable.Repeat(goliath, DeckDefinition.RequiredCardCount));
    var superBarrierState = CompleteMulligan(GameEngine.CreateGame(superBarrierDeck, goliathDeck, seed: 10_241));
    superBarrierState = AdvanceUntilActivePlayerHasPp(superBarrierState, caster, luria.Cost);
    var superBarrierPlay = GameEngine.GetLegalActions(superBarrierState).OfType<PlayFollowerAction>().First();
    superBarrierState = GameEngine.Apply(superBarrierState, superBarrierPlay);
    superBarrierState = AdvanceUntilActivePlayerHasPp(superBarrierState, opponent, goliath.Cost);
    var goliathPlay = GameEngine.GetLegalActions(superBarrierState).OfType<PlayFollowerAction>().First();
    superBarrierState = GameEngine.Apply(superBarrierState, goliathPlay);
    superBarrierState = AdvanceUntilActivePlayerHasPp(superBarrierState, caster, 7);
    superBarrierState = GameEngine.Apply(
        superBarrierState,
        GameEngine.GetLegalActions(superBarrierState).OfType<SuperEvolveAction>()
            .Single(action => action.FollowerInstanceId == superBarrierPlay.CardInstanceId));
    var superLuriaBeforeCombat = superBarrierState.Players[caster].Board
        .Single(follower => follower.InstanceId == superBarrierPlay.CardInstanceId);
    var defenseBeforeCombat = superLuriaBeforeCombat.CurrentDefense;
    superBarrierState = GameEngine.Apply(
        superBarrierState,
        new AttackFollowerAction(superBarrierPlay.CardInstanceId, goliathPlay.CardInstanceId));
    var superLuriaAfterCombat = superBarrierState.Players[caster].Board
        .Single(follower => follower.InstanceId == superBarrierPlay.CardInstanceId);
    if (superLuriaAfterCombat.HasBarrier || superLuriaAfterCombat.CurrentDefense != defenseBeforeCombat)
    {
        throw new InvalidOperationException("A zero-damage collision with a super-evolved follower did not consume Barrier correctly.");
    }
}

internal static void VerifyShangfengjinRedCurrentAndAuraEffects()
{
    var shangfengjin = CardCatalog.Get(CardIds.HeadchoppingExecutionerShangfengjin);
    var redCurrent = CardCatalog.Get(CardIds.RedCurrent);
    var goliath = CardCatalog.Get(CardIds.Goliath);
    var waveflower = CardCatalog.Get(CardIds.LazyWaveflower);
    const int caster = 0;
    const int opponent = 1;

    var shangfengjinDeck = new DeckDefinition(
        "Shangfengjin Fanfare test",
        [
            .. Enumerable.Repeat(shangfengjin, 20),
            .. Enumerable.Repeat(goliath, 20)
        ]);
    var fanfareState = FindDiscardState(
        shangfengjinDeck,
        CardIds.HeadchoppingExecutionerShangfengjin,
        CardIds.Goliath,
        shangfengjin.Cost,
        seedStart: 10_300);
    var fanfareHandCountBefore = fanfareState.Players[caster].Hand.Count;
    var shangfengjinPlay = GameEngine.GetLegalActions(fanfareState).OfType<PlayFollowerAction>()
        .First(action =>
            fanfareState.Players[caster].Hand.Single(card => card.InstanceId == action.CardInstanceId).Definition.Id == shangfengjin.Id &&
            action.OwnHandCardTargetInstanceIds?.Any(targetId =>
                fanfareState.Players[caster].Hand.Single(card => card.InstanceId == targetId).Definition.Id == goliath.Id) == true);
    fanfareState = GameEngine.Apply(fanfareState, shangfengjinPlay);
    var shangfengjinOnBoard = fanfareState.Players[caster].Board
        .Single(follower => follower.InstanceId == shangfengjinPlay.CardInstanceId);
    if (!shangfengjinOnBoard.HasStorm || !shangfengjinOnBoard.HasBane || !shangfengjinOnBoard.HasAura ||
        fanfareState.Players[caster].Hand.Count != fanfareHandCountBefore ||
        fanfareState.Players[caster].Hand.Count(card => card.Definition.Id == redCurrent.Id) != 2)
    {
        throw new InvalidOperationException("Shangfengjin did not discard up to one other card, gain its keywords, and add two Red Currents.");
    }

    var redCurrentDeck = new DeckDefinition(
        "Red Current test",
        [
            .. Enumerable.Repeat(redCurrent, 20),
            .. Enumerable.Repeat(goliath, 20)
        ]);
    var goliathDeck = new DeckDefinition(
        "Red Current target test",
        Enumerable.Repeat(goliath, DeckDefinition.RequiredCardCount));
    GameState? destroyState = null;
    PlaySpellAction? redCurrentPlay = null;
    int discardedCardInstanceId = 0;
    for (var seed = 10_400UL; seed < 10_600UL && destroyState is null; seed++)
    {
        var candidateState = CompleteMulligan(GameEngine.CreateGame(redCurrentDeck, goliathDeck, seed));
        candidateState = AdvanceUntilActivePlayerHasPp(candidateState, opponent, goliath.Cost);
        var targetPlay = GameEngine.GetLegalActions(candidateState).OfType<PlayFollowerAction>().First();
        candidateState = GameEngine.Apply(candidateState, targetPlay);
        candidateState = AdvanceUntilActivePlayerHasPp(candidateState, caster, redCurrent.Cost);
        var candidateAction = GameEngine.GetLegalActions(candidateState).OfType<PlaySpellAction>()
            .FirstOrDefault(action =>
                candidateState.Players[caster].Hand.Single(card => card.InstanceId == action.CardInstanceId).Definition.Id == redCurrent.Id &&
                action.Target is EnemyFollowerTarget target && target.FollowerInstanceId == targetPlay.CardInstanceId &&
                action.OwnHandCardTargetInstanceIds?.Any(targetId =>
                    candidateState.Players[caster].Hand.Single(card => card.InstanceId == targetId).Definition.Id == goliath.Id) == true);
        if (candidateAction is null)
        {
            continue;
        }

        destroyState = candidateState;
        redCurrentPlay = candidateAction;
        discardedCardInstanceId = candidateAction.OwnHandCardTargetInstanceIds!.Single(targetId =>
            candidateState.Players[caster].Hand.Single(card => card.InstanceId == targetId).Definition.Id == goliath.Id);
    }

    if (destroyState is null || redCurrentPlay is null)
    {
        throw new InvalidOperationException("The Red Current test setup could not draw both the spell and a discard target.");
    }

    destroyState = GameEngine.Apply(destroyState, redCurrentPlay);
    if (destroyState.Players[opponent].Board.Count != 0 ||
        !destroyState.Players[caster].Graveyard.Any(card => card.InstanceId == discardedCardInstanceId))
    {
        throw new InvalidOperationException("Red Current did not discard its selected hand card and destroy its selected enemy follower.");
    }

    var auraDeck = new DeckDefinition(
        "Aura target test",
        Enumerable.Repeat(shangfengjin, DeckDefinition.RequiredCardCount));
    GameState? auraTargetState = null;
    for (var seed = 10_600UL; seed < 10_800UL && auraTargetState is null; seed++)
    {
        var candidateState = CompleteMulligan(GameEngine.CreateGame(redCurrentDeck, auraDeck, seed));
        candidateState = AdvanceUntilActivePlayerHasPp(candidateState, opponent, shangfengjin.Cost);
        candidateState = GameEngine.Apply(candidateState, GameEngine.GetLegalActions(candidateState).OfType<PlayFollowerAction>().First());
        candidateState = AdvanceUntilActivePlayerHasPp(candidateState, caster, redCurrent.Cost);
        if (candidateState.Players[caster].Hand.Any(card => card.Definition.Id == redCurrent.Id) &&
            candidateState.Players[caster].Hand.Count >= 2)
        {
            auraTargetState = candidateState;
        }
    }

    if (auraTargetState is null || !auraTargetState.Players[opponent].Board.Single().HasAura ||
        GameEngine.GetLegalActions(auraTargetState).OfType<PlaySpellAction>().Any(action =>
            auraTargetState.Players[caster].Hand.Single(card => card.InstanceId == action.CardInstanceId).Definition.Id == redCurrent.Id))
    {
        throw new InvalidOperationException("Aura did not prevent Red Current from selecting its only enemy follower.");
    }

    var waveflowerDeck = new DeckDefinition(
        "Aura random-damage test",
        Enumerable.Repeat(waveflower, DeckDefinition.RequiredCardCount));
    var randomDamageState = CompleteMulligan(GameEngine.CreateGame(waveflowerDeck, auraDeck, seed: 10_800));
    randomDamageState = AdvanceUntilActivePlayerHasPp(randomDamageState, opponent, shangfengjin.Cost);
    randomDamageState = GameEngine.Apply(randomDamageState, GameEngine.GetLegalActions(randomDamageState).OfType<PlayFollowerAction>().First());
    randomDamageState = AdvanceUntilActivePlayerHasPp(randomDamageState, caster, waveflower.Cost);
    randomDamageState = GameEngine.Apply(randomDamageState, GameEngine.GetLegalActions(randomDamageState).OfType<PlaySpellAction>().First());
    if (randomDamageState.Players[opponent].Board.Count != 0)
    {
        throw new InvalidOperationException("Aura incorrectly prevented random damage from affecting the follower.");
    }
}

internal static void VerifySolarFlareRoarAndIlantzaEffects()
{
    var solarFlareRoar = CardCatalog.Get(CardIds.SolarFlareRoar);
    var ilantza = CardCatalog.Get(CardIds.BoundJusticeIlantza);
    var gladiator = CardCatalog.Get(CardIds.Gladiator);
    var olivia = CardCatalog.Get(CardIds.ValiantFallenAngelOlivia);
    const int caster = 0;
    const int opponent = 1;

    var solarDeck = new DeckDefinition(
        "Solar Flare Roar test",
        [
            .. Enumerable.Repeat(solarFlareRoar, 20),
            .. Enumerable.Repeat(gladiator, 20)
        ]);
    var gladiatorDeck = new DeckDefinition(
        "Solar Flare Roar follower test",
        Enumerable.Repeat(gladiator, DeckDefinition.RequiredCardCount));
    GameState? solarState = null;
    PlaySpellAction? solarPlay = null;
    for (var seed = 10_900UL; seed < 11_100UL && solarState is null; seed++)
    {
        var candidateState = CompleteMulligan(GameEngine.CreateGame(solarDeck, gladiatorDeck, seed));
        candidateState = AdvanceUntilActivePlayerHasPp(candidateState, caster, gladiator.Cost);
        var ownGladiatorPlay = GameEngine.GetLegalActions(candidateState).OfType<PlayFollowerAction>()
            .FirstOrDefault(action => candidateState.Players[caster].Hand
                .Single(card => card.InstanceId == action.CardInstanceId).Definition.Id == gladiator.Id);
        if (ownGladiatorPlay is null)
        {
            continue;
        }

        candidateState = GameEngine.Apply(candidateState, ownGladiatorPlay);
        candidateState = AdvanceUntilActivePlayerHasPp(candidateState, opponent, gladiator.Cost);
        candidateState = GameEngine.Apply(candidateState, GameEngine.GetLegalActions(candidateState).OfType<PlayFollowerAction>().First());
        candidateState = AdvanceUntilActivePlayerHasPp(candidateState, caster, solarFlareRoar.Cost);
        var candidateSolarPlay = GameEngine.GetLegalActions(candidateState).OfType<PlaySpellAction>()
            .FirstOrDefault(action => candidateState.Players[caster].Hand
                .Single(card => card.InstanceId == action.CardInstanceId).Definition.Id == solarFlareRoar.Id);
        if (candidateSolarPlay is not null)
        {
            solarState = candidateState;
            solarPlay = candidateSolarPlay;
        }
    }

    if (solarState is null || solarPlay is null)
    {
        throw new InvalidOperationException("The Solar Flare Roar test setup could not draw the spell and a friendly follower.");
    }

    solarState = GameEngine.Apply(solarState, solarPlay);
    if (solarState.Players[caster].Board.Count != 0 || solarState.Players[opponent].Board.Count != 0)
    {
        throw new InvalidOperationException("Solar Flare Roar did not count both sides' followers and deal that damage to every follower.");
    }

    var ilantzaDeck = new DeckDefinition(
        "Ilantza unevolved test",
        Enumerable.Repeat(ilantza, DeckDefinition.RequiredCardCount));
    var unevolvedState = CompleteMulligan(GameEngine.CreateGame(ilantzaDeck, gladiatorDeck, seed: 11_100));
    unevolvedState = AdvanceUntilActivePlayerHasPp(unevolvedState, opponent, gladiator.Cost);
    unevolvedState = GameEngine.Apply(unevolvedState, GameEngine.GetLegalActions(unevolvedState).OfType<PlayFollowerAction>().First());
    unevolvedState = AdvanceUntilActivePlayerHasPp(unevolvedState, opponent, gladiator.Cost);
    unevolvedState = GameEngine.Apply(unevolvedState, GameEngine.GetLegalActions(unevolvedState).OfType<PlayFollowerAction>().First());
    unevolvedState = AdvanceUntilActivePlayerHasPp(unevolvedState, caster, ilantza.Cost);
    unevolvedState = GameEngine.Apply(unevolvedState, GameEngine.GetLegalActions(unevolvedState).OfType<PlayFollowerAction>().First());
    unevolvedState = GameEngine.Apply(unevolvedState, new EndTurnAction());
    if (unevolvedState.Players[opponent].Board.Count != 0)
    {
        throw new InvalidOperationException("Unevolved Ilantza did not deal 8 random damage to up to two enemy followers at the end of its owner's turn.");
    }

    var manualEvolutionState = CompleteMulligan(GameEngine.CreateGame(ilantzaDeck, gladiatorDeck, seed: 11_101));
    manualEvolutionState = AdvanceUntilActivePlayerHasPp(manualEvolutionState, caster, ilantza.Cost);
    var ilantzaPlay = GameEngine.GetLegalActions(manualEvolutionState).OfType<PlayFollowerAction>().First();
    manualEvolutionState = GameEngine.Apply(manualEvolutionState, ilantzaPlay);
    manualEvolutionState = GameEngine.Apply(
        manualEvolutionState,
        GameEngine.GetLegalActions(manualEvolutionState).OfType<EvolveAction>()
            .Single(action => action.FollowerInstanceId == ilantzaPlay.CardInstanceId));
    var manuallyEvolvedIlantza = manualEvolutionState.Players[caster].Board
        .Single(follower => follower.InstanceId == ilantzaPlay.CardInstanceId);
    var enemyHealthBeforeManualEndTurn = manualEvolutionState.Players[opponent].Health;
    if (manuallyEvolvedIlantza.HasWard || !manuallyEvolvedIlantza.HasIntimidate)
    {
        throw new InvalidOperationException("Manually evolving Ilantza did not remove Ward and grant Intimidate.");
    }

    manualEvolutionState = GameEngine.Apply(manualEvolutionState, new EndTurnAction());
    if (manualEvolutionState.Players[opponent].Health != enemyHealthBeforeManualEndTurn - 8)
    {
        throw new InvalidOperationException("Evolved Ilantza did not deal 8 damage to the enemy leader at the end of its owner's turn.");
    }

    var oliviaIlantzaDeck = new DeckDefinition(
        "Olivia ability-evolution Ilantza test",
        [
            .. Enumerable.Repeat(olivia, 20),
            .. Enumerable.Repeat(ilantza, 20)
        ]);
    GameState? abilityEvolutionState = null;
    PlayFollowerAction? abilityIlantzaPlay = null;
    SuperEvolveAction? oliviaSuperEvolve = null;
    for (var seed = 11_200UL; seed < 11_400UL && abilityEvolutionState is null; seed++)
    {
        var candidateState = CompleteMulligan(GameEngine.CreateGame(oliviaIlantzaDeck, gladiatorDeck, seed));
        candidateState = AdvanceUntilActivePlayerHasPp(candidateState, caster, olivia.Cost);
        var oliviaPlay = GameEngine.GetLegalActions(candidateState).OfType<PlayFollowerAction>()
            .FirstOrDefault(action => candidateState.Players[caster].Hand
                .Single(card => card.InstanceId == action.CardInstanceId).Definition.Id == olivia.Id);
        if (oliviaPlay is null)
        {
            continue;
        }

        candidateState = GameEngine.Apply(candidateState, oliviaPlay);
        candidateState = AdvanceUntilActivePlayerHasPp(candidateState, caster, ilantza.Cost);
        var candidateIlantzaPlay = GameEngine.GetLegalActions(candidateState).OfType<PlayFollowerAction>()
            .FirstOrDefault(action => candidateState.Players[caster].Hand
                .Single(card => card.InstanceId == action.CardInstanceId).Definition.Id == ilantza.Id);
        if (candidateIlantzaPlay is null)
        {
            continue;
        }

        candidateState = GameEngine.Apply(candidateState, candidateIlantzaPlay);
        var candidateSuperEvolve = GameEngine.GetLegalActions(candidateState).OfType<SuperEvolveAction>()
            .FirstOrDefault(action => action.FollowerInstanceId == oliviaPlay.CardInstanceId &&
                                      action.OtherFollowerTargetInstanceId == candidateIlantzaPlay.CardInstanceId);
        if (candidateSuperEvolve is not null)
        {
            abilityEvolutionState = candidateState;
            abilityIlantzaPlay = candidateIlantzaPlay;
            oliviaSuperEvolve = candidateSuperEvolve;
        }
    }

    if (abilityEvolutionState is null || abilityIlantzaPlay is null || oliviaSuperEvolve is null)
    {
        throw new InvalidOperationException("The Olivia ability-evolution test setup could not prepare both followers.");
    }

    abilityEvolutionState = GameEngine.Apply(abilityEvolutionState, oliviaSuperEvolve);
    var abilitySuperEvolvedIlantza = abilityEvolutionState.Players[caster].Board
        .Single(follower => follower.InstanceId == abilityIlantzaPlay.CardInstanceId);
    var enemyHealthBeforeAbilityEndTurn = abilityEvolutionState.Players[opponent].Health;
    if (abilitySuperEvolvedIlantza.EvolutionState != EvolutionState.SuperEvolved ||
        !abilitySuperEvolvedIlantza.HasWard || abilitySuperEvolvedIlantza.HasIntimidate)
    {
        throw new InvalidOperationException("Olivia's ability-based super evolution incorrectly triggered Ilantza's evolution effect.");
    }

    abilityEvolutionState = GameEngine.Apply(abilityEvolutionState, new EndTurnAction());
    if (abilityEvolutionState.Players[opponent].Health != enemyHealthBeforeAbilityEndTurn - 8)
    {
        throw new InvalidOperationException("Ability-super-evolved Ilantza was not treated as evolved for its end-of-turn leader damage.");
    }
}

internal static void VerifyNewDragonAndGeneratedCardEffects()
{
    VerifyPresentationOfTheWorldEffects();
    VerifyAncientHeavenbladePolalaiEffects();
    VerifyHeavenbladeAbyssAndKimikaEffects();
    VerifyHeraldingDragonewtEnhance();
}

internal static void VerifyPresentationOfTheWorldEffects()
{
    var presentation = CardCatalog.Get(CardIds.PresentationOfTheWorld);
    var goliath = CardCatalog.Get(CardIds.Goliath);
    const int caster = 0;
    const int opponent = 1;

    var destroyDeck = new DeckDefinition(
        "Presentation normal-effect test",
        Enumerable.Repeat(presentation, DeckDefinition.RequiredCardCount));
    var targetDeck = new DeckDefinition(
        "Presentation target test",
        Enumerable.Repeat(goliath, DeckDefinition.RequiredCardCount));
    var destroyState = CompleteMulligan(GameEngine.CreateGame(destroyDeck, targetDeck, seed: 10_026));
    destroyState = AdvanceUntilActivePlayerHasPp(destroyState, opponent, goliath.Cost);
    destroyState = GameEngine.Apply(destroyState, GameEngine.GetLegalActions(destroyState).OfType<PlayFollowerAction>().First());
    destroyState = AdvanceUntilActivePlayerHasPp(destroyState, caster, presentation.Cost);
    var normalPlay = GameEngine.GetLegalActions(destroyState).OfType<PlaySpellAction>().First();
    var handBeforeNormalPlay = destroyState.Players[caster].Hand.Count;
    destroyState = GameEngine.Apply(destroyState, normalPlay);
    if (destroyState.Players[caster].Hand.Count != Math.Min(PlayerState.HandLimit, handBeforeNormalPlay + 1) ||
        destroyState.Players[opponent].Board.Count != 0)
    {
        throw new InvalidOperationException("Presentation of the World did not draw as many of two cards as the hand limit allowed and destroy the highest-attack enemy follower.");
    }

    var enhanceState = CompleteMulligan(GameEngine.CreateGame(destroyDeck, targetDeck, seed: 10_027));
    enhanceState = AdvanceUntilActivePlayerHasPp(enhanceState, opponent, goliath.Cost);
    enhanceState = GameEngine.Apply(enhanceState, GameEngine.GetLegalActions(enhanceState).OfType<PlayFollowerAction>().First());
    enhanceState = GameEngine.Apply(enhanceState, new EndTurnAction());
    enhanceState = AdvanceUntilActivePlayerHasPp(enhanceState, opponent, goliath.Cost);
    enhanceState = GameEngine.Apply(enhanceState, GameEngine.GetLegalActions(enhanceState).OfType<PlayFollowerAction>().First());
    enhanceState = AdvanceUntilActivePlayerHasPp(enhanceState, caster, 10);
    var enemyHealthBeforeEnhance = enhanceState.Players[opponent].Health;
    var enhancePlay = GameEngine.GetLegalActions(enhanceState).OfType<PlaySpellAction>().First();
    enhanceState = GameEngine.Apply(enhanceState, enhancePlay);
    if (enhanceState.Players[opponent].Health != enemyHealthBeforeEnhance - 4 ||
        enhanceState.Players[opponent].Board.Count != 1 ||
        enhanceState.Players[opponent].Board.Single().CurrentDefense != 1)
    {
        throw new InvalidOperationException("Presentation of the World's 10 PP Enhance did not destroy one target and deal 4 damage to all remaining enemies and their leader.");
    }
}

internal static void VerifyAncientHeavenbladePolalaiEffects()
{
    var polalai = CardCatalog.Get(CardIds.AncientHeavenbladePolalai);
    var luria = CardCatalog.Get(CardIds.MasterOfSkyFateLuria);
    const int caster = 0;
    const int opponent = 1;

    var baneDeck = new DeckDefinition("Polalai Bane test", Enumerable.Repeat(polalai, DeckDefinition.RequiredCardCount));
    var barrierDeck = new DeckDefinition("Polalai Barrier test", Enumerable.Repeat(luria, DeckDefinition.RequiredCardCount));
    var baneState = CompleteMulligan(GameEngine.CreateGame(baneDeck, barrierDeck, seed: 10_028));
    baneState = AdvanceUntilActivePlayerHasPp(baneState, caster, polalai.Cost);
    var polalaiPlay = GameEngine.GetLegalActions(baneState).OfType<PlayFollowerAction>().First();
    baneState = GameEngine.Apply(baneState, polalaiPlay);
    baneState = AdvanceUntilActivePlayerHasPp(baneState, opponent, luria.Cost);
    var luriaPlay = GameEngine.GetLegalActions(baneState).OfType<PlayFollowerAction>().First();
    baneState = GameEngine.Apply(baneState, luriaPlay);
    baneState = AdvanceUntilActivePlayerHasPp(baneState, caster, 3);
    baneState = GameEngine.Apply(baneState, new AttackFollowerAction(polalaiPlay.CardInstanceId, luriaPlay.CardInstanceId));
    if (baneState.Players[opponent].Board.Any(follower => follower.InstanceId == luriaPlay.CardInstanceId))
    {
        throw new InvalidOperationException("Polalai's Bane did not destroy a Barrier follower after combat damage was reduced to zero.");
    }

    var evolutionState = CompleteMulligan(GameEngine.CreateGame(baneDeck, baneDeck, seed: 10_029));
    evolutionState = AdvanceUntilActivePlayerHasPp(evolutionState, caster, polalai.Cost);
    var evolutionPlay = GameEngine.GetLegalActions(evolutionState).OfType<PlayFollowerAction>().First();
    evolutionState = GameEngine.Apply(evolutionState, evolutionPlay);
    evolutionState = AdvanceUntilActivePlayerHasPp(evolutionState, caster, 5);
    var handBeforeEvolution = evolutionState.Players[caster].Hand.Count;
    evolutionState = GameEngine.Apply(
        evolutionState,
        GameEngine.GetLegalActions(evolutionState).OfType<EvolveAction>().Single(action => action.FollowerInstanceId == evolutionPlay.CardInstanceId));
    if (evolutionState.Players[caster].Hand.Count != handBeforeEvolution + 1 ||
        evolutionState.Players[caster].Hand.Count(card => card.Definition.Id == CardIds.HeavenbladeAbyss) != 1)
    {
        throw new InvalidOperationException("Polalai's evolution did not add exactly one Heavenblade Abyss to hand.");
    }

    var superState = CompleteMulligan(GameEngine.CreateGame(baneDeck, baneDeck, seed: 10_030));
    superState = AdvanceUntilActivePlayerHasPp(superState, caster, polalai.Cost);
    var superPlay = GameEngine.GetLegalActions(superState).OfType<PlayFollowerAction>().First();
    superState = GameEngine.Apply(superState, superPlay);
    superState = AdvanceUntilActivePlayerHasPp(superState, caster, 7);
    // Waiting to turn 7 fills the hand in an all-Polalai test deck. Play three more
    // low-cost copies first so all three generated cards have space to enter the hand.
    for (var copy = 0; copy < 3; copy++)
    {
        var extraPolalai = GameEngine.GetLegalActions(superState).OfType<PlayFollowerAction>()
            .First(action => action.CardInstanceId != superPlay.CardInstanceId);
        superState = GameEngine.Apply(superState, extraPolalai);
    }
    var handBeforeSuperEvolution = superState.Players[caster].Hand.Count;
    superState = GameEngine.Apply(
        superState,
        GameEngine.GetLegalActions(superState).OfType<SuperEvolveAction>().Single(action => action.FollowerInstanceId == superPlay.CardInstanceId));
    if (superState.Players[caster].Hand.Count != handBeforeSuperEvolution + 3 ||
        superState.Players[caster].Hand.Count(card => card.Definition.Id == CardIds.HeavenbladeAbyss) != 3)
    {
        throw new InvalidOperationException("Polalai's super evolution did not replace the normal evolution result with exactly three Heavenblade Abyss cards.");
    }

    var discardDeck = new DeckDefinition(
        "Polalai discard test",
        [.. Enumerable.Repeat(CardCatalog.Get(CardIds.JawsParting), 20), .. Enumerable.Repeat(polalai, 20)]);
    var discardState = FindDiscardState(discardDeck, CardIds.JawsParting, CardIds.AncientHeavenbladePolalai, 3, seedStart: 10_100);
    var discardAction = GameEngine.GetLegalActions(discardState).OfType<PlaySpellAction>()
        .First(action => discardState.Players[caster].Hand.Single(card => card.InstanceId == action.CardInstanceId).Definition.Id == CardIds.JawsParting &&
                         action.OwnHandCardTargetInstanceIds!.Any(targetId => discardState.Players[caster].Hand.Single(card => card.InstanceId == targetId).Definition.Id == CardIds.AncientHeavenbladePolalai));
    discardState = GameEngine.Apply(discardState, discardAction);
    if (discardState.Players[caster].Board.Count != 1 ||
        discardState.Players[caster].Board.Single().Definition.Id != CardIds.AncientHeavenbladePolalai)
    {
        throw new InvalidOperationException("Discarding Polalai did not summon a fresh Polalai copy.");
    }
}

internal static void VerifyHeavenbladeAbyssAndKimikaEffects()
{
    var abyss = CardCatalog.Get(CardIds.HeavenbladeAbyss);
    var kimika = CardCatalog.Get(CardIds.SmilingChefKimika);
    const int caster = 0;
    const int opponent = 1;

    var spellDeck = new DeckDefinition("Heavenblade Abyss cast test", Enumerable.Repeat(abyss, DeckDefinition.RequiredCardCount));
    var spellState = CompleteMulligan(GameEngine.CreateGame(spellDeck, spellDeck, seed: 10_031));
    spellState = AdvanceUntilActivePlayerHasPp(spellState, opponent, abyss.Cost);
    spellState = GameEngine.Apply(spellState, GameEngine.GetLegalActions(spellState).OfType<PlaySpellAction>().First());
    spellState = AdvanceUntilActivePlayerHasPp(spellState, caster, abyss.Cost);
    var casterHealthBefore = spellState.Players[caster].Health;
    var opponentHealthBefore = spellState.Players[opponent].Health;
    spellState = GameEngine.Apply(spellState, GameEngine.GetLegalActions(spellState).OfType<PlaySpellAction>().First());
    if (spellState.Players[caster].Health != casterHealthBefore + 1 ||
        spellState.Players[opponent].Health != opponentHealthBefore - 1)
    {
        throw new InvalidOperationException("Casting Heavenblade Abyss did not deal one damage and restore one health.");
    }

    var kimikaDeck = new DeckDefinition(
        "Kimika discard test",
        [.. Enumerable.Repeat(kimika, 20), .. Enumerable.Repeat(abyss, 20)]);
    var kimikaState = FindDiscardState(kimikaDeck, CardIds.SmilingChefKimika, CardIds.HeavenbladeAbyss, 2, seedStart: 10_200);
    var kimikaPlay = GameEngine.GetLegalActions(kimikaState).OfType<PlayFollowerAction>()
        .First(action => kimikaState.Players[caster].Hand.Single(card => card.InstanceId == action.CardInstanceId).Definition.Id == CardIds.SmilingChefKimika &&
                         action.OwnHandCardTargetInstanceIds!.Any(targetId => kimikaState.Players[caster].Hand.Single(card => card.InstanceId == targetId).Definition.Id == CardIds.HeavenbladeAbyss));
    var handBeforeKimika = kimikaState.Players[caster].Hand.Count;
    var enemyHealthBeforeKimika = kimikaState.Players[opponent].Health;
    kimikaState = GameEngine.Apply(kimikaState, kimikaPlay);
    if (kimikaState.Players[caster].Hand.Count != handBeforeKimika - 1 ||
        kimikaState.Players[opponent].Health != enemyHealthBeforeKimika - 1 ||
        kimikaState.Players[caster].Board.Single().Definition.Id != CardIds.SmilingChefKimika)
    {
        throw new InvalidOperationException("Kimika's Fanfare did not discard a card, draw one, heal, and resolve the discarded Heavenblade Abyss trigger.");
    }
}

internal static void VerifyHeraldingDragonewtEnhance()
{
    var dragonewt = CardCatalog.Get(CardIds.HeraldingDragonewt);
    const int caster = 0;
    var deck = new DeckDefinition("Heralding Dragonewt Enhance test", Enumerable.Repeat(dragonewt, DeckDefinition.RequiredCardCount));
    var state = CompleteMulligan(GameEngine.CreateGame(deck, deck, seed: 10_032));
    state = AdvanceUntilActivePlayerHasPp(state, caster, 4);
    var play = GameEngine.GetLegalActions(state).OfType<PlayFollowerAction>().First();
    state = GameEngine.Apply(state, play);
    if (state.Players[caster].Board.Count != 3 ||
        state.Players[caster].Board.Any(follower => follower.Definition.Id != CardIds.HeraldingDragonewt))
    {
        throw new InvalidOperationException("Heralding Dragonewt's 4 PP Enhance did not summon two additional copies.");
    }
}

internal static void VerifyAwakeningAndAmuletEffects()
{
    VerifyLazyWaveflowerEffects();
    VerifyDarkDimensionEffects();
}

internal static void VerifyLeaderEffectsAndCrests()
{
    VerifyWorldPartnerZoeEffects();
    VerifyAshenAnathemaBanderstCrest();
}

internal static void VerifyWorldPartnerZoeEffects()
{
    var zoe = CardCatalog.Get(CardIds.WorldPartnerZoe);
    var abyss = CardCatalog.Get(CardIds.HeavenbladeAbyss);
    const int caster = 0;
    const int opponent = 1;
    var zoeDeck = new DeckDefinition("Zoe leader-effect test", Enumerable.Repeat(zoe, DeckDefinition.RequiredCardCount));
    var abyssDeck = new DeckDefinition("Zoe opponent damage test", Enumerable.Repeat(abyss, DeckDefinition.RequiredCardCount));

    var normalState = CompleteMulligan(GameEngine.CreateGame(zoeDeck, abyssDeck, seed: 10_037));
    normalState = AdvanceUntilActivePlayerHasPp(normalState, caster, zoe.Cost);
    var maxPpBefore = normalState.Players[caster].MaxPlayPoints;
    normalState = GameEngine.Apply(normalState, GameEngine.GetLegalActions(normalState).OfType<PlayFollowerAction>().First());
    if (normalState.Players[caster].MaxPlayPoints != maxPpBefore + 1 ||
        normalState.Players[caster].CurrentPlayPoints != 0)
    {
        throw new InvalidOperationException("Zoe's Fanfare did not increase maximum PP without restoring current PP.");
    }

    var enhancedState = CompleteMulligan(GameEngine.CreateGame(zoeDeck, abyssDeck, seed: 10_038));
    enhancedState = AdvanceUntilActivePlayerHasPp(enhancedState, caster, 10);
    var enhancedPlay = GameEngine.GetLegalActions(enhancedState).OfType<PlayFollowerAction>().First();
    enhancedState = GameEngine.Apply(enhancedState, enhancedPlay);
    var zoeOnBoard = enhancedState.Players[caster].Board.Single(follower => follower.InstanceId == enhancedPlay.CardInstanceId);
    if (!zoeOnBoard.HasStorm ||
        enhancedState.Players[caster].Health != 1 ||
        enhancedState.Players[caster].MaxHealth != 1 ||
        enhancedState.Players[caster].TimedLeaderEffects.Count != 1)
    {
        throw new InvalidOperationException("Zoe's 10 PP Enhance did not grant Storm, set leader health to 1, and add the temporary leader effect.");
    }

    enhancedState = GameEngine.Apply(enhancedState, new EndTurnAction());
    var firstEnemySpell = GameEngine.GetLegalActions(enhancedState).OfType<PlaySpellAction>().First();
    enhancedState = GameEngine.Apply(enhancedState, firstEnemySpell);
    if (enhancedState.Players[caster].Health != 1)
    {
        throw new InvalidOperationException("Zoe's leader effect did not convert opponent-turn damage to zero.");
    }

    enhancedState = GameEngine.Apply(enhancedState, new EndTurnAction());
    if (enhancedState.Players[caster].TimedLeaderEffects.Count != 0)
    {
        throw new InvalidOperationException("Zoe's temporary leader effect did not expire at the end of the opponent's turn.");
    }

    enhancedState = GameEngine.Apply(enhancedState, new EndTurnAction());
    var secondEnemySpell = GameEngine.GetLegalActions(enhancedState).OfType<PlaySpellAction>().First();
    enhancedState = GameEngine.Apply(enhancedState, secondEnemySpell);
    if (!enhancedState.IsGameOver || enhancedState.Winner != opponent)
    {
        throw new InvalidOperationException("Zoe's leader effect still prevented damage after it had expired.");
    }
}

internal static void VerifyAshenAnathemaBanderstCrest()
{
    var banderst = CardCatalog.Get(CardIds.AshenAnathemaBanderst);
    var goliath = CardCatalog.Get(CardIds.Goliath);
    var abyss = CardCatalog.Get(CardIds.HeavenbladeAbyss);
    const int caster = 0;
    const int opponent = 1;

    var boardDamageDeck = new DeckDefinition("Banderst board-damage test", Enumerable.Repeat(banderst, DeckDefinition.RequiredCardCount));
    var goliathDeck = new DeckDefinition("Banderst target test", Enumerable.Repeat(goliath, DeckDefinition.RequiredCardCount));
    var boardDamageState = CompleteMulligan(GameEngine.CreateGame(boardDamageDeck, goliathDeck, seed: 10_039));
    boardDamageState = AdvanceUntilActivePlayerHasPp(boardDamageState, opponent, goliath.Cost);
    boardDamageState = GameEngine.Apply(boardDamageState, GameEngine.GetLegalActions(boardDamageState).OfType<PlayFollowerAction>().First());
    boardDamageState = AdvanceUntilActivePlayerHasPp(boardDamageState, caster, banderst.Cost);
    var opponentHealthBefore = boardDamageState.Players[opponent].Health;
    boardDamageState = GameEngine.Apply(boardDamageState, GameEngine.GetLegalActions(boardDamageState).OfType<PlayFollowerAction>().First());
    if (boardDamageState.Players[opponent].Board.Count != 0 ||
        boardDamageState.Players[opponent].Health != opponentHealthBefore)
    {
        throw new InvalidOperationException("Banderst's Fanfare did not damage every enemy follower while leaving the enemy leader unharmed.");
    }

    var crestDeck = new DeckDefinition("Banderst crest test", Enumerable.Repeat(banderst, DeckDefinition.RequiredCardCount));
    var abyssDeck = new DeckDefinition("Crest heal test", Enumerable.Repeat(abyss, DeckDefinition.RequiredCardCount));
    var crestState = CompleteMulligan(GameEngine.CreateGame(crestDeck, abyssDeck, seed: 10_040));
    crestState = AdvanceUntilActivePlayerHasPp(crestState, caster, banderst.Cost);
    var banderstPlay = GameEngine.GetLegalActions(crestState).OfType<PlayFollowerAction>().First();
    crestState = GameEngine.Apply(crestState, banderstPlay);
    var superEvolve = GameEngine.GetLegalActions(crestState).OfType<SuperEvolveAction>()
        .Single(action => action.FollowerInstanceId == banderstPlay.CardInstanceId);
    crestState = GameEngine.Apply(crestState, superEvolve);
    var crestObservation = GameEngine.ToObservation(crestState, caster);
    if (crestState.Players[opponent].Crests.Count != 1 ||
        crestState.Players[opponent].Crests.Single().Definition.Id != CrestIds.AshenAnathemaBanderst ||
        crestObservation.Opponent.Crests?.Single().Id != CrestIds.AshenAnathemaBanderst)
    {
        throw new InvalidOperationException("Banderst's super evolution did not grant a visible named crest to the opponent.");
    }

    crestState = GameEngine.Apply(crestState, new EndTurnAction());
    if (crestState.Players[opponent].Health != PlayerState.StartingHealth - 2)
    {
        throw new InvalidOperationException("Banderst's crest did not deal 2 damage at the start of its controller's turn.");
    }

    crestState = GameEngine.Apply(crestState, GameEngine.GetLegalActions(crestState).OfType<PlaySpellAction>().First());
    if (crestState.Players[opponent].Health != PlayerState.StartingHealth - 2)
    {
        throw new InvalidOperationException("Banderst's crest did not deal 1 damage after the first actual leader restoration.");
    }

    crestState = GameEngine.Apply(crestState, GameEngine.GetLegalActions(crestState).OfType<PlaySpellAction>().First());
    if (crestState.Players[opponent].Health != PlayerState.StartingHealth - 1)
    {
        throw new InvalidOperationException("Banderst's crest incorrectly triggered more than once during the same controller turn.");
    }
}

internal static void VerifyLazyWaveflowerEffects()
{
    var waveflower = CardCatalog.Get(CardIds.LazyWaveflower);
    var goliath = CardCatalog.Get(CardIds.Goliath);
    const int caster = 0;
    const int opponent = 1;
    var waveflowerDeck = new DeckDefinition(
        "Lazy Waveflower test",
        Enumerable.Repeat(waveflower, DeckDefinition.RequiredCardCount));
    var goliathDeck = new DeckDefinition(
        "Lazy Waveflower target test",
        Enumerable.Repeat(goliath, DeckDefinition.RequiredCardCount));

    var normalState = CompleteMulligan(GameEngine.CreateGame(waveflowerDeck, goliathDeck, seed: 10_033));
    normalState = AdvanceUntilActivePlayerHasPp(normalState, opponent, goliath.Cost);
    normalState = GameEngine.Apply(normalState, GameEngine.GetLegalActions(normalState).OfType<PlayFollowerAction>().First());
    normalState = AdvanceUntilActivePlayerHasPp(normalState, caster, waveflower.Cost);
    if (normalState.Players[caster].MaxPlayPoints >= 7)
    {
        throw new InvalidOperationException("The non-Awakening Waveflower test unexpectedly reached 7 maximum PP.");
    }

    var normalHealth = normalState.Players[opponent].Health;
    normalState = GameEngine.Apply(normalState, GameEngine.GetLegalActions(normalState).OfType<PlaySpellAction>().First());
    if (normalState.Players[opponent].Health != normalHealth ||
        normalState.Players[opponent].Board.Single().CurrentDefense != 1)
    {
        throw new InvalidOperationException("Lazy Waveflower did not resolve its two separate random 2-damage effects without the Awakening leader damage.");
    }

    var awakeningState = CompleteMulligan(GameEngine.CreateGame(waveflowerDeck, goliathDeck, seed: 10_034));
    awakeningState = AdvanceUntilActivePlayerHasPp(awakeningState, opponent, goliath.Cost);
    awakeningState = GameEngine.Apply(awakeningState, GameEngine.GetLegalActions(awakeningState).OfType<PlayFollowerAction>().First());
    awakeningState = AdvanceUntilActivePlayerHasPp(awakeningState, caster, 7);
    var awakeningHealth = awakeningState.Players[opponent].Health;
    awakeningState = GameEngine.Apply(awakeningState, GameEngine.GetLegalActions(awakeningState).OfType<PlaySpellAction>().First());
    if (awakeningState.Players[caster].MaxPlayPoints < 7 ||
        awakeningState.Players[opponent].Health != awakeningHealth - 2 ||
        awakeningState.Players[opponent].Board.Single().CurrentDefense != 1)
    {
        throw new InvalidOperationException("Lazy Waveflower did not apply its Awakening leader damage at 7 maximum PP.");
    }
}

internal static void VerifyDarkDimensionEffects()
{
    var darkDimension = CardCatalog.Get(CardIds.DarkDimension);
    var goliath = CardCatalog.Get(CardIds.Goliath);
    var polalai = CardCatalog.Get(CardIds.AncientHeavenbladePolalai);
    const int caster = 0;
    const int opponent = 1;
    var darkDeck = new DeckDefinition(
        "Dark Dimension test",
        Enumerable.Repeat(darkDimension, DeckDefinition.RequiredCardCount));
    var goliathDeck = new DeckDefinition(
        "Dark Dimension target test",
        Enumerable.Repeat(goliath, DeckDefinition.RequiredCardCount));

    var state = CompleteMulligan(GameEngine.CreateGame(darkDeck, goliathDeck, seed: 10_035));
    state = AdvanceUntilActivePlayerHasPp(state, opponent, goliath.Cost);
    state = GameEngine.Apply(state, GameEngine.GetLegalActions(state).OfType<PlayFollowerAction>().First());
    state = AdvanceUntilActivePlayerHasPp(state, caster, darkDimension.Cost);
    var darkPlay = GameEngine.GetLegalActions(state).OfType<PlayAmuletAction>().First();
    state = GameEngine.Apply(state, darkPlay);
    if (state.Players[caster].OccupiedBoardSlots != 1 || state.Players[caster].Amulets.Single().Countdown != 2)
    {
        throw new InvalidOperationException("Dark Dimension did not enter the shared board with Countdown 2.");
    }

    state = GameEngine.Apply(state, new EndTurnAction());
    if (state.Players[opponent].Board.Single().CurrentDefense != 3)
    {
        throw new InvalidOperationException("Dark Dimension did not deal 2 damage at the end of the turn it was played.");
    }

    state = GameEngine.Apply(state, new EndTurnAction());
    if (state.Players[caster].Amulets.Single().Countdown != 1)
    {
        throw new InvalidOperationException("Dark Dimension did not reduce Countdown from 2 to 1 at the start of its controller's turn.");
    }

    state = GameEngine.Apply(state, new EndTurnAction());
    if (state.Players[opponent].Board.Single().CurrentDefense != 1)
    {
        throw new InvalidOperationException("Dark Dimension did not deal damage a second time before its Countdown expired.");
    }

    state = GameEngine.Apply(state, new EndTurnAction());
    if (state.Players[caster].Amulets.Count != 0 ||
        !state.Players[caster].Graveyard.Any(card => card.InstanceId == darkPlay.CardInstanceId))
    {
        throw new InvalidOperationException("Dark Dimension was not destroyed when Countdown reached zero.");
    }

    var polalaiDeck = new DeckDefinition(
        "Dark Dimension erosion protection test",
        Enumerable.Repeat(polalai, DeckDefinition.RequiredCardCount));
    var protectionState = CompleteMulligan(GameEngine.CreateGame(darkDeck, polalaiDeck, seed: 10_036));
    protectionState = AdvanceUntilActivePlayerHasPp(protectionState, opponent, polalai.Cost);
    protectionState = GameEngine.Apply(protectionState, GameEngine.GetLegalActions(protectionState).OfType<PlayFollowerAction>().First());
    protectionState = AdvanceUntilActivePlayerHasPp(protectionState, caster, darkDimension.Cost);
    protectionState = GameEngine.Apply(protectionState, GameEngine.GetLegalActions(protectionState).OfType<PlayAmuletAction>().First());
    protectionState = GameEngine.Apply(protectionState, new EndTurnAction());
    if (protectionState.Players[opponent].Board.Single().CurrentDefense != polalai.Defense)
    {
        throw new InvalidOperationException("Dark Dimension incorrectly damaged an Erosion-trait follower.");
    }
}

internal static GameState FindDiscardState(
    DeckDefinition deck,
    string sourceCardId,
    string requiredDiscardCardId,
    int requiredPp,
    ulong seedStart)
{
    const int caster = 0;
    for (var seed = seedStart; seed < seedStart + 200; seed++)
    {
        var state = CompleteMulligan(GameEngine.CreateGame(deck, deck, seed));
        state = AdvanceUntilActivePlayerHasPp(state, caster, requiredPp);
        if (GameEngine.GetLegalActions(state).Any(action => action switch
        {
            PlaySpellAction spell when state.Players[caster].Hand.Single(card => card.InstanceId == spell.CardInstanceId).Definition.Id == sourceCardId =>
                spell.OwnHandCardTargetInstanceIds?.Any(targetId => state.Players[caster].Hand.Single(card => card.InstanceId == targetId).Definition.Id == requiredDiscardCardId) == true,
            PlayFollowerAction follower when state.Players[caster].Hand.Single(card => card.InstanceId == follower.CardInstanceId).Definition.Id == sourceCardId =>
                follower.OwnHandCardTargetInstanceIds?.Any(targetId => state.Players[caster].Hand.Single(card => card.InstanceId == targetId).Definition.Id == requiredDiscardCardId) == true,
            _ => false
        }))
        {
            return state;
        }
    }

    throw new InvalidOperationException("The discard-trigger test setup could not draw the required cards.");
}

internal static GameState AdvanceUntilActivePlayerHasPp(GameState state, int player, int requiredPp)
{
    for (var step = 0; step < 100; step++)
    {
        if (state.ActivePlayer == player && state.Players[player].CurrentPlayPoints >= requiredPp)
        {
            return state;
        }

        state = GameEngine.Apply(state, new EndTurnAction());
    }

    throw new InvalidOperationException("The test setup did not reach the requested player and PP value.");
}

internal static GameState AdvanceWithBanditOpponent(GameState state, int caster, int opponent, int targetOwnTurn)
{
    for (var step = 0; step < 100; step++)
    {
        if (state.ActivePlayer == caster && state.Players[caster].OwnTurnNumber >= targetOwnTurn)
        {
            return state;
        }

        if (state.ActivePlayer == opponent && state.Players[opponent].Board.Count < PlayerState.BoardLimit)
        {
            var banditPlay = GameEngine.GetLegalActions(state).OfType<PlayFollowerAction>().FirstOrDefault();
            if (banditPlay is not null)
            {
                state = GameEngine.Apply(state, banditPlay);
                continue;
            }
        }

        state = GameEngine.Apply(state, new EndTurnAction());
    }

    throw new InvalidOperationException("The test setup did not reach the requested turn.");
}

internal static void RunLilimAndBatTest()
{
    var lilim = CardCatalog.Get(CardIds.CuteDemonLilim);
    var bat = CardCatalog.Get(CardIds.Bat);
    var lilimDeck = new DeckDefinition("莉莉姆效果测试", Enumerable.Repeat(lilim, DeckDefinition.RequiredCardCount));
    var batDeck = new DeckDefinition("蝙蝠效果测试", Enumerable.Repeat(bat, DeckDefinition.RequiredCardCount));

    var state = GameEngine.CreateGame(batDeck, lilimDeck, seed: 30_041);
    while (state.Phase == GamePhase.Mulligan)
    {
        state = GameEngine.Apply(state, new MulliganAction([]));
    }

    var attackTriggerChecks = 0;
    var drainChecks = 0;
    var lastWordsChecks = 0;
    var attackCount = 0;

    for (var step = 0; step < 200 && !state.IsGameOver; step++)
    {
        var action = ChooseLilimAndBatTestAction(state, ref attackCount);
        var before = state;
        state = GameEngine.Apply(state, action);
        AuditLilimAndBatStep(before, state, action, ref attackTriggerChecks, ref drainChecks, ref lastWordsChecks);
    }

    if (attackTriggerChecks == 0 || drainChecks == 0 || lastWordsChecks == 0)
    {
        throw new InvalidOperationException(
            $"Lilim/Bat effects were not fully exercised: attack trigger {attackTriggerChecks}, drain {drainChecks}, last words {lastWordsChecks}.");
    }

    Console.WriteLine("Lilim and Bat test passed.");
    Console.WriteLine($"【攻击时】对所有主战者造成1点伤害：已验证 {attackTriggerChecks} 次攻击。");
    Console.WriteLine($"【虹吸】：已验证 {drainChecks} 次攻击按造成的伤害回复主战者生命。");
    Console.WriteLine($"【谢幕曲】：已验证 {lastWordsChecks} 张莉莉姆被破坏后各将 1 张蝙蝠加入手牌。");
}

/// <summary>
/// Plays a follower when possible and otherwise attacks, alternating between the enemy
/// leader and an enemy follower so both attack paths are exercised.
/// </summary>
internal static GameAction ChooseLilimAndBatTestAction(GameState state, ref int attackCount)
{
    var legalActions = GameEngine.GetLegalActions(state);
    var play = legalActions
        .OfType<PlayFollowerAction>()
        .FirstOrDefault(action =>
            action.HandCardTargetInstanceId is null &&
            action.EnemyFollowerTargetInstanceIds is null &&
            action.ModeChoiceIndex is null &&
            action.OwnHandCardTargetInstanceIds is null);
    if (play is not null)
    {
        return play;
    }

    var attackLeader = legalActions.OfType<AttackLeaderAction>().FirstOrDefault();
    var attackFollower = legalActions.OfType<AttackFollowerAction>().FirstOrDefault();
    GameAction? attack = attackCount % 2 == 0
        ? attackLeader is not null ? attackLeader : attackFollower
        : attackFollower is not null ? attackFollower : attackLeader;
    if (attack is not null)
    {
        attackCount++;
        return attack;
    }

    return legalActions.OfType<EndTurnAction>().First();
}

/// <summary>
/// Checks the effects that 可爱恶魔·莉莉姆 and 蝙蝠 rely on after every single action:
/// attack triggers hit both leaders, Drain heals by the damage dealt, and Last Words hand over a Bat.
/// </summary>
internal static void AuditLilimAndBatStep(
    GameState before,
    GameState after,
    GameAction action,
    ref int attackTriggerChecks,
    ref int drainChecks,
    ref int lastWordsChecks)
{
    if (after.IsGameOver)
    {
        // The finishing blow ends the match; the simultaneous self-damage is intentionally skipped.
        return;
    }

    if (action is AttackLeaderAction or AttackFollowerAction)
    {
        var attackerIndex = before.ActivePlayer;
        var defenderIndex = attackerIndex == 0 ? 1 : 0;
        var attackerInstanceId = action switch
        {
            AttackLeaderAction leaderAttack => leaderAttack.AttackerInstanceId,
            AttackFollowerAction followerAttack => followerAttack.AttackerInstanceId,
            _ => throw new InvalidOperationException("Unreachable attack action.")
        };
        var attacker = before.Players[attackerIndex].Board
            .Single(follower => follower.InstanceId == attackerInstanceId);
        var attackerHealthChange = after.Players[attackerIndex].Health - before.Players[attackerIndex].Health;
        var defenderHealthLoss = before.Players[defenderIndex].Health - after.Players[defenderIndex].Health;
        var leaderDamage = action is AttackLeaderAction ? attacker.Attack : 0;

        if (attacker.Definition.Id == CardIds.CuteDemonLilim)
        {
            if (attackerHealthChange != -1 || defenderHealthLoss != 1 + leaderDamage)
            {
                throw new InvalidOperationException(
                    "A Lilim attack must deal 1 damage to both leaders before its own attack damage.");
            }

            attackTriggerChecks++;
        }
        else if (attacker.Definition.Id == CardIds.Bat)
        {
            var expectedHeal = before.Players[attackerIndex].Health < before.Players[attackerIndex].MaxHealth
                ? attacker.Attack
                : 0;
            if (attackerHealthChange != expectedHeal)
            {
                throw new InvalidOperationException(
                    "Drain must restore the controller's leader by the damage the Bat deals.");
            }

            drainChecks++;
        }
    }

    for (var player = 0; player < 2; player++)
    {
        var lilimsLost = CountBoardCards(before.Players[player], CardIds.CuteDemonLilim) -
                         CountBoardCards(after.Players[player], CardIds.CuteDemonLilim);
        if (lilimsLost <= 0)
        {
            continue;
        }

        var batsGained = CountOwnedBats(after.Players[player]) - CountOwnedBats(before.Players[player]);
        if (batsGained != lilimsLost)
        {
            throw new InvalidOperationException(
                "Lilim's Last Words must add one Bat to its owner's hand.");
        }

        lastWordsChecks += lilimsLost;
    }
}

internal static int CountBoardCards(PlayerState player, string cardId) =>
    player.Board.Count(follower => follower.Definition.Id == cardId);

internal static int CountOwnedBats(PlayerState player) =>
    player.Hand.Count(card => card.Definition.Id == CardIds.Bat) +
    player.Graveyard.Count(card => card.Definition.Id == CardIds.Bat);

internal static void RunLathAndSkeletonTest()
{
    var lath = CardCatalog.Get(CardIds.DevilDrummerLath);
    var lathDeck = new DeckDefinition("拉兹效果测试", Enumerable.Repeat(lath, DeckDefinition.RequiredCardCount));
    var evolutionDamage = lath.EvolutionEffects!
        .Single(effect => effect.Kind == CardEffectKind.DealDamageToEnemyFollower)
        .Amount;

    var state = GameEngine.CreateGame(lathDeck, lathDeck, seed: 30_043);
    while (state.Phase == GamePhase.Mulligan)
    {
        state = GameEngine.Apply(state, new MulliganAction([]));
    }

    var evolutionDamageChecks = 0;
    var superEvolutionDamageChecks = 0;
    var lastWordsChecks = 0;
    var transcriptChecks = 0;

    for (var step = 0; step < 400 && !state.IsGameOver; step++)
    {
        var action = ChooseLathTestAction(state);
        var before = state;
        state = GameEngine.Apply(state, action);
        AuditLathStep(
            before,
            state,
            action,
            evolutionDamage,
            ref evolutionDamageChecks,
            ref superEvolutionDamageChecks,
            ref lastWordsChecks,
            ref transcriptChecks);
    }

    if (evolutionDamageChecks == 0 || lastWordsChecks == 0 || transcriptChecks == 0)
    {
        throw new InvalidOperationException(
            $"Lath's effects were not fully exercised: evolution damage {evolutionDamageChecks}, super evolution {superEvolutionDamageChecks}, last words {lastWordsChecks}, transcript {transcriptChecks}.");
    }

    Console.WriteLine("Lath and Skeleton Soldier test passed.");
    Console.WriteLine($"【进化时】选择对手的1个随从造成{evolutionDamage}点伤害：已验证 {evolutionDamageChecks} 次指定目标，其中超进化 {superEvolutionDamageChecks} 次。");
    Console.WriteLine($"【谢幕曲】：已验证 {lastWordsChecks} 次召唤骸骨士兵。");
}

/// <summary>
/// Plays a follower, then evolves (preferring an evolution that names an enemy follower), then
/// attacks. Both decks are 拉兹 mirrors, so the game lasts long enough for evolution and
/// super evolution to come online.
/// </summary>
internal static GameAction ChooseLathTestAction(GameState state)
{
    var legalActions = GameEngine.GetLegalActions(state);
    var play = legalActions
        .OfType<PlayFollowerAction>()
        .FirstOrDefault(action =>
            action.HandCardTargetInstanceId is null &&
            action.EnemyFollowerTargetInstanceIds is null &&
            action.ModeChoiceIndex is null &&
            action.OwnHandCardTargetInstanceIds is null);
    if (play is not null)
    {
        return play;
    }

    var superEvolve = legalActions
        .OfType<SuperEvolveAction>()
        .OrderByDescending(action => action.EnemyFollowerTargetInstanceId is not null)
        .FirstOrDefault();
    if (superEvolve is not null)
    {
        return superEvolve;
    }

    var evolve = legalActions
        .OfType<EvolveAction>()
        .OrderByDescending(action => action.EnemyFollowerTargetInstanceId is not null)
        .FirstOrDefault();
    if (evolve is not null)
    {
        return evolve;
    }

    GameAction? attack = legalActions.OfType<AttackFollowerAction>().FirstOrDefault() is { } followerAttack
        ? followerAttack
        : legalActions.OfType<AttackLeaderAction>().FirstOrDefault();
    return attack ?? legalActions.OfType<EndTurnAction>().First();
}

/// <summary>
/// Checks after every action that a targeted 【进化时】 dealt exactly its printed damage and that a
/// destroyed 恶魔鼓手·拉兹 summoned one 骸骨士兵 for its owner.
/// </summary>
internal static void AuditLathStep(
    GameState before,
    GameState after,
    GameAction action,
    int evolutionDamage,
    ref int evolutionDamageChecks,
    ref int superEvolutionDamageChecks,
    ref int lastWordsChecks,
    ref int transcriptChecks)
{
    var targetInstanceId = action switch
    {
        EvolveAction evolve => evolve.EnemyFollowerTargetInstanceId,
        SuperEvolveAction superEvolve => superEvolve.EnemyFollowerTargetInstanceId,
        _ => null
    };

    if (targetInstanceId is not null)
    {
        var opponentIndex = before.ActivePlayer == 0 ? 1 : 0;
        var targetBefore = before.Players[opponentIndex].Board
            .Single(follower => follower.InstanceId == targetInstanceId.Value);
        var targetAfter = after.Players[opponentIndex].Board
            .SingleOrDefault(follower => follower.InstanceId == targetInstanceId.Value);
        if (targetAfter is null)
        {
            if (targetBefore.CurrentDefense > evolutionDamage)
            {
                throw new InvalidOperationException(
                    "An evolution effect destroyed a follower it did not damage enough to destroy.");
            }
        }
        else if (targetBefore.CurrentDefense - targetAfter.CurrentDefense != evolutionDamage)
        {
            throw new InvalidOperationException(
                "An evolution effect must deal exactly its printed damage to the chosen follower.");
        }

        evolutionDamageChecks++;
        if (action is SuperEvolveAction)
        {
            superEvolutionDamageChecks++;
        }

        if (transcriptChecks == 0)
        {
            transcriptChecks = VerifyEvolutionTranscript(before, after, action) ? 1 : 0;
        }
    }

    for (var player = 0; player < 2; player++)
    {
        var lathsLost = CountBoardCards(before.Players[player], CardIds.DevilDrummerLath) -
                        CountBoardCards(after.Players[player], CardIds.DevilDrummerLath);
        if (lathsLost <= 0)
        {
            continue;
        }

        var skeletonsSummoned =
            CountBoardCards(after.Players[player], CardIds.SkeletonSoldier) -
            CountBoardCards(before.Players[player], CardIds.SkeletonSoldier);
        if (skeletonsSummoned != lathsLost)
        {
            throw new InvalidOperationException(
                "Lath's Last Words must summon one Skeleton Soldier for each Lath that leaves the board.");
        }

        lastWordsChecks += lathsLost;
    }
}
internal static void RunNetherLieutenantTest()
{
    var lieutenant = CardCatalog.Get(CardIds.NetherLieutenant);
    var swordsman = CardCatalog.Get(CardIds.Gladiator);
    var lieutenantDeck = new DeckDefinition(
        "幽冥中尉测试",
        Enumerable.Repeat(lieutenant, DeckDefinition.RequiredCardCount));
    var opponentDeck = new DeckDefinition(
        "幽冥中尉对手测试",
        Enumerable.Repeat(swordsman, DeckDefinition.RequiredCardCount));

    var state = GameEngine.CreateGame(lieutenantDeck, opponentDeck, seed: 30_107);
    while (state.Phase == GamePhase.Mulligan)
    {
        state = GameEngine.Apply(state, new MulliganAction([]));
    }

    var summonChecks = 0;
    var suppressedChecks = 0;

    for (var step = 0; step < 200 && !state.IsGameOver; step++)
    {
        var action = ChooseLieutenantTestAction(state);
        var before = state;
        state = GameEngine.Apply(state, action);
        AuditLieutenantStep(before, state, ref summonChecks, ref suppressedChecks);
    }

    if (summonChecks == 0 || suppressedChecks == 0)
    {
        throw new InvalidOperationException(
            $"幽冥中尉 was not fully exercised: summons {summonChecks}, suppressed {suppressedChecks}.");
    }

    Console.WriteLine("Nether lieutenant test passed.");
    Console.WriteLine($"【谢幕曲】召唤1个带 +1/+0 与【突进】且失去【谢幕曲】的复制体：已验证 {summonChecks} 次。");
    Console.WriteLine($"失去【谢幕曲】的复制体再被破坏时不再召唤（链条终止）：已验证 {suppressedChecks} 次。");
}

/// <summary>Plays 幽冥中尉 and trades it away so its Last Words fires.</summary>
internal static GameAction ChooseLieutenantTestAction(GameState state)
{
    var legalActions = GameEngine.GetLegalActions(state);
    var play = legalActions
        .OfType<PlayFollowerAction>()
        .FirstOrDefault(action =>
            action.HandCardTargetInstanceId is null &&
            action.EnemyFollowerTargetInstanceIds is null &&
            action.ModeChoiceIndex is null &&
            action.OwnHandCardTargetInstanceIds is null);
    if (play is not null)
    {
        return play;
    }

    var trade = legalActions.OfType<AttackFollowerAction>().FirstOrDefault();
    return trade ?? (GameAction)legalActions.OfType<EndTurnAction>().First();
}

/// <summary>
/// Checks that a destroyed 幽冥中尉 summons one copy with +1/+0, 【突进】 and no Last Words, and that the
/// suppressed copy stops the chain.
/// </summary>
internal static void AuditLieutenantStep(GameState before, GameState after, ref int summonChecks, ref int suppressedChecks)
{
    for (var player = 0; player < 2; player++)
    {
        foreach (var lost in before.Players[player].Board.Where(follower =>
                     follower.Definition.Id == CardIds.NetherLieutenant &&
                     after.Players[player].Board.All(candidate => candidate.InstanceId != follower.InstanceId)))
        {
            var copies = after.Players[player].Board
                .Where(follower => follower.Definition.Id == CardIds.NetherLieutenant &&
                                   before.Players[player].Board.All(previous =>
                                       previous.InstanceId != follower.InstanceId))
                .ToArray();
            if (lost.Card.HasSuppressedLastWords)
            {
                if (copies.Length != 0)
                {
                    throw new InvalidOperationException(
                        "A 幽冥中尉 that lost its Last Words must not summon another copy.");
                }

                suppressedChecks++;
                continue;
            }

            if (copies.Length != 1)
            {
                throw new InvalidOperationException("幽冥中尉 must summon exactly one copy when it is destroyed.");
            }

            var copy = copies[0];
            if (copy.Attack != lost.Definition.Attack + 1 ||
                copy.MaxDefense != lost.Definition.Defense ||
                !copy.HasRush ||
                !copy.Card.HasSuppressedLastWords)
            {
                throw new InvalidOperationException(
                    "The summoned copy must be +1/+0 with 【突进】 and without 【谢幕曲】.");
            }

            summonChecks++;
        }
    }
}

/// <summary>
/// 对手卡组识别：只凭对手已经公开打出的牌，必须能唯一定位到卡组库里的那一副；而对不属于任何
/// 已存卡组的牌，必须报"无法识别"，绝不能凭空编出一副卡组塞进后续判断。
/// </summary>
internal static void RunOpponentDeckInferenceTest()
{
    foreach (var deckId in new[] { "DECK-002", "DECK-003" })
    {
        var deck = DeckCatalog.Get(deckId);
        var signature = deck.Entries.Take(3).Select(entry => entry.CardId).ToArray();
        var identified = OpponentDeckInference.Identify(signature);
        if (identified.Count != 1 || !string.Equals(identified[0].DeckId, deckId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"卡组识别失败：用「{deck.Name}」的 {string.Join("、", signature)} 推断出 " +
                $"[{string.Join("、", identified.Select(candidate => candidate.DeckName))}]，应唯一命中它自己。");
        }

        var possible = OpponentDeckInference.PossibleCards(signature);
        if (possible.Count == 0)
        {
            throw new InvalidOperationException(
                $"卡组识别对「{deck.Name}」给出的可能卡池为空，威胁估计会永远为 0。");
        }

        var burst = OpponentDeckInference.EstimatedHandBurst(
            signature,
            opponentHandCount: 5,
            opponentMaxPlayPoints: 10,
            opponentDeckCount: 30);
        if (burst < 0 || double.IsNaN(burst))
        {
            throw new InvalidOperationException($"「{deck.Name}」的手牌爆发估计非法：{burst}。");
        }
    }

    // 一张不在任何已存卡组里的牌，必须让推断放弃而不是瞎猜。
    var unresolved = OpponentDeckInference.Identify(["BASE-001"]);
    if (unresolved.Count != 0)
    {
        throw new InvalidOperationException(
            "对手打出不属于任何已存卡组的牌时，识别必须报告无法确定；" +
            $"实际给出了 [{string.Join("、", unresolved.Select(candidate => candidate.DeckName))}]。");
    }

    // 卡池未知时威胁估计必须为 0，否则会把编造出来的爆发当成事实。
    if (OpponentDeckInference.EstimatedHandBurst(["BASE-001"], 5, 10, 30) != 0)
    {
        throw new InvalidOperationException("卡组未知时手牌爆发估计必须为 0。");
    }

    Console.WriteLine("Opponent deck inference test passed.");
    Console.WriteLine(
        "Three revealed cards pin the deck down to exactly one saved list, and a card no saved deck " +
        "contains makes the inference give up instead of inventing one.");
}

internal static void RunStoredDeckRuleTest()
{
    var card = CardCatalog.Get(CardIds.Gladiator);
    var unfinished = new DeckListDefinition(
        "DECK-TEST",
        "未完成的卡组",
        "测试",
        [new DeckCardEntry(card.Id, DeckDefinition.RequiredCardCount - 1)]);
    unfinished.Validate();
    CardCatalog.ValidateDeckEntries(unfinished.Entries);

    var overfilled = new DeckListDefinition(
        "DECK-TEST",
        "超出上限的卡组",
        "测试",
        [new DeckCardEntry(card.Id, DeckDefinition.RequiredCardCount + 1)]);
    try
    {
        overfilled.Validate();
        throw new InvalidOperationException("A deck over the limit must be rejected.");
    }
    catch (ArgumentException)
    {
        // Expected.
    }

    Console.WriteLine("Stored deck rule test passed.");
    Console.WriteLine($"未满 {DeckDefinition.RequiredCardCount} 张的卡组可以保存/读取（校验通过），超过上限的会被拒绝。");
}

internal static void RunIstanbulDeadTest()
{
    var istanbul = CardCatalog.Get(CardIds.IstanbulDeadVersusMalchiget);
    var lath = CardCatalog.Get(CardIds.DevilDrummerLath);
    var skeleton = CardCatalog.Get(CardIds.SkeletonSoldier);
    var deck = new DeckDefinition(
        "伊斯坦戴德测试",
        Enumerable.Range(0, DeckDefinition.RequiredCardCount).Select(index => (index % 4) switch
        {
            0 => istanbul,
            1 => lath,
            _ => skeleton
        }));
    var opponentDeck = new DeckDefinition(
        "伊斯坦戴德对手测试",
        Enumerable.Repeat(CardCatalog.Get(CardIds.Gladiator), DeckDefinition.RequiredCardCount));

    var state = GameEngine.CreateGame(deck, opponentDeck, seed: 30_103);
    while (state.Phase == GamePhase.Mulligan)
    {
        state = GameEngine.Apply(state, new MulliganAction([]));
    }

    var recallChecks = 0;
    var costPriorityChecks = 0;
    var crestChecks = 0;
    var crestEffectChecks = 0;

    for (var step = 0; step < 200 && !state.IsGameOver; step++)
    {
        var action = ChooseIstanbulDeadTestAction(state);
        var before = state;
        state = GameEngine.Apply(state, action);
        AuditIstanbulDeadStep(
            before,
            state,
            action,
            ref recallChecks,
            ref costPriorityChecks,
            ref crestChecks,
            ref crestEffectChecks);
    }

    if (recallChecks == 0 || costPriorityChecks == 0 || crestChecks == 0 || crestEffectChecks == 0)
    {
        throw new InvalidOperationException(
            $"伊斯坦戴德 was not fully exercised: recall {recallChecks}, cost priority {costPriorityChecks}, crest {crestChecks}, crest effect {crestEffectChecks}.");
    }

    Console.WriteLine("Istanbul dead versus Malchiget test passed.");
    Console.WriteLine($"【亡者召回_2】从墓地召唤复制体且墓地不减：已验证 {recallChecks} 次。");
    Console.WriteLine($"亡者召回优先取不超过2的最高费用档：已验证 {costPriorityChecks} 次。");
    Console.WriteLine($"【超进化时】使自己获得纹章：已验证 {crestChecks} 次。");
    Console.WriteLine($"纹章「回合结束时破坏自己有谢幕曲的随机1张＋对手随机1个随从」：已验证 {crestEffectChecks} 次。");
}

/// <summary>Plays 伊斯坦戴德 and trades cheap 亡者 followers to stock the graveyard.</summary>
internal static GameAction ChooseIstanbulDeadTestAction(GameState state)
{
    var legalActions = GameEngine.GetLegalActions(state);
    var player = state.Players[state.ActivePlayer];
    var plays = legalActions
        .OfType<PlayFollowerAction>()
        .Where(action => action.HandCardTargetInstanceId is null &&
                         action.EnemyFollowerTargetInstanceIds is null &&
                         action.ModeChoiceIndex is null &&
                         action.OwnHandCardTargetInstanceIds is null)
        .ToArray();
    CardDefinition HandDefinition(PlayFollowerAction action) =>
        player.Hand.Single(card => card.InstanceId == action.CardInstanceId).Definition;

    var istanbul = plays.FirstOrDefault(action => HandDefinition(action).Id == CardIds.IstanbulDeadVersusMalchiget);
    if (istanbul is not null)
    {
        return istanbul;
    }

    var superEvolve = legalActions
        .OfType<SuperEvolveAction>()
        .FirstOrDefault(action => player.Board
            .Single(follower => follower.InstanceId == action.FollowerInstanceId)
            .Definition.Id == CardIds.IstanbulDeadVersusMalchiget);
    if (superEvolve is not null)
    {
        return superEvolve;
    }

    if (player.OccupiedBoardSlots < 3)
    {
        var cheap = plays.FirstOrDefault(action => HandDefinition(action).Cost <= 2);
        if (cheap is not null)
        {
            return cheap;
        }
    }

    var trade = legalActions.OfType<AttackFollowerAction>().FirstOrDefault();
    return trade ?? (GameAction)legalActions.OfType<EndTurnAction>().First();
}

/// <summary>
/// Checks the 亡者召回_2 Fanfare (copies, graveyard untouched, highest cost under the limit first), the
/// super-evolution crest and the crest's end-of-turn shatter.
/// </summary>
internal static void AuditIstanbulDeadStep(
    GameState before,
    GameState after,
    GameAction action,
    ref int recallChecks,
    ref int costPriorityChecks,
    ref int crestChecks,
    ref int crestEffectChecks)
{
    var player = before.ActivePlayer;
    var opponent = player == 0 ? 1 : 0;

    if (action is PlayFollowerAction play)
    {
        var handCard = before.Players[player].Hand.Single(card => card.InstanceId == play.CardInstanceId);
        if (handCard.Definition.Id == CardIds.IstanbulDeadVersusMalchiget)
        {
            var summoned = after.Players[player].Board
                .Where(follower => before.Players[player].Board.All(previous =>
                    previous.InstanceId != follower.InstanceId))
                .Where(follower => follower.InstanceId != play.CardInstanceId)
                .ToArray();
            var room = PlayerState.BoardLimit - (before.Players[player].OccupiedBoardSlots + 1);
            var eligible = before.Players[player].Graveyard
                .Where(card => card.Definition.Type == CardType.Follower && card.Definition.Cost <= 2)
                .ToArray();
            var expectedCount = Math.Clamp(eligible.Length > 0 ? room : 0, 0, 3);
            if (summoned.Length != expectedCount)
            {
                throw new InvalidOperationException(
                    $"亡者召回_2 must summon {expectedCount} follower(s), not {summoned.Length}.");
            }

            if (after.Players[player].Graveyard.Count != before.Players[player].Graveyard.Count)
            {
                throw new InvalidOperationException("亡者召回 must leave the graveyard untouched.");
            }

            if (summoned.Length > 0)
            {
                recallChecks++;
                var highestCost = eligible.Max(card => card.Definition.Cost);
                foreach (var follower in summoned)
                {
                    if (follower.Definition.Cost != highestCost)
                    {
                        throw new InvalidOperationException(
                            $"亡者召回 must prefer cost {highestCost} over lower costs.");
                    }
                }

                costPriorityChecks++;
            }
        }
    }

    if (action is SuperEvolveAction superEvolve)
    {
        var evolving = before.Players[player].Board
            .Single(follower => follower.InstanceId == superEvolve.FollowerInstanceId);
        if (evolving.Definition.Id == CardIds.IstanbulDeadVersusMalchiget)
        {
            if (after.Players[player].Crests.All(crest =>
                    crest.Definition.Id != CrestIds.IstanbulDeadVersusMalchiget))
            {
                throw new InvalidOperationException("【超进化时】 must grant the 伊斯坦戴德 crest to its owner.");
            }

            crestChecks++;
        }
    }

    if (action is EndTurnAction &&
        before.Players[player].Crests.Any(crest =>
            crest.Definition.Id == CrestIds.IstanbulDeadVersusMalchiget))
    {
        var ownLastWords = before.Players[player].Board
            .Where(follower => follower.Definition.LastWordsEffects is { Count: > 0 })
            .ToArray();
        var ownDestroyed = ownLastWords.Count(follower =>
            after.Players[player].Board.All(candidate => candidate.InstanceId != follower.InstanceId));
        var enemyBefore = before.Players[opponent].Board.Count;
        var enemyDestroyed = enemyBefore - after.Players[opponent].Board.Count;
        if (ownLastWords.Length == 0)
        {
            if (ownDestroyed != 0 || enemyDestroyed != 0)
            {
                throw new InvalidOperationException(
                    "Without a card of its own with Last Words the crest must do nothing.");
            }

            return;
        }

        if (ownDestroyed != 1)
        {
            throw new InvalidOperationException("The crest must destroy exactly one own card with Last Words.");
        }

        if (enemyBefore > 0 && enemyDestroyed != 1)
        {
            throw new InvalidOperationException("The crest must also destroy one random enemy follower.");
        }

        crestEffectChecks++;
    }
}

internal static void RunGalatadeTest()
{
    var galatade = CardCatalog.Get(CardIds.GalatadeVersusZet);
    var swordsman = CardCatalog.Get(CardIds.SilentDemonGeneral);
    var galatadeDeck = new DeckDefinition(
        "伽罗塔德测试",
        Enumerable.Repeat(galatade, DeckDefinition.RequiredCardCount));
    var attackerDeck = new DeckDefinition(
        "伽罗塔德对手测试",
        Enumerable.Repeat(swordsman, DeckDefinition.RequiredCardCount));

    var state = GameEngine.CreateGame(galatadeDeck, attackerDeck, seed: 30_101);
    while (state.Phase == GamePhase.Mulligan)
    {
        state = GameEngine.Apply(state, new MulliganAction([]));
    }

    var reductionChecks = 0;
    var discountedPlayChecks = 0;
    var stormModeChecks = 0;
    var wardModeChecks = 0;
    var modeIndex = 0;

    for (var step = 0; step < 200 && !state.IsGameOver; step++)
    {
        var action = ChooseGalatadeTestAction(state, ref modeIndex);
        var before = state;
        state = GameEngine.Apply(state, action);
        AuditGalatadeStep(
            before,
            state,
            action,
            ref reductionChecks,
            ref discountedPlayChecks,
            ref stormModeChecks,
            ref wardModeChecks);
    }

    if (reductionChecks == 0 || discountedPlayChecks == 0 || stormModeChecks == 0 || wardModeChecks == 0)
    {
        throw new InvalidOperationException(
            $"伽罗塔德 was not fully exercised: reduction {reductionChecks}, discount {discountedPlayChecks}, storm {stormModeChecks}, ward {wardModeChecks}.");
    }

    Console.WriteLine("Galatade versus Zet test passed.");
    Console.WriteLine($"在手牌中发动：回合结束时生命≤12则费用-1，可叠加且永久保留：已验证 {reductionChecks} 次。");
    Console.WriteLine($"减费后的实际费用被正确收取（以低于原费用的 PP 打出）：已验证 {discountedPlayChecks} 次。");
    Console.WriteLine($"【入场曲】模式(1) 本随从获得【疾驰】并对自己的主战者造成2点伤害：已验证 {stormModeChecks} 次。");
    Console.WriteLine($"【入场曲】模式(2) 本随从获得【守护】并对对手所有随从造成8点伤害：已验证 {wardModeChecks} 次。");
}

/// <summary>Plays 伽罗塔德 whenever its (possibly reduced) cost allows, alternating its modes.</summary>
internal static GameAction ChooseGalatadeTestAction(GameState state, ref int modeIndex)
{
    var legalActions = GameEngine.GetLegalActions(state);
    var plays = legalActions
        .OfType<PlayFollowerAction>()
        .Where(action => action.ModeChoiceIndex is not null)
        .ToArray();
    if (plays.Length > 0)
    {
        var desiredMode = modeIndex % 2;
        var action = plays.FirstOrDefault(candidate => candidate.ModeChoiceIndex == desiredMode) ?? plays[0];
        modeIndex++;
        return action;
    }

    // The opponent develops a board and trades into our followers so the board does not clog up.
    var play = legalActions
        .OfType<PlayFollowerAction>()
        .FirstOrDefault(action =>
            action.HandCardTargetInstanceId is null &&
            action.EnemyFollowerTargetInstanceIds is null &&
            action.ModeChoiceIndex is null &&
            action.OwnHandCardTargetInstanceIds is null);
    if (play is not null)
    {
        return play;
    }

    var attack = legalActions.OfType<AttackFollowerAction>().FirstOrDefault();
    return attack ?? (GameAction)legalActions.OfType<EndTurnAction>().First();
}

/// <summary>
/// Checks the in-hand cost reduction (end of turn, leader at 12 or less, stacking and permanent), that
/// the reduced cost is what gets paid, and both Fanfare modes of 伽罗塔德.
/// </summary>
internal static void AuditGalatadeStep(
    GameState before,
    GameState after,
    GameAction action,
    ref int reductionChecks,
    ref int discountedPlayChecks,
    ref int stormModeChecks,
    ref int wardModeChecks)
{
    var player = before.ActivePlayer;
    var opponent = player == 0 ? 1 : 0;

    if (action is EndTurnAction)
    {
        // Only the player ending their turn reduces the copies still in their hand.
        var health = before.Players[player].Health;
        foreach (var card in before.Players[player].Hand.Where(card =>
                     card.Definition.Id == CardIds.GalatadeVersusZet))
        {
            var afterCard = after.Players[player].Hand
                .FirstOrDefault(candidate => candidate.InstanceId == card.InstanceId);
            var expected = health <= 12 ? card.CostReduction + 1 : card.CostReduction;
            if (afterCard is not null && afterCard.CostReduction != expected)
            {
                throw new InvalidOperationException(
                    $"The in-hand reduction must apply once per end of turn while the leader is at 12 or less (was {card.CostReduction}, expected {expected}).");
            }
        }

        if (health <= 12)
        {
            reductionChecks++;
        }
    }

    if (action is not PlayFollowerAction play)
    {
        return;
    }

    var handCard = before.Players[player].Hand.Single(card => card.InstanceId == play.CardInstanceId);
    if (handCard.Definition.Id != CardIds.GalatadeVersusZet)
    {
        return;
    }

    var paid = before.Players[player].CurrentPlayPoints - after.Players[player].CurrentPlayPoints;
    var expectedCost = Math.Max(0, handCard.Definition.Cost - handCard.CostReduction);
    if (paid != expectedCost)
    {
        throw new InvalidOperationException(
            $"The reduced cost must be paid: expected {expectedCost}, paid {paid}.");
    }

    if (handCard.CostReduction > 0)
    {
        discountedPlayChecks++;
    }

    var played = after.Players[player].Board.Single(follower => follower.InstanceId == play.CardInstanceId);
    if (play.ModeChoiceIndex == 0)
    {
        if (!played.HasStorm || played.HasWard)
        {
            throw new InvalidOperationException("Fanfare mode (1) must grant 【疾驰】 only.");
        }

        var selfDamage = before.Players[player].Health - after.Players[player].Health;
        if (selfDamage != 2)
        {
            throw new InvalidOperationException("Fanfare mode (1) must deal 2 damage to its own leader.");
        }

        stormModeChecks++;
        return;
    }

    if (!played.HasWard || played.HasStorm)
    {
        throw new InvalidOperationException("Fanfare mode (2) must grant 【守护】 only.");
    }

    foreach (var enemy in before.Players[opponent].Board)
    {
        var survivor = after.Players[opponent].Board
            .FirstOrDefault(candidate => candidate.InstanceId == enemy.InstanceId);
        if (survivor is null)
        {
            if (enemy.CurrentDefense > 8)
            {
                throw new InvalidOperationException("Fanfare mode (2) destroyed a follower it could not kill.");
            }

            continue;
        }

        if (enemy.CurrentDefense - survivor.CurrentDefense != 8)
        {
            throw new InvalidOperationException("Fanfare mode (2) must deal 8 damage to every enemy follower.");
        }
    }

    wardModeChecks++;
}

internal static void RunDeathHostTest()
{
    var macmillan = CardCatalog.Get(CardIds.DeathHostMacmillan);
    var skeleton = CardCatalog.Get(CardIds.SkeletonSoldier);
    var macmillanDeck = new DeckDefinition(
        "马克米朗测试（墓地充足）",
        Enumerable.Range(0, DeckDefinition.RequiredCardCount)
            .Select(index => index % 5 == 4 ? macmillan : skeleton));
    var bareDeck = new DeckDefinition(
        "马克米朗测试（墓地不足）",
        Enumerable.Repeat(macmillan, DeckDefinition.RequiredCardCount));
    var opponentDeck = new DeckDefinition(
        "马克米朗对手测试",
        Enumerable.Repeat(CardCatalog.Get(CardIds.SkeletonSoldier), DeckDefinition.RequiredCardCount));

    var paidSummonChecks = 0;
    var unpaidSummonChecks = 0;
    var empowerChecks = 0;

    // Scenario 1: no filler spells, so the graveyard stays too small for 【唤灵_10】.
    var bareState = GameEngine.CreateGame(bareDeck, opponentDeck, seed: 30_095);
    while (bareState.Phase == GamePhase.Mulligan)
    {
        bareState = GameEngine.Apply(bareState, new MulliganAction([]));
    }

    for (var step = 0; step < 120 && !bareState.IsGameOver; step++)
    {
        var action = ChooseMacmillanTestAction(bareState);
        var before = bareState;
        bareState = GameEngine.Apply(bareState, action);
        AuditMacmillanStep(before, bareState, action, ref paidSummonChecks, ref unpaidSummonChecks, ref empowerChecks);
    }

    // Scenario 2: the filler spells build a graveyard large enough to pay the 唤灵.
    var state = GameEngine.CreateGame(macmillanDeck, opponentDeck, seed: 30_097);
    while (state.Phase == GamePhase.Mulligan)
    {
        state = GameEngine.Apply(state, new MulliganAction([]));
    }

    for (var step = 0; step < 200 && !state.IsGameOver; step++)
    {
        var action = ChooseMacmillanTestAction(state);
        var before = state;
        state = GameEngine.Apply(state, action);
        AuditMacmillanStep(before, state, action, ref paidSummonChecks, ref unpaidSummonChecks, ref empowerChecks);
    }

    if (paidSummonChecks == 0 || unpaidSummonChecks == 0 || empowerChecks == 0)
    {
        throw new InvalidOperationException(
            $"马克米朗 was not fully exercised: paid {paidSummonChecks}, unpaid {unpaidSummonChecks}, empower {empowerChecks}.");
    }

    Console.WriteLine("Death host Macmillan test passed.");
    Console.WriteLine($"【唤灵_10】召唤3个腐臭的僵尸并消耗10张墓地：已验证 {paidSummonChecks} 次。");
    Console.WriteLine($"墓地不足10张时唤灵不发动：已验证 {unpaidSummonChecks} 次。");
    Console.WriteLine($"「亡者随从进入战场 +1/+0 且获得【突进】【守护】并打主战者1点」：已验证 {empowerChecks} 次。");
}

/// <summary>
/// Keeps a small 亡者 board, trades it into the opponent to fill the graveyard, and plays 马克米朗 as
/// soon as its 【唤灵_10】 can be paid.
/// </summary>
internal static GameAction ChooseMacmillanTestAction(GameState state)
{
    var legalActions = GameEngine.GetLegalActions(state);
    var player = state.Players[state.ActivePlayer];
    var plays = legalActions
        .OfType<PlayFollowerAction>()
        .Where(action =>
            action.HandCardTargetInstanceId is null &&
            action.EnemyFollowerTargetInstanceIds is null &&
            action.ModeChoiceIndex is null &&
            action.OwnHandCardTargetInstanceIds is null)
        .ToArray();
    CardDefinition HandDefinition(PlayFollowerAction action) =>
        player.Hand.Single(card => card.InstanceId == action.CardInstanceId).Definition;

    var macmillan = plays.FirstOrDefault(action => HandDefinition(action).Id == CardIds.DeathHostMacmillan);
    if (macmillan is not null && player.Graveyard.Count >= 10)
    {
        return macmillan;
    }

    // Keep a couple of slots free for 马克米朗 itself.
    if (player.OccupiedBoardSlots < 3)
    {
        var token = plays.FirstOrDefault(action => HandDefinition(action).Id == CardIds.SkeletonSoldier);
        if (token is not null)
        {
            return token;
        }
    }

    // Trades put 亡者 followers into the graveyard, which is what the 唤灵 pays with.
    var trade = legalActions.OfType<AttackFollowerAction>().FirstOrDefault();
    if (trade is not null)
    {
        return trade;
    }

    return plays.Length > 0
        ? plays[0]
        : legalActions.OfType<EndTurnAction>().First();
}

/// <summary>
/// Checks the 唤灵_10 Fanfare and the 亡者 aura: every 亡者 follower entering on the owner's turn gets
/// +1/+0, 【突进】 and 【守护】, and pings the opponent's leader.
/// </summary>
internal static void AuditMacmillanStep(
    GameState before,
    GameState after,
    GameAction action,
    ref int paidSummonChecks,
    ref int unpaidSummonChecks,
    ref int empowerChecks)
{
    var player = before.ActivePlayer;
    var opponent = player == 0 ? 1 : 0;

    if (action is PlayFollowerAction play)
    {
        var handCard = before.Players[player].Hand.Single(card => card.InstanceId == play.CardInstanceId);
        if (handCard.Definition.Id == CardIds.DeathHostMacmillan)
        {
            var graveyardBefore = before.Players[player].Graveyard.Count;
            var summoned = after.Players[player].Board.Count(follower =>
                follower.Definition.Id == CardIds.RottenZombie &&
                before.Players[player].Board.All(previous => previous.InstanceId != follower.InstanceId));
            if (graveyardBefore >= 10)
            {
                var room = PlayerState.BoardLimit - (before.Players[player].OccupiedBoardSlots + 1);
                if (summoned != Math.Clamp(room, 0, 3))
                {
                    throw new InvalidOperationException("A paid 【唤灵_10】 must summon 3 腐臭的僵尸 when there is room.");
                }

                if (after.Players[player].Graveyard.Count != graveyardBefore - 10)
                {
                    throw new InvalidOperationException("A paid 【唤灵_10】 must consume exactly 10 graveyard cards.");
                }

                paidSummonChecks++;
            }
            else
            {
                if (summoned != 0 || after.Players[player].Graveyard.Count != graveyardBefore)
                {
                    throw new InvalidOperationException("An unpaid 【唤灵_10】 must summon nothing and leave the graveyard alone.");
                }

                unpaidSummonChecks++;
            }
        }
    }

    // The aura: 亡者 followers entering on the owner's turn are empowered, and each ping deals 1.
    var enteredTraitFollowers = after.Players[player].Board
        .Where(follower => before.Players[player].Board.All(previous => previous.InstanceId != follower.InstanceId) &&
                           follower.Definition.Traits?.Contains("亡者", StringComparer.Ordinal) == true)
        .ToArray();
    var macmillanInPlay = after.Players[player].Board.Any(follower =>
        follower.Definition.Id == CardIds.DeathHostMacmillan);
    if (enteredTraitFollowers.Length > 0 && macmillanInPlay)
    {
        foreach (var follower in enteredTraitFollowers)
        {
            var definition = follower.Definition;
            if (follower.Attack != definition.Attack + 1 ||
                follower.MaxDefense != definition.Defense ||
                !follower.HasRush ||
                !follower.HasWard)
            {
                throw new InvalidOperationException(
                    "A 亡者 follower entering play must get +1/+0 with 【突进】 and 【守护】 from 马克米朗.");
            }
        }

        var pings = Math.Min(
            enteredTraitFollowers.Length,
            before.Players[opponent].Health - after.Players[opponent].Health);
        if (pings != enteredTraitFollowers.Length)
        {
            throw new InvalidOperationException(
                "Each empowered 亡者 follower must deal 1 damage to the opponent's leader.");
        }

        empowerChecks++;
    }
}

internal static void RunDepartingAspirationTest()
{
    var fencing = CardCatalog.Get(CardIds.DepartingAspirationFencingAndMutsuki);
    var skeleton = CardCatalog.Get(CardIds.SkeletonSoldier);
    var fencingDeck = new DeckDefinition(
        "出发的憧憬测试",
        Enumerable.Repeat(fencing, DeckDefinition.RequiredCardCount));
    var skeletonDeck = new DeckDefinition(
        "出发的憧憬对手测试",
        Enumerable.Repeat(skeleton, DeckDefinition.RequiredCardCount));

    var state = GameEngine.CreateGame(fencingDeck, skeletonDeck, seed: 30_089);
    while (state.Phase == GamePhase.Mulligan)
    {
        state = GameEngine.Apply(state, new MulliganAction([]));
    }

    var fanfareLeaderChecks = 0;
    var fanfareSweepChecks = 0;
    var evolutionDrawChecks = 0;
    var evolutionPlayPointChecks = 0;
    var playIndex = 0;
    var evolveIndex = 0;

    for (var step = 0; step < 200 && !state.IsGameOver; step++)
    {
        var action = ChooseFencingTestAction(state, ref playIndex, ref evolveIndex);
        var before = state;
        state = GameEngine.Apply(state, action);
        AuditFencingStep(
            before,
            state,
            action,
            ref fanfareLeaderChecks,
            ref fanfareSweepChecks,
            ref evolutionDrawChecks,
            ref evolutionPlayPointChecks);
    }

    if (fanfareLeaderChecks == 0 || fanfareSweepChecks == 0 ||
        evolutionDrawChecks == 0 || evolutionPlayPointChecks == 0)
    {
        throw new InvalidOperationException(
            $"出发的憧憬 was not fully exercised: leader {fanfareLeaderChecks}, sweep {fanfareSweepChecks}, draw {evolutionDrawChecks}, play points {evolutionPlayPointChecks}.");
    }

    Console.WriteLine("Departing aspiration test passed.");
    Console.WriteLine($"【入场曲】模式(1) 对主战者4点伤害＋回复4点生命：已验证 {fanfareLeaderChecks} 次。");
    Console.WriteLine($"【入场曲】模式(2) 敌方全体5点伤害＋回复1点进化点（上限2）：已验证 {fanfareSweepChecks} 次。");
    Console.WriteLine($"【进化时】模式(1) 抽取2张卡牌：已验证 {evolutionDrawChecks} 次。");
    Console.WriteLine($"【进化时】模式(2) 回复2点能量点（不超过最大PP）：已验证 {evolutionPlayPointChecks} 次。");
}

/// <summary>Plays and evolves 出发的憧憬, alternating its modes.</summary>
internal static GameAction ChooseFencingTestAction(GameState state, ref int playIndex, ref int evolveIndex)
{
    var legalActions = GameEngine.GetLegalActions(state);
    var plays = legalActions
        .OfType<PlayFollowerAction>()
        .Where(action => action.ModeChoiceIndex is not null)
        .ToArray();
    if (plays.Length > 0)
    {
        var desiredMode = playIndex % 2;
        var action = plays.FirstOrDefault(candidate => candidate.ModeChoiceIndex == desiredMode) ?? plays[0];
        playIndex++;
        return action;
    }

    var evolves = legalActions.OfType<EvolveAction>().ToArray();
    if (evolves.Length > 0)
    {
        var desiredMode = evolveIndex % 2;
        var action = evolves.FirstOrDefault(candidate => candidate.ModeChoiceIndex == desiredMode) ?? evolves[0];
        evolveIndex++;
        return action;
    }

    var play = legalActions
        .OfType<PlayFollowerAction>()
        .FirstOrDefault(action =>
            action.HandCardTargetInstanceId is null &&
            action.EnemyFollowerTargetInstanceIds is null &&
            action.OwnHandCardTargetInstanceIds is null);
    return play ?? (GameAction)legalActions.OfType<EndTurnAction>().First();
}

/// <summary>
/// Checks both Fanfare modes and both evolution modes, including the evolution-point cap of two and
/// the play-point restoration cap of the player's maximum PP.
/// </summary>
internal static void AuditFencingStep(
    GameState before,
    GameState after,
    GameAction action,
    ref int fanfareLeaderChecks,
    ref int fanfareSweepChecks,
    ref int evolutionDrawChecks,
    ref int evolutionPlayPointChecks)
{
    var player = before.ActivePlayer;
    var opponent = player == 0 ? 1 : 0;
    int? modeChoiceIndex;
    if (action is PlayFollowerAction play)
    {
        var handCard = before.Players[player].Hand.Single(card => card.InstanceId == play.CardInstanceId);
        if (handCard.Definition.Id != CardIds.DepartingAspirationFencingAndMutsuki)
        {
            return;
        }

        modeChoiceIndex = play.ModeChoiceIndex;
    }
    else if (action is EvolveAction evolve)
    {
        var evolving = before.Players[player].Board
            .Single(follower => follower.InstanceId == evolve.FollowerInstanceId);
        if (evolving.Definition.Id != CardIds.DepartingAspirationFencingAndMutsuki)
        {
            return;
        }

        modeChoiceIndex = evolve.ModeChoiceIndex;
    }
    else
    {
        return;
    }

    if (modeChoiceIndex is null)
    {
        throw new InvalidOperationException("This card must always record the mode it resolved.");
    }

    var beforePlayer = before.Players[player];
    var afterPlayer = after.Players[player];

    if (action is PlayFollowerAction)
    {
        if (modeChoiceIndex == 0)
        {
            var leaderDamage = before.Players[opponent].Health - after.Players[opponent].Health;
            if (leaderDamage != 4)
            {
                throw new InvalidOperationException("Fanfare mode (1) must deal 4 damage to the opponent's leader.");
            }

            var expectedHeal = Math.Min(4, beforePlayer.MaxHealth - beforePlayer.Health);
            if (afterPlayer.Health - beforePlayer.Health != expectedHeal)
            {
                throw new InvalidOperationException("Fanfare mode (1) must restore 4 health.");
            }

            fanfareLeaderChecks++;
            return;
        }

        foreach (var enemy in before.Players[opponent].Board)
        {
            var survivor = after.Players[opponent].Board
                .FirstOrDefault(candidate => candidate.InstanceId == enemy.InstanceId);
            if (survivor is null)
            {
                if (enemy.CurrentDefense > 5)
                {
                    throw new InvalidOperationException("Fanfare mode (2) destroyed a follower it could not kill.");
                }

                continue;
            }

            if (enemy.CurrentDefense - survivor.CurrentDefense != 5)
            {
                throw new InvalidOperationException("Fanfare mode (2) must deal 5 damage to every enemy follower.");
            }
        }

        var expectedEvolutionPoints = Math.Min(
            PlayerState.StartingEvolutionPoints,
            beforePlayer.EvolutionPoints + 1);
        if (afterPlayer.EvolutionPoints != expectedEvolutionPoints)
        {
            throw new InvalidOperationException("Fanfare mode (2) must restore one evolution point, capped at two.");
        }

        fanfareSweepChecks++;
        return;
    }

    if (modeChoiceIndex == 0)
    {
        var retained = beforePlayer.Hand.Select(card => card.InstanceId).ToHashSet();
        var drawn = afterPlayer.Hand.Count(card => !retained.Contains(card.InstanceId));
        if (afterPlayer.Hand.Count - beforePlayer.Hand.Count != 2 &&
            drawn + afterPlayer.Graveyard.Count - beforePlayer.Graveyard.Count != 2)
        {
            throw new InvalidOperationException("Evolution mode (1) must draw 2 cards.");
        }

        evolutionDrawChecks++;
        return;
    }

    var expectedPlayPoints = Math.Min(beforePlayer.MaxPlayPoints, beforePlayer.CurrentPlayPoints + 2);
    if (afterPlayer.CurrentPlayPoints != expectedPlayPoints)
    {
        throw new InvalidOperationException(
            "Evolution mode (2) must restore 2 play points without exceeding the maximum PP.");
    }

    evolutionPlayPointChecks++;
}

internal static void RunAbyssalColonelTest()
{
    var colonel = CardCatalog.Get(CardIds.AbyssalColonel);
    var silentDemon = CardCatalog.Get(CardIds.SilentDemonGeneral);
    var colonelDeck = new DeckDefinition(
        "渊底上校测试",
        Enumerable.Repeat(colonel, DeckDefinition.RequiredCardCount));
    var opponentDeck = new DeckDefinition(
        "渊底上校对手测试",
        Enumerable.Repeat(silentDemon, DeckDefinition.RequiredCardCount));

    var state = GameEngine.CreateGame(colonelDeck, opponentDeck, seed: 30_083);
    while (state.Phase == GamePhase.Mulligan)
    {
        state = GameEngine.Apply(state, new MulliganAction([]));
    }

    var crystallizeChecks = 0;
    var amuletLastWordsChecks = 0;
    var followerLastWordsChecks = 0;

    for (var step = 0; step < 200 && !state.IsGameOver; step++)
    {
        // 结晶 shares Accelerate's condition: it must never be offered while the card's own cost
        // can be paid.
        var legalActions = GameEngine.GetLegalActions(state);
        if (state.Players[state.ActivePlayer].CurrentPlayPoints >= colonel.Cost &&
            legalActions.OfType<PlayCrystallizeAction>().Any())
        {
            throw new InvalidOperationException(
                "结晶 must not be available while the card's own cost can be paid.");
        }

        var action = ChooseColonelTestAction(state);
        var before = state;
        state = GameEngine.Apply(state, action);
        AuditColonelStep(
            before,
            state,
            action,
            ref crystallizeChecks,
            ref amuletLastWordsChecks,
            ref followerLastWordsChecks);
    }

    if (crystallizeChecks == 0 || amuletLastWordsChecks == 0 || followerLastWordsChecks == 0)
    {
        throw new InvalidOperationException(
            $"渊底上校 was not fully exercised: crystallize {crystallizeChecks}, amulet last words {amuletLastWordsChecks}, follower last words {followerLastWordsChecks}.");
    }

    Console.WriteLine("Abyssal Colonel test passed.");
    Console.WriteLine($"【结晶 2】以护符形态打出，带【吟唱_4】：已验证 {crystallizeChecks} 次（剩余费用已达本体费用时不会提供这个选项）。");
    Console.WriteLine($"结晶护符倒计时归零被破坏后召唤本体（且不带本体的破坏＋回血）：已验证 {amuletLastWordsChecks} 次。");
    Console.WriteLine($"本体【谢幕曲】破坏对手随机1个随从并回复2点生命：已验证 {followerLastWordsChecks} 次。");
}

/// <summary>结晶 when possible, otherwise plays 渊底上校 and trades it away to fire its Last Words.</summary>
internal static GameAction ChooseColonelTestAction(GameState state)
{
    var legalActions = GameEngine.GetLegalActions(state);
    var crystallize = legalActions.OfType<PlayCrystallizeAction>().FirstOrDefault();
    if (crystallize is not null)
    {
        return crystallize;
    }

    var play = legalActions
        .OfType<PlayFollowerAction>()
        .FirstOrDefault(action =>
            action.HandCardTargetInstanceId is null &&
            action.EnemyFollowerTargetInstanceIds is null &&
            action.ModeChoiceIndex is null &&
            action.OwnHandCardTargetInstanceIds is null);
    if (play is not null)
    {
        return play;
    }

    var trade = legalActions
        .OfType<AttackFollowerAction>()
        .FirstOrDefault(action => state.Players[state.ActivePlayer].Board
            .Single(follower => follower.InstanceId == action.AttackerInstanceId)
            .Definition.Id == CardIds.AbyssalColonel);
    return trade ?? (GameAction)legalActions.OfType<EndTurnAction>().First();
}

/// <summary>
/// Checks the 结晶 play, the crystallized amulet's destruction (summon only) and the follower form's
/// printed Last Words (destroy one random enemy follower, then restore 2 health).
/// </summary>
internal static void AuditColonelStep(
    GameState before,
    GameState after,
    GameAction action,
    ref int crystallizeChecks,
    ref int amuletLastWordsChecks,
    ref int followerLastWordsChecks)
{
    var player = before.ActivePlayer;
    var opponent = player == 0 ? 1 : 0;

    if (action is PlayCrystallizeAction crystallize)
    {
        var handCard = before.Players[player].Hand.Single(card => card.InstanceId == crystallize.CardInstanceId);
        if (handCard.Definition.Type != CardType.Follower)
        {
            throw new InvalidOperationException("The crystallized card must be a follower card played as an amulet.");
        }

        var amulet = after.Players[player].Amulets
            .SingleOrDefault(candidate => candidate.InstanceId == crystallize.CardInstanceId)
            ?? throw new InvalidOperationException("结晶 must put the card on the board as an amulet.");
        if (amulet.Countdown != 4)
        {
            throw new InvalidOperationException("The crystallized amulet must start with 【吟唱 4】.");
        }

        if (after.Players[player].Board.Any(follower => follower.InstanceId == crystallize.CardInstanceId))
        {
            throw new InvalidOperationException("结晶 must not place a follower on the board.");
        }

        var spent = before.Players[player].CurrentPlayPoints - after.Players[player].CurrentPlayPoints;
        if (spent != 2)
        {
            throw new InvalidOperationException("结晶 must cost its printed 2 PP.");
        }

        crystallizeChecks++;
        return;
    }

    if (action is EndTurnAction)
    {
        // The engine decreases an amulet's Countdown at the start of its owner's turn, which happens
        // while the opponent ends their turn, so both sides are checked here.
        for (var owner = 0; owner < 2; owner++)
        {
            var destroyedAmulets = before.Players[owner].Amulets
                .Where(amulet => amulet.Crystallized is not null &&
                                 after.Players[owner].Amulets.All(candidate => candidate.InstanceId != amulet.InstanceId))
                .ToArray();
            if (destroyedAmulets.Length == 0)
            {
                continue;
            }

            var summoned = after.Players[owner].Board
                .Where(follower => follower.Definition.Id == CardIds.AbyssalColonel &&
                                   before.Players[owner].Board.All(previous =>
                                       previous.InstanceId != follower.InstanceId))
                .ToArray();
            // Every destroyed amulet summons one follower, or fewer when the board has no room left.
            if (summoned.Length == 0 || summoned.Length > destroyedAmulets.Length)
            {
                throw new InvalidOperationException(
                    $"The crystallized amulet's Last Words must summon 渊底上校: destroyed {destroyedAmulets.Length}, summoned {summoned.Length}.");
            }

            foreach (var follower in summoned)
            {
                if (follower.Attack != 4 || follower.MaxDefense != 6 || !follower.HasWard)
                {
                    throw new InvalidOperationException("The summoned follower must be a 4/6 渊底上校 with 【守护】.");
                }
            }

            foreach (var amulet in destroyedAmulets)
            {
                if (!after.Players[owner].Graveyard.Any(card => card.InstanceId == amulet.InstanceId))
                {
                    throw new InvalidOperationException("A destroyed amulet must go to its owner's graveyard.");
                }
            }

            // The crystallized form keeps only 【吟唱_4】 and its summon: no 【守护】 on the board and
            // none of the follower's printed Last Words.
            var amuletOpponent = owner == 0 ? 1 : 0;
            if (after.Players[amuletOpponent].Board.Count != before.Players[amuletOpponent].Board.Count)
            {
                throw new InvalidOperationException("The crystallized amulet must not destroy an enemy follower.");
            }

            if (after.Players[owner].Health != before.Players[owner].Health)
            {
                throw new InvalidOperationException("The crystallized amulet must not restore its leader's health.");
            }

            amuletLastWordsChecks++;
        }
    }

    foreach (var lost in before.Players[player].Board.Where(follower =>
                 follower.Definition.Id == CardIds.AbyssalColonel &&
                 after.Players[player].Board.All(candidate => candidate.InstanceId != follower.InstanceId)))
    {
        var enemiesBefore = before.Players[opponent].Board.Count;
        var enemiesAfter = after.Players[opponent].Board.Count;
        if (enemiesBefore > 0 && enemiesAfter != enemiesBefore - 1)
        {
            throw new InvalidOperationException(
                "渊底上校's Last Words must destroy exactly one random enemy follower.");
        }

        var expectedHeal = Math.Min(2, before.Players[player].MaxHealth - before.Players[player].Health);
        if (after.Players[player].Health - before.Players[player].Health != expectedHeal)
        {
            throw new InvalidOperationException(
                "渊底上校's Last Words must restore 2 health to its controller's leader.");
        }

        followerLastWordsChecks++;
    }
}

internal static void RunDeathbedAnathemaTest()
{
    var tohime = CardCatalog.Get(CardIds.DeathbedAnathemaTohime);
    var lilim = CardCatalog.Get(CardIds.CuteDemonLilim);
    var bat = CardCatalog.Get(CardIds.Bat);
    var knight = CardCatalog.Get(CardIds.Knight);
    var deck = new DeckDefinition(
        "傍死的安纳提玛·徒姬测试",
        Enumerable.Range(0, DeckDefinition.RequiredCardCount).Select(index => (index % 5) switch
        {
            0 => tohime,
            1 => lilim,
            2 => bat,
            3 => knight,
            _ => tohime
        }));
    var opponentDeck = new DeckDefinition(
        "徒姬对手测试",
        Enumerable.Repeat(knight, DeckDefinition.RequiredCardCount));

    var state = GameEngine.CreateGame(deck, opponentDeck, seed: 30_079);
    while (state.Phase == GamePhase.Mulligan)
    {
        state = GameEngine.Apply(state, new MulliganAction([]));
    }

    var fanfareChecks = 0;
    var rushChecks = 0;
    var nonNightmareRushChecks = 0;
    var superEvolutionChecks = 0;

    for (var step = 0; step < 200 && !state.IsGameOver; step++)
    {
        var action = ChooseTohimeTestAction(state);
        var before = state;
        state = GameEngine.Apply(state, action);
        AuditTohimeStep(
            before,
            state,
            action,
            ref fanfareChecks,
            ref rushChecks,
            ref nonNightmareRushChecks,
            ref superEvolutionChecks);
    }

    if (fanfareChecks == 0 || rushChecks == 0 || nonNightmareRushChecks == 0 || superEvolutionChecks == 0)
    {
        throw new InvalidOperationException(
            $"徒姬 was not fully exercised: fanfare {fanfareChecks}, rush {rushChecks}, non-Nightmare {nonNightmareRushChecks}, super evolution {superEvolutionChecks}.");
    }

    Console.WriteLine("Deathbed Anathema Tohime test passed.");
    Console.WriteLine($"【入场曲】从牌组随机召唤2种费用≤2的梦魇随从（不同种类、从牌组移出）：已验证 {fanfareChecks} 次。");
    Console.WriteLine($"「其他梦魇随从进入战场获得【突进】」：已验证 {rushChecks} 次。");
    Console.WriteLine($"非梦魇随从不会被授予【突进】：已验证 {nonNightmareRushChecks} 次。");
    Console.WriteLine($"【超进化时】只给其他梦魇随从 +2/+2：已验证 {superEvolutionChecks} 次。");
}

/// <summary>Plays a follower and super evolves 徒姬 as soon as super evolution is available.</summary>
internal static GameAction ChooseTohimeTestAction(GameState state)
{
    var legalActions = GameEngine.GetLegalActions(state);
    var player = state.Players[state.ActivePlayer];
    var tohimeInPlay = player.Board.Any(follower =>
        follower.Definition.Id == CardIds.DeathbedAnathemaTohime);

    // Prefer super evolving 徒姬 itself.
    var superEvolve = legalActions
        .OfType<SuperEvolveAction>()
        .FirstOrDefault(action => player.Board
            .Single(follower => follower.InstanceId == action.FollowerInstanceId)
            .Definition.Id == CardIds.DeathbedAnathemaTohime);
    if (superEvolve is not null)
    {
        return superEvolve;
    }

    var plays = legalActions
        .OfType<PlayFollowerAction>()
        .Where(action =>
            action.HandCardTargetInstanceId is null &&
            action.EnemyFollowerTargetInstanceIds is null &&
            action.ModeChoiceIndex is null &&
            action.OwnHandCardTargetInstanceIds is null)
        .ToArray();
    CardDefinition HandDefinition(PlayFollowerAction action) =>
        player.Hand.Single(card => card.InstanceId == action.CardInstanceId).Definition;

    // Play 徒姬 first: it needs a free board slot and its Fanfare pulls candidates out of the deck.
    var tohime = plays.FirstOrDefault(action => HandDefinition(action).Id == CardIds.DeathbedAnathemaTohime);
    if (tohime is not null)
    {
        return tohime;
    }

    // Once 徒姬 is in play, a non-梦魇 follower checks the aura's profession filter.
    if (tohimeInPlay)
    {
        var otherProfession = plays.FirstOrDefault(action =>
            HandDefinition(action).Profession != CardProfession.Nightmare);
        if (otherProfession is not null)
        {
            return otherProfession;
        }
    }

    return legalActions.OfType<EndTurnAction>().First();
}

/// <summary>Distinct 梦魇 follower kinds with cost 2 or less that are still in the deck.</summary>
internal static string[] EligibleNightmareDeckKinds(PlayerState player) => player.Deck
    .Where(card => card.Definition.Type == CardType.Follower &&
                   card.Definition.Profession == CardProfession.Nightmare &&
                   card.Definition.Cost <= 2)
    .Select(card => card.Definition.Id)
    .Distinct(StringComparer.Ordinal)
    .ToArray();

/// <summary>
/// Checks 徒姬's deck summon, its 【突进】 aura for other 梦魇 followers and its profession-filtered
/// 【超进化时】 stat buff.
/// </summary>
internal static void AuditTohimeStep(
    GameState before,
    GameState after,
    GameAction action,
    ref int fanfareChecks,
    ref int rushChecks,
    ref int nonNightmareRushChecks,
    ref int superEvolutionChecks)
{
    var player = before.ActivePlayer;
    var boardBefore = before.Players[player].Board;
    var entered = after.Players[player].Board
        .Where(follower => boardBefore.All(previous => previous.InstanceId != follower.InstanceId))
        .ToArray();

    // Every follower that enters while 徒姬 is in play gains 【突进】 only when it is a 梦魇 follower.
    var tohimeInPlay = after.Players[player].Board
        .Any(follower => follower.Definition.Id == CardIds.DeathbedAnathemaTohime);
    foreach (var follower in entered)
    {
        if (follower.Definition.Id == CardIds.DeathbedAnathemaTohime)
        {
            if (follower.HasRush)
            {
                throw new InvalidOperationException("徒姬 must not grant 【突进】 to itself.");
            }

            continue;
        }

        if (!tohimeInPlay)
        {
            continue;
        }

        if (follower.Definition.Profession == CardProfession.Nightmare)
        {
            if (!follower.HasRush)
            {
                throw new InvalidOperationException("A 梦魇 follower entering play must gain 【突进】 from 徒姬.");
            }

            rushChecks++;
        }
        else
        {
            if (follower.HasRush)
            {
                throw new InvalidOperationException("A non-梦魇 follower must not gain 【突进】 from 徒姬.");
            }

            nonNightmareRushChecks++;
        }
    }

    if (action is PlayFollowerAction play)
    {
        var playedCard = before.Players[player].Hand.Single(card => card.InstanceId == play.CardInstanceId);
        if (playedCard.Definition.Id != CardIds.DeathbedAnathemaTohime)
        {
            return;
        }

        var summoned = entered
            .Where(follower => follower.InstanceId != play.CardInstanceId)
            .ToArray();
        var kindsBefore = EligibleNightmareDeckKinds(before.Players[player]);
        var room = PlayerState.BoardLimit - (before.Players[player].OccupiedBoardSlots + 1);
        var expected = Math.Clamp(Math.Min(kindsBefore.Length, room), 0, 2);
        if (summoned.Length != expected)
        {
            throw new InvalidOperationException(
                $"The Fanfare must summon {expected} follower(s) from the deck, but summoned {summoned.Length}.");
        }

        if (summoned.Length == 2 && summoned[0].Definition.Id == summoned[1].Definition.Id)
        {
            throw new InvalidOperationException("The Fanfare must summon two different kinds.");
        }

        foreach (var follower in summoned)
        {
            if (follower.Definition.Profession != CardProfession.Nightmare || follower.Definition.Cost > 2)
            {
                throw new InvalidOperationException("Only 梦魇 followers costing 2 or less may be summoned.");
            }

            if (before.Players[player].Deck.Any(card => card.InstanceId == follower.InstanceId) == false)
            {
                throw new InvalidOperationException("A summoned follower must come from the controller's deck.");
            }

            if (after.Players[player].Deck.Any(card => card.InstanceId == follower.InstanceId))
            {
                throw new InvalidOperationException("A summoned follower must leave the deck.");
            }
        }

        fanfareChecks++;
        return;
    }

    if (action is not SuperEvolveAction superEvolve)
    {
        return;
    }

    var superEvolved = before.Players[player].Board
        .Single(follower => follower.InstanceId == superEvolve.FollowerInstanceId);
    if (superEvolved.Definition.Id != CardIds.DeathbedAnathemaTohime)
    {
        return;
    }

    foreach (var follower in after.Players[player].Board)
    {
        if (follower.InstanceId == superEvolve.FollowerInstanceId)
        {
            continue;
        }

        var previous = boardBefore.FirstOrDefault(candidate => candidate.InstanceId == follower.InstanceId);
        if (previous is null)
        {
            continue;
        }

        var attackGain = follower.Attack - previous.Attack;
        var defenseGain = follower.MaxDefense - previous.MaxDefense;
        if (follower.Definition.Profession == CardProfession.Nightmare)
        {
            if (attackGain != 2 || defenseGain != 2)
            {
                throw new InvalidOperationException("【超进化时】 must give other 梦魇 followers +2/+2.");
            }
        }
        else if (attackGain != 0 || defenseGain != 0)
        {
            throw new InvalidOperationException("【超进化时】 must not strengthen followers of other professions.");
        }
    }

    superEvolutionChecks++;
}

internal static void RunTightropeWalkerTest()
{
    var walker = CardCatalog.Get(CardIds.CatTightropeWalker);
    var swordsman = CardCatalog.Get(CardIds.Gladiator);
    var walkerDeck = new DeckDefinition(
        "猫咪走绳师测试",
        Enumerable.Repeat(walker, DeckDefinition.RequiredCardCount));
    var targetDeck = new DeckDefinition(
        "走绳师靶子测试",
        Enumerable.Repeat(swordsman, DeckDefinition.RequiredCardCount));

    var state = GameEngine.CreateGame(walkerDeck, targetDeck, seed: 30_077);
    while (state.Phase == GamePhase.Mulligan)
    {
        state = GameEngine.Apply(state, new MulliganAction([]));
    }

    var fanfareChecks = 0;
    var evolutionChecks = 0;

    for (var step = 0; step < 200 && !state.IsGameOver; step++)
    {
        var action = ChooseFollowerPlayThenEvolveAction(state);
        var before = state;
        state = GameEngine.Apply(state, action);
        AuditTightropeStep(before, state, action, ref fanfareChecks, ref evolutionChecks);
    }

    if (fanfareChecks == 0 || evolutionChecks == 0)
    {
        throw new InvalidOperationException(
            $"猫咪走绳师 was not fully exercised: fanfare {fanfareChecks}, evolution {evolutionChecks}.");
    }

    Console.WriteLine("Cat tightrope walker test passed.");
    Console.WriteLine($"【入场曲】对指定敌方随从造成3点伤害并召唤骸骨士兵：已验证 {fanfareChecks} 次。");
    Console.WriteLine($"【进化时】发动与入场曲相同的能力：已验证 {evolutionChecks} 次。");
}

/// <summary>Plays a follower, then evolves when an evolution point is available, and never attacks.</summary>
internal static GameAction ChooseFollowerPlayThenEvolveAction(GameState state)
{
    var legalActions = GameEngine.GetLegalActions(state);
    var play = legalActions
        .OfType<PlayFollowerAction>()
        .FirstOrDefault(action =>
            action.HandCardTargetInstanceId is null &&
            action.EnemyFollowerTargetInstanceIds is not null &&
            action.ModeChoiceIndex is null &&
            action.OwnHandCardTargetInstanceIds is null) ??
        legalActions
            .OfType<PlayFollowerAction>()
            .FirstOrDefault(action =>
                action.HandCardTargetInstanceId is null &&
                action.EnemyFollowerTargetInstanceIds is null &&
                action.ModeChoiceIndex is null &&
                action.OwnHandCardTargetInstanceIds is null);
    if (play is not null)
    {
        return play;
    }

    var evolve = legalActions.OfType<EvolveAction>().FirstOrDefault();
    return evolve ?? (GameAction)legalActions.OfType<EndTurnAction>().First();
}

/// <summary>
/// Checks that 猫咪走绳师's Fanfare damages the chosen enemy follower for 3, summons one 骸骨士兵, and
/// that evolving it repeats both halves of the Fanfare.
/// </summary>
internal static void AuditTightropeStep(
    GameState before,
    GameState after,
    GameAction action,
    ref int fanfareChecks,
    ref int evolutionChecks)
{
    var player = before.ActivePlayer;
    var opponent = player == 0 ? 1 : 0;
    int? targetInstanceId;
    switch (action)
    {
        case PlayFollowerAction play:
            var handCard = before.Players[player].Hand.Single(card => card.InstanceId == play.CardInstanceId);
            if (handCard.Definition.Id != CardIds.CatTightropeWalker)
            {
                return;
            }

            targetInstanceId = play.EnemyFollowerTargetInstanceIds is { Count: > 0 } playTargets
                ? playTargets[0]
                : null;
            break;
        case EvolveAction evolve:
            var evolving = before.Players[player].Board
                .Single(follower => follower.InstanceId == evolve.FollowerInstanceId);
            if (evolving.Definition.Id != CardIds.CatTightropeWalker)
            {
                return;
            }

            targetInstanceId = evolve.EnemyFollowerTargetInstanceId;
            break;
        default:
            return;
    }

    // The damage half: the chosen follower must take exactly 3, or die if it had no more than 3.
    var enemiesBefore = before.Players[opponent].Board;
    if (targetInstanceId is null)
    {
        if (enemiesBefore.Count > 0)
        {
            throw new InvalidOperationException("A legal enemy follower existed, so one must have been chosen.");
        }
    }
    else
    {
        var targetBefore = enemiesBefore.Single(follower => follower.InstanceId == targetInstanceId.Value);
        var targetAfter = after.Players[opponent].Board
            .SingleOrDefault(follower => follower.InstanceId == targetInstanceId.Value);
        var damage = targetAfter is null
            ? targetBefore.CurrentDefense
            : targetBefore.CurrentDefense - targetAfter.CurrentDefense;
        if (targetAfter is not null ? damage != 3 : targetBefore.CurrentDefense > 3)
        {
            throw new InvalidOperationException("The Fanfare must deal exactly 3 damage to the chosen follower.");
        }
    }

    // The summon half: one 骸骨士兵 appears unless the board had no room.
    var roomForSkeleton = PlayerState.BoardLimit -
        (before.Players[player].OccupiedBoardSlots + (action is PlayFollowerAction ? 1 : 0));
    var skeletons = after.Players[player].Board.Count(follower =>
        follower.Definition.Id == CardIds.SkeletonSoldier &&
        before.Players[player].Board.All(previous => previous.InstanceId != follower.InstanceId));
    if (skeletons != Math.Min(1, Math.Max(0, roomForSkeleton)))
    {
        throw new InvalidOperationException("The Fanfare must summon one 骸骨士兵 when the board has room.");
    }

    if (action is PlayFollowerAction)
    {
        fanfareChecks++;
    }
    else
    {
        evolutionChecks++;
    }
}

internal static void RunHeavenEyeTest()
{
    var bibati = CardCatalog.Get(CardIds.AncientHeavenEyeBibati);
    var abyss = CardCatalog.Get(CardIds.HeavenEyeAbyss);
    var concert = CardCatalog.Get(CardIds.NightSongConcert);
    var paidDeck = new DeckDefinition(
        "比芭提（墓地充足）测试",
        Enumerable.Range(0, DeckDefinition.RequiredCardCount)
            .Select(index => index % 3 == 0 ? concert : bibati));
    var bareDeck = new DeckDefinition(
        "比芭提（墓地不足）测试",
        Enumerable.Repeat(bibati, DeckDefinition.RequiredCardCount));

    var abilityEvolutionChecks = 0;
    var unpaidChecks = 0;
    var manualEvolutionChecks = 0;

    // Scenario 1: a 1-cost spell keeps the graveyard large enough for 【唤灵_4】.
    var paidState = GameEngine.CreateGame(paidDeck, bareDeck, seed: 30_071);
    while (paidState.Phase == GamePhase.Mulligan)
    {
        paidState = GameEngine.Apply(paidState, new MulliganAction([]));
    }

    for (var step = 0; step < 120 && !paidState.IsGameOver; step++)
    {
        var action = ChoosePaidHeavenEyeAction(paidState);
        var before = paidState;
        paidState = GameEngine.Apply(paidState, action);
        AuditHeavenEyeStep(
            before,
            paidState,
            action,
            ref unpaidChecks,
            ref abilityEvolutionChecks,
            ref manualEvolutionChecks);
    }

    // Scenario 2: no spells at all, so the Fanfare stays unpaid and the manual evolution is the
    // only way 「本随从进化时」 can fire.
    var bareState = GameEngine.CreateGame(bareDeck, bareDeck, seed: 30_073);
    while (bareState.Phase == GamePhase.Mulligan)
    {
        bareState = GameEngine.Apply(bareState, new MulliganAction([]));
    }

    for (var step = 0; step < 120 && !bareState.IsGameOver; step++)
    {
        var action = ChooseBareHeavenEyeAction(bareState);
        var before = bareState;
        bareState = GameEngine.Apply(bareState, action);
        AuditHeavenEyeStep(
            before,
            bareState,
            action,
            ref unpaidChecks,
            ref abilityEvolutionChecks,
            ref manualEvolutionChecks);
    }

    if (abilityEvolutionChecks == 0 || unpaidChecks == 0 || manualEvolutionChecks == 0)
    {
        throw new InvalidOperationException(
            $"比芭提 was not fully exercised: ability evolution {abilityEvolutionChecks}, unpaid {unpaidChecks}, manual evolution {manualEvolutionChecks}.");
    }

    Console.WriteLine("Ancient Heaven Eye Bibati test passed.");
    Console.WriteLine($"【入场曲】【唤灵_4】本随从进化：墓地足够时核对 {abilityEvolutionChecks} 次（消耗4张、+2/+2、并获得天眼深渊）。");
    Console.WriteLine($"墓地不足时【唤灵_4】不发动、随从保持进化前：核对 {unpaidChecks} 次。");
    Console.WriteLine($"「本随从进化时」在手动进化时同样触发：核对 {manualEvolutionChecks} 次。");
}

/// <summary>Builds the graveyard with the token spell first, then plays 比芭提.</summary>
internal static GameAction ChoosePaidHeavenEyeAction(GameState state)
{
    var legalActions = GameEngine.GetLegalActions(state);
    var spells = legalActions
        .OfType<PlaySpellAction>()
        .Where(action => action.Target is null && action.OwnHandCardTargetInstanceIds is null)
        .ToArray();
    var followers = legalActions
        .OfType<PlayFollowerAction>()
        .Where(action =>
            action.HandCardTargetInstanceId is null &&
            action.EnemyFollowerTargetInstanceIds is null &&
            action.ModeChoiceIndex is null &&
            action.OwnHandCardTargetInstanceIds is null)
        .ToArray();

    // As soon as the graveyard can pay 【唤灵_4】, play 比芭提 so the ability evolution is exercised.
    if (state.Players[state.ActivePlayer].Graveyard.Count >= 4 && followers.Length > 0)
    {
        return followers[0];
    }

    if (spells.Length > 0)
    {
        return spells[0];
    }

    return followers.Length > 0
        ? followers[0]
        : legalActions.OfType<EndTurnAction>().First();
}

/// <summary>Plays 比芭提 and evolves it manually as soon as an evolution point is available.</summary>
internal static GameAction ChooseBareHeavenEyeAction(GameState state)
{
    var legalActions = GameEngine.GetLegalActions(state);
    var follower = legalActions
        .OfType<PlayFollowerAction>()
        .FirstOrDefault(action =>
            action.HandCardTargetInstanceId is null &&
            action.EnemyFollowerTargetInstanceIds is null &&
            action.ModeChoiceIndex is null &&
            action.OwnHandCardTargetInstanceIds is null);
    if (follower is not null)
    {
        return follower;
    }

    var evolve = legalActions.OfType<EvolveAction>().FirstOrDefault();
    return evolve ?? (GameAction)legalActions.OfType<EndTurnAction>().First();
}

internal static int CountHeavenEyeAbyssInHand(PlayerState player) =>
    player.Hand.Count(card => card.Definition.Id == CardIds.HeavenEyeAbyss);

/// <summary>
/// Checks 比芭提's 【唤灵_4】 ability evolution and its 「本随从进化时」 trigger, which must fire for
/// ability evolutions and manual evolutions alike.
/// </summary>
internal static void AuditHeavenEyeStep(
    GameState before,
    GameState after,
    GameAction action,
    ref int unpaidChecks,
    ref int abilityEvolutionChecks,
    ref int manualEvolutionChecks)
{
    var player = before.ActivePlayer;
    var graveyardBefore = before.Players[player].Graveyard.Count;
    var graveyardAfter = after.Players[player].Graveyard.Count;
    var abyssBefore = CountHeavenEyeAbyssInHand(before.Players[player]);
    var abyssAfter = CountHeavenEyeAbyssInHand(after.Players[player]);

    if (action is PlayFollowerAction play)
    {
        var handCard = before.Players[player].Hand.Single(card => card.InstanceId == play.CardInstanceId);
        if (handCard.Definition.Id != CardIds.AncientHeavenEyeBibati)
        {
            return;
        }

        var played = after.Players[player].Board.Single(follower => follower.InstanceId == play.CardInstanceId);
        var definition = played.Definition;
        if (graveyardBefore >= 4)
        {
            if (played.EvolutionState != EvolutionState.Evolved ||
                played.Attack != definition.Attack + 2 ||
                played.MaxDefense != definition.Defense + 2)
            {
                throw new InvalidOperationException("A paid 【唤灵_4】 must evolve 比芭提 by the ability (+2/+2).");
            }

            if (graveyardAfter != graveyardBefore - 4)
            {
                throw new InvalidOperationException("A paid 【唤灵_4】 must consume exactly 4 graveyard cards.");
            }

            if (before.Players[player].Hand.Count >= PlayerState.HandLimit)
            {
                throw new InvalidOperationException(
                    "This test keeps the hand below its limit so the added 天眼深渊 must land in hand.");
            }

            if (abyssAfter != abyssBefore + 1)
            {
                throw new InvalidOperationException("「本随从进化时」 must add one 天眼深渊 when the ability evolves 比芭提.");
            }

            abilityEvolutionChecks++;
            return;
        }

        if (played.EvolutionState != EvolutionState.Unevolved ||
            played.Attack != definition.Attack ||
            played.MaxDefense != definition.Defense)
        {
            throw new InvalidOperationException("An unpaid 【唤灵_4】 must leave 比芭提 unevolved.");
        }

        if (abyssAfter != abyssBefore)
        {
            throw new InvalidOperationException("An unpaid 【唤灵_4】 must not add 天眼深渊.");
        }

        unpaidChecks++;
        return;
    }

    if (action is not EvolveAction evolve)
    {
        return;
    }

    var evolving = before.Players[player].Board
        .Single(follower => follower.InstanceId == evolve.FollowerInstanceId);
    if (evolving.Definition.Id != CardIds.AncientHeavenEyeBibati)
    {
        return;
    }

    var evolved = after.Players[player].Board.Single(follower => follower.InstanceId == evolve.FollowerInstanceId);
    if (evolved.EvolutionState != EvolutionState.Evolved ||
        evolved.Attack != evolving.Attack + 2 ||
        evolved.MaxDefense != evolving.MaxDefense + 2)
    {
        throw new InvalidOperationException("A manual evolution must give 比芭提 +2/+2.");
    }

    if (before.Players[player].Hand.Count >= PlayerState.HandLimit)
    {
        throw new InvalidOperationException(
            "This test keeps the hand below its limit so the added 天眼深渊 must land in hand.");
    }

    if (abyssAfter != abyssBefore + 1)
    {
        throw new InvalidOperationException("「本随从进化时」 must also fire for a manual evolution.");
    }

    manualEvolutionChecks++;
}

internal static void RunConcertTest()
{
    var concert = CardCatalog.Get(CardIds.NightSongConcert);
    var skeleton = CardCatalog.Get(CardIds.SkeletonSoldier);
    var concertDeck = new DeckDefinition(
        "夜之歌的演唱会测试",
        Enumerable.Repeat(concert, DeckDefinition.RequiredCardCount));
    var skeletonDeck = new DeckDefinition(
        "骸骨士兵测试对手",
        Enumerable.Repeat(skeleton, DeckDefinition.RequiredCardCount));

    var state = GameEngine.CreateGame(concertDeck, skeletonDeck, seed: 30_059);
    while (state.Phase == GamePhase.Mulligan)
    {
        state = GameEngine.Apply(state, new MulliganAction([]));
    }

    var paidChecks = 0;
    var skippedChecks = 0;
    var distributionChecks = 0;

    for (var step = 0; step < 400 && !state.IsGameOver; step++)
    {
        var action = ChooseConcertTestAction(state);
        var before = state;
        state = GameEngine.Apply(state, action);
        AuditConcertStep(before, state, action, ref paidChecks, ref skippedChecks, ref distributionChecks);
    }

    if (paidChecks == 0 || skippedChecks == 0 || distributionChecks == 0)
    {
        throw new InvalidOperationException(
            $"The concert was not fully exercised: paid {paidChecks}, skipped {skippedChecks}, distribution {distributionChecks}.");
    }

    Console.WriteLine("Night song concert test passed.");
    Console.WriteLine($"分配6点伤害（按登场顺序）：已验证 {distributionChecks} 次。");
    Console.WriteLine($"【唤灵_6】墓地足够时消耗 6 张并对主战者造成2点伤害：已验证 {paidChecks} 次。");
    Console.WriteLine($"【唤灵_6】墓地不足时牌照常结算、不消耗墓地也不打主战者：已验证 {skippedChecks} 次。");
}

/// <summary>Plays the cheapest card that needs no target and never attacks, so the concert's own
/// graveyard growth and the opponent's board are both predictable.</summary>
internal static GameAction ChooseConcertTestAction(GameState state)
{
    var legalActions = GameEngine.GetLegalActions(state);
    var spell = legalActions
        .OfType<PlaySpellAction>()
        .FirstOrDefault(action => action.Target is null && action.OwnHandCardTargetInstanceIds is null);
    if (spell is not null)
    {
        return spell;
    }

    var follower = legalActions
        .OfType<PlayFollowerAction>()
        .FirstOrDefault(action =>
            action.HandCardTargetInstanceId is null &&
            action.EnemyFollowerTargetInstanceIds is null &&
            action.ModeChoiceIndex is null &&
            action.OwnHandCardTargetInstanceIds is null);
    if (follower is not null)
    {
        return follower;
    }

    return legalActions.OfType<EndTurnAction>().First();
}

/// <summary>
/// Checks every 夜之歌的演唱会: the 6 damage is distributed by entry order, and the 【唤灵_6】 part
/// only consumes 6 graveyard cards and hits the enemy leader when the graveyard is large enough.
/// </summary>
internal static void AuditConcertStep(
    GameState before,
    GameState after,
    GameAction action,
    ref int paidChecks,
    ref int skippedChecks,
    ref int distributionChecks)
{
    if (action is not PlaySpellAction play)
    {
        return;
    }

    var player = before.ActivePlayer;
    var handCard = before.Players[player].Hand.Single(card => card.InstanceId == play.CardInstanceId);
    if (handCard.Definition.Id != CardIds.NightSongConcert)
    {
        return;
    }

    var opponent = player == 0 ? 1 : 0;
    var graveyardBefore = before.Players[player].Graveyard.Count;
    var graveyardAfter = after.Players[player].Graveyard.Count;
    var leaderDamage = before.Players[opponent].Health - after.Players[opponent].Health;

    var enemiesBefore = before.Players[opponent].Board;
    var distributedDamage = enemiesBefore.Sum(enemy =>
    {
        var survivor = after.Players[opponent].Board
            .FirstOrDefault(candidate => candidate.InstanceId == enemy.InstanceId);
        return survivor is null ? enemy.CurrentDefense : enemy.CurrentDefense - survivor.CurrentDefense;
    });
    var expectedDamage = Math.Min(6, enemiesBefore.Sum(enemy => enemy.CurrentDefense));

    if (distributedDamage != expectedDamage)
    {
        throw new InvalidOperationException(
            $"The concert must distribute 6 damage by entry order: expected {expectedDamage}, dealt {distributedDamage}.");
    }

    if (enemiesBefore.Count > 0)
    {
        distributionChecks++;
    }

    if (graveyardBefore >= 6)
    {
        if (leaderDamage != 2)
        {
            throw new InvalidOperationException("A paid 【唤灵_6】 must deal 2 damage to the opponent's leader.");
        }

        if (graveyardAfter != graveyardBefore - 5)
        {
            throw new InvalidOperationException(
                "A paid 【唤灵_6】 must consume exactly 6 cards; the spell itself then enters the graveyard.");
        }

        VerifyStepTranscript(before, after, action, "【唤灵_6】消耗自己墓地的 6 张卡");
        paidChecks++;
        return;
    }

    if (leaderDamage != 0)
    {
        throw new InvalidOperationException("An unpaid 【唤灵_6】 must not damage the opponent's leader.");
    }

    if (graveyardAfter != graveyardBefore + 1)
    {
        throw new InvalidOperationException("An unpaid 【唤灵_6】 must leave the graveyard untouched.");
    }

    VerifyStepTranscript(before, after, action, "此效果未发动");
    skippedChecks++;
}

internal static void RunSummonerAndTokenTest()
{
    var summoner = CardCatalog.Get(CardIds.TroublesomeSummoner);
    var summonerDeck = new DeckDefinition(
        "唤灵师效果测试",
        Enumerable.Repeat(summoner, DeckDefinition.RequiredCardCount));

    var state = GameEngine.CreateGame(summonerDeck, summonerDeck, seed: 30_053);
    while (state.Phase == GamePhase.Mulligan)
    {
        state = GameEngine.Apply(state, new MulliganAction([]));
    }

    var fanfareChecks = 0;
    var evolutionChecks = 0;
    var banishChecks = 0;
    var zombieLastWordsChecks = 0;
    var suppressedLastWordsChecks = 0;

    for (var step = 0; step < 400 && !state.IsGameOver; step++)
    {
        var action = ChooseSummonerTestAction(state);
        var before = state;
        state = GameEngine.Apply(state, action);
        AuditSummonerStep(
            before,
            state,
            action,
            ref fanfareChecks,
            ref evolutionChecks,
            ref banishChecks,
            ref zombieLastWordsChecks,
            ref suppressedLastWordsChecks);
    }

    if (fanfareChecks == 0 || evolutionChecks == 0 || banishChecks == 0 ||
        zombieLastWordsChecks == 0 || suppressedLastWordsChecks == 0)
    {
        throw new InvalidOperationException(
            $"Summoner effects were not fully exercised: fanfare {fanfareChecks}, evolution {evolutionChecks}, banish {banishChecks}, zombie last words {zombieLastWordsChecks}, suppressed {suppressedLastWordsChecks}.");
    }

    Console.WriteLine("Summoner and token test passed.");
    Console.WriteLine($"【入场曲】召唤怨灵与骸骨士兵：已验证 {fanfareChecks} 次。");
    Console.WriteLine($"【进化时】召唤腐臭的僵尸：已验证 {evolutionChecks} 次。");
    Console.WriteLine($"【消失】：已验证 {banishChecks} 次离场（未进入墓地）。");
    Console.WriteLine($"【谢幕曲】：腐臭的僵尸再生 {zombieLastWordsChecks} 次，失去【谢幕曲】的复制体再次离场 {suppressedLastWordsChecks} 次均未再生。");
}

/// <summary>Plays a summoner, evolves it when possible, and otherwise attacks.</summary>
internal static GameAction ChooseSummonerTestAction(GameState state)
{
    var legalActions = GameEngine.GetLegalActions(state);
    var play = legalActions
        .OfType<PlayFollowerAction>()
        .FirstOrDefault(action =>
            action.HandCardTargetInstanceId is null &&
            action.EnemyFollowerTargetInstanceIds is null &&
            action.ModeChoiceIndex is null &&
            action.OwnHandCardTargetInstanceIds is null);
    if (play is not null)
    {
        return play;
    }

    var evolve = legalActions.OfType<EvolveAction>().FirstOrDefault();
    if (evolve is not null)
    {
        return evolve;
    }

    GameAction? attack = legalActions.OfType<AttackFollowerAction>().FirstOrDefault() is { } followerAttack
        ? followerAttack
        : legalActions.OfType<AttackLeaderAction>().FirstOrDefault();
    return attack ?? legalActions.OfType<EndTurnAction>().First();
}

/// <summary>
/// Checks the summoner's Fanfare and Evolution summons, that 怨灵 is banished instead of buried, and
/// that 腐臭的僵尸 regenerates once before its Last Words are suppressed.
/// </summary>
internal static void AuditSummonerStep(
    GameState before,
    GameState after,
    GameAction action,
    ref int fanfareChecks,
    ref int evolutionChecks,
    ref int banishChecks,
    ref int zombieLastWordsChecks,
    ref int suppressedLastWordsChecks)
{
    var player = before.ActivePlayer;

    if (action is PlayFollowerAction play)
    {
        var playedCard = before.Players[player].Hand.Single(card => card.InstanceId == play.CardInstanceId);
        if (playedCard.Definition.Id == CardIds.TroublesomeSummoner)
        {
            var summoned = after.Players[player].Board
                .Where(follower => follower.InstanceId != play.CardInstanceId &&
                                   before.Players[player].Board.All(previous => previous.InstanceId != follower.InstanceId))
                .ToArray();
            var wraiths = summoned.Count(follower => follower.Definition.Id == CardIds.Wraith);
            var skeletons = summoned.Count(follower => follower.Definition.Id == CardIds.SkeletonSoldier);
            var occupiedAfterPlay = before.Players[player].OccupiedBoardSlots + 1;
            var expectedTokens = Math.Clamp(PlayerState.BoardLimit - occupiedAfterPlay, 0, 2);
            if (wraiths + skeletons != expectedTokens)
            {
                throw new InvalidOperationException(
                    $"The summoner's Fanfare must summon {expectedTokens} token(s) with {PlayerState.BoardLimit - occupiedAfterPlay} free slot(s), but it summoned {wraiths + skeletons}.");
            }

            if (wraiths > 0)
            {
                var wraith = summoned.First(follower => follower.Definition.Id == CardIds.Wraith);
                if (!wraith.HasStorm || wraith.Attack != 1 || wraith.MaxDefense != 1)
                {
                    throw new InvalidOperationException("怨灵 must be a 1/1 follower with 【疾驰】.");
                }
            }

            if (wraiths == 1 && skeletons == 1)
            {
                fanfareChecks++;
            }
        }
    }

    if (action is EvolveAction evolve)
    {
        var evolving = before.Players[player].Board
            .Single(follower => follower.InstanceId == evolve.FollowerInstanceId);
        if (evolving.Definition.Id == CardIds.TroublesomeSummoner)
        {
            var zombie = after.Players[player].Board
                .Where(follower => follower.Definition.Id == CardIds.RottenZombie &&
                                   before.Players[player].Board.All(previous => previous.InstanceId != follower.InstanceId))
                .ToArray();
            if (zombie.Length > 0)
            {
                if (zombie.Length != 1 || zombie[0].Attack != 2 || zombie[0].MaxDefense != 2)
                {
                    throw new InvalidOperationException("The summoner's Evolution must summon one 2/2 腐臭的僵尸.");
                }

                evolutionChecks++;
            }
        }
    }

    for (var owner = 0; owner < 2; owner++)
    {
        var beforeBoard = before.Players[owner].Board;
        var afterBoard = after.Players[owner].Board;
        var graveyard = after.Players[owner].Graveyard;
        foreach (var follower in beforeBoard)
        {
            if (afterBoard.Any(candidate => candidate.InstanceId == follower.InstanceId))
            {
                continue;
            }

            if (follower.Definition.Id == CardIds.Wraith)
            {
                if (graveyard.Any(card => card.InstanceId == follower.InstanceId))
                {
                    throw new InvalidOperationException("怨灵 must be banished instead of entering a graveyard.");
                }

                banishChecks++;
            }

            if (follower.Definition.Id != CardIds.RottenZombie)
            {
                continue;
            }

            var regenerated = afterBoard.Count(candidate =>
                candidate.Definition.Id == CardIds.RottenZombie &&
                beforeBoard.All(previous => previous.InstanceId != candidate.InstanceId));
            if (follower.Card.HasSuppressedLastWords)
            {
                if (regenerated != 0)
                {
                    throw new InvalidOperationException("A 腐臭的僵尸 that lost its Last Words must not summon anything.");
                }

                suppressedLastWordsChecks++;
                continue;
            }

            if (regenerated != 1)
            {
                throw new InvalidOperationException("腐臭的僵尸 must summon exactly one replacement when it is destroyed.");
            }

            var replacement = afterBoard.Single(candidate =>
                candidate.Definition.Id == CardIds.RottenZombie &&
                beforeBoard.All(previous => previous.InstanceId != candidate.InstanceId));
            if (!replacement.Card.HasSuppressedLastWords)
            {
                throw new InvalidOperationException("The summoned 腐臭的僵尸 must have lost its Last Words.");
            }

            zombieLastWordsChecks++;
        }
    }
}

internal static void RunBaruTest()
{
    var baru = CardCatalog.Get(CardIds.ElementalResonanceBaru);
    var baruDeck = new DeckDefinition("巴尔效果测试", Enumerable.Repeat(baru, DeckDefinition.RequiredCardCount));

    var state = GameEngine.CreateGame(baruDeck, baruDeck, seed: 30_047);
    while (state.Phase == GamePhase.Mulligan)
    {
        state = GameEngine.Apply(state, new MulliganAction([]));
    }

    var modeOneAloneChecks = 0;
    var modeOneWithAllyChecks = 0;
    var modeTwoChecks = 0;
    var modeOneTranscriptChecks = 0;
    var modeTwoTranscriptChecks = 0;
    var playIndex = 0;

    for (var step = 0; step < 200 && !state.IsGameOver; step++)
    {
        var action = ChooseBaruTestAction(state, ref playIndex);
        var before = state;
        state = GameEngine.Apply(state, action);
        AuditBaruStep(
            before,
            state,
            action,
            ref modeOneAloneChecks,
            ref modeOneWithAllyChecks,
            ref modeTwoChecks,
            ref modeOneTranscriptChecks,
            ref modeTwoTranscriptChecks);
    }

    if (modeOneAloneChecks == 0 || modeOneWithAllyChecks == 0 || modeTwoChecks == 0 ||
        modeOneTranscriptChecks == 0 || modeTwoTranscriptChecks == 0)
    {
        throw new InvalidOperationException(
            $"Baru's modes were not fully exercised: mode 1 alone {modeOneAloneChecks}, mode 1 with an ally {modeOneWithAllyChecks}, mode 2 {modeTwoChecks}, transcripts {modeOneTranscriptChecks}/{modeTwoTranscriptChecks}.");
    }

    // The rule agent scores every legal action, so it must handle both mode effects without throwing.
    var agentState = GameEngine.CreateGame(baruDeck, baruDeck, seed: 30_049);
    while (agentState.Phase == GamePhase.Mulligan)
    {
        agentState = GameEngine.Apply(agentState, new MulliganAction([]));
    }

    var agent = new GreedyPlayerAgent();
    var agentActions = 0;
    for (var step = 0; step < 40 && !agentState.IsGameOver; step++)
    {
        var observation = GameEngine.ToObservation(agentState, agentState.ActivePlayer);
        var action = agent.ChooseAction(observation, GameEngine.GetLegalActions(agentState));
        agentState = GameEngine.Apply(agentState, action);
        agentActions++;
    }

    if (agentActions == 0)
    {
        throw new InvalidOperationException("The rule agent did not play any action with Baru in hand.");
    }

    Console.WriteLine("Baru mode test passed.");
    Console.WriteLine($"【模式】(1) 本随从与随机1个其他随从+1/+1：单独站场 {modeOneAloneChecks} 次、有其他随从 {modeOneWithAllyChecks} 次均已核对。");
    Console.WriteLine($"【模式】(2) 对随机1个敌方随从造成3点伤害：已验证 {modeTwoChecks} 次。");
    Console.WriteLine($"规则牌手连续决策 {agentActions} 步未出现无法评分的效果。");
}

/// <summary>Plays 巴尔 whenever possible, alternating its two modes, and otherwise attacks.</summary>
internal static GameAction ChooseBaruTestAction(GameState state, ref int playIndex)
{
    var legalActions = GameEngine.GetLegalActions(state);
    var plays = legalActions
        .OfType<PlayFollowerAction>()
        .Where(action => action.ModeChoiceIndex is not null)
        .ToArray();
    if (plays.Length > 0)
    {
        var mode = playIndex % 2;
        playIndex++;
        return plays.FirstOrDefault(action => action.ModeChoiceIndex == mode) ?? plays[0];
    }

    GameAction? attack = legalActions.OfType<AttackFollowerAction>().FirstOrDefault() is { } followerAttack
        ? followerAttack
        : legalActions.OfType<AttackLeaderAction>().FirstOrDefault();
    return attack ?? legalActions.OfType<EndTurnAction>().First();
}

/// <summary>
/// Checks after every 巴尔 that mode (1) always strengthens 巴尔 itself (and exactly one random other
/// allied follower when one exists) and that mode (2) deals 3 damage to exactly one random enemy follower.
/// </summary>
internal static void AuditBaruStep(
    GameState before,
    GameState after,
    GameAction action,
    ref int modeOneAloneChecks,
    ref int modeOneWithAllyChecks,
    ref int modeTwoChecks,
    ref int modeOneTranscriptChecks,
    ref int modeTwoTranscriptChecks)
{
    if (action is not PlayFollowerAction play || play.ModeChoiceIndex is null)
    {
        return;
    }

    var player = before.ActivePlayer;
    var handCard = before.Players[player].Hand.Single(card => card.InstanceId == play.CardInstanceId);
    if (handCard.Definition.Id != CardIds.ElementalResonanceBaru)
    {
        return;
    }

    var opponent = player == 0 ? 1 : 0;
    var played = after.Players[player].Board.Single(follower => follower.InstanceId == play.CardInstanceId);
    var definition = played.Definition;

    if (play.ModeChoiceIndex == 0)
    {
        if (played.Attack != definition.Attack + 1 || played.MaxDefense != definition.Defense + 1)
        {
            throw new InvalidOperationException("Baru must always strengthen itself with mode (1).");
        }

        var alliesBefore = before.Players[player].Board;
        var strengthened = after.Players[player].Board
            .Where(follower => follower.InstanceId != play.CardInstanceId)
            .Count(follower =>
            {
                var beforeFollower = alliesBefore.FirstOrDefault(candidate => candidate.InstanceId == follower.InstanceId);
                return beforeFollower is not null &&
                       follower.Attack == beforeFollower.Attack + 1 &&
                       follower.MaxDefense == beforeFollower.MaxDefense + 1;
            });

        var expected = alliesBefore.Count > 0 ? 1 : 0;
        if (strengthened != expected)
        {
            throw new InvalidOperationException(
                "Baru's mode (1) must strengthen exactly one other allied follower when one is present.");
        }

        if (alliesBefore.Count > 0)
        {
            modeOneWithAllyChecks++;
            VerifyStepTranscript(before, after, action, "随机其他随从：");
        }
        else
        {
            modeOneAloneChecks++;
            VerifyStepTranscript(before, after, action, "随机的那一半落空");
        }

        VerifyStepTranscript(before, after, action, "本随从 +1/+1");
        modeOneTranscriptChecks++;
        return;
    }

    var enemiesBefore = before.Players[opponent].Board;
    if (enemiesBefore.Count == 0)
    {
        return;
    }

    var changed = enemiesBefore
        .Select(enemy => (
            Before: enemy,
            After: after.Players[opponent].Board.FirstOrDefault(candidate => candidate.InstanceId == enemy.InstanceId)))
        .Where(pair => pair.After is null || pair.After.CurrentDefense != pair.Before.CurrentDefense)
        .ToArray();
    if (changed.Length != 1)
    {
        throw new InvalidOperationException("Baru's mode (2) must damage exactly one random enemy follower.");
    }

    var target = changed[0];
    if (target.After is null)
    {
        if (target.Before.CurrentDefense > 3)
        {
            throw new InvalidOperationException("Baru's mode (2) destroyed a follower it could not have killed.");
        }
    }
    else if (target.Before.CurrentDefense - target.After.CurrentDefense != 3)
    {
        throw new InvalidOperationException("Baru's mode (2) must deal exactly 3 damage.");
    }

    modeTwoChecks++;
    VerifyStepTranscript(before, after, action, "随机目标：");
    modeTwoTranscriptChecks++;
}

/// <summary>
/// Renders one step through the battle reporter and checks that its mode resolution text mentions
/// the expected detail, so the transcript cannot silently lose a mode's outcome.
/// </summary>
internal static void VerifyStepTranscript(GameState before, GameState after, GameAction action, string requiredText)
{
    var writer = new StringWriter();
    var originalOut = Console.Out;
    Console.SetOut(writer);
    try
    {
        var reporter = new BattleConsoleReporter();
        reporter.PrintOpening(before, 30_047);
        reporter.PrintStep(new MatchStep(before, before.ActivePlayer, action, after));
    }
    finally
    {
        Console.SetOut(originalOut);
    }

    var transcript = writer.ToString();
    if (!transcript.Contains(requiredText, StringComparison.Ordinal))
    {
        throw new InvalidOperationException(
            $"The battle transcript must mention \"{requiredText}\", but it printed:{Environment.NewLine}{transcript}");
    }
}

/// <summary>
/// Renders one evolution step through the battle reporter and checks that it names the follower
/// the 【进化时】 effect damaged.
/// </summary>
internal static bool VerifyEvolutionTranscript(GameState before, GameState after, GameAction action)
{
    var writer = new StringWriter();
    var originalOut = Console.Out;
    Console.SetOut(writer);
    try
    {
        var reporter = new BattleConsoleReporter();
        reporter.PrintOpening(before, 30_043);
        reporter.PrintStep(new MatchStep(before, before.ActivePlayer, action, after));
    }
    finally
    {
        Console.SetOut(originalOut);
    }

    if (!writer.ToString().Contains("【进化时】对", StringComparison.Ordinal))
    {
        throw new InvalidOperationException("The battle transcript must name the follower an evolution effect damaged.");
    }

    return true;
}

internal static void RunGreedyStarchiumPriorityTest()
{
    var agent = new GreedyPlayerAgent();
    var starchium = CardCatalog.Get(CardIds.RoyalSeveringHeavenStarchium);
    var goliath = CardCatalog.Get(CardIds.Goliath);
    var gladiator = CardCatalog.Get(CardIds.Gladiator);

    // 换牌现在是一条与卡组无关的费用曲线：低费留、高费换。
    // 原来这里在"手牌里没有斯塔奇乌姆"时给每张牌 −10 分，于是任何既非龙族、又没有那张牌的
    // 卡组都会无脑全换四张（实测中速梦的换牌张数恒为 4.00），把 2 费优质节奏牌也一起扔掉。
    var cheapOpening = CreateObservation(
        phase: GamePhase.Mulligan,
        currentPlayPoints: 0,
        ownHand:
        [
            new HandCardView(1, gladiator),
            new HandCardView(2, gladiator),
            new HandCardView(3, gladiator),
            new HandCardView(4, gladiator)
        ]);
    var keepAllCheap = new MulliganAction([]);
    var replaceAllCheap = new MulliganAction([1, 2, 3, 4]);
    if (agent.ChooseAction(cheapOpening, [keepAllCheap, replaceAllCheap]) != keepAllCheap)
    {
        throw new InvalidOperationException(
            "Greedy player threw away an opening hand of cheap tempo cards; a non-Dragon deck must not mulligan everything.");
    }

    var expensive = CardCatalog.Get(CardIds.ValiantFallenAngelOlivia);
    var expensiveOpening = CreateObservation(
        phase: GamePhase.Mulligan,
        currentPlayPoints: 0,
        ownHand:
        [
            new HandCardView(5, expensive),
            new HandCardView(6, expensive),
            new HandCardView(7, expensive),
            new HandCardView(8, expensive)
        ]);
    var keepAllExpensive = new MulliganAction([]);
    var replaceAllExpensive = new MulliganAction([5, 6, 7, 8]);
    if (agent.ChooseAction(expensiveOpening, [keepAllExpensive, replaceAllExpensive]) != replaceAllExpensive)
    {
        throw new InvalidOperationException(
            "Greedy player kept an opening hand made entirely of expensive cards.");
    }

    var foundStarchiumOpening = CreateObservation(
        phase: GamePhase.Mulligan,
        currentPlayPoints: 0,
        ownHand:
        [
            new HandCardView(11, starchium),
            new HandCardView(12, gladiator),
            new HandCardView(13, goliath),
            new HandCardView(14, gladiator)
        ]);
    var chosenWithStarchium = agent.ChooseAction(
        foundStarchiumOpening,
        [new MulliganAction([]), new MulliganAction([11, 12, 13, 14])]);
    if (chosenWithStarchium is not MulliganAction keptStarchium ||
        keptStarchium.ReplaceInstanceIds.Contains(11))
    {
        throw new InvalidOperationException("Greedy player incorrectly mulliganed away Starchium.");
    }

    var playableStarchium = CreateObservation(
        phase: GamePhase.Main,
        currentPlayPoints: 4,
        ownHand: [new HandCardView(21, starchium), new HandCardView(22, gladiator)]);
    var chosenPlay = agent.ChooseAction(
        playableStarchium,
        [new PlayFollowerAction(21), new PlayFollowerAction(22), new EndTurnAction()]);
    if (chosenPlay is not PlayFollowerAction { CardInstanceId: 21 })
    {
        throw new InvalidOperationException("Greedy player did not prioritise playing Starchium at 4 PP.");
    }

    var starchiumOnBoard = new VisibleFollower(
        31,
        starchium.Id,
        starchium.Name,
        starchium.Attack,
        starchium.Defense,
        starchium.Defense,
        starchium.Keywords,
        EvolutionState.Unevolved,
        HasAttacked: false);
    var evolutionObservation = CreateObservation(
        phase: GamePhase.Main,
        currentPlayPoints: 5,
        ownHand: [],
        board: [starchiumOnBoard],
        evolutionPoints: 1,
        superEvolutionPoints: 1);
    var chosenEvolution = agent.ChooseAction(
        evolutionObservation,
        [new EvolveAction(31), new SuperEvolveAction(31), new EndTurnAction()]);
    if (chosenEvolution is not EvolveAction { FollowerInstanceId: 31 })
    {
        throw new InvalidOperationException("Greedy player did not strongly prioritise Starchium's normal evolution.");
    }

    var ordinaryFollower = new VisibleFollower(
        41,
        goliath.Id,
        goliath.Name,
        goliath.Attack,
        goliath.Defense,
        goliath.Defense,
        goliath.Keywords,
        EvolutionState.Unevolved,
        HasAttacked: false);
    var genericEvolutionObservation = CreateObservation(
        phase: GamePhase.Main,
        currentPlayPoints: 5,
        ownHand: [],
        board: [ordinaryFollower],
        evolutionPoints: 1,
        superEvolutionPoints: 1);
    var chosenGenericEvolution = agent.ChooseAction(
        genericEvolutionObservation,
        [new EvolveAction(41), new SuperEvolveAction(41), new EndTurnAction()]);
    if (chosenGenericEvolution is not SuperEvolveAction { FollowerInstanceId: 41 })
    {
        throw new InvalidOperationException("Greedy player did not prioritise an available super evolution.");
    }

    Console.WriteLine("Greedy Starchium priority test passed.");
    Console.WriteLine("The rule player searches for and keeps Starchium, plays it at 4 PP, and gives evolution and super evolution positive priority.");
}

internal static void RunGuoLongAgentPriorityTest()
{
    var agent = new GreedyPlayerAgent();
    var oracle = CardCatalog.Get(CardIds.DragonOracle);
    var polalai = CardCatalog.Get(CardIds.AncientHeavenbladePolalai);
    var ilantza = CardCatalog.Get(CardIds.BoundJusticeIlantza);
    var solarFlare = CardCatalog.Get(CardIds.SolarFlareRoar);
    var luria = CardCatalog.Get(CardIds.MasterOfSkyFateLuria);

    var opening = CreateObservation(
        phase: GamePhase.Mulligan,
        currentPlayPoints: 0,
        ownHand:
        [
            new HandCardView(101, oracle),
            new HandCardView(102, polalai),
            new HandCardView(103, ilantza),
            new HandCardView(104, solarFlare)
        ]);
    var keepEarlyDragonPlan = new MulliganAction([103]);
    var chosenMulligan = agent.ChooseAction(
        opening,
        [new MulliganAction([]), keepEarlyDragonPlan, new MulliganAction([101, 102, 103, 104])]);
    if (chosenMulligan != keepEarlyDragonPlan)
    {
        throw new InvalidOperationException("Greedy player did not retain Dragon Oracle and Polalai while replacing the slowest opening card.");
    }

    var oracleTurn = CreateObservation(
        phase: GamePhase.Main,
        currentPlayPoints: 3,
        ownHand: [new HandCardView(111, oracle), new HandCardView(112, luria)]);
    var chosenPlay = agent.ChooseAction(
        oracleTurn,
        [new PlaySpellAction(111, Target: null), new PlayFollowerAction(112), new EndTurnAction()]);
    if (chosenPlay is not PlaySpellAction { CardInstanceId: 111 })
    {
        throw new InvalidOperationException("Greedy player did not prioritise Dragon Oracle's PP acceleration.");
    }

    var deck = DeckCatalog.Create("DECK-002", "郭龙牌手验证");
    var state = CompleteMulligan(GameEngine.CreateGame(deck, deck, seed: 30_014));
    while (state.Players[state.ActivePlayer].CurrentPlayPoints < 3)
    {
        state = GameEngine.Apply(state, new EndTurnAction());
    }

    var lookahead = new LookaheadPlayerAgent(rolloutsPerAction: 2, futureTurnHorizon: 1, seed: 30_015);
    var legalActions = GameEngine.GetLegalActions(state);
    var action = lookahead.ChooseAction(
        state,
        GameEngine.ToObservation(state, state.ActivePlayer),
        legalActions);
    if (!legalActions.Contains(action) || lookahead.LastDecision is null)
    {
        throw new InvalidOperationException("Lookahead player could not evaluate a 郭龙 position.");
    }

    Console.WriteLine("郭龙 agent priority test passed.");
    Console.WriteLine("The rule and lookahead players keep Dragon acceleration, avoid throwing away rainbow payoffs, and can evaluate a live 郭龙 position.");
}

internal static GameObservation CreateObservation(
    GamePhase phase,
    int currentPlayPoints,
    IReadOnlyList<HandCardView> ownHand,
    IReadOnlyList<VisibleFollower>? board = null,
    int evolutionPoints = 0,
    int superEvolutionPoints = 0)
{
    var self = new PlayerView(
        Health: PlayerState.StartingHealth,
        MaxHealth: PlayerState.StartingHealth,
        CurrentPlayPoints: currentPlayPoints,
        MaxPlayPoints: currentPlayPoints,
        OwnTurnNumber: 5,
        EvolutionPoints: evolutionPoints,
        SuperEvolutionPoints: superEvolutionPoints,
        UsedEarlyExtraPlayPoint: false,
        UsedLateExtraPlayPoint: false,
        AttackedEnemyLeaderOnPreviousTurn: false,
        HandCount: ownHand.Count,
        DeckCount: 30,
        GraveyardCount: 0,
        Board: board ?? []);
    var opponent = self with { Board = [] };
    return new GameObservation(
        PerspectivePlayer: 0,
        ActivePlayer: 0,
        TurnNumber: 5,
        Phase: phase,
        Self: self,
        Opponent: opponent,
        OwnHand: ownHand);
}

internal static void RunLookaheadAgentTest()
{
    var gladiator = CardCatalog.Get(CardIds.Gladiator);
    var testDeck = new DeckDefinition(
        "Lookahead agent test",
        Enumerable.Repeat(gladiator, DeckDefinition.RequiredCardCount));
    var state = CompleteMulligan(GameEngine.CreateGame(testDeck, testDeck, seed: 30_010));

    while (state.Players[state.ActivePlayer].CurrentPlayPoints < 4)
    {
        state = GameEngine.Apply(state, new EndTurnAction());
    }

    var opponent = state.Players[state.ActivePlayer == 0 ? 1 : 0];
    var handBeforePlanning = opponent.Hand.Select(card => card.InstanceId).ToArray();
    var deckBeforePlanning = opponent.Deck.Select(card => card.InstanceId).ToArray();
    var observation = GameEngine.ToObservation(state, state.ActivePlayer);
    var legalActions = GameEngine.GetLegalActions(state);
    var agent = new LookaheadPlayerAgent(rolloutsPerAction: 6, futureTurnHorizon: 2, seed: 30_011);
    var action = agent.ChooseAction(state, observation, legalActions);

    if (!legalActions.Contains(action) || agent.LastDecision is null ||
        agent.LastDecision.Evaluations.Count != legalActions.Count ||
        agent.LastDecision.Evaluations.Any(evaluation => evaluation.Simulations != 6) ||
        !opponent.Hand.Select(card => card.InstanceId).SequenceEqual(handBeforePlanning) ||
        !opponent.Deck.Select(card => card.InstanceId).SequenceEqual(deckBeforePlanning))
    {
        throw new InvalidOperationException("Lookahead agent did not safely evaluate every legal action.");
    }

    _ = GameEngine.Apply(state, action);
    Console.WriteLine("Lookahead agent test passed.");
    Console.WriteLine("Every legal action received short simulated futures without changing the live hidden cards or deck order.");
}

/// <summary>
/// Guard for the frozen 1.0 snapshot.
/// <para>
/// "1.0" is the reference opponent every 2.0 experiment is measured against, so its numbers must not
/// drift. This test pins the two things that would silently change its strength: the hand-tuned
/// evaluator weights and the score scale. It deliberately does NOT compare V1 against the live agent,
/// because the live agent is supposed to change.
/// </para>
/// <para>
/// The rollout budget (10) and the horizon (3) are benchmark parameters passed in per run, not class
/// defaults, so they are pinned by how the benchmark is invoked rather than here.
/// </para>
/// </summary>
internal static void RunFrozenLookaheadV1Test()
{
    double[] expected =
    [
        2.0, 0.7, 0.4, 0.5, 0.6, 0.15, 0.05, 0.45, 0.4, 0.35, 0.45, 1.0, 0.0, 0.0, 0.0, 0.0
    ];

    var actual = LookaheadPlayerAgentV1.PositionWeights;
    if (actual.Length != expected.Length)
    {
        throw new InvalidOperationException(
            $"冻结的 1.0 基线权重个数被改动了：应为 {expected.Length}，实际 {actual.Length}。");
    }

    for (var index = 0; index < expected.Length; index++)
    {
        if (Math.Abs(actual[index] - expected[index]) > 1e-12)
        {
            throw new InvalidOperationException(
                $"冻结的 1.0 基线权重第 {index} 项被改动了：应为 {expected[index]:R}，实际 {actual[index]:R}。" +
                "1.0 是所有 2.0 实验的标尺，不能变。要改请改 LookaheadPlayerAgent（2.0）。");
        }
    }

    if (Math.Abs(LookaheadPlayerAgentV1.ScoreScale - 12.0) > 1e-12)
    {
        throw new InvalidOperationException(
            $"冻结的 1.0 基线分数尺度被改动了：应为 12.0，实际 {LookaheadPlayerAgentV1.ScoreScale:R}。");
    }

    Console.WriteLine("Frozen 1.0 baseline test passed.");
    Console.WriteLine("The 1.0 reference weights and score scale are unchanged, so benchmark comparisons stay valid.");
}

/// <summary>
/// Guard for the frozen 2.0 snapshot. 2.0 is the yardstick every 3.0 experiment is measured against,
/// so the things that would silently change it are pinned here: the evaluator weights, the score
/// scale, and the horizon cycle that defines the 2.0 algorithm.
/// </summary>
internal static void RunFrozenLookaheadV2Test()
{
    double[] expectedWeights =
    [
        2.0, 0.7, 0.4, 0.5, 0.6, 0.15, 0.05, 0.45, 0.4, 0.35, 0.45, 1.0, 0.0, 0.0, 0.0, 0.0
    ];

    var actualWeights = LookaheadPlayerAgentV2.PositionWeights;
    if (actualWeights.Length != expectedWeights.Length)
    {
        throw new InvalidOperationException(
            $"冻结的 2.0 基线权重个数被改动了：应为 {expectedWeights.Length}，实际 {actualWeights.Length}。");
    }

    for (var index = 0; index < expectedWeights.Length; index++)
    {
        if (Math.Abs(actualWeights[index] - expectedWeights[index]) > 1e-12)
        {
            throw new InvalidOperationException(
                $"冻结的 2.0 基线权重第 {index} 项被改动了：应为 {expectedWeights[index]:R}，实际 {actualWeights[index]:R}。" +
                "2.0 是 3.0 实验的标尺，不能变。");
        }
    }

    if (Math.Abs(LookaheadPlayerAgentV2.ScoreScale - 12.0) > 1e-12)
    {
        throw new InvalidOperationException(
            $"冻结的 2.0 基线分数尺度被改动了：应为 12.0，实际 {LookaheadPlayerAgentV2.ScoreScale:R}。");
    }

    int[] expectedCycle = [1, 3];
    if (!LookaheadPlayerAgentV2.FrozenHorizonCycle.SequenceEqual(expectedCycle))
    {
        throw new InvalidOperationException(
            "冻结的 2.0 基线视野循环被改动了：应为 [1, 3]，实际 [" +
            string.Join(", ", LookaheadPlayerAgentV2.FrozenHorizonCycle) + "]。");
    }

    Console.WriteLine("Frozen 2.0 baseline test passed.");
    Console.WriteLine("The 2.0 reference weights, score scale and horizon cycle {1,3} are unchanged.");
}

internal static void RunLookaheadDragonAccelerationTest()
{
    var oracle = CardCatalog.Get(CardIds.DragonOracle);
    var testDeck = new DeckDefinition(
        "Lookahead Dragon Oracle test",
        Enumerable.Repeat(oracle, DeckDefinition.RequiredCardCount));
    var state = CompleteMulligan(GameEngine.CreateGame(testDeck, testDeck, seed: 30_016));

    while (state.Players[state.ActivePlayer].CurrentPlayPoints < oracle.Cost)
    {
        state = GameEngine.Apply(state, new EndTurnAction());
    }

    var legalActions = GameEngine.GetLegalActions(state);
    var observation = GameEngine.ToObservation(state, state.ActivePlayer);

    // 这条自检现在验证的是**回退闸的机制本身**，而不是"前瞻应该打出龙之启示"。
    //
    // 原因：实测（同牌手 2000 局，只有闸门下限不同）0.035 → 47.5%，0 → 52.5%，p = 0.013。
    // 也就是说那个保守下限拦掉的正确判断比它拦掉的短视错误更多。所以默认下限归零，
    // 前瞻会采用自己的判断——而它在只推演 2 回合时确实低估斜坡，于是放过龙之启示。
    //
    // 这是一个**已知的短视**：正确做法是去改评估器，不是恢复全局下限（那会让整体掉 5 个百分点）。
    // 下面的断言把闸门机制钉住，并在末尾把短视打印出来，避免它被悄悄忘掉。
    var conservative = new LookaheadPlayerAgent(
        rolloutsPerAction: 20,
        futureTurnHorizon: 2,
        seed: 30_017,
        minimumPracticalAdvantage: 0.035);
    var conservativeAction = conservative.ChooseAction(state, observation, legalActions);
    if (conservativeAction is not PlaySpellAction { CardInstanceId: var conservativeCardId } ||
        state.Players[state.ActivePlayer].Hand.Single(card => card.InstanceId == conservativeCardId)
            .Definition.Id != oracle.Id)
    {
        throw new InvalidOperationException(
            "回退闸没有把决策交回规则牌手：保守下限下应当由规则牌手打出龙之启示。");
    }

    var defaultAgent = new LookaheadPlayerAgent(rolloutsPerAction: 20, futureTurnHorizon: 2, seed: 30_017);
    var defaultAction = defaultAgent.ChooseAction(state, observation, legalActions);
    var defaultPlaysOracle = defaultAction is PlaySpellAction { CardInstanceId: var defaultCardId } &&
                             state.Players[state.ActivePlayer].Hand
                                 .Single(card => card.InstanceId == defaultCardId).Definition.Id == oracle.Id;

    Console.WriteLine("Lookahead rail test passed.");
    Console.WriteLine(
        "A conservative rail (0.035) hands the decision back to the rule agent, which plays Dragon Oracle.");
    Console.WriteLine(defaultPlaysOracle
        ? "  默认下限 0：前瞻也打出龙之启示。"
        : "  ⚠ 默认下限 0：前瞻放过龙之启示，只推演 2 回合看不到斜坡收益——**已知短视**，" +
          "应改评估器而不是恢复全局下限。");
}

internal static void RunLookaheadMatchRunnerTest()
{
    var gladiator = CardCatalog.Get(CardIds.Gladiator);
    var testDeck = new DeckDefinition(
        "Lookahead MatchRunner test",
        Enumerable.Repeat(gladiator, DeckDefinition.RequiredCardCount));
    var result = MatchRunner.PlayToEnd(
        GameEngine.CreateGame(testDeck, testDeck, seed: 30_012),
        new LookaheadPlayerAgent(rolloutsPerAction: 2, futureTurnHorizon: 1, seed: 30_013),
        new GreedyPlayerAgent());

    if (result.Actions.All(entry => entry.Player != 0))
    {
        throw new InvalidOperationException("Lookahead agent did not receive turns through MatchRunner.");
    }

    Console.WriteLine("Lookahead MatchRunner integration test passed.");
    Console.WriteLine("A full match completed with the lookahead agent taking Player 1's turns.");
}

internal static GameState CompleteMulligan(GameState state)
{
    while (state.Phase == GamePhase.Mulligan)
    {
        state = GameEngine.Apply(state, new MulliganAction([]));
    }

    return state;
}

internal static void VerifyMulliganDoesNotRedrawSelectedCard(DeckDefinition deckA, DeckDefinition deckB)
{
    var initialState = GameEngine.CreateGame(deckA, deckB, seed: 314_159);
    var player = initialState.ActivePlayer;
    var replacedCardId = initialState.Players[player].Hand[0].InstanceId;
    var afterMulligan = GameEngine.Apply(initialState, new MulliganAction([replacedCardId]));

    if (afterMulligan.Players[player].Hand.Any(card => card.InstanceId == replacedCardId))
    {
        throw new InvalidOperationException("A selected mulligan card was redrawn.");
    }
}

internal static DeckDefinition CreateMatchDeck(string deckId, string playerLabel)
{
    var savedDeck = DeckCatalog.Get(deckId);
    var storedCount = savedDeck.Entries.Sum(entry => entry.Count);
    if (storedCount != DeckDefinition.RequiredCardCount)
    {
        throw new InvalidOperationException(
            $"{deckId}（{savedDeck.Name}）目前只有 {storedCount} 张卡，未满 {DeckDefinition.RequiredCardCount} 张，无法用于对局。请先在卡组编辑器补满后再试。");
    }

    return DeckCatalog.Create(deckId, $"{savedDeck.Name} {playerLabel}");
}

}
