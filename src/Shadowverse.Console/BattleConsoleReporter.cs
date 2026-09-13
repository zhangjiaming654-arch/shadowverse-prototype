using Shadowverse.Engine.Agents;
using Shadowverse.Engine.Game;
using Shadowverse.Engine.Models;
using Shadowverse.Engine.Simulation;

namespace Shadowverse.ConsoleApp;

/// <summary>Turns the engine's precise action log into a readable Chinese battle transcript.</summary>
public sealed class BattleConsoleReporter
{
    private readonly Dictionary<int, int[]> _handsAfterMulligan = [];

    public void PrintOpening(GameState state, ulong seed)
    {
        Console.WriteLine("========== 对局开始 ==========");
        Console.WriteLine($"随机种子：{seed}；先手：Player {state.StartingPlayer + 1}");
        Console.WriteLine("双方资源：EP 2 点、SEP 2 点；后手另有两次额外 PP 额度。");
        Console.WriteLine($"Player 1 初始手牌：{Cards(state.Players[0].Hand)}");
        Console.WriteLine($"Player 2 初始手牌：{Cards(state.Players[1].Hand)}");
        Console.WriteLine();
    }

    public void PrintStep(MatchStep step)
    {
        switch (step.Action)
        {
            case MulliganAction mulligan:
                PrintMulligan(step, mulligan);
                break;
            case PlayFollowerAction play:
                PrintPlay(step, play);
                break;
            case PlayAmuletAction playAmulet:
                PrintAmulet(step, playAmulet);
                break;
            case PlayCrystallizeAction crystallize:
                PrintCrystallize(step, crystallize);
                break;
            case PlayAccelerateAction accelerate:
                PrintAccelerate(step, accelerate);
                break;
            case PlaySpellAction playSpell:
                PrintSpell(step, playSpell);
                break;
            case EvolveAction evolve:
                PrintEvolution(
                    step,
                    evolve.FollowerInstanceId,
                    isSuperEvolution: false,
                    modeChoiceIndex: evolve.ModeChoiceIndex,
                    ownHandCardTargetInstanceIds: evolve.OwnHandCardTargetInstanceIds,
                    enemyFollowerTargetInstanceId: evolve.EnemyFollowerTargetInstanceId);
                break;
            case SuperEvolveAction superEvolve:
                PrintEvolution(
                    step,
                    superEvolve.FollowerInstanceId,
                    isSuperEvolution: true,
                    superEvolve.OtherFollowerTargetInstanceId,
                    superEvolve.ModeChoiceIndex,
                    superEvolve.OwnHandCardTargetInstanceIds,
                    superEvolve.EnemyFollowerTargetInstanceId);
                break;
            case UseExtraPlayPointAction:
                PrintExtraPlayPoint(step);
                break;
            case AttackLeaderAction attackLeader:
                PrintLeaderAttack(step, attackLeader);
                break;
            case AttackFollowerAction attackFollower:
                PrintFollowerAttack(step, attackFollower);
                break;
            case EndTurnAction:
                PrintEndTurn(step);
                break;
        }
    }

    public void PrintConclusion(MatchResult match)
    {
        Console.WriteLine();
        Console.WriteLine("========== 对局结束 ==========");
        Console.WriteLine($"赢家：Player {match.Winner + 1}；总动作数：{match.ActionCount}");
        Console.WriteLine(
            $"最终生命：P1={LeaderHealth(match.FinalState.Players[0])}，P2={LeaderHealth(match.FinalState.Players[1])}");
        Console.WriteLine($"P1 墓地：{Cards(match.FinalState.Players[0].Graveyard)}");
        Console.WriteLine($"P2 墓地：{Cards(match.FinalState.Players[1].Graveyard)}");
    }

    /// <summary>Prints the planning comparison before the corresponding real action is applied.</summary>
    public void PrintLookaheadDecision(MatchStep step, LookaheadDecision decision)
    {
        if (decision.Player != step.ActingPlayer || decision.TurnNumber != step.BeforeState.TurnNumber)
        {
            return;
        }

        var simulations = decision.Evaluations.FirstOrDefault()?.Simulations ?? 0;
        Console.WriteLine(
            $"[前瞻] Player {decision.Player + 1} 对每个候选操作模拟 {simulations} 次；推荐：{ActionDescription(step.BeforeState, decision.SelectedAction)}");

        // The planner may deliberately choose the rule-agent baseline when the apparent
        // rollout lead is too small to trust. Always show that chosen evaluation first,
        // even if it is not one of the raw top-three estimates.
        var selectedEvaluation = decision.Evaluations.Single(evaluation =>
            ReferenceEquals(evaluation.Action, decision.SelectedAction));
        var displayedEvaluations = new[] { selectedEvaluation }
            .Concat(decision.Evaluations.Where(evaluation =>
                !ReferenceEquals(evaluation.Action, decision.SelectedAction)))
            .Take(3);

        foreach (var (evaluation, rank) in displayedEvaluations.Select((evaluation, index) => (evaluation, index + 1)))
        {
            Console.WriteLine(
                $"           候选 {rank}：{ActionDescription(step.BeforeState, evaluation.Action)}；短期局面优势估计 {evaluation.EstimatedWinChance:P1}；本窗口内直接获胜 {evaluation.CompletedWins} 次。");
        }
    }

    private void PrintMulligan(MatchStep step, MulliganAction action)
    {
        var player = step.ActingPlayer;
        var before = step.BeforeState.Players[player];
        var after = step.AfterState.Players[player];
        var replaced = before.Hand.Where(card => action.ReplaceInstanceIds.Contains(card.InstanceId));

        Console.WriteLine($"[起手] Player {player + 1} 原手牌：{Cards(before.Hand)}");
        Console.WriteLine(action.ReplaceInstanceIds.Count == 0
            ? $"[重抽] Player {player + 1} 选择不换牌。"
            : $"[重抽] Player {player + 1} 换掉：{Cards(replaced)}");
        Console.WriteLine($"[重抽] Player {player + 1} 完成后：{Cards(after.Hand)}");

        _handsAfterMulligan[player] = after.Hand.Select(card => card.InstanceId).ToArray();

        if (step.AfterState.Phase != GamePhase.Main)
        {
            return;
        }

        var startingPlayer = step.AfterState.ActivePlayer;
        var drawn = NewlyHeldCards(
            step.AfterState.Players[startingPlayer].Hand,
            _handsAfterMulligan[startingPlayer]);

        Console.WriteLine();
        Console.WriteLine($"========== 第 1 回合：Player {startingPlayer + 1} ==========");
        Console.WriteLine($"{TurnResources(step.AfterState, startingPlayer)}；抽到：{Cards(drawn)}；当前手牌：{Cards(step.AfterState.Players[startingPlayer].Hand)}");
    }

    private static void PrintPlay(MatchStep step, PlayFollowerAction action)
    {
        var player = step.ActingPlayer;
        var before = step.BeforeState.Players[player];
        var after = step.AfterState.Players[player];
        var card = before.Hand.Single(card => card.InstanceId == action.CardInstanceId);

        Console.WriteLine(
            $"[出牌] Player {player + 1} 打出 {Card(card)}；PP {before.CurrentPlayPoints}→{after.CurrentPlayPoints}；场面：{Board(after.Board)}");

        PrintEnhance(before, after, card);

        if (card.Definition.Effect?.Kind == CardEffectKind.ReturnOwnHandCardToDeckThenDrawCards)
        {
            PrintReturnHandCardFanfare(before, after, action, card);
        }

        if (card.Definition.FanfareEffects is { Count: > 0 } fanfareEffects)
        {
            PrintStandardFanfare(before, after, card, fanfareEffects);
            PrintRandomDamageFanfare(step, fanfareEffects);
            PrintApocalypseDeckFanfare(before, after, fanfareEffects);
            PrintUpToTwoFollowerDamageFanfare(step, action, card, fanfareEffects);
            PrintSingleFollowerDamageFanfare(step, action, fanfareEffects);
            PrintAllEnemyFollowerDamageFanfare(step, fanfareEffects);
            PrintDiscardAndAllDamageFanfare(step, action, fanfareEffects);
            PrintDiscardFanfare(step, action, fanfareEffects);
        }

        PrintFanfareModeResolution(step, card.Definition, action.ModeChoiceIndex, "入场曲");

        PrintWinnerIfFinished(step.AfterState);
    }

    private static void PrintAmulet(MatchStep step, PlayAmuletAction action)
    {
        var player = step.ActingPlayer;
        var before = step.BeforeState.Players[player];
        var after = step.AfterState.Players[player];
        var card = before.Hand.Single(card => card.InstanceId == action.CardInstanceId);
        var amulet = after.Amulets.Single(candidate => candidate.InstanceId == action.CardInstanceId);
        Console.WriteLine(
            $"[护符] Player {player + 1} 打出 {Card(card)}；PP {before.CurrentPlayPoints}→{after.CurrentPlayPoints}；吟唱：{Countdown(amulet)}；场面随从：{Board(after.Board)}");
    }

    /// <summary>Prints a 结晶 play: the card enters the board as an amulet with its own Countdown.</summary>
    private static void PrintCrystallize(MatchStep step, PlayCrystallizeAction action)
    {
        var player = step.ActingPlayer;
        var before = step.BeforeState.Players[player];
        var after = step.AfterState.Players[player];
        var card = before.Hand.Single(candidate => candidate.InstanceId == action.CardInstanceId);
        var amulet = after.Amulets.Single(candidate => candidate.InstanceId == action.CardInstanceId);
        var crystallize = card.Definition.Crystallize
            ?? throw new InvalidOperationException("The reported action has no Crystallize definition.");

        Console.WriteLine(
            $"[结晶 {crystallize.Cost}] Player {player + 1} 以护符形态打出 {Card(card)}；PP {before.CurrentPlayPoints}→{after.CurrentPlayPoints}；吟唱：{Countdown(amulet)}");
    }

    private static void PrintAccelerate(MatchStep step, PlayAccelerateAction action)
    {
        var player = step.ActingPlayer;
        var before = step.BeforeState.Players[player];
        var after = step.AfterState.Players[player];
        var card = before.Hand.Single(card => card.InstanceId == action.CardInstanceId);
        var accelerate = card.Definition.Accelerate
            ?? throw new InvalidOperationException("The reported action has no Accelerate definition.");

        Console.WriteLine(
            $"[激奏] Player {player + 1} 对 {Card(card)} 使用【激奏 {accelerate.Cost}】；PP {before.CurrentPlayPoints}→{after.CurrentPlayPoints}；该卡进入墓地。" );
        if (accelerate.Effects.Any(effect => effect.Kind == CardEffectKind.IncreaseOwnMaxPlayPoints))
        {
            Console.WriteLine($"           最大 PP：{before.MaxPlayPoints}→{after.MaxPlayPoints}；当前 PP 不会因激奏增加。");
        }

        PrintWinnerIfFinished(step.AfterState);
    }

    private static void PrintDiscardAndAllDamageFanfare(
        MatchStep step,
        PlayFollowerAction action,
        IReadOnlyList<CardEffect> effects)
    {
        var discardEffect = effects.SingleOrDefault(effect =>
            effect.Kind is CardEffectKind.DiscardOwnHandCards or CardEffectKind.DiscardOwnHandCardsUpTo);
        var damageEffect = effects.SingleOrDefault(effect => effect.Kind == CardEffectKind.DealDamageToAllEnemyFollowersAndLeader);
        if (discardEffect is null || damageEffect is null)
        {
            return;
        }

        var player = step.ActingPlayer;
        var opponent = OtherPlayer(player);
        var discarded = (action.OwnHandCardTargetInstanceIds ?? [])
            .Select(targetId => step.BeforeState.Players[player].Hand.Single(card => card.InstanceId == targetId));
        var followerResults = ChangedFollowerResults(
            step.BeforeState.Players[opponent].Board,
            step.AfterState.Players[opponent].Board);

        Console.WriteLine($"           【入场曲】舍弃：{Cards(discarded)}");
        Console.WriteLine(followerResults.Count == 0
            ? "           对方没有随从，战场伤害未结算。"
            : $"           对敌方所有随从各造成 {damageEffect.Amount} 点伤害：{string.Join("；", followerResults)}");
        Console.WriteLine(
            $"           Player {opponent + 1} 主战者受到 {damageEffect.Amount} 点伤害：{LeaderHealth(step.BeforeState.Players[opponent])}→{LeaderHealth(step.AfterState.Players[opponent])}");
    }

    private static void PrintReturnHandCardFanfare(
        PlayerState before,
        PlayerState after,
        PlayFollowerAction action,
        CardInstance follower)
    {
        if (action.HandCardTargetInstanceId is null)
        {
            Console.WriteLine("           【入场曲】没有可选择的其他手牌，效果不执行。");
            return;
        }

        var returned = before.Hand.Single(card => card.InstanceId == action.HandCardTargetInstanceId.Value);
        var retainedHandIds = before.Hand
            .Where(card => card.InstanceId != follower.InstanceId && card.InstanceId != returned.InstanceId)
            .Select(card => card.InstanceId);
        var drawn = NewlyHeldCards(after.Hand, retainedHandIds);

        Console.WriteLine($"           【入场曲】将 {Card(returned)} 返回牌组；抽到：{Cards(drawn)}");
    }

    private static void PrintEnhance(PlayerState before, PlayerState after, CardInstance card)
    {
        var resolvedEnhance = card.Definition.EnhanceEffects?
            .Where(enhance => enhance.Cost <= before.CurrentPlayPoints)
            .OrderByDescending(enhance => enhance.Cost)
            .FirstOrDefault();
        if (resolvedEnhance is null || card.Definition.EnhanceEffects is null)
        {
            return;
        }

        var effects = card.Definition.EnhanceEffects
            .Where(enhance => enhance.Cost <= resolvedEnhance.Cost)
            .SelectMany(enhance => enhance.Effects)
            .ToArray();

        var totalStatGain = effects
            .Where(effect => effect.Kind == CardEffectKind.GainStats)
            .Sum(effect => effect.Amount);
        if (totalStatGain > 0)
        {
            var afterFollower = after.Board.Single(follower => follower.InstanceId == card.InstanceId);
            Console.WriteLine(
                $"           【爆能强化 {resolvedEnhance.Cost}】已发动：{card.Definition.Name} +{totalStatGain}/+{totalStatGain}，成为 {afterFollower.Attack}/{afterFollower.CurrentDefense}。" );
        }

        if (effects.Any(effect => effect.Kind == CardEffectKind.GainStorm))
        {
            Console.WriteLine($"           【爆能强化 {resolvedEnhance.Cost}】{card.Definition.Name} 获得【疾驰】。" );
        }

        if (effects.Any(effect => effect.Kind == CardEffectKind.SetOwnLeaderMaxHealth))
        {
            Console.WriteLine($"           【爆能强化 {resolvedEnhance.Cost}】主战者生命上限：{LeaderHealth(before)}→{LeaderHealth(after)}。" );
        }

        if (effects.Any(effect => effect.Kind == CardEffectKind.GrantOwnLeaderDamageImmunityUntilEndOfOpponentTurn))
        {
            Console.WriteLine($"           【爆能强化 {resolvedEnhance.Cost}】主战者获得临时效果：直到对手回合结束，受到的伤害变为0。" );
        }

        var search = effects.SingleOrDefault(effect =>
            effect.Kind == CardEffectKind.SearchDeckForFollowerWithMinimumCostToHand);
        if (search is not null)
        {
            var retainedHandIds = before.Hand
                .Where(handCard => handCard.InstanceId != card.InstanceId)
                .Select(handCard => handCard.InstanceId);
            var found = NewlyHeldCards(after.Hand, retainedHandIds);
            var sentToGraveyard = after.Graveyard
                .Where(graveCard => before.Graveyard.All(beforeCard => beforeCard.InstanceId != graveCard.InstanceId))
                .ToArray();
            var result = found.Any()
                ? $"加入手牌：{Cards(found)}"
                : sentToGraveyard.Length > 0
                    ? $"手牌已满，进入墓地：{Cards(sentToGraveyard)}"
                    : "未找到符合条件的随从。";
            Console.WriteLine($"           【爆能强化 {resolvedEnhance.Cost}】检索费用{search.Amount}或以上的随从：{result}");
        }

        var restorePp = effects.Where(effect => effect.Kind == CardEffectKind.RestoreOwnPlayPoints).Sum(effect => effect.Amount);
        if (restorePp > 0)
        {
            Console.WriteLine($"           【爆能强化 {resolvedEnhance.Cost}】回复 {restorePp} 点能量点。" );
        }

        var summonEffects = effects.Where(effect => effect.Kind == CardEffectKind.SummonFollower).ToArray();
        if (summonEffects.Length > 0)
        {
            var summoned = after.Board
                .Where(follower => before.Board.All(beforeFollower => beforeFollower.InstanceId != follower.InstanceId))
                .ToArray();
            Console.WriteLine(summoned.Length == 0
                ? $"           【爆能强化 {resolvedEnhance.Cost}】战场已满，无法召唤随从。"
                : $"           【爆能强化 {resolvedEnhance.Cost}】召唤：{Board(summoned)}");
        }
    }

    private static void PrintDiscardFanfare(
        MatchStep step,
        PlayFollowerAction action,
        IReadOnlyList<CardEffect> effects)
    {
        var discardEffect = effects.SingleOrDefault(effect =>
            effect.Kind is CardEffectKind.DiscardOwnHandCards or CardEffectKind.DiscardOwnHandCardsUpTo);
        if (discardEffect is null || effects.Any(effect => effect.Kind == CardEffectKind.DealDamageToAllEnemyFollowersAndLeader))
        {
            return;
        }

        var player = step.ActingPlayer;
        var discarded = (action.OwnHandCardTargetInstanceIds ?? [])
            .Select(targetId => step.BeforeState.Players[player].Hand.Single(card => card.InstanceId == targetId));
        Console.WriteLine($"           【入场曲】舍弃：{Cards(discarded)}");
    }

    private static void PrintStandardFanfare(
        PlayerState before,
        PlayerState after,
        CardInstance follower,
        IReadOnlyList<CardEffect> effects)
    {
        if (effects.Any(effect => effect.Kind == CardEffectKind.DrawCards))
        {
            var retainedHandIds = before.Hand
                .Where(card => card.InstanceId != follower.InstanceId)
                .Select(card => card.InstanceId);
            Console.WriteLine($"           【入场曲】抽到：{Cards(NewlyHeldCards(after.Hand, retainedHandIds))}");
        }

        if (effects.Any(effect => effect.Kind == CardEffectKind.RestoreOwnLeaderHealth))
        {
            Console.WriteLine($"           【入场曲】主战者生命：{LeaderHealth(before)}→{LeaderHealth(after)}");
        }

        if (effects.Any(effect => effect.Kind == CardEffectKind.RestoreOwnPlayPoints))
        {
            var playPointAfterCost = before.CurrentPlayPoints - follower.Definition.Cost;
            Console.WriteLine($"           【入场曲】回复 PP：{playPointAfterCost}→{after.CurrentPlayPoints}");
        }

        if (effects.Any(effect => effect.Kind == CardEffectKind.IncreaseOwnMaxPlayPoints))
        {
            Console.WriteLine($"           【入场曲】最大 PP：{before.MaxPlayPoints}→{after.MaxPlayPoints}；当前 PP 不会因此增加。" );
        }

        if (effects.Any(effect => effect.Kind == CardEffectKind.GainStormIfOwnFollowersAttackedEnemyLeaderPreviousTurn))
        {
            Console.WriteLine(before.AttackedEnemyLeaderOnPreviousTurn
                ? $"           【入场曲】上一回合已攻击主战者，{follower.Definition.Name} 获得【疾驰】。"
                : "           【入场曲】上一回合未攻击主战者，未获得【疾驰】。");
        }

        if (effects.Any(effect => effect.Kind is CardEffectKind.SummonFollower or
                CardEffectKind.SummonFollowerWithoutLastWords or
                CardEffectKind.SummonRandomDistinctDeckFollowers or
                CardEffectKind.RecallFollowerFromGraveyard))
        {
            var summoned = after.Board
                .Where(candidate => candidate.InstanceId != follower.InstanceId &&
                                    before.Board.All(previous => previous.InstanceId != candidate.InstanceId))
                .ToArray();
            Console.WriteLine(summoned.Length == 0
                ? "           【入场曲】战场已满，无法召唤随从。"
                : $"           【入场曲】召唤：{Board(summoned)}");
        }

        var evolveSelf = effects.SingleOrDefault(effect => effect.Kind == CardEffectKind.EvolveSelf);
        if (evolveSelf is not null)
        {
            if (evolveSelf.NecromancyCost > 0)
            {
                var graveyardBefore = before.Graveyard.Count;
                Console.WriteLine(graveyardBefore >= evolveSelf.NecromancyCost
                    ? $"           【唤灵_{evolveSelf.NecromancyCost}】消耗自己墓地的 {evolveSelf.NecromancyCost} 张卡（结算前墓地 {graveyardBefore} 张）。"
                    : $"           【唤灵_{evolveSelf.NecromancyCost}】结算前墓地只有 {graveyardBefore} 张，不足 {evolveSelf.NecromancyCost} 张，此效果未发动。");
            }

            var evolved = after.Board.FirstOrDefault(candidate => candidate.InstanceId == follower.InstanceId);
            if (evolved is not null && evolved.EvolutionState != EvolutionState.Unevolved)
            {
                Console.WriteLine(
                    $"           【入场曲】本随从进化：{follower.Definition.Name}（{follower.Definition.Attack}/{follower.Definition.Defense}）→ {Follower(evolved)}");
                PrintOnEvolveHandGain(before, after, follower);
            }
        }
    }

    /// <summary>Prints the cards 「本随从进化时」 adds to hand.</summary>
    private static void PrintOnEvolveHandGain(PlayerState before, PlayerState after, CardInstance follower)
    {
        if (follower.Definition.OnEvolveEffects?.Any(effect => effect.Kind == CardEffectKind.AddCopyToHand) != true)
        {
            return;
        }

        var retainedHandIds = before.Hand
            .Where(card => card.InstanceId != follower.InstanceId)
            .Select(card => card.InstanceId);
        var added = NewlyHeldCards(after.Hand, retainedHandIds).ToArray();
        Console.WriteLine(added.Length > 0
            ? $"           本随从进化时：加入手牌：{Cards(added)}"
            : "           本随从进化时：手牌已满，加入的牌进入墓地。");
    }

    private static void PrintFanfareModeResolution(
        MatchStep step,
        CardDefinition definition,
        int? modeChoiceIndex,
        string triggerName,
        IReadOnlyList<ModeDefinition>? modeOptions = null)
    {
        var options = modeOptions ?? definition.FanfareModeOptions;
        if (options is null)
        {
            return;
        }

        if (modeChoiceIndex is null || modeChoiceIndex.Value < 0 ||
            modeChoiceIndex.Value >= options.Count)
        {
            throw new InvalidOperationException("The mode card did not record a valid selected option.");
        }

        var player = step.ActingPlayer;
        var opponent = OtherPlayer(player);
        var option = options[modeChoiceIndex.Value];
        var beforePlayer = step.BeforeState.Players[player];
        var afterPlayer = step.AfterState.Players[player];
        Console.WriteLine($"           【{triggerName}】【模式】选择：{option.Name}。");

        if (option.Effects.Any(effect => effect.Kind == CardEffectKind.DrawCards))
        {
            var priorHandIds = beforePlayer.Hand.Select(card => card.InstanceId);
            Console.WriteLine($"           抽到：{Cards(NewlyHeldCards(afterPlayer.Hand, priorHandIds))}");
        }

        if (option.Effects.Any(effect => effect.Kind == CardEffectKind.RestoreOwnLeaderHealth))
        {
            Console.WriteLine($"           主战者生命：{LeaderHealth(beforePlayer)}→{LeaderHealth(afterPlayer)}");
        }

        var defenseDecrease = option.Effects.SingleOrDefault(effect =>
            effect.Kind == CardEffectKind.DecreaseAllEnemyFollowersDefense);
        if (defenseDecrease is not null)
        {
            var results = step.BeforeState.Players[opponent].Board.Select(beforeFollower =>
            {
                var afterFollower = step.AfterState.Players[opponent].Board
                    .SingleOrDefault(follower => follower.InstanceId == beforeFollower.InstanceId);
                return $"{Follower(beforeFollower)}：{BattleResult(beforeFollower, afterFollower)}";
            });
            Console.WriteLine(
                $"           对方全体 -0/-{defenseDecrease.Amount}：{string.Join("；", results.DefaultIfEmpty("对方战场为空"))}");
        }

        var statEffects = option.Effects
            .Where(effect => effect.Kind is CardEffectKind.GainStats or CardEffectKind.GainStatsToRandomOtherAlliedFollower)
            .ToArray();
        if (statEffects.Length > 0)
        {
            var playedInstanceId = step.Action is PlayFollowerAction play ? play.CardInstanceId : (int?)null;
            var statAmount = statEffects.Max(effect => effect.Amount);
            var played = playedInstanceId is null
                ? null
                : afterPlayer.Board.FirstOrDefault(follower => follower.InstanceId == playedInstanceId.Value);
            if (played is not null)
            {
                Console.WriteLine($"           本随从 +{statAmount}/+{statAmount}：{Follower(played)}");
            }

            var strengthened = afterPlayer.Board
                .Where(follower => follower.InstanceId != playedInstanceId)
                .Select(follower => (
                    Before: beforePlayer.Board.FirstOrDefault(candidate => candidate.InstanceId == follower.InstanceId),
                    After: follower))
                .Where(pair => pair.Before is not null &&
                               (pair.Before.Attack != pair.After.Attack ||
                                pair.Before.CurrentDefense != pair.After.CurrentDefense))
                .Select(pair => $"{Follower(pair.Before!)} → {Follower(pair.After)}")
                .ToArray();
            if (played is not null)
            {
                Console.WriteLine(strengthened.Length == 0
                    ? "           没有其他己方随从，随机的那一半落空。"
                    : $"           随机其他随从：{string.Join("；", strengthened)}");
            }
        }

        if (option.Effects.Any(effect => effect.Kind == CardEffectKind.DealDamageToRandomEnemyFollower))
        {
            var opponentBoardBefore = step.BeforeState.Players[opponent].Board;
            var damaged = opponentBoardBefore
                .Select(beforeFollower => (
                    Before: beforeFollower,
                    After: step.AfterState.Players[opponent].Board
                        .FirstOrDefault(follower => follower.InstanceId == beforeFollower.InstanceId)))
                .Where(pair => pair.After is null || pair.After.CurrentDefense != pair.Before.CurrentDefense)
                .Select(pair => $"{Follower(pair.Before)}：{BattleResult(pair.Before, pair.After)}")
                .ToArray();
            Console.WriteLine(opponentBoardBefore.Count == 0
                ? "           对方战场为空，随机伤害没有目标。"
                : damaged.Length == 0
                    ? "           随机伤害被抵消，没有随从受损。"
                    : $"           随机目标：{string.Join("；", damaged)}");
        }

        if (option.Effects.Any(effect => effect.Kind == CardEffectKind.DealDamageToEnemyLeader))
        {
            Console.WriteLine(
                $"           对方主战者生命：{LeaderHealth(step.BeforeState.Players[opponent])}→{LeaderHealth(step.AfterState.Players[opponent])}");
        }

        if (option.Effects.Any(effect => effect.Kind == CardEffectKind.DealDamageToAllEnemyFollowers))
        {
            var results = step.BeforeState.Players[opponent].Board.Select(beforeFollower =>
            {
                var afterFollower = step.AfterState.Players[opponent].Board
                    .SingleOrDefault(follower => follower.InstanceId == beforeFollower.InstanceId);
                return $"{Follower(beforeFollower)}：{BattleResult(beforeFollower, afterFollower)}";
            });
            Console.WriteLine(
                $"           对方全体随从：{string.Join("；", results.DefaultIfEmpty("对方战场为空"))}");
        }

        if (option.Effects.Any(effect => effect.Kind == CardEffectKind.RestoreOwnEvolutionPoints))
        {
            Console.WriteLine(
                $"           进化点：{step.BeforeState.Players[player].EvolutionPoints}→{step.AfterState.Players[player].EvolutionPoints}");
        }

        if (option.Effects.Any(effect => effect.Kind == CardEffectKind.RestoreOwnPlayPoints))
        {
            Console.WriteLine(
                $"           能量点：{step.BeforeState.Players[player].CurrentPlayPoints}→{step.AfterState.Players[player].CurrentPlayPoints}");
        }
    }

    private static void PrintApocalypseDeckFanfare(
        PlayerState before,
        PlayerState after,
        IReadOnlyList<CardEffect> effects)
    {
        if (effects.Any(effect => effect.Kind == CardEffectKind.ReplaceOwnDeckWithApocalypseDeck))
        {
            Console.WriteLine(
                $"           【入场曲】牌库 {before.Deck.Count} 张→{after.Deck.Count} 张，已替换并洗为【启示录牌组】。");
        }
    }

    private static void PrintAllEnemyFollowerDamageFanfare(MatchStep step, IReadOnlyList<CardEffect> effects)
    {
        var effect = effects.SingleOrDefault(candidate =>
            candidate.Kind == CardEffectKind.DealDamageToAllEnemyFollowers);
        if (effect is null)
        {
            return;
        }

        var opponent = OtherPlayer(step.ActingPlayer);
        var before = step.BeforeState.Players[opponent].Board;
        var after = step.AfterState.Players[opponent].Board;
        var results = before.Select(beforeFollower =>
        {
            var afterFollower = after.SingleOrDefault(follower => follower.InstanceId == beforeFollower.InstanceId);
            return $"{Follower(beforeFollower)}：{BattleResult(beforeFollower, afterFollower)}";
        });
        Console.WriteLine(
            $"           【入场曲】对方全体随从各受 {effect.Amount} 点伤害：{string.Join("；", results.DefaultIfEmpty("对方战场为空"))}");
    }

    private static void PrintRandomDamageFanfare(MatchStep step, IReadOnlyList<CardEffect> effects)
    {
        var standardDamage = effects.SingleOrDefault(effect =>
            effect.Kind == CardEffectKind.DealDamageToRandomEnemyFollower);
        var conditionalDamage = effects.SingleOrDefault(effect =>
            effect.Kind == CardEffectKind.DealDamageToRandomEnemyFollowerIfOwnFollowersAttackedEnemyLeaderPreviousTurn);
        if (standardDamage is null && conditionalDamage is null)
        {
            return;
        }

        var player = step.ActingPlayer;
        var opponent = OtherPlayer(player);
        var activationCount = (standardDamage is null ? 0 : 1) +
            (conditionalDamage is not null && step.BeforeState.Players[player].AttackedEnemyLeaderOnPreviousTurn ? 1 : 0);
        var damage = standardDamage?.Amount ?? conditionalDamage!.Amount;
        var results = ChangedFollowerResults(
            step.BeforeState.Players[opponent].Board,
            step.AfterState.Players[opponent].Board);

        Console.WriteLine(results.Count == 0
            ? $"           【入场曲】随机 {damage} 点伤害发动 {activationCount} 次；对方没有随从，未结算。"
            : $"           【入场曲】随机 {damage} 点伤害发动 {activationCount} 次：{string.Join("；", results)}");
    }

    /// <summary>Prints a 【入场曲】 that damages one chosen enemy follower, such as 猫咪走绳师's.</summary>
    private static void PrintSingleFollowerDamageFanfare(
        MatchStep step,
        PlayFollowerAction action,
        IReadOnlyList<CardEffect> effects)
    {
        var damageEffect = effects.SingleOrDefault(effect =>
            effect.Kind == CardEffectKind.DealDamageToEnemyFollower);
        if (damageEffect is null)
        {
            return;
        }

        var opponent = OtherPlayer(step.ActingPlayer);
        if (action.EnemyFollowerTargetInstanceIds is not { Count: > 0 } targets)
        {
            Console.WriteLine("           【入场曲】对手战场上没有随从，没有可选择的伤害目标。");
            return;
        }

        var beforeTarget = step.BeforeState.Players[opponent].Board
            .Single(target => target.InstanceId == targets[0]);
        var afterTarget = step.AfterState.Players[opponent].Board
            .SingleOrDefault(target => target.InstanceId == targets[0]);
        Console.WriteLine(
            $"           【入场曲】对 {Follower(beforeTarget)} 造成 {damageEffect.Amount} 点伤害：{BattleResult(beforeTarget, afterTarget)}");
    }

    private static void PrintUpToTwoFollowerDamageFanfare(
        MatchStep step,
        PlayFollowerAction action,
        CardInstance follower,
        IReadOnlyList<CardEffect> effects)
    {
        var damageEffect = effects.SingleOrDefault(effect =>
            effect.Kind == CardEffectKind.DealDamageToUpToTwoEnemyFollowersAndLeader);
        if (damageEffect is null)
        {
            return;
        }

        var opponent = OtherPlayer(step.ActingPlayer);
        var targetIds = action.EnemyFollowerTargetInstanceIds;
        if (targetIds is null)
        {
            throw new InvalidOperationException("This Fanfare action is missing its enemy follower targets.");
        }

        if (targetIds.Count == 0)
        {
            Console.WriteLine(
                $"           【入场曲】对方战场没有随从，直接对 Player {opponent + 1} 主战者造成 {damageEffect.Amount} 点伤害：{LeaderHealth(step.BeforeState.Players[opponent])}→{LeaderHealth(step.AfterState.Players[opponent])}");
            return;
        }

        var targetResults = targetIds.Select(targetId =>
        {
            var beforeTarget = step.BeforeState.Players[opponent].Board
                .Single(target => target.InstanceId == targetId);
            var afterTarget = step.AfterState.Players[opponent].Board
                .SingleOrDefault(target => target.InstanceId == targetId);
            return $"{Follower(beforeTarget)}：{BattleResult(beforeTarget, afterTarget)}";
        });

        Console.WriteLine(
            $"           【入场曲】{follower.Definition.Name} 对 {string.Join("；", targetResults)} 各造成 {damageEffect.Amount} 点伤害。");
        Console.WriteLine(
            $"           同时对 Player {opponent + 1} 主战者造成 {damageEffect.Amount} 点伤害：{LeaderHealth(step.BeforeState.Players[opponent])}→{LeaderHealth(step.AfterState.Players[opponent])}");
    }

    private static void PrintSpell(MatchStep step, PlaySpellAction action)
    {
        var player = step.ActingPlayer;
        var opponent = OtherPlayer(player);
        var before = step.BeforeState.Players[player];
        var after = step.AfterState.Players[player];
        var card = before.Hand.Single(card => card.InstanceId == action.CardInstanceId);

        Console.WriteLine(
            $"[法术] Player {player + 1} 使用 {Card(card)}；PP {before.CurrentPlayPoints}→{after.CurrentPlayPoints}");

        var effects = GetSpellEffects(card.Definition);
        if (effects.Any(effect => effect.Kind == CardEffectKind.SetEnemyLeaderMaxHealth))
        {
            PrintSpellSetMaximumHealth(step, card);
        }
        else if (effects.Any(effect => effect.Kind == CardEffectKind.IncreaseOwnMaxPlayPointsAndDrawIfAtTen))
        {
            PrintDragonOracle(step, action, card);
        }
        else if (effects.Any(effect => effect.Kind == CardEffectKind.SummonFollower))
        {
            PrintSummonSpell(step, card);
        }
        else if (effects.Any(effect => effect.Kind == CardEffectKind.DiscardOwnHandCards))
        {
            PrintDiscardAndRandomDamageSpell(step, action, card, effects);
        }
        else if (effects.Any(effect => effect.Kind == CardEffectKind.DistributeDamageAmongEnemyFollowersByEntryOrder))
        {
            PrintDistributedDamageSpell(step, effects);
        }
        else if (effects.Any(effect => effect.Kind == CardEffectKind.DestroyRandomEnemyFollowerWithHighestAttack))
        {
            PrintPresentationOfTheWorld(step, action, card, effects);
        }
        else if (effects.Any(effect => effect.Kind == CardEffectKind.DealDamageToEnemyLeader) &&
                 effects.Any(effect => effect.Kind == CardEffectKind.RestoreOwnLeaderHealth))
        {
            PrintDirectDamageAndHealSpell(step, effects);
        }
        else if (effects.Any(effect => effect.Kind == CardEffectKind.DealDamageToRandomEnemyFollower) &&
                 effects.Any(effect => effect.Kind == CardEffectKind.DealDamageToEnemyLeaderIfAwakened))
        {
            PrintLazyWaveflower(step, effects);
        }
        else switch (action.Target)
        {
            case EnemyLeaderTarget:
                Console.WriteLine(
                    $"           目标：Player {opponent + 1} 主战者；生命 {step.BeforeState.Players[opponent].Health}→{step.AfterState.Players[opponent].Health}");
                break;
            case EnemyFollowerTarget target:
                var beforeFollower = step.BeforeState.Players[opponent].Board
                    .Single(follower => follower.InstanceId == target.FollowerInstanceId);
                var afterFollower = step.AfterState.Players[opponent].Board
                    .SingleOrDefault(follower => follower.InstanceId == target.FollowerInstanceId);
                Console.WriteLine(
                    $"           目标：Player {opponent + 1} 的 {Follower(beforeFollower)}；结果：{BattleResult(beforeFollower, afterFollower)}");
                break;
            case null:
                PrintSpellDraw(step, action, card);
                break;
        }

        PrintWinnerIfFinished(step.AfterState);
    }

    private static void PrintPresentationOfTheWorld(
        MatchStep step,
        PlaySpellAction action,
        CardInstance spell,
        IReadOnlyList<CardEffect> effects)
    {
        var player = step.ActingPlayer;
        var opponent = OtherPlayer(player);
        var beforeOpponent = step.BeforeState.Players[opponent];
        var highestAttack = beforeOpponent.Board.Count == 0 ? (int?)null : beforeOpponent.Board.Max(follower => follower.Attack);
        var candidates = highestAttack is null
            ? Array.Empty<FollowerInstance>()
            : beforeOpponent.Board.Where(follower => follower.Attack == highestAttack.Value).ToArray();

        PrintSpellDraw(step, action, spell);
        Console.WriteLine(candidates.Length == 0
            ? "           对方没有随从，随机破坏不结算。"
            : $"           随机破坏攻击力最高的随从（攻击力 {highestAttack}）：候选 {Board(candidates)}。");

        var enhance = spell.Definition.EnhanceEffects?
            .Where(candidate => candidate.Cost <= step.BeforeState.Players[player].CurrentPlayPoints)
            .OrderByDescending(candidate => candidate.Cost)
            .FirstOrDefault();
        var damageEffect = enhance?.Effects.SingleOrDefault(effect =>
            effect.Kind == CardEffectKind.DealDamageToAllEnemyFollowersAndLeader);
        if (damageEffect is null)
        {
            return;
        }

        var results = ChangedFollowerResults(beforeOpponent.Board, step.AfterState.Players[opponent].Board);
        Console.WriteLine(results.Count == 0
            ? $"           【爆能强化 {enhance!.Cost}】对方没有随从，战场伤害未结算。"
            : $"           【爆能强化 {enhance!.Cost}】对敌方所有随从各造成 {damageEffect.Amount} 点伤害：{string.Join("；", results)}");
        Console.WriteLine(
            $"           Player {opponent + 1} 主战者受到 {damageEffect.Amount} 点伤害：{LeaderHealth(step.BeforeState.Players[opponent])}→{LeaderHealth(step.AfterState.Players[opponent])}");
    }

    private static void PrintDirectDamageAndHealSpell(MatchStep step, IReadOnlyList<CardEffect> effects)
    {
        var player = step.ActingPlayer;
        var opponent = OtherPlayer(player);
        var damage = effects.Single(effect => effect.Kind == CardEffectKind.DealDamageToEnemyLeader).Amount;
        var heal = effects.Single(effect => effect.Kind == CardEffectKind.RestoreOwnLeaderHealth).Amount;
        Console.WriteLine(
            $"           对 Player {opponent + 1} 主战者造成 {damage} 点伤害：{LeaderHealth(step.BeforeState.Players[opponent])}→{LeaderHealth(step.AfterState.Players[opponent])}");
        Console.WriteLine(
            $"           Player {player + 1} 主战者回复 {heal} 点生命：{LeaderHealth(step.BeforeState.Players[player])}→{LeaderHealth(step.AfterState.Players[player])}");
    }

    private static void PrintLazyWaveflower(MatchStep step, IReadOnlyList<CardEffect> effects)
    {
        var player = step.ActingPlayer;
        var opponent = OtherPlayer(player);
        var damage = effects.First(effect => effect.Kind == CardEffectKind.DealDamageToRandomEnemyFollower).Amount;
        var times = effects.Count(effect => effect.Kind == CardEffectKind.DealDamageToRandomEnemyFollower);
        var results = ChangedFollowerResults(
            step.BeforeState.Players[opponent].Board,
            step.AfterState.Players[opponent].Board);
        Console.WriteLine(results.Count == 0
            ? $"           随机随从伤害发动 {times} 次；对方没有随从，未结算。"
            : $"           随机随从伤害发动 {times} 次、每次 {damage} 点：{string.Join("；", results)}");
        Console.WriteLine(step.BeforeState.Players[player].MaxPlayPoints >= 7
            ? $"           【觉醒】已满足（最大 PP {step.BeforeState.Players[player].MaxPlayPoints}）：Player {opponent + 1} 主战者生命 {LeaderHealth(step.BeforeState.Players[opponent])}→{LeaderHealth(step.AfterState.Players[opponent])}"
            : $"           【觉醒】未满足（最大 PP {step.BeforeState.Players[player].MaxPlayPoints}），不对主战者造成伤害。");
    }

    private static void PrintDiscardAndRandomDamageSpell(
        MatchStep step,
        PlaySpellAction action,
        CardInstance spell,
        IReadOnlyList<CardEffect> effects)
    {
        var player = step.ActingPlayer;
        var opponent = OtherPlayer(player);
        var discarded = (action.OwnHandCardTargetInstanceIds ?? [])
            .Select(targetId => step.BeforeState.Players[player].Hand.Single(card => card.InstanceId == targetId));
        var damage = effects.Single(effect => effect.Kind == CardEffectKind.DealDamageToRandomEnemyFollowerAndLeader).Amount;
        var followerResults = ChangedFollowerResults(
            step.BeforeState.Players[opponent].Board,
            step.AfterState.Players[opponent].Board);

        Console.WriteLine($"           舍弃：{Cards(discarded)}");
        Console.WriteLine(followerResults.Count == 0
            ? $"           随机随从伤害：对方没有随从，未结算；主战者仍受到 {damage} 点伤害。"
            : $"           随机随从伤害：{string.Join("；", followerResults)}");
        Console.WriteLine(
            $"           Player {opponent + 1} 主战者受到 {damage} 点伤害：{LeaderHealth(step.BeforeState.Players[opponent])}→{LeaderHealth(step.AfterState.Players[opponent])}");
    }

    private static void PrintDistributedDamageSpell(MatchStep step, IReadOnlyList<CardEffect> effects)
    {
        var player = step.ActingPlayer;
        var opponent = OtherPlayer(player);
        var effect = effects.Single(effect => effect.Kind == CardEffectKind.DistributeDamageAmongEnemyFollowersByEntryOrder);
        var damage = effect.Amount +
            (step.BeforeState.Players[player].AttackedEnemyLeaderOnPreviousTurn
                ? effect.BonusAmountIfOwnFollowersAttackedEnemyLeaderPreviousTurn
                : 0);
        var remainingDamage = damage;
        var results = new List<string>();

        foreach (var beforeFollower in step.BeforeState.Players[opponent].Board)
        {
            if (remainingDamage == 0)
            {
                break;
            }

            var assignedDamage = Math.Min(remainingDamage, beforeFollower.CurrentDefense);
            remainingDamage -= assignedDamage;
            var afterFollower = step.AfterState.Players[opponent].Board
                .SingleOrDefault(follower => follower.InstanceId == beforeFollower.InstanceId);
            results.Add($"{Follower(beforeFollower)} 分配 {assignedDamage} 点：{BattleResult(beforeFollower, afterFollower)}");
        }

        Console.WriteLine(results.Count == 0
            ? $"           按登场顺序分配 {damage} 点伤害；对方没有随从，未结算。"
            : $"           按登场顺序分配 {damage} 点伤害：{string.Join("；", results)}");

        var necromancyDamage = effects.SingleOrDefault(effect =>
            effect.Kind == CardEffectKind.DealDamageToEnemyLeader && effect.NecromancyCost > 0);
        if (necromancyDamage is not null)
        {
            var graveyardBefore = step.BeforeState.Players[player].Graveyard.Count;
            var opponentHealthBefore = step.BeforeState.Players[opponent].Health;
            var opponentHealthAfter = step.AfterState.Players[opponent].Health;
            Console.WriteLine(graveyardBefore >= necromancyDamage.NecromancyCost
                ? $"           【唤灵_{necromancyDamage.NecromancyCost}】消耗自己墓地的 {necromancyDamage.NecromancyCost} 张卡（结算前墓地 {graveyardBefore} 张）：对手主战者生命 {opponentHealthBefore}→{opponentHealthAfter}"
                : $"           【唤灵_{necromancyDamage.NecromancyCost}】结算前墓地只有 {graveyardBefore} 张，不足 {necromancyDamage.NecromancyCost} 张，此效果未发动。");
        }
    }

    private static void PrintSpellDraw(MatchStep step, PlaySpellAction action, CardInstance spell)
    {
        var player = step.ActingPlayer;
        var before = step.BeforeState.Players[player];
        var after = step.AfterState.Players[player];
        var priorHandIds = before.Hand
            .Where(card => card.InstanceId != action.CardInstanceId)
            .Select(card => card.InstanceId);
        var drawn = NewlyHeldCards(after.Hand, priorHandIds);
        var priorGraveyardIds = before.Graveyard.Select(card => card.InstanceId).ToHashSet();
        var burned = after.Graveyard
            .Where(card => card.InstanceId != spell.InstanceId && !priorGraveyardIds.Contains(card.InstanceId));

        Console.WriteLine($"           抽到：{Cards(drawn)}");
        if (burned.Any())
        {
            Console.WriteLine($"           手牌已满，进入墓地：{Cards(burned)}");
        }
    }

    private static IReadOnlyList<CardEffect> GetSpellEffects(CardDefinition definition)
    {
        var effects = new List<CardEffect>();
        if (definition.Effect is not null)
        {
            effects.Add(definition.Effect);
        }

        if (definition.SpellEffects is not null)
        {
            effects.AddRange(definition.SpellEffects);
        }

        return effects;
    }

    private static IReadOnlyList<string> ChangedFollowerResults(
        IReadOnlyList<FollowerInstance> beforeFollowers,
        IReadOnlyList<FollowerInstance> afterFollowers) =>
        beforeFollowers
            .Select(beforeFollower =>
            {
                var afterFollower = afterFollowers.SingleOrDefault(candidate =>
                    candidate.InstanceId == beforeFollower.InstanceId);
                return (beforeFollower, afterFollower);
            })
            .Where(pair => pair.afterFollower is null ||
                pair.afterFollower.CurrentDefense < pair.beforeFollower.CurrentDefense ||
                BarrierWasConsumed(pair.beforeFollower, pair.afterFollower))
            .Select(pair => $"{Follower(pair.beforeFollower)}：{BattleResult(pair.beforeFollower, pair.afterFollower)}")
            .ToArray();

    private static void PrintSpellSetMaximumHealth(MatchStep step, CardInstance spell)
    {
        var opponent = OtherPlayer(step.ActingPlayer);
        Console.WriteLine(
            $"           {spell.Definition.Name}：Player {opponent + 1} 主战者生命 {LeaderHealth(step.BeforeState.Players[opponent])}→{LeaderHealth(step.AfterState.Players[opponent])}（最大生命值变为 1）。");
    }

    private static void PrintDragonOracle(MatchStep step, PlaySpellAction action, CardInstance spell)
    {
        var player = step.ActingPlayer;
        var before = step.BeforeState.Players[player];
        var after = step.AfterState.Players[player];
        Console.WriteLine($"           最大 PP：{before.MaxPlayPoints}→{after.MaxPlayPoints}");
        if (after.MaxPlayPoints == 10)
        {
            Console.WriteLine("           最大 PP 已为 10，触发抽牌：");
            PrintSpellDraw(step, action, spell);
        }
        else
        {
            Console.WriteLine("           最大 PP 未达到 10，不抽牌。");
        }
    }

    private static void PrintSummonSpell(MatchStep step, CardInstance spell)
    {
        var player = step.ActingPlayer;
        var opponent = OtherPlayer(player);
        var before = step.BeforeState.Players[player];
        var after = step.AfterState.Players[player];
        var summoned = after.Board
            .Where(follower => before.Board.All(beforeFollower => beforeFollower.InstanceId != follower.InstanceId))
            .ToArray();
        Console.WriteLine($"           召唤：{Board(summoned)}");

        var enhance = spell.Definition.EnhanceEffects?
            .Where(candidate => candidate.Cost <= before.CurrentPlayPoints)
            .OrderByDescending(candidate => candidate.Cost)
            .FirstOrDefault();
        var damageEffect = enhance?.Effects.SingleOrDefault(effect =>
            effect.Kind == CardEffectKind.DealDamageToRandomEnemyFollower);
        if (damageEffect is null)
        {
            return;
        }

        var beforeOpponent = step.BeforeState.Players[opponent];
        var afterOpponent = step.AfterState.Players[opponent];
        var target = beforeOpponent.Board.FirstOrDefault(beforeFollower =>
        {
            var afterFollower = afterOpponent.Board.SingleOrDefault(candidate =>
                candidate.InstanceId == beforeFollower.InstanceId);
            return afterFollower is null || afterFollower.CurrentDefense < beforeFollower.CurrentDefense;
        });
        Console.WriteLine(target is null
            ? $"           【爆能强化 {enhance!.Cost}】对方没有随从，随机伤害未结算。"
            : $"           【爆能强化 {enhance!.Cost}】随机对 {Follower(target)} 造成 {damageEffect.Amount} 点伤害。");
    }

    private static void PrintEvolution(
        MatchStep step,
        int followerInstanceId,
        bool isSuperEvolution,
        int? otherFollowerTargetInstanceId = null,
        int? modeChoiceIndex = null,
        IReadOnlyList<int>? ownHandCardTargetInstanceIds = null,
        int? enemyFollowerTargetInstanceId = null)
    {
        var player = step.ActingPlayer;
        var beforePlayer = step.BeforeState.Players[player];
        var afterPlayer = step.AfterState.Players[player];
        var beforeFollower = beforePlayer.Board.Single(follower => follower.InstanceId == followerInstanceId);
        var afterFollower = afterPlayer.Board.Single(follower => follower.InstanceId == followerInstanceId);
        var resourceBefore = isSuperEvolution
            ? beforePlayer.SuperEvolutionPoints
            : beforePlayer.EvolutionPoints;
        var resourceAfter = isSuperEvolution
            ? afterPlayer.SuperEvolutionPoints
            : afterPlayer.EvolutionPoints;
        var resourceName = isSuperEvolution ? "SEP" : "EP";
        var actionName = isSuperEvolution ? "超进化" : "进化";

        Console.WriteLine(
            $"[{actionName}] Player {player + 1} 消耗 1 点 {resourceName}（{resourceBefore}→{resourceAfter}），{Follower(beforeFollower)} → {Follower(afterFollower)}");

        PrintEvolutionEffects(step, beforeFollower, isSuperEvolution, ownHandCardTargetInstanceIds, enemyFollowerTargetInstanceId);
        var evolutionModes = beforeFollower.Definition.EvolutionModeOptions ??
            (beforeFollower.Definition.EvolutionRepeatsFanfareMode
                ? beforeFollower.Definition.FanfareModeOptions
                : null);
        if (evolutionModes is not null)
        {
            PrintFanfareModeResolution(
                step,
                beforeFollower.Definition,
                modeChoiceIndex,
                isSuperEvolution ? "超进化时" : "进化时",
                evolutionModes);
        }

        if (otherFollowerTargetInstanceId is not null)
        {
            var beforeTarget = beforePlayer.Board.Single(follower => follower.InstanceId == otherFollowerTargetInstanceId.Value);
            var afterTarget = afterPlayer.Board.Single(follower => follower.InstanceId == otherFollowerTargetInstanceId.Value);
            Console.WriteLine($"           【超进化时】使 {Follower(beforeTarget)} → {Follower(afterTarget)}");
        }

        if (isSuperEvolution && beforeFollower.Definition.SuperEvolutionEffects?.Any(effect =>
                effect.Kind == CardEffectKind.DrawCards) == true)
        {
            var drawn = NewlyHeldCards(afterPlayer.Hand, beforePlayer.Hand.Select(card => card.InstanceId));
            Console.WriteLine($"           【超进化时】抽到：{Cards(drawn)}");
        }

        var crestEffect = isSuperEvolution
            ? beforeFollower.Definition.SuperEvolutionEffects?.SingleOrDefault(effect =>
                effect.Kind == CardEffectKind.GiveEnemyCrest)
            : null;
        if (crestEffect is not null)
        {
            var opponent = OtherPlayer(player);
            var gainedCrest = step.AfterState.Players[opponent].Crests
                .SingleOrDefault(crest => crest.Definition.Id == crestEffect.ReferencedCardId);
            Console.WriteLine(gainedCrest is null
                ? "           【超进化时】对手的主战者区域已满，未能获得纹章。"
                : $"           【超进化时】Player {opponent + 1} 获得纹章：{gainedCrest.Definition.Name}。" );
        }
    }

    private static void PrintEvolutionEffects(
        MatchStep step,
        FollowerInstance source,
        bool isSuperEvolution,
        IReadOnlyList<int>? ownHandCardTargetInstanceIds,
        int? enemyFollowerTargetInstanceId = null)
    {
        var effects = isSuperEvolution
            ? source.Definition.SuperEvolutionEvolutionEffects ?? source.Definition.EvolutionEffects
            : source.Definition.EvolutionEffects;
        var onEvolveGain = source.Definition.OnEvolveEffects?
            .FirstOrDefault(effect => effect.Kind == CardEffectKind.AddCopyToHand);
        if ((effects is null || effects.Count == 0) && onEvolveGain is null)
        {
            return;
        }

        effects ??= [];

        var player = step.ActingPlayer;
        var before = step.BeforeState.Players[player];
        var after = step.AfterState.Players[player];
        var summoned = after.Board
            .Where(follower => before.Board.All(beforeFollower => beforeFollower.InstanceId != follower.InstanceId))
            .ToArray();

        if (effects.Any(effect => effect.Kind == CardEffectKind.SummonFollower))
        {
            Console.WriteLine(summoned.Length == 0
                ? "           【进化时】战场已满，无法召唤随从。"
                : $"           【进化时】召唤：{Board(summoned)}");
        }

        var statEffect = effects.SingleOrDefault(effect =>
            effect.Kind == CardEffectKind.GainStatsToOtherAlliedFollowers);
        if (statEffect is not null)
        {
            // Only list the followers that actually gained stats, so a profession-filtered effect
            // does not claim the rest of the board was strengthened.
            var strengthened = after.Board
                .Where(follower => follower.InstanceId != source.InstanceId)
                .Select(follower => (
                    After: follower,
                    Before: before.Board.FirstOrDefault(candidate => candidate.InstanceId == follower.InstanceId)))
                .Where(pair => pair.Before is null ||
                               pair.Before.Attack != pair.After.Attack ||
                               pair.Before.CurrentDefense != pair.After.CurrentDefense)
                .Select(pair => pair.Before is null
                    ? Follower(pair.After)
                    : $"{Follower(pair.Before)} → {Follower(pair.After)}");
            Console.WriteLine(
                $"           【{(isSuperEvolution ? "超进化" : "进化")}时】其他己方随从 +{statEffect.Amount}/+{statEffect.Amount}：{string.Join("、", strengthened.DefaultIfEmpty("没有符合条件的随从"))}");
        }

        var addedCardEffect = effects.SingleOrDefault(effect => effect.Kind == CardEffectKind.AddCopyToHand);
        if (addedCardEffect is not null)
        {
            var previousHandIds = before.Hand.Select(card => card.InstanceId).ToHashSet();
            var added = after.Hand.Where(card => !previousHandIds.Contains(card.InstanceId));
            Console.WriteLine($"           【{(isSuperEvolution ? "超进化" : "进化")}时】加入手牌：{Cards(added)}");
        }

        if (effects.Any(effect => effect.Kind == CardEffectKind.DiscardOwnHandCardsUpTo))
        {
            var discarded = (ownHandCardTargetInstanceIds ?? [])
                .Select(targetId => before.Hand.Single(card => card.InstanceId == targetId));
            Console.WriteLine($"           【{(isSuperEvolution ? "超进化" : "进化")}时】舍弃：{Cards(discarded)}");
        }

        if (effects.Any(effect => effect.Kind == CardEffectKind.DrawCards))
        {
            var retainedIds = before.Hand
                .Where(card => !(ownHandCardTargetInstanceIds ?? []).Contains(card.InstanceId))
                .Select(card => card.InstanceId);
            Console.WriteLine($"           【{(isSuperEvolution ? "超进化" : "进化")}时】抽到：{Cards(NewlyHeldCards(after.Hand, retainedIds))}");
        }

        var targetedDamage = effects.SingleOrDefault(effect =>
            effect.Kind == CardEffectKind.DealDamageToEnemyFollower);
        if (targetedDamage is not null)
        {
            var actionName = isSuperEvolution ? "超进化时" : "进化时";
            if (enemyFollowerTargetInstanceId is null)
            {
                Console.WriteLine($"           【{actionName}】对手战场上没有随从，没有可选择的伤害目标。");
            }
            else
            {
                var opponent = OtherPlayer(player);
                var targetBefore = step.BeforeState.Players[opponent].Board
                    .Single(follower => follower.InstanceId == enemyFollowerTargetInstanceId.Value);
                var targetAfter = step.AfterState.Players[opponent].Board
                    .SingleOrDefault(follower => follower.InstanceId == enemyFollowerTargetInstanceId.Value);
                Console.WriteLine(targetAfter is null
                    ? $"           【{actionName}】对 {Follower(targetBefore)} 造成 {targetedDamage.Amount} 点伤害：被破坏"
                    : $"           【{actionName}】对 {Follower(targetBefore)} 造成 {targetedDamage.Amount} 点伤害 → 剩余体力 {targetAfter.CurrentDefense}");
            }
        }

        var restore = effects.Where(effect => effect.Kind == CardEffectKind.RestoreOwnLeaderHealth).Sum(effect => effect.Amount);
        if (restore > 0)
        {
            Console.WriteLine(
                $"           【{(isSuperEvolution ? "超进化" : "进化")}时】主战者回复 {restore} 点生命：{LeaderHealth(before)}→{LeaderHealth(after)}");
        }

        if (onEvolveGain is not null)
        {
            var retainedHandIds = before.Hand.Select(card => card.InstanceId);
            Console.WriteLine(
                $"           【本随从进化时】加入手牌：{Cards(NewlyHeldCards(after.Hand, retainedHandIds))}");
        }
    }

    private static void PrintExtraPlayPoint(MatchStep step)
    {
        var player = step.ActingPlayer;
        var before = step.BeforeState.Players[player];
        var after = step.AfterState.Players[player];
        var allowance = before.OwnTurnNumber <= 5 ? "前 5 回合额度" : "第 6 回合后的额度";

        Console.WriteLine(
            $"[额外 PP] 后手 Player {player + 1} 使用{allowance}；PP {before.CurrentPlayPoints}→{after.CurrentPlayPoints}");
    }

    private static void PrintLeaderAttack(MatchStep step, AttackLeaderAction action)
    {
        var player = step.ActingPlayer;
        var opponent = OtherPlayer(player);
        var attacker = step.BeforeState.Players[player].Board.Single(follower => follower.InstanceId == action.AttackerInstanceId);
        var beforeHealth = step.BeforeState.Players[opponent].Health;
        var afterHealth = step.AfterState.Players[opponent].Health;

        Console.WriteLine(
            $"[攻击领袖] Player {player + 1} 的 {Follower(attacker)} 攻击 Player {opponent + 1}；生命 {beforeHealth}→{afterHealth}");

        PrintAttackEffect(step, attacker);

        PrintWinnerIfFinished(step.AfterState);
    }

    private static void PrintFollowerAttack(MatchStep step, AttackFollowerAction action)
    {
        var player = step.ActingPlayer;
        var opponent = OtherPlayer(player);
        var attacker = step.BeforeState.Players[player].Board.Single(follower => follower.InstanceId == action.AttackerInstanceId);
        var defender = step.BeforeState.Players[opponent].Board.Single(follower => follower.InstanceId == action.DefenderInstanceId);

        var afterAttacker = step.AfterState.Players[player].Board
            .SingleOrDefault(follower => follower.InstanceId == action.AttackerInstanceId);
        var afterDefender = step.AfterState.Players[opponent].Board
            .SingleOrDefault(follower => follower.InstanceId == action.DefenderInstanceId);

        Console.WriteLine(
            $"[随从交换] P{player + 1} 的 {Follower(attacker)} 攻击 P{opponent + 1} 的 {Follower(defender)}");
        PrintAttackEffect(step, attacker);
        Console.WriteLine(
            $"           攻击者结果：{BattleResult(attacker, afterAttacker)}；防御者结果：{BattleResult(defender, afterDefender)}");
        Console.WriteLine(
            $"           场面：P{player + 1} {Board(step.AfterState.Players[player].Board)} ｜ P{opponent + 1} {Board(step.AfterState.Players[opponent].Board)}");

        var opponentHealthBefore = step.BeforeState.Players[opponent].Health;
        var opponentHealthAfter = step.AfterState.Players[opponent].Health;
        if (opponentHealthBefore != opponentHealthAfter)
        {
            Console.WriteLine(
                $"           超进化击破追加伤害：Player {opponent + 1} 生命 {opponentHealthBefore}→{opponentHealthAfter}");
            PrintWinnerIfFinished(step.AfterState);
        }

        PrintLastWords(step, player, attacker, afterAttacker);
        PrintLastWords(step, opponent, defender, afterDefender);
        PrintBanished(step);
    }

    private static void PrintAttackEffect(MatchStep step, FollowerInstance attacker)
    {
        if (attacker.Definition.AttackEffects?.Any(effect =>
                effect.Kind == CardEffectKind.GainTemporaryAttackIfOwnFollowersAttackedEnemyLeaderPreviousTurn) != true)
        {
            return;
        }

        Console.WriteLine(step.BeforeState.Players[step.ActingPlayer].AttackedEnemyLeaderOnPreviousTurn
            ? "           【攻击时】上一回合已攻击主战者，本随从回合结束前 +1/+0。"
            : "           【攻击时】上一回合未攻击主战者，未获得 +1/+0。");
    }

    private static void PrintLastWords(
        MatchStep step,
        int owner,
        FollowerInstance beforeFollower,
        FollowerInstance? afterFollower)
    {
        if (afterFollower is not null || beforeFollower.Card.HasSuppressedLastWords ||
            beforeFollower.Definition.LastWordsEffects is not { Count: > 0 })
        {
            return;
        }

        var beforeHandIds = step.BeforeState.Players[owner].Hand.Select(card => card.InstanceId);
        var added = NewlyHeldCards(step.AfterState.Players[owner].Hand, beforeHandIds);
        if (added.Any())
        {
            Console.WriteLine($"           【谢幕曲】Player {owner + 1} 获得：{Cards(added)}（该复制失去【谢幕曲】）。");
        }

        var beforeBoardIds = step.BeforeState.Players[owner].Board.Select(follower => follower.InstanceId);
        var summoned = step.AfterState.Players[owner].Board
            .Where(follower => !beforeBoardIds.Contains(follower.InstanceId))
            .ToArray();
        if (summoned.Length > 0)
        {
            Console.WriteLine($"           【谢幕曲】Player {owner + 1} 召唤：{Board(summoned)}。");
        }
    }

    /// <summary>
    /// Reports followers that left play without being destroyed, i.e.【消失】: they are gone from the
    /// board but never entered their owner's graveyard.
    /// </summary>
    private static void PrintBanished(MatchStep step)
    {
        for (var player = 0; player < 2; player++)
        {
            var before = step.BeforeState.Players[player];
            var after = step.AfterState.Players[player];
            foreach (var follower in before.Board)
            {
                if (after.Board.Any(candidate => candidate.InstanceId == follower.InstanceId) ||
                    after.Graveyard.Any(card => card.InstanceId == follower.InstanceId))
                {
                    continue;
                }

                Console.WriteLine($"           【消失】Player {player + 1} 的 {Follower(follower)} 离场并消失。");
            }
        }
    }

    private static void PrintEndTurn(MatchStep step)
    {
        var player = step.ActingPlayer;
        var nextPlayer = OtherPlayer(player);

        Console.WriteLine($"[结束回合] Player {player + 1} 结束回合。");

        PrintEndOfTurnAmuletEffects(step);
        PrintBanished(step);
        PrintEndOfTurnCrestEffects(step);
        PrintStartOfTurnCrestEffects(step, nextPlayer);
        PrintCountdownChanges(step, nextPlayer);

        if (step.AfterState.IsGameOver)
        {
            Console.WriteLine($"           Player {nextPlayer + 1} 牌库已空，无法抽牌。");
            PrintWinnerIfFinished(step.AfterState);
            return;
        }

        var beforeHandIds = step.BeforeState.Players[nextPlayer].Hand
            .Select(card => card.InstanceId);
        var drawn = NewlyHeldCards(step.AfterState.Players[nextPlayer].Hand, beforeHandIds);
        var next = step.AfterState.Players[nextPlayer];

        Console.WriteLine();
        Console.WriteLine($"========== 第 {step.AfterState.TurnNumber} 回合：Player {nextPlayer + 1} ==========");
        Console.WriteLine(
            $"{TurnResources(step.AfterState, nextPlayer)}；抽到：{Cards(drawn)}；手牌：{Cards(next.Hand)}");
    }

    /// <summary>Prints a crest's 「自己的回合结束时」 resolution, such as the one 伊斯坦戴德 grants.</summary>
    private static void PrintEndOfTurnCrestEffects(MatchStep step)
    {
        var player = step.ActingPlayer;
        var crests = step.BeforeState.Players[player].Crests
            .Where(crest => crest.Definition.EndOfOwnTurnEffects is { Count: > 0 })
            .ToArray();
        foreach (var crest in crests)
        {
            var lostFollowers = step.BeforeState.Players[player].Board
                .Where(follower => step.AfterState.Players[player].Board
                    .All(candidate => candidate.InstanceId != follower.InstanceId))
                .Select(Follower)
                .ToArray();
            var lostAmulets = step.BeforeState.Players[player].Amulets
                .Where(amulet => step.AfterState.Players[player].Amulets
                    .All(candidate => candidate.InstanceId != amulet.InstanceId))
                .Select(amulet => amulet.Definition.Name)
                .ToArray();
            var enemy = OtherPlayer(player);
            var lostEnemyFollowers = step.BeforeState.Players[enemy].Board
                .Where(follower => step.AfterState.Players[enemy].Board
                    .All(candidate => candidate.InstanceId != follower.InstanceId))
                .Select(Follower)
                .ToArray();
            Console.WriteLine(
                $"           【纹章：{crest.Definition.Name}】回合结束：破坏了 {Join(lostFollowers, lostAmulets)}；对手被破坏：{Join(lostEnemyFollowers)}");
        }
    }

    private static string Join(params string[][] groups)
    {
        var items = groups.SelectMany(group => group).ToArray();
        return items.Length == 0 ? "无" : string.Join("、", items);
    }

    private static void PrintEndOfTurnAmuletEffects(MatchStep step)
    {
        var player = step.ActingPlayer;
        var relevantAmulets = step.BeforeState.Players[player].Amulets
            .Where(amulet => amulet.Definition.EndOfOwnTurnEffects?.Count > 0)
            .ToArray();
        if (relevantAmulets.Length == 0)
        {
            return;
        }

        var results = ChangedFollowerResults(
                step.BeforeState.Players[player].Board,
                step.AfterState.Players[player].Board)
            .Concat(ChangedFollowerResults(
                step.BeforeState.Players[OtherPlayer(player)].Board,
                step.AfterState.Players[OtherPlayer(player)].Board))
            .ToArray();
        foreach (var amulet in relevantAmulets)
        {
            var damage = amulet.Definition.EndOfOwnTurnEffects!
                .SingleOrDefault(effect => effect.Kind == CardEffectKind.DealDamageToAllFollowersWithoutTrait);
            if (damage is not null)
            {
                Console.WriteLine(results.Length == 0
                    ? $"           【{amulet.Definition.Name}】回合结束效果发动：没有非{damage.ReferencedCardId}随从受到伤害。"
                    : $"           【{amulet.Definition.Name}】回合结束效果：所有非{damage.ReferencedCardId}随从各受 {damage.Amount} 点伤害：{string.Join("；", results)}");
            }
        }
    }

    private static void PrintStartOfTurnCrestEffects(MatchStep step, int player)
    {
        var startDamageCrests = step.BeforeState.Players[player].Crests
            .Where(crest => crest.Definition.StartOfOwnTurnEffects?.Any(effect =>
                effect.Kind == CardEffectKind.DealDamageToOwnLeader) == true)
            .ToArray();
        if (startDamageCrests.Length == 0)
        {
            return;
        }

        var before = step.BeforeState.Players[player];
        var after = step.AfterState.Players[player];
        foreach (var crest in startDamageCrests)
        {
            var damage = crest.Definition.StartOfOwnTurnEffects!
                .Where(effect => effect.Kind == CardEffectKind.DealDamageToOwnLeader)
                .Sum(effect => effect.Amount);
            Console.WriteLine(
                $"           【纹章：{crest.Definition.Name}】Player {player + 1} 回合开始，主战者受 {damage} 点伤害：{LeaderHealth(before)}→{LeaderHealth(after)}。" );
        }
    }

    private static void PrintCountdownChanges(MatchStep step, int player)
    {
        foreach (var before in step.BeforeState.Players[player].Amulets.Where(amulet => amulet.Countdown is not null))
        {
            var after = step.AfterState.Players[player].Amulets
                .SingleOrDefault(amulet => amulet.InstanceId == before.InstanceId);
            Console.WriteLine(after is null
                ? $"           【{before.Definition.Name}】吟唱 {before.Countdown}→0，护符被破坏。"
                : $"           【{before.Definition.Name}】吟唱 {before.Countdown}→{after.Countdown}。");
            if (after is null && before.LastWordsEffects is { Count: > 0 })
            {
                var boardIdsBefore = step.BeforeState.Players[player].Board.Select(follower => follower.InstanceId);
                var summoned = step.AfterState.Players[player].Board
                    .Where(follower => !boardIdsBefore.Contains(follower.InstanceId))
                    .ToArray();
                if (summoned.Length > 0)
                {
                    Console.WriteLine($"           【谢幕曲】召唤：{Board(summoned)}");
                }
            }
        }
    }

    private static IEnumerable<CardInstance> NewlyHeldCards(
        IReadOnlyList<CardInstance> after,
        IEnumerable<int> beforeIds)
    {
        var existing = beforeIds.ToHashSet();
        return after.Where(card => !existing.Contains(card.InstanceId));
    }

    private static void PrintWinnerIfFinished(GameState state)
    {
        if (state.IsGameOver)
        {
            Console.WriteLine($"           Player {state.Winner!.Value + 1} 获胜！");
        }
    }

    private static string BattleResult(FollowerInstance before, FollowerInstance? after)
    {
        return after is null
            ? $"{before.Definition.Name} 被破坏"
            : $"{after.Definition.Name} 剩余体力 {after.CurrentDefense}/{after.MaxDefense}" +
              (BarrierWasConsumed(before, after) ? "，【屏障】抵消本次伤害后消失" : string.Empty);
    }

    private static bool BarrierWasConsumed(FollowerInstance before, FollowerInstance? after) =>
        after is not null && before.HasBarrier && !after.HasBarrier;

    private static string ActionDescription(GameState state, GameAction action)
    {
        var active = state.Players[state.ActivePlayer];
        var opponent = state.Players[OtherPlayer(state.ActivePlayer)];

        return action switch
        {
            PlayFollowerAction play => $"打出 {active.Hand.Single(card => card.InstanceId == play.CardInstanceId).Definition.Name}",
            PlayAmuletAction playAmulet => $"打出护符 {active.Hand.Single(card => card.InstanceId == playAmulet.CardInstanceId).Definition.Name}",
            PlayCrystallizeAction crystallize =>
                $"以结晶打出 {active.Hand.Single(card => card.InstanceId == crystallize.CardInstanceId).Definition.Name}",
            PlayAccelerateAction accelerate => $"激奏 {active.Hand.Single(card => card.InstanceId == accelerate.CardInstanceId).Definition.Name}",
            PlaySpellAction spell => $"使用 {active.Hand.Single(card => card.InstanceId == spell.CardInstanceId).Definition.Name}",
            EvolveAction evolve =>
                $"进化 {active.Board.Single(follower => follower.InstanceId == evolve.FollowerInstanceId).Definition.Name}" +
                EvolutionTargetSuffix(opponent.Board, evolve.EnemyFollowerTargetInstanceId),
            SuperEvolveAction superEvolve =>
                $"超进化 {active.Board.Single(follower => follower.InstanceId == superEvolve.FollowerInstanceId).Definition.Name}" +
                EvolutionTargetSuffix(opponent.Board, superEvolve.EnemyFollowerTargetInstanceId),
            AttackLeaderAction attackLeader => $"{active.Board.Single(follower => follower.InstanceId == attackLeader.AttackerInstanceId).Definition.Name} 攻击主战者",
            AttackFollowerAction attackFollower =>
                $"{active.Board.Single(follower => follower.InstanceId == attackFollower.AttackerInstanceId).Definition.Name} 攻击 {opponent.Board.Single(follower => follower.InstanceId == attackFollower.DefenderInstanceId).Definition.Name}",
            UseExtraPlayPointAction => "使用额外 PP",
            EndTurnAction => "结束回合",
            MulliganAction mulligan => $"重抽 {mulligan.ReplaceInstanceIds.Count} 张",
            _ => "未知操作"
        };
    }

    private static string Cards(IEnumerable<CardInstance> cards)
    {
        var text = string.Join("、", cards.Select(Card));
        return string.IsNullOrEmpty(text) ? "无" : text;
    }

    private static string LeaderHealth(PlayerState player) => $"{player.Health}/{player.MaxHealth}";

    private static string Card(CardInstance card)
    {
        var definition = card.Definition;
        var details = definition.Type switch
        {
            CardType.Follower => $"{definition.Cost}费 {definition.Attack}/{definition.Defense}{Keywords(definition.Keywords)}",
            CardType.Spell => $"{definition.Cost}费 法术",
            CardType.Amulet => $"{definition.Cost}费 护符",
            _ => throw new InvalidOperationException("Unknown card type.")
        };

        return $"{definition.Name}（{details}）";
    }

    private static string Board(IReadOnlyList<FollowerInstance> board)
    {
        var text = string.Join("、", board.Select(Follower));
        return string.IsNullOrEmpty(text) ? "空" : text;
    }

    /// <summary>Names the enemy follower an 【进化时】 effect chose, when it had one.</summary>
    private static string EvolutionTargetSuffix(IReadOnlyList<FollowerInstance> opponentBoard, int? targetInstanceId)
    {
        if (targetInstanceId is null)
        {
            return string.Empty;
        }

        var target = opponentBoard.FirstOrDefault(follower => follower.InstanceId == targetInstanceId.Value);
        return target is null ? string.Empty : $" → {Follower(target)}";
    }

    private static string Follower(FollowerInstance follower) =>
        $"{follower.Definition.Name}（{follower.Attack}/{follower.CurrentDefense}{Keywords(follower.Keywords)}{Evolution(follower.EvolutionState)}）";

    private static string Countdown(AmuletInstance amulet) =>
        amulet.Countdown is { } countdown ? $"【吟唱 {countdown}】" : "无吟唱";

    private static string TurnResources(GameState state, int player)
    {
        var current = state.Players[player];
        var extraPlayPoint = player == state.StartingPlayer
            ? "额外PP：无"
            : $"额外PP：前期{(current.UsedEarlyExtraPlayPoint ? "已用" : "可用")}／后期{(current.UsedLateExtraPlayPoint ? "已用" : "可用")}";

        return $"PP：{current.CurrentPlayPoints}/{current.MaxPlayPoints}；EP：{current.EvolutionPoints}；SEP：{current.SuperEvolutionPoints}；{extraPlayPoint}";
    }

    private static string Keywords(CardKeyword keywords)
    {
        var labels = new List<string>();
        if (keywords.HasFlag(CardKeyword.Ward))
        {
            labels.Add("守护");
        }

        if (keywords.HasFlag(CardKeyword.Storm))
        {
            labels.Add("疾驰");
        }

        if (keywords.HasFlag(CardKeyword.Bane))
        {
            labels.Add("毁灭");
        }

        if (keywords.HasFlag(CardKeyword.Intimidate))
        {
            labels.Add("威慑");
        }

        if (keywords.HasFlag(CardKeyword.Rush))
        {
            labels.Add("突进");
        }

        if (keywords.HasFlag(CardKeyword.IgnoreWard))
        {
            labels.Add("无视守护");
        }

        if (keywords.HasFlag(CardKeyword.Barrier))
        {
            labels.Add("屏障");
        }

        if (keywords.HasFlag(CardKeyword.Aura))
        {
            labels.Add("灵气");
        }

        if (keywords.HasFlag(CardKeyword.Drain))
        {
            labels.Add("虹吸");
        }

        return labels.Count == 0 ? string.Empty : $" 【{string.Join("、】【", labels)}】";
    }

    private static string Evolution(EvolutionState evolutionState) => evolutionState switch
    {
        EvolutionState.Evolved => " 【进化】",
        EvolutionState.SuperEvolved => " 【超进化】",
        _ => string.Empty
    };

    private static int OtherPlayer(int player) => player == 0 ? 1 : 0;
}
