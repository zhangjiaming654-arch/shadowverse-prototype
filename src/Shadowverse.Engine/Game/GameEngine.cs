using Shadowverse.Engine.Cards;
using Shadowverse.Engine.Models;

namespace Shadowverse.Engine.Game;

public static class GameEngine
{
    public static GameState CreateGame(DeckDefinition firstDeck, DeckDefinition secondDeck, ulong seed)
    {
        ArgumentNullException.ThrowIfNull(firstDeck);
        ArgumentNullException.ThrowIfNull(secondDeck);

        var state = new GameState(
            new PlayerState(firstDeck.Name),
            new PlayerState(secondDeck.Name),
            seed == 0 ? 1UL : seed);

        AddDeckCards(state, 0, firstDeck);
        AddDeckCards(state, 1, secondDeck);
        Shuffle(state, 0);
        Shuffle(state, 1);

        state.StartingPlayer = NextInt(state, 2);
        state.ActivePlayer = state.StartingPlayer;
        state.Phase = GamePhase.Mulligan;

        DrawCards(state, 0, 4);
        DrawCards(state, 1, 4);
        return state;
    }

    public static IReadOnlyList<GameAction> GetLegalActions(GameState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (state.IsGameOver)
        {
            return [];
        }

        return state.Phase switch
        {
            GamePhase.Mulligan => GetMulliganActions(state),
            GamePhase.Main => GetMainPhaseActions(state),
            _ => []
        };
    }

    /// <summary>
    /// Copies a state without advancing it, including its internal random state.
    /// <para>
    /// Needed by causal-branch experiments: two branches must start from a byte-identical
    /// snapshot and then differ only in the one forced action under test.
    /// <see cref="GameState.DeepCopy"/> is internal, so this is the public seam.
    /// This adds an API only; it changes no existing behaviour.
    /// </para>
    /// </summary>
    public static GameState Clone(GameState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return state.DeepCopy();
    }

    /// <summary>
    /// 完整状态指纹：覆盖两名玩家的全部标量、全部可变实例字段，以及
    /// <c>Phase / StartingPlayer / ActivePlayer / TurnNumber / Winner / RandomState / NextInstanceId</c>。
    /// <para>
    /// <b>它不是"决策指纹"</b>。决策指纹只哈希回合 + 行动方 + 合法动作，即使共享引用被改写也可能保持不变，
    /// 因此**不能**用来证明状态未被污染。分支实验的污染检查必须用这个完整指纹。
    /// </para>
    /// </summary>
    public static string StateFingerprint(GameState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        var builder = new System.Text.StringBuilder();
        builder.Append(state.Phase).Append('|')
            .Append(state.StartingPlayer).Append('|')
            .Append(state.ActivePlayer).Append('|')
            .Append(state.TurnNumber).Append('|')
            .Append(state.Winner?.ToString() ?? "-").Append('|')
            .Append(state.RandomState).Append('|')
            .Append(state.NextInstanceId).Append('|');
        for (var seat = 0; seat < state.Players.Length; seat++)
        {
            AppendPlayerFingerprint(builder, seat, state.Players[seat]);
        }

        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(builder.ToString())));
    }

    private static void AppendPlayerFingerprint(System.Text.StringBuilder builder, int seat, PlayerState player)
    {
        builder.Append('#').Append(seat).Append(':')
            .Append(player.DeckName).Append(',')
            .Append(player.Health).Append(',').Append(player.MaxHealth).Append(',')
            .Append(player.CurrentPlayPoints).Append(',').Append(player.MaxPlayPoints).Append(',')
            .Append(player.OwnTurnNumber).Append(',')
            .Append(player.EvolutionPoints).Append(',').Append(player.SuperEvolutionPoints).Append(',')
            .Append(player.UsedEvolutionOrSuperEvolutionThisTurn ? 1 : 0).Append(',')
            .Append(player.UsedEarlyExtraPlayPoint ? 1 : 0).Append(',')
            .Append(player.UsedLateExtraPlayPoint ? 1 : 0).Append(',')
            .Append(player.AttackedEnemyLeaderThisTurn ? 1 : 0).Append(',')
            .Append(player.AttackedEnemyLeaderOnPreviousTurn ? 1 : 0).Append(';');

        foreach (var card in player.DeckInternal) { AppendCardFingerprint(builder, card); }

        builder.Append(';');
        foreach (var card in player.HandInternal) { AppendCardFingerprint(builder, card); }

        builder.Append(';');
        foreach (var follower in player.BoardInternal)
        {
            builder.Append('F').Append(follower.InstanceId).Append(',')
                .Append(follower.Attack).Append(',').Append(follower.MaxDefense).Append(',')
                .Append(follower.CurrentDefense).Append(',')
                .Append(follower.HasAttacked ? 1 : 0).Append(',')
                .Append(follower.EvolutionState).Append(',')
                .Append((int)follower.GrantedKeywords).Append(',').Append((int)follower.ConsumedKeywords).Append(',')
                .Append(follower.TemporaryAttackBonus).Append(',')
                .Append(follower.SummonedOnTurn).Append(';');
            AppendCardFingerprint(builder, follower.Card);
        }

        builder.Append(';');
        foreach (var amulet in player.AmuletsInternal)
        {
            builder.Append('A').Append(amulet.InstanceId).Append(',')
                .Append(amulet.Countdown?.ToString() ?? "-").Append(',')
                // 显式编码"普通护符 / 结晶护符"身份：CurrentCost 与 LastWords 都取决于它
                .Append(amulet.Crystallized is null
                    ? "-"
                    : $"c{amulet.Crystallized.Cost}x{amulet.Crystallized.Countdown}")
                .Append(';');
            AppendCardFingerprint(builder, amulet.Card);
        }

        builder.Append(';');
        foreach (var effect in player.TimedLeaderEffectsInternal)
        {
            builder.Append('T').Append(effect.Kind).Append(',').Append(effect.ExpiresAtEndOfPlayerTurn).Append(';');
        }

        builder.Append(';');
        foreach (var crest in player.CrestsInternal)
        {
            builder.Append('C').Append(crest.Definition.Id).Append(',')
                .Append(crest.LastOwnLeaderRestoreTriggerTurn).Append(';');
        }

        builder.Append(';');
        foreach (var card in player.GraveyardInternal) { AppendCardFingerprint(builder, card); }

        builder.Append(';');
        foreach (var id in player.RevealedCardIdsInternal) { builder.Append(id).Append(','); }

        builder.Append(';');
    }

    private static void AppendCardFingerprint(System.Text.StringBuilder builder, CardInstance card)
    {
        // CardDefinition 是不可变的共享目录定义。这里**必须**用跨进程稳定的标识，
        // 且用唯一的 Id 而非 Name（Name 可能重名）。不能用 RuntimeHelpers.GetHashCode ——
        // 引用身份哈希每个进程都不同，会让同一个局面在两次运行里算出不同指纹（实测踩到过）。
        builder.Append(card.Definition.Id).Append(',')
            .Append(card.InstanceId).Append(',')
            .Append(card.HasSuppressedLastWords ? 1 : 0).Append(',')
            .Append(card.CostReduction).Append(';');
    }

    /// <summary>
    /// 克隆完整性审计：两个状态之间是否共享**任何**可变的 <see cref="CardInstance"/> 引用。
    /// <para>
    /// 比逐个 <c>ReferenceEquals</c> 更强：只要返回 false，就说明不存在任何能让一条分支的
    /// 改动（例如回手之后触发手牌减费）泄漏到另一条分支的卡牌别名路径，
    /// 不需要依赖某张具体卡去构造动作链。
    /// </para>
    /// </summary>
    public static bool ShareAnyCardInstance(GameState first, GameState second)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);

        var left = new HashSet<CardInstance>(ReferenceEqualityComparer.Instance);
        var right = new HashSet<CardInstance>(ReferenceEqualityComparer.Instance);
        CollectCardInstances(first, left);
        CollectCardInstances(second, right);
        return left.Overlaps(right);
    }

    /// <summary>收集一个状态里可达的全部 <see cref="CardInstance"/>（含场上随从与护符所持有的卡）。</summary>
    public static IReadOnlyCollection<CardInstance> CollectCardInstances(GameState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var set = new HashSet<CardInstance>(ReferenceEqualityComparer.Instance);
        CollectCardInstances(state, set);
        return set;
    }

    private static void CollectCardInstances(GameState state, HashSet<CardInstance> set)
    {
        foreach (var player in state.Players)
        {
            foreach (var card in player.DeckInternal) { set.Add(card); }
            foreach (var card in player.HandInternal) { set.Add(card); }
            foreach (var card in player.GraveyardInternal) { set.Add(card); }
            foreach (var follower in player.BoardInternal) { set.Add(follower.Card); }
            foreach (var amulet in player.AmuletsInternal) { set.Add(amulet.Card); }
        }
    }

    /// <summary>Returns a new state, leaving the supplied state unchanged for replay and search use.</summary>
    public static GameState Apply(GameState state, GameAction action)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(action);

        if (state.IsGameOver)
        {
            throw new InvalidOperationException("The game is already over.");
        }

        var next = state.DeepCopy();

        if (next.Phase == GamePhase.Mulligan)
        {
            if (action is not MulliganAction mulligan)
            {
                throw new InvalidOperationException("Only a mulligan action is valid during the mulligan phase.");
            }

            ApplyMulligan(next, mulligan);
            return next;
        }

        RecordRevealedCards(next, action);

        switch (action)
        {
            case PlayFollowerAction play:
                ApplyPlayFollower(next, play);
                break;
            case PlayAmuletAction playAmulet:
                ApplyPlayAmulet(next, playAmulet);
                break;
            case PlayCrystallizeAction crystallize:
                ApplyCrystallize(next, crystallize);
                break;
            case PlayAccelerateAction accelerate:
                ApplyAccelerate(next, accelerate);
                break;
            case PlaySpellAction playSpell:
                ApplyPlaySpell(next, playSpell);
                break;
            case EvolveAction evolve:
                ApplyEvolve(next, evolve);
                break;
            case SuperEvolveAction superEvolve:
                ApplySuperEvolve(next, superEvolve);
                break;
            case UseExtraPlayPointAction:
                ApplyExtraPlayPoint(next);
                break;
            case AttackLeaderAction attackLeader:
                ApplyAttackLeader(next, attackLeader);
                break;
            case AttackFollowerAction attackFollower:
                ApplyAttackFollower(next, attackFollower);
                break;
            case EndTurnAction:
                ApplyEndTurn(next);
                break;
            default:
                throw new InvalidOperationException("Unknown action type.");
        }

        return next;
    }

    public static GameObservation ToObservation(GameState state, int perspectivePlayer)
    {
        ArgumentNullException.ThrowIfNull(state);
        ValidatePlayerIndex(perspectivePlayer);

        var opponent = OtherPlayer(perspectivePlayer);
        return new GameObservation(
            perspectivePlayer,
            state.ActivePlayer,
            state.TurnNumber,
            state.Phase,
            ToPlayerView(state.Players[perspectivePlayer]),
            ToPlayerView(state.Players[opponent]),
            state.Players[perspectivePlayer].Hand
                .Select(card => new HandCardView(card.InstanceId, card.Definition))
                .ToArray(),
            // 排序后再交出去：内容对本人公开，顺序对本人也是隐藏信息。
            state.Players[perspectivePlayer].Deck
                .Select(card => card.Definition.Id)
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToArray());
    }

    /// <summary>
    /// Builds one legal imagined future for a planning agent. The acting player keeps their
    /// known hand, board, and graveyard, while unseen deck order is shuffled. The opponent's
    /// hidden hand and deck are mixed and dealt again with the same public hand/deck counts.
    /// This prevents a rollout agent from using the live hidden card locations as information.
    /// </summary>
    public static GameState CreateDeterminization(GameState state, int perspectivePlayer, ulong seed)
    {
        ArgumentNullException.ThrowIfNull(state);
        ValidatePlayerIndex(perspectivePlayer);

        var determinization = state.DeepCopy();
        determinization.RandomState = seed == 0 ? 1UL : seed;

        // The acting player's own deck is known as a card list but not as an order.
        Shuffle(determinization, perspectivePlayer);

        var opponent = determinization.Players[OtherPlayer(perspectivePlayer)];
        var hiddenHandCount = opponent.HandInternal.Count;
        var hiddenCards = opponent.HandInternal.Concat(opponent.DeckInternal).ToList();
        opponent.HandInternal.Clear();
        opponent.DeckInternal.Clear();

        ShuffleCards(determinization, hiddenCards);
        opponent.HandInternal.AddRange(hiddenCards.Take(hiddenHandCount));
        opponent.DeckInternal.AddRange(hiddenCards.Skip(hiddenHandCount));

        return determinization;
    }

    private static IReadOnlyList<GameAction> GetMulliganActions(GameState state)
    {
        var hand = state.Players[state.ActivePlayer].Hand;
        var actions = new List<GameAction>();

        for (var mask = 0; mask < (1 << hand.Count); mask++)
        {
            var replacements = new List<int>();
            for (var cardIndex = 0; cardIndex < hand.Count; cardIndex++)
            {
                if ((mask & (1 << cardIndex)) != 0)
                {
                    replacements.Add(hand[cardIndex].InstanceId);
                }
            }

            actions.Add(new MulliganAction(replacements));
        }

        return actions;
    }

    private static IReadOnlyList<GameAction> GetMainPhaseActions(GameState state)
    {
        var actions = new List<GameAction>();
        var active = state.Players[state.ActivePlayer];
        var opponent = state.Players[OtherPlayer(state.ActivePlayer)];

        foreach (var card in active.Hand)
        {
            if (card.Definition.Type == CardType.Follower &&
                active.OccupiedBoardSlots < PlayerState.BoardLimit &&
                GetCardCost(card, active.CurrentPlayPoints) <= active.CurrentPlayPoints)
            {
                actions.AddRange(GetFollowerActions(state, card));
            }

            if (card.Definition.Type == CardType.Amulet &&
                active.OccupiedBoardSlots < PlayerState.BoardLimit &&
                GetCardCost(card, active.CurrentPlayPoints) <= active.CurrentPlayPoints)
            {
                actions.Add(new PlayAmuletAction(card.InstanceId));
            }

            if (card.Definition.Type == CardType.Spell &&
                GetCardCost(card, active.CurrentPlayPoints) <= active.CurrentPlayPoints)
            {
                actions.AddRange(GetSpellActions(state, card));
            }

            // Accelerate is only available when the normal card cannot be paid for.
            // A full board does not unlock Accelerate when the player has enough PP for
            // the card's normal cost.
            if (card.Definition.Accelerate is { } accelerate &&
                accelerate.Cost <= active.CurrentPlayPoints &&
                active.CurrentPlayPoints < GetCardCost(card, active.CurrentPlayPoints))
            {
                actions.Add(new PlayAccelerateAction(card.InstanceId));
            }

            // 结晶 follows the same condition as Accelerate: it only unlocks while the card's own
            // cost cannot be paid, and the crystallized amulet still needs a free board slot.
            if (card.Definition.Crystallize is { } crystallize &&
                crystallize.Cost <= active.CurrentPlayPoints &&
                active.CurrentPlayPoints < GetCardCost(card, active.CurrentPlayPoints) &&
                active.OccupiedBoardSlots < PlayerState.BoardLimit)
            {
                actions.Add(new PlayCrystallizeAction(card.InstanceId));
            }
        }

        if (CanEvolve(state))
        {
            actions.AddRange(active.Board
                .Where(follower => follower.EvolutionState == EvolutionState.Unevolved)
                .SelectMany(follower => GetEvolveActions(state, follower)));
        }

        if (CanSuperEvolve(state))
        {
            actions.AddRange(active.Board
                .Where(follower => follower.EvolutionState == EvolutionState.Unevolved)
                .SelectMany(follower => GetSuperEvolveActions(state, follower)));
        }

        if (CanUseExtraPlayPoint(state))
        {
            actions.Add(new UseExtraPlayPointAction());
        }

        // Intimidate takes priority over Ward, so an Intimidate follower is neither a
        // legal attack target nor a Ward target that prevents leader attacks.
        var wardTargets = opponent.Board.Where(follower => follower.HasWard && !follower.HasIntimidate).ToArray();
        var followerAttackTargets = opponent.Board.Where(follower => !follower.HasIntimidate).ToArray();
        foreach (var attacker in active.Board.Where(follower => CanAttackFollowerThisTurn(state, follower)))
        {
            if (wardTargets.Length > 0 && !attacker.CanIgnoreWard)
            {
                actions.AddRange(wardTargets.Select(target =>
                    new AttackFollowerAction(attacker.InstanceId, target.InstanceId)));
                continue;
            }

            actions.AddRange(followerAttackTargets.Select(target =>
                new AttackFollowerAction(attacker.InstanceId, target.InstanceId)));
        }

        actions.AddRange(active.Board
            .Where(follower => CanAttackLeaderThisTurn(state, follower) &&
                               (wardTargets.Length == 0 || follower.CanIgnoreWard))
            .Select(follower => new AttackLeaderAction(follower.InstanceId)));

        actions.Add(new EndTurnAction());
        return actions;
    }

    private static IReadOnlyList<GameAction> GetFollowerActions(GameState state, CardInstance follower)
    {
        var effects = GetFanfareEffects(follower.Definition);
        var handTargetOptions = GetHandTargetOptions(state, follower, effects);
        var ownHandTargetOptions = GetOwnHandTargetOptions(state, follower.InstanceId, effects);
        var enemyTargetOptions = GetEnemyFollowerTargetOptions(state, effects);
        var modeChoiceOptions = GetFanfareModeChoiceOptions(follower.Definition);

        return handTargetOptions
            .SelectMany(handTarget => ownHandTargetOptions.Select(ownHandTargets =>
                (handTarget, ownHandTargets)))
            .SelectMany(targets => enemyTargetOptions.Select(enemyTargets =>
                (targets.handTarget, targets.ownHandTargets, enemyTargets)))
            .SelectMany(targets => modeChoiceOptions.Select(modeChoice =>
                (GameAction)new PlayFollowerAction(
                    follower.InstanceId,
                    targets.handTarget,
                    targets.enemyTargets,
                    modeChoice,
                    targets.ownHandTargets)))
            .ToArray();
    }

    private static IReadOnlyList<int?> GetFanfareModeChoiceOptions(CardDefinition definition) =>
        definition.FanfareModeOptions is null
            ? [null]
            : Enumerable.Range(0, definition.FanfareModeOptions.Count).Select(index => (int?)index).ToArray();

    /// <summary>
    /// 【进化时】【模式】: the card's own evolution modes, the Fanfare modes it repeats, or nothing.
    /// </summary>
    private static IReadOnlyList<ModeDefinition>? GetEvolutionModeOptions(CardDefinition definition) =>
        definition.EvolutionModeChoices;

    private static IReadOnlyList<int?> GetEvolutionModeChoiceOptions(CardDefinition definition) =>
        GetEvolutionModeOptions(definition) is { Count: > 0 } options
            ? Enumerable.Range(0, options.Count).Select(index => (int?)index).ToArray()
            : [null];

    private static IReadOnlyList<CardEffect> GetEvolutionModeEffects(CardDefinition definition, int? modeChoiceIndex)
    {
        var options = GetEvolutionModeOptions(definition);
        if (options is null)
        {
            if (modeChoiceIndex is not null)
            {
                throw new InvalidOperationException("This follower does not choose a mode when evolving.");
            }

            return [];
        }

        if (modeChoiceIndex is null || modeChoiceIndex.Value < 0 || modeChoiceIndex.Value >= options.Count)
        {
            throw new InvalidOperationException("A valid evolution mode must be selected.");
        }

        return options[modeChoiceIndex.Value].Effects;
    }

    private static IReadOnlyList<int?> GetHandTargetOptions(
        GameState state,
        CardInstance follower,
        IReadOnlyList<CardEffect> effects)
    {
        if (!effects.Any(effect => effect.Kind == CardEffectKind.ReturnOwnHandCardToDeckThenDrawCards))
        {
            return [null];
        }

        var selectableHandCards = state.Players[state.ActivePlayer].Hand
            .Where(card => card.InstanceId != follower.InstanceId)
            .ToArray();

        // The follower remains playable even when it is the only card in hand.
        // In that case its mandatory target cannot be chosen and the Fanfare does nothing.
        return selectableHandCards.Length == 0
            ? [null]
            : selectableHandCards.Select(card => (int?)card.InstanceId).ToArray();
    }

    private static IReadOnlyList<IReadOnlyList<int>?> GetOwnHandTargetOptions(
        GameState state,
        int? sourceInHandInstanceId,
        IReadOnlyList<CardEffect> effects)
    {
        var discardEffect = effects.SingleOrDefault(effect =>
            effect.Kind is CardEffectKind.DiscardOwnHandCards or CardEffectKind.DiscardOwnHandCardsUpTo);
        if (discardEffect is null)
        {
            return [null];
        }

        var selectableCards = state.Players[state.ActivePlayer].Hand
            .Where(card => card.InstanceId != sourceInHandInstanceId)
            .Select(card => card.InstanceId)
            .ToArray();
        var requiredTargetCount = discardEffect.Kind == CardEffectKind.DiscardOwnHandCardsUpTo
            ? Math.Min(discardEffect.Amount, selectableCards.Length)
            : discardEffect.Amount;
        if (selectableCards.Length < requiredTargetCount)
        {
            return [];
        }

        var options = new List<IReadOnlyList<int>>();
        var selected = new List<int>();

        void AddCombinations(int startIndex)
        {
            if (selected.Count == requiredTargetCount)
            {
                options.Add(selected.ToArray());
                return;
            }

            for (var index = startIndex; index <= selectableCards.Length - (requiredTargetCount - selected.Count); index++)
            {
                selected.Add(selectableCards[index]);
                AddCombinations(index + 1);
                selected.RemoveAt(selected.Count - 1);
            }
        }

        AddCombinations(0);
        return options;
    }

    private static IReadOnlyList<IReadOnlyList<int>?> GetEnemyFollowerTargetOptions(
        GameState state,
        IReadOnlyList<CardEffect> effects)
    {
        // A single-target 【入场曲】 such as 猫咪走绳师's: one option per legal enemy follower, and
        // an option without a target when none can be chosen.
        if (effects.Any(effect => effect.Kind == CardEffectKind.DealDamageToEnemyFollower))
        {
            var singleTargets = state.Players[OtherPlayer(state.ActivePlayer)].Board
                .Where(follower => !follower.HasAura)
                .ToArray();
            return singleTargets.Length == 0
                ? [Array.Empty<int>()]
                : singleTargets.Select(follower => (IReadOnlyList<int>)[follower.InstanceId]).ToArray();
        }

        if (!effects.Any(effect => effect.Kind == CardEffectKind.DealDamageToUpToTwoEnemyFollowersAndLeader))
        {
            return [null];
        }

        var enemyFollowers = state.Players[OtherPlayer(state.ActivePlayer)].Board
            .Where(follower => !follower.HasAura)
            .ToArray();
        if (enemyFollowers.Length == 0)
        {
            // The leader damage is unconditional, even when there is no follower to select.
            return [Array.Empty<int>()];
        }

        if (enemyFollowers.Length == 1)
        {
            return [[enemyFollowers[0].InstanceId]];
        }

        var options = new List<IReadOnlyList<int>>();
        for (var first = 0; first < enemyFollowers.Length - 1; first++)
        {
            for (var second = first + 1; second < enemyFollowers.Length; second++)
            {
                options.Add([enemyFollowers[first].InstanceId, enemyFollowers[second].InstanceId]);
            }
        }

        return options;
    }

    private static IReadOnlyList<GameAction> GetEvolveActions(GameState state, FollowerInstance follower)
    {
        var evolutionEffects = GetEvolutionEffects(follower.Definition);
        var modeOptions = GetEvolutionModeChoiceOptions(follower.Definition);
        var handTargetOptions = GetOwnHandTargetOptions(
            state,
            sourceInHandInstanceId: null,
            evolutionEffects);
        var enemyTargetOptions = GetEvolutionEnemyTargetOptions(state, evolutionEffects);

        return enemyTargetOptions
            .SelectMany(enemyTarget => modeOptions.SelectMany(modeChoice => handTargetOptions.Select(ownHandTargets =>
                (GameAction)new EvolveAction(follower.InstanceId, modeChoice, ownHandTargets, enemyTarget))))
            .ToArray();
    }

    private static IReadOnlyList<GameAction> GetSuperEvolveActions(GameState state, FollowerInstance follower)
    {
        var evolutionEffects = GetEvolutionEffectsForSuperEvolution(follower.Definition);
        var modeOptions = GetEvolutionModeChoiceOptions(follower.Definition);
        var handTargetOptions = GetOwnHandTargetOptions(state, sourceInHandInstanceId: null, evolutionEffects);
        var enemyTargetOptions = GetEvolutionEnemyTargetOptions(state, evolutionEffects);

        if (!GetSuperEvolutionEffects(follower.Definition)
                .Any(effect => effect.Kind == CardEffectKind.SuperEvolveAnotherUnevolvedFollower))
        {
            return enemyTargetOptions
                .SelectMany(enemyTarget => modeOptions.SelectMany(modeChoice => handTargetOptions.Select(ownHandTargets =>
                    (GameAction)new SuperEvolveAction(follower.InstanceId, null, modeChoice, ownHandTargets, enemyTarget))))
                .ToArray();
        }

        var otherUnevolvedFollowers = state.Players[state.ActivePlayer].Board
            .Where(candidate => candidate.InstanceId != follower.InstanceId &&
                                candidate.EvolutionState == EvolutionState.Unevolved)
            .ToArray();

        var otherTargetOptions = otherUnevolvedFollowers.Length == 0
            ? new int?[] { null }
            : otherUnevolvedFollowers.Select(target => (int?)target.InstanceId).ToArray();
        return otherTargetOptions
            .SelectMany(otherTarget => enemyTargetOptions.SelectMany(enemyTarget => modeOptions.SelectMany(modeChoice =>
                handTargetOptions.Select(ownHandTargets =>
                    (GameAction)new SuperEvolveAction(follower.InstanceId, otherTarget, modeChoice, ownHandTargets, enemyTarget)))))
            .ToArray();
    }

    /// <summary>
    /// 【进化时】选择对手的战场上的1个随从的效果（例如 恶魔鼓手·拉兹）需要为每个合法目标展开一个动作。
    /// When the opponent has no follower the single null option keeps the evolution legal and the
    /// effect does nothing, which matches how the game resolves an effect without a target.
    /// </summary>
    private static int?[] GetEvolutionEnemyTargetOptions(GameState state, IReadOnlyList<CardEffect> effects)
    {
        if (!effects.Any(effect => effect.Kind == CardEffectKind.DealDamageToEnemyFollower))
        {
            return [null];
        }

        // 【灵气】随从不能成为敌方能力的目标，与其他指定目标的效果保持一致。
        var targets = state.Players[OtherPlayer(state.ActivePlayer)].Board
            .Where(follower => !follower.HasAura)
            .ToArray();
        return targets.Length == 0
            ? [null]
            : targets.Select(target => (int?)target.InstanceId).ToArray();
    }

    private static void ApplyMulligan(GameState state, MulliganAction action)
    {
        var active = state.Players[state.ActivePlayer];
        var replacementIds = action.ReplaceInstanceIds ?? throw new InvalidOperationException("Replacement cards are required.");

        if (replacementIds.Distinct().Count() != replacementIds.Count)
        {
            throw new InvalidOperationException("A card cannot be replaced twice.");
        }

        var cardsToReplace = replacementIds.Select(id => active.HandInternal.SingleOrDefault(card => card.InstanceId == id))
            .ToArray();

        if (cardsToReplace.Any(card => card is null))
        {
            throw new InvalidOperationException("Only cards in the active player's hand may be replaced.");
        }

        // Draw replacements before returning the selected cards. This guarantees that a
        // specific card selected for a mulligan cannot be drawn again in that mulligan,
        // while another copy with the same name can still be drawn.
        foreach (var card in cardsToReplace!)
        {
            active.HandInternal.Remove(card!);
        }

        DrawCards(state, state.ActivePlayer, cardsToReplace.Length);

        foreach (var card in cardsToReplace)
        {
            active.DeckInternal.Add(card!);
        }

        Shuffle(state, state.ActivePlayer);

        if (state.IsGameOver)
        {
            return;
        }

        if (state.ActivePlayer == state.StartingPlayer)
        {
            state.ActivePlayer = OtherPlayer(state.ActivePlayer);
            return;
        }

        state.ActivePlayer = state.StartingPlayer;
        state.Phase = GamePhase.Main;
        StartTurn(state);
    }

    private static void ApplyPlayFollower(GameState state, PlayFollowerAction action)
    {
        var active = state.Players[state.ActivePlayer];
        var card = active.HandInternal.SingleOrDefault(candidate => candidate.InstanceId == action.CardInstanceId)
            ?? throw new InvalidOperationException("The selected card is not in the active player's hand.");

        if (card.Definition.Type != CardType.Follower)
        {
            throw new InvalidOperationException("Only follower cards can be played in the current prototype.");
        }

        if (active.OccupiedBoardSlots >= PlayerState.BoardLimit)
        {
            throw new InvalidOperationException("The active player's board is full.");
        }

        var resolvedEnhance = GetResolvedEnhance(card.Definition, active.CurrentPlayPoints);
        var playCost = GetCardCost(card, active.CurrentPlayPoints);
        if (playCost > active.CurrentPlayPoints)
        {
            throw new InvalidOperationException("The active player does not have enough play points.");
        }

        active.CurrentPlayPoints -= playCost;
        active.HandInternal.Remove(card);
        var follower = new FollowerInstance(card, state.TurnNumber);
        active.BoardInternal.Add(follower);

        ApplyEnteringFollowerPassiveGrants(state, state.ActivePlayer, follower);
        ApplyFollowerEffect(state, action, card);
        ApplyEnhanceEffects(state, follower, resolvedEnhance);
    }

    private static void ApplyPlayAmulet(GameState state, PlayAmuletAction action)
    {
        var active = state.Players[state.ActivePlayer];
        var card = active.HandInternal.SingleOrDefault(candidate => candidate.InstanceId == action.CardInstanceId)
            ?? throw new InvalidOperationException("The selected card is not in the active player's hand.");
        if (card.Definition.Type != CardType.Amulet)
        {
            throw new InvalidOperationException("Only amulet cards can be played with an amulet action.");
        }

        if (active.OccupiedBoardSlots >= PlayerState.BoardLimit)
        {
            throw new InvalidOperationException("The active player's board is full.");
        }

        var playCost = GetCardCost(card, active.CurrentPlayPoints);
        if (playCost > active.CurrentPlayPoints)
        {
            throw new InvalidOperationException("The active player does not have enough play points.");
        }

        active.CurrentPlayPoints -= playCost;
        active.HandInternal.Remove(card);
        active.AmuletsInternal.Add(new AmuletInstance(card));
    }

    /// <summary>
    /// 结晶: plays the card as an amulet that keeps the crystallized Countdown and Last Words instead
    /// of the follower's own abilities. Like Accelerate it is only playable while the card's own cost
    /// cannot be paid.
    /// </summary>
    private static void ApplyCrystallize(GameState state, PlayCrystallizeAction action)
    {
        var active = state.Players[state.ActivePlayer];
        var card = active.HandInternal.SingleOrDefault(candidate => candidate.InstanceId == action.CardInstanceId)
            ?? throw new InvalidOperationException("The selected card is not in the active player's hand.");
        var crystallize = card.Definition.Crystallize
            ?? throw new InvalidOperationException("This card does not have a Crystallize ability.");
        if (active.OccupiedBoardSlots >= PlayerState.BoardLimit)
        {
            throw new InvalidOperationException("The active player's board is full.");
        }

        if (crystallize.Cost > active.CurrentPlayPoints)
        {
            throw new InvalidOperationException("The active player does not have enough play points for Crystallize.");
        }

        if (active.CurrentPlayPoints >= GetCardCost(card, active.CurrentPlayPoints))
        {
            throw new InvalidOperationException(
                "Crystallize is only available while the card's own cost cannot be paid.");
        }

        active.CurrentPlayPoints -= crystallize.Cost;
        active.HandInternal.Remove(card);
        active.AmuletsInternal.Add(new AmuletInstance(card, crystallize));
    }

    private static void ApplyAccelerate(GameState state, PlayAccelerateAction action)
    {
        var active = state.Players[state.ActivePlayer];
        var card = active.HandInternal.SingleOrDefault(candidate => candidate.InstanceId == action.CardInstanceId)
            ?? throw new InvalidOperationException("The selected card is not in the active player's hand.");
        var accelerate = card.Definition.Accelerate
            ?? throw new InvalidOperationException("This card does not have an Accelerate ability.");
        if (accelerate.Cost > active.CurrentPlayPoints)
        {
            throw new InvalidOperationException("The active player does not have enough play points for Accelerate.");
        }

        active.CurrentPlayPoints -= accelerate.Cost;
        active.HandInternal.Remove(card);

        foreach (var effect in accelerate.Effects)
        {
            switch (effect.Kind)
            {
                case CardEffectKind.IncreaseOwnMaxPlayPoints:
                    IncreaseOwnMaxPlayPoints(state.Players[state.ActivePlayer], effect.Amount);
                    break;
                default:
                    throw new InvalidOperationException($"Unsupported Accelerate effect: {effect.Kind}.");
            }
        }

        active.GraveyardInternal.Add(card);
    }

    private static void ApplyFollowerEffect(GameState state, PlayFollowerAction action, CardInstance follower)
    {
        var effects = GetFanfareEffects(follower.Definition)
            .Concat(GetFanfareModeEffects(follower.Definition, action.ModeChoiceIndex))
            .ToArray();
        if (effects.Length == 0)
        {
            if (action.HandCardTargetInstanceId is not null ||
                action.EnemyFollowerTargetInstanceIds is not null ||
                action.ModeChoiceIndex is not null ||
                action.OwnHandCardTargetInstanceIds is not null)
            {
                throw new InvalidOperationException("This follower does not select Fanfare targets.");
            }

            return;
        }

        foreach (var effect in effects)
        {
            // 【唤灵_N】 is paid per effect; without enough cards the effect is skipped and the
            // rest of the Fanfare still resolves.
            if (!TryPayNecromancyCost(state, state.ActivePlayer, effect))
            {
                continue;
            }

            switch (effect.Kind)
            {
                case CardEffectKind.ReturnOwnHandCardToDeckThenDrawCards:
                    ApplyReturnHandCardToDeckThenDraw(state, action.HandCardTargetInstanceId, effect.Amount);
                    break;
                case CardEffectKind.DrawCards:
                    DrawCards(state, state.ActivePlayer, effect.Amount);
                    break;
                case CardEffectKind.RestoreOwnLeaderHealth:
                    RestoreLeaderHealth(state, state.ActivePlayer, effect.Amount);
                    break;
                case CardEffectKind.RestoreOwnPlayPoints:
                    RestorePlayPoints(state.Players[state.ActivePlayer], effect.Amount);
                    break;
                case CardEffectKind.SummonFollower:
                    SummonFollowers(state, effect.ReferencedCardId!, effect.Amount);
                    break;
                case CardEffectKind.SummonFollowerWithoutLastWords:
                    SummonFollowers(state, effect.ReferencedCardId!, effect.Amount, suppressLastWords: true);
                    break;
                case CardEffectKind.ReplaceOwnDeckWithApocalypseDeck:
                    ReplaceOwnDeckWithApocalypseDeck(state);
                    break;
                case CardEffectKind.DealDamageToUpToTwoEnemyFollowersAndLeader:
                    ApplyDamageToUpToTwoEnemyFollowersAndLeader(
                        state,
                        action.EnemyFollowerTargetInstanceIds,
                        effect.Amount);
                    break;
                case CardEffectKind.DealDamageToEnemyFollower:
                    ApplyTargetedFollowerDamage(
                        state,
                        action.EnemyFollowerTargetInstanceIds is { Count: > 0 } targets ? targets[0] : null,
                        effect.Amount);
                    break;
                case CardEffectKind.SummonRandomDistinctDeckFollowers:
                    SummonRandomDistinctFollowersFromDeck(
                        state,
                        state.ActivePlayer,
                        effect.Amount,
                        effect.ReferencedCardId,
                        effect.MaximumCost);
                    break;
                case CardEffectKind.RecallFollowerFromGraveyard:
                    RecallFollowersFromGraveyard(state, state.ActivePlayer, effect.Amount, effect.MaximumCost);
                    break;
                case CardEffectKind.DealDamageToRandomEnemyFollower:
                    ApplyDamageToRandomEnemyFollower(state, effect.Amount);
                    break;
                case CardEffectKind.DealDamageToRandomEnemyFollowerIfOwnFollowersAttackedEnemyLeaderPreviousTurn:
                    if (state.Players[state.ActivePlayer].AttackedEnemyLeaderOnPreviousTurn)
                    {
                        ApplyDamageToRandomEnemyFollower(state, effect.Amount);
                    }

                    break;
                case CardEffectKind.DiscardOwnHandCards:
                    DiscardOwnHandCards(state, action.OwnHandCardTargetInstanceIds, effect.Amount);
                    break;
                case CardEffectKind.DiscardOwnHandCardsUpTo:
                    DiscardOwnHandCardsUpTo(state, action.OwnHandCardTargetInstanceIds, effect.Amount);
                    break;
                case CardEffectKind.AddCopyToHand:
                    AddCopiesToHand(state, state.ActivePlayer, effect.ReferencedCardId!, effect.Amount);
                    break;
                case CardEffectKind.DealDamageToAllEnemyFollowersAndLeader:
                    ApplyDamageToAllEnemyFollowersAndLeader(state, effect.Amount);
                    break;
                case CardEffectKind.DealDamageToAllEnemyFollowers:
                    ApplyDamageToAllEnemyFollowers(state, effect.Amount);
                    break;
                case CardEffectKind.IncreaseOwnMaxPlayPoints:
                    IncreaseOwnMaxPlayPoints(state.Players[state.ActivePlayer], effect.Amount);
                    break;
                case CardEffectKind.GainStormIfOwnFollowersAttackedEnemyLeaderPreviousTurn:
                    if (state.Players[state.ActivePlayer].AttackedEnemyLeaderOnPreviousTurn)
                    {
                        var playedFollower = state.Players[state.ActivePlayer].BoardInternal
                            .Single(candidate => candidate.InstanceId == follower.InstanceId);
                        GrantFollowerKeywords(playedFollower, CardKeyword.Storm);
                    }

                    break;
                case CardEffectKind.DecreaseAllEnemyFollowersDefense:
                    DecreaseAllEnemyFollowersDefense(state, effect.Amount);
                    break;
                case CardEffectKind.DealDamageToEnemyLeader:
                    DealDamageToLeader(state, OtherPlayer(state.ActivePlayer), effect.Amount);
                    break;
                case CardEffectKind.DealDamageToOwnLeader:
                    DealDamageToLeader(state, state.ActivePlayer, effect.Amount);
                    break;
                case CardEffectKind.RestoreOwnEvolutionPoints:
                    RestoreEvolutionPoints(state.Players[state.ActivePlayer], effect.Amount);
                    break;
                case CardEffectKind.GainStorm:
                    GrantFollowerKeywords(
                        state.Players[state.ActivePlayer].BoardInternal
                            .Single(candidate => candidate.InstanceId == follower.InstanceId),
                        CardKeyword.Storm);
                    break;
                case CardEffectKind.GainWard:
                    GrantFollowerKeywords(
                        state.Players[state.ActivePlayer].BoardInternal
                            .Single(candidate => candidate.InstanceId == follower.InstanceId),
                        CardKeyword.Ward);
                    break;
                case CardEffectKind.GainStats:
                    IncreaseFollowerStats(
                        state.Players[state.ActivePlayer].BoardInternal
                            .Single(candidate => candidate.InstanceId == follower.InstanceId),
                        effect.Amount);
                    break;
                case CardEffectKind.GainStatsToRandomOtherAlliedFollower:
                    IncreaseRandomOtherAlliedFollowerStats(state, follower.InstanceId, effect.Amount);
                    break;
                case CardEffectKind.EvolveSelf:
                    EvolveFollowerByAbility(state, follower.InstanceId);
                    break;
                default:
                    throw new InvalidOperationException($"Unsupported follower effect: {effect.Kind}.");
            }
        }
    }

    private static void ApplyReturnHandCardToDeckThenDraw(GameState state, int? targetInstanceId, int drawCount)
    {
        // Once the follower has left the hand, its effect may only choose a remaining hand card.
        // If no card remains, the mandatory selection cannot be performed and the effect ends.
        var active = state.Players[state.ActivePlayer];
        if (targetInstanceId is null)
        {
            if (active.HandInternal.Count > 0)
            {
                throw new InvalidOperationException("This Fanfare requires selecting another card from the active player's hand.");
            }

            return;
        }

        var target = active.HandInternal.SingleOrDefault(card => card.InstanceId == targetInstanceId.Value)
            ?? throw new InvalidOperationException("The selected Fanfare target is not in the active player's hand.");

        active.HandInternal.Remove(target);
        active.DeckInternal.Add(target);
        Shuffle(state, state.ActivePlayer);
        DrawCards(state, state.ActivePlayer, drawCount);
    }

    private static IReadOnlyList<CardEffect> GetFanfareEffects(CardDefinition definition)
    {
        var effects = new List<CardEffect>();
        if (definition.Effect is not null)
        {
            effects.Add(definition.Effect);
        }

        if (definition.FanfareEffects is not null)
        {
            effects.AddRange(definition.FanfareEffects);
        }

        return effects;
    }

    private static IReadOnlyList<CardEffect> GetFanfareModeEffects(
        CardDefinition definition,
        int? modeChoiceIndex)
    {
        if (definition.FanfareModeOptions is null)
        {
            if (modeChoiceIndex is not null)
            {
                throw new InvalidOperationException("This follower has no Fanfare mode to choose.");
            }

            return [];
        }

        if (modeChoiceIndex is null ||
            modeChoiceIndex.Value < 0 ||
            modeChoiceIndex.Value >= definition.FanfareModeOptions.Count)
        {
            throw new InvalidOperationException("A valid Fanfare mode must be selected.");
        }

        return definition.FanfareModeOptions[modeChoiceIndex.Value].Effects;
    }

    private static IReadOnlyList<CardEffect> GetSuperEvolutionEffects(CardDefinition definition) =>
        definition.SuperEvolutionEffects ?? [];

    private static IReadOnlyList<CardEffect> GetEvolutionEffects(CardDefinition definition) =>
        definition.EvolutionEffects ?? [];

    private static IReadOnlyList<CardEffect> GetEvolutionEffectsForSuperEvolution(CardDefinition definition) =>
        definition.SuperEvolutionEvolutionEffects ?? GetEvolutionEffects(definition);

    private static EnhanceDefinition? GetResolvedEnhance(CardDefinition definition, int currentPlayPoints) =>
        definition.EnhanceEffects?
            .Where(enhance => enhance.Cost <= currentPlayPoints)
            .OrderByDescending(enhance => enhance.Cost)
            .FirstOrDefault();

    private static int GetPlayCost(CardDefinition definition, int currentPlayPoints) =>
        GetResolvedEnhance(definition, currentPlayPoints)?.Cost ?? definition.Cost;

    /// <summary>
    /// What the player actually pays for a card in hand: its printed (or enhanced) cost minus the
    /// reductions the copy accumulated in hand. The reduction itself is unbounded, the paid cost is not.
    /// </summary>
    private static int GetCardCost(CardInstance card, int currentPlayPoints) =>
        Math.Max(0, GetPlayCost(card.Definition, currentPlayPoints) - card.CostReduction);

    /// <summary>
    /// The cost a card currently has, wherever it is. Reductions an ability applied while the card sat
    /// in hand stay attached to that copy, so a follower printed at 8 that was played for 2 counts as a
    /// 2-cost card for 【亡者召回_2】 once it reaches the graveyard.
    /// </summary>
    private static int CurrentCost(CardInstance card) =>
        Math.Max(0, card.Definition.Cost - card.CostReduction);

    /// <summary>
    /// 在手牌中发动: at the end of its owner's turn, every card in hand with a hand reduction drops its
    /// cost once while the leader's health is at or below the printed threshold.
    /// </summary>
    private static void ApplyHandCostReductions(PlayerState owner)
    {
        foreach (var card in owner.HandInternal)
        {
            if (card.Definition.HandCostReduction is { } reduction &&
                owner.Health <= reduction.HealthThreshold)
            {
                card.CostReduction += reduction.Reduction;
            }
        }
    }

    private static void ApplyEnhanceEffects(
        GameState state,
        FollowerInstance follower,
        EnhanceDefinition? resolvedEnhance)
    {
        if (resolvedEnhance is null || follower.Definition.EnhanceEffects is null)
        {
            return;
        }

        foreach (var effect in follower.Definition.EnhanceEffects
                     .Where(enhance => enhance.Cost <= resolvedEnhance.Cost)
                     .SelectMany(enhance => enhance.Effects))
        {
            switch (effect.Kind)
            {
                case CardEffectKind.GainStats:
                    IncreaseFollowerStats(follower, effect.Amount);
                    break;
                case CardEffectKind.SearchDeckForFollowerWithMinimumCostToHand:
                    SearchDeckForFollowerWithMinimumCostToHand(state, effect.Amount);
                    break;
                case CardEffectKind.RestoreOwnPlayPoints:
                    RestorePlayPoints(state.Players[state.ActivePlayer], effect.Amount);
                    break;
                case CardEffectKind.SummonFollower:
                    SummonFollowers(state, effect.ReferencedCardId!, effect.Amount);
                    break;
                case CardEffectKind.GainStorm:
                    GrantFollowerKeywords(follower, CardKeyword.Storm);
                    break;
                case CardEffectKind.SetOwnLeaderMaxHealth:
                    SetLeaderMaxHealth(state.Players[state.ActivePlayer], effect.Amount);
                    break;
                case CardEffectKind.GrantOwnLeaderDamageImmunityUntilEndOfOpponentTurn:
                    state.Players[state.ActivePlayer].TimedLeaderEffectsInternal.Add(
                        new TimedLeaderEffectInstance(
                            TimedLeaderEffectKind.PreventAllDamage,
                            OtherPlayer(state.ActivePlayer)));
                    break;
                default:
                    throw new InvalidOperationException($"Unsupported Enhance effect: {effect.Kind}.");
            }
        }
    }

    private static void ApplySpellEnhanceEffects(GameState state, CardDefinition definition, EnhanceDefinition? resolvedEnhance)
    {
        if (resolvedEnhance is null || definition.EnhanceEffects is null)
        {
            return;
        }

        foreach (var effect in definition.EnhanceEffects
                     .Where(enhance => enhance.Cost <= resolvedEnhance.Cost)
                     .SelectMany(enhance => enhance.Effects))
        {
            switch (effect.Kind)
            {
                case CardEffectKind.DealDamageToRandomEnemyFollower:
                    ApplyDamageToRandomEnemyFollower(state, effect.Amount);
                    break;
                case CardEffectKind.DealDamageToAllEnemyFollowersAndLeader:
                    ApplyDamageToAllEnemyFollowersAndLeader(state, effect.Amount);
                    break;
                default:
                    throw new InvalidOperationException($"Unsupported spell Enhance effect: {effect.Kind}.");
            }
        }
    }

    private static IReadOnlyList<GameAction> GetSpellActions(GameState state, CardInstance spell)
    {
        var effects = GetSpellEffects(spell.Definition);
        if (effects.Count == 0)
        {
            throw new InvalidOperationException($"Spell {spell.Definition.Id} has no resolvable effect.");
        }

        var ownHandTargetOptions = GetSpellHandTargetOptions(state, spell, effects);
        var spellTargetOptions = GetSpellTargetOptions(state, effects);

        return ownHandTargetOptions
            .SelectMany(ownHandTargets => spellTargetOptions.Select(target =>
                (GameAction)new PlaySpellAction(spell.InstanceId, target, ownHandTargets)))
            .ToArray();
    }

    private static IReadOnlyList<IReadOnlyList<int>?> GetSpellHandTargetOptions(
        GameState state,
        CardInstance spell,
        IReadOnlyList<CardEffect> effects)
    {
        var discardEffect = effects.SingleOrDefault(effect => effect.Kind == CardEffectKind.DiscardOwnHandCards);
        if (discardEffect is null)
        {
            return [null];
        }

        var selectableCards = state.Players[state.ActivePlayer].Hand
            .Where(card => card.InstanceId != spell.InstanceId)
            .Select(card => card.InstanceId)
            .ToArray();
        if (selectableCards.Length < discardEffect.Amount)
        {
            // The selection is mandatory, so the spell is not playable without enough other hand cards.
            return [];
        }

        var options = new List<IReadOnlyList<int>>();
        var selected = new List<int>();

        void AddCombinations(int startIndex)
        {
            if (selected.Count == discardEffect.Amount)
            {
                options.Add(selected.ToArray());
                return;
            }

            for (var index = startIndex; index <= selectableCards.Length - (discardEffect.Amount - selected.Count); index++)
            {
                selected.Add(selectableCards[index]);
                AddCombinations(index + 1);
                selected.RemoveAt(selected.Count - 1);
            }
        }

        AddCombinations(0);
        return options;
    }

    private static IReadOnlyList<SpellTarget?> GetSpellTargetOptions(
        GameState state,
        IReadOnlyList<CardEffect> effects)
    {
        var selectableEnemyFollowers = state.Players[OtherPlayer(state.ActivePlayer)].Board
            .Where(follower => !follower.HasAura)
            .ToArray();

        if (effects.Any(effect => effect.Kind == CardEffectKind.DestroyEnemyFollower))
        {
            return selectableEnemyFollowers
                .Select(follower => (SpellTarget)new EnemyFollowerTarget(follower.InstanceId))
                .ToArray();
        }

        if (!effects.Any(effect => effect.Kind == CardEffectKind.DealDamageToEnemyFollowerOrLeader))
        {
            return [null];
        }

        return
        [
            new EnemyLeaderTarget(),
            .. selectableEnemyFollowers
                .Select(follower => (SpellTarget)new EnemyFollowerTarget(follower.InstanceId))
        ];
    }

    private static void ApplyPlaySpell(GameState state, PlaySpellAction action)
    {
        var active = state.Players[state.ActivePlayer];
        var card = active.HandInternal.SingleOrDefault(candidate => candidate.InstanceId == action.CardInstanceId)
            ?? throw new InvalidOperationException("The selected card is not in the active player's hand.");

        if (card.Definition.Type != CardType.Spell)
        {
            throw new InvalidOperationException("Only spell cards can be played with a spell action.");
        }

        var resolvedEnhance = GetResolvedEnhance(card.Definition, active.CurrentPlayPoints);
        var playCost = GetCardCost(card, active.CurrentPlayPoints);
        if (playCost > active.CurrentPlayPoints)
        {
            throw new InvalidOperationException("The active player does not have enough play points.");
        }

        var effects = GetSpellEffects(card.Definition);
        if (effects.Count == 0)
        {
            throw new InvalidOperationException($"Spell {card.Definition.Id} has no resolvable effect.");
        }

        active.CurrentPlayPoints -= playCost;
        active.HandInternal.Remove(card);

        foreach (var effect in effects)
        {
            // 【唤灵_N】 is paid per effect. Without enough cards in the graveyard only that effect
            // is skipped; the rest of the spell still resolves and the card is still playable.
            if (!TryPayNecromancyCost(state, state.ActivePlayer, effect))
            {
                continue;
            }

            switch (effect.Kind)
            {
                case CardEffectKind.DealDamageToEnemyFollowerOrLeader:
                    ApplyDamageSpell(state, action.Target, effect.Amount);
                    break;
                case CardEffectKind.DrawCards:
                    EnsureSpellHasNoTarget(action);
                    DrawCards(state, state.ActivePlayer, effect.Amount);
                    break;
                case CardEffectKind.SetEnemyLeaderMaxHealth:
                    EnsureSpellHasNoTarget(action);
                    SetLeaderMaxHealth(state.Players[OtherPlayer(state.ActivePlayer)], effect.Amount);
                    break;
                case CardEffectKind.SummonFollower:
                    EnsureSpellHasNoTarget(action);
                    SummonFollowers(state, effect.ReferencedCardId!, effect.Amount);
                    break;
                case CardEffectKind.DealDamageToRandomEnemyFollower:
                    EnsureSpellHasNoTarget(action);
                    ApplyDamageToRandomEnemyFollower(state, effect.Amount);
                    break;
                case CardEffectKind.DiscardOwnHandCards:
                    DiscardOwnHandCards(state, action.OwnHandCardTargetInstanceIds, effect.Amount);
                    break;
                case CardEffectKind.DestroyEnemyFollower:
                    DestroyEnemyFollower(state, action.Target);
                    break;
                case CardEffectKind.DealDamageToRandomEnemyFollowerAndLeader:
                    EnsureSpellHasNoTarget(action);
                    ApplyDamageToRandomEnemyFollowerAndLeader(state, effect.Amount);
                    break;
                case CardEffectKind.DistributeDamageAmongEnemyFollowersByEntryOrder:
                    EnsureSpellHasNoTarget(action);
                    ApplyDistributedDamageToEnemyFollowersByEntryOrder(
                        state,
                        GetEffectAmount(state, effect));
                    break;
                case CardEffectKind.IncreaseOwnMaxPlayPointsAndDrawIfAtTen:
                    EnsureSpellHasNoTarget(action);
                    IncreaseOwnMaxPlayPointsAndDrawIfAtTen(state, effect.Amount);
                    break;
                case CardEffectKind.DestroyRandomEnemyFollowerWithHighestAttack:
                    EnsureSpellHasNoTarget(action);
                    DestroyRandomEnemyFollowerWithHighestAttack(state);
                    break;
                case CardEffectKind.DealDamageToEnemyLeader:
                    EnsureSpellHasNoTarget(action);
                    DealDamageToLeader(state, OtherPlayer(state.ActivePlayer), effect.Amount);
                    break;
                case CardEffectKind.DealDamageToEnemyLeaderIfAwakened:
                    EnsureSpellHasNoTarget(action);
                    if (active.MaxPlayPoints >= 7)
                    {
                        DealDamageToLeader(state, OtherPlayer(state.ActivePlayer), effect.Amount);
                    }

                    break;
                case CardEffectKind.RestoreOwnLeaderHealth:
                    EnsureSpellHasNoTarget(action);
                    RestoreLeaderHealth(state, state.ActivePlayer, effect.Amount);
                    break;
                case CardEffectKind.DealDamageToAllFollowersByFollowerCount:
                    EnsureSpellHasNoTarget(action);
                    DealDamageToAllFollowersByFollowerCount(state);
                    break;
                default:
                    throw new InvalidOperationException($"Unsupported spell effect: {effect.Kind}.");
            }
        }

        ApplySpellEnhanceEffects(state, card.Definition, resolvedEnhance);
        active.GraveyardInternal.Add(card);
    }

    /// <summary>
    /// 【唤灵_N】Spends N cards from the controller's graveyard to activate the effect. Returns false
    /// when the graveyard holds fewer than N cards, in which case nothing is consumed.
    /// </summary>
    private static bool TryPayNecromancyCost(GameState state, int playerIndex, CardEffect effect)
    {
        if (effect.NecromancyCost <= 0)
        {
            return true;
        }

        var graveyard = state.Players[playerIndex].GraveyardInternal;
        if (graveyard.Count < effect.NecromancyCost)
        {
            return false;
        }

        graveyard.RemoveRange(0, effect.NecromancyCost);
        return true;
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

    private static void EnsureSpellHasNoTarget(PlaySpellAction action)
    {
        if (action.Target is not null)
        {
            throw new InvalidOperationException("This spell does not select an enemy target.");
        }
    }

    private static int GetEffectAmount(GameState state, CardEffect effect) =>
        effect.Amount + (state.Players[state.ActivePlayer].AttackedEnemyLeaderOnPreviousTurn
            ? effect.BonusAmountIfOwnFollowersAttackedEnemyLeaderPreviousTurn
            : 0);

    private static void DiscardOwnHandCards(
        GameState state,
        IReadOnlyList<int>? targetInstanceIds,
        int discardCount)
    {
        var active = state.Players[state.ActivePlayer];
        var selectedTargetIds = targetInstanceIds ?? [];
        if (selectedTargetIds.Count != discardCount || selectedTargetIds.Distinct().Count() != selectedTargetIds.Count)
        {
            throw new InvalidOperationException($"This spell requires selecting exactly {discardCount} different hand cards.");
        }

        var selectedCards = selectedTargetIds
            .Select(targetId => active.HandInternal.SingleOrDefault(card => card.InstanceId == targetId)
                ?? throw new InvalidOperationException("A selected discard target is not in the active player's hand."))
            .ToArray();

        foreach (var selectedCard in selectedCards)
        {
            DiscardCard(state, state.ActivePlayer, selectedCard);
        }
    }

    private static void DiscardOwnHandCardsUpTo(
        GameState state,
        IReadOnlyList<int>? targetInstanceIds,
        int maximumDiscardCount)
    {
        var active = state.Players[state.ActivePlayer];
        var selectedTargetIds = targetInstanceIds ?? [];
        var expectedCount = Math.Min(maximumDiscardCount, active.HandInternal.Count);
        if (selectedTargetIds.Count != expectedCount ||
            selectedTargetIds.Distinct().Count() != selectedTargetIds.Count)
        {
            throw new InvalidOperationException(
                $"This effect requires selecting all {expectedCount} available hand cards, up to {maximumDiscardCount}.");
        }

        var selectedCards = selectedTargetIds
            .Select(targetId => active.HandInternal.SingleOrDefault(card => card.InstanceId == targetId)
                ?? throw new InvalidOperationException("A selected discard target is not in the active player's hand."))
            .ToArray();
        foreach (var selectedCard in selectedCards)
        {
            DiscardCard(state, state.ActivePlayer, selectedCard);
        }
    }

    private static void DiscardCard(GameState state, int playerIndex, CardInstance card)
    {
        var player = state.Players[playerIndex];
        if (!player.HandInternal.Remove(card))
        {
            throw new InvalidOperationException("Only a card in the owner's hand can be discarded.");
        }

        player.GraveyardInternal.Add(card);
        ApplyDiscardedEffects(state, playerIndex, card);
    }

    private static void ApplyDiscardedEffects(GameState state, int ownerIndex, CardInstance source)
    {
        if (source.Definition.DiscardedEffects is null)
        {
            return;
        }

        foreach (var effect in source.Definition.DiscardedEffects)
        {
            switch (effect.Kind)
            {
                case CardEffectKind.SummonFollower:
                    SummonFollowers(state, ownerIndex, effect.ReferencedCardId!, effect.Amount);
                    break;
                case CardEffectKind.DealDamageToEnemyLeader:
                    DealDamageToLeader(state, OtherPlayer(ownerIndex), effect.Amount);
                    break;
                case CardEffectKind.RestoreOwnLeaderHealth:
                    RestoreLeaderHealth(state, ownerIndex, effect.Amount);
                    break;
                default:
                    throw new InvalidOperationException($"Unsupported discard trigger effect: {effect.Kind}.");
            }
        }
    }

    private static void ApplyDamageSpell(GameState state, SpellTarget? target, int damage)
    {
        var opponentIndex = OtherPlayer(state.ActivePlayer);
        var opponent = state.Players[opponentIndex];

        switch (target)
        {
            case EnemyLeaderTarget:
                DealDamageToLeader(state, opponentIndex, damage);
                break;
            case EnemyFollowerTarget followerTarget:
                var follower = opponent.BoardInternal.SingleOrDefault(candidate =>
                                   candidate.InstanceId == followerTarget.FollowerInstanceId)
                               ?? throw new InvalidOperationException("The selected spell target is not on the opponent's board.");
                if (follower.HasAura)
                {
                    throw new InvalidOperationException("An Aura follower cannot be selected by an opponent's effect.");
                }

                DealDamageToFollower(state, opponentIndex, follower, damage);
                DestroyFollowerIfNeeded(state, opponentIndex, follower);
                break;
            default:
                throw new InvalidOperationException("This spell requires an enemy follower or leader target.");
        }
    }

    private static void ApplyDamageToUpToTwoEnemyFollowersAndLeader(
        GameState state,
        IReadOnlyList<int>? targetInstanceIds,
        int damage)
    {
        var opponentIndex = OtherPlayer(state.ActivePlayer);
        var opponent = state.Players[opponentIndex];

        var selectableFollowers = opponent.BoardInternal
            .Where(follower => !follower.HasAura)
            .ToArray();
        var requiredTargetCount = Math.Min(2, selectableFollowers.Length);
        var selectedTargetIds = targetInstanceIds ?? [];
        if (selectedTargetIds.Count != requiredTargetCount || selectedTargetIds.Distinct().Count() != selectedTargetIds.Count)
        {
            throw new InvalidOperationException(
                $"This Fanfare requires selecting exactly {requiredTargetCount} different enemy followers.");
        }

        var targets = selectedTargetIds
            .Select(targetId => selectableFollowers.SingleOrDefault(follower => follower.InstanceId == targetId)
                ?? throw new InvalidOperationException("A selected Fanfare target is not a selectable enemy follower."))
            .ToArray();

        foreach (var target in targets)
        {
            DealDamageToFollower(state, opponentIndex, target, damage);
            DestroyFollowerIfNeeded(state, opponentIndex, target);
        }

        DealDamageToLeader(state, opponentIndex, damage);
    }

    private static void ApplyDamageToRandomEnemyFollower(GameState state, int damage)
    {
        var opponentIndex = OtherPlayer(state.ActivePlayer);
        var opponent = state.Players[opponentIndex];
        if (opponent.BoardInternal.Count == 0)
        {
            return;
        }

        var target = opponent.BoardInternal[NextInt(state, opponent.BoardInternal.Count)];
        DealDamageToFollower(state, opponentIndex, target, damage);
        DestroyFollowerIfNeeded(state, opponentIndex, target);
    }

    private static void ApplyDamageToUpToTwoRandomEnemyFollowers(GameState state, int damage)
    {
        var opponentIndex = OtherPlayer(state.ActivePlayer);
        var candidates = state.Players[opponentIndex].BoardInternal.ToList();
        var targetCount = Math.Min(2, candidates.Count);
        for (var targetIndex = 0; targetIndex < targetCount; targetIndex++)
        {
            var selectedIndex = NextInt(state, candidates.Count);
            var target = candidates[selectedIndex];
            candidates.RemoveAt(selectedIndex);
            DealDamageToFollower(state, opponentIndex, target, damage);
            DestroyFollowerIfNeeded(state, opponentIndex, target);
        }
    }

    private static void DestroyEnemyFollower(GameState state, SpellTarget? target)
    {
        var opponentIndex = OtherPlayer(state.ActivePlayer);
        var opponent = state.Players[opponentIndex];
        if (target is not EnemyFollowerTarget followerTarget)
        {
            throw new InvalidOperationException("This spell requires selecting an enemy follower.");
        }

        var follower = opponent.BoardInternal.SingleOrDefault(candidate =>
                           candidate.InstanceId == followerTarget.FollowerInstanceId)
                       ?? throw new InvalidOperationException("The selected spell target is not on the opponent's board.");
        if (follower.HasAura)
        {
            throw new InvalidOperationException("An Aura follower cannot be selected by an opponent's effect.");
        }

        DestroyFollower(state, opponentIndex, follower);
    }

    private static void DestroyRandomEnemyFollowerWithHighestAttack(GameState state)
    {
        var opponentIndex = OtherPlayer(state.ActivePlayer);
        var opponent = state.Players[opponentIndex];
        if (opponent.BoardInternal.Count == 0)
        {
            return;
        }

        var highestAttack = opponent.BoardInternal.Max(follower => follower.Attack);
        var candidates = opponent.BoardInternal
            .Where(follower => follower.Attack == highestAttack)
            .ToArray();
        var target = candidates[NextInt(state, candidates.Length)];
        DestroyFollower(state, opponentIndex, target);
    }

    private static void ApplyDamageToRandomEnemyFollowerAndLeader(GameState state, int damage)
    {
        var opponentIndex = OtherPlayer(state.ActivePlayer);
        ApplyDamageToRandomEnemyFollower(state, damage);
        DealDamageToLeader(state, opponentIndex, damage);
    }

    private static void ApplyDamageToAllEnemyFollowersAndLeader(GameState state, int damage)
    {
        var opponentIndex = OtherPlayer(state.ActivePlayer);
        var opponent = state.Players[opponentIndex];
        foreach (var follower in opponent.BoardInternal.ToArray())
        {
            DealDamageToFollower(state, opponentIndex, follower, damage);
            DestroyFollowerIfNeeded(state, opponentIndex, follower);
        }

        DealDamageToLeader(state, opponentIndex, damage);
    }

    private static void ApplyDamageToAllEnemyFollowers(GameState state, int damage)
    {
        var opponentIndex = OtherPlayer(state.ActivePlayer);
        foreach (var follower in state.Players[opponentIndex].BoardInternal.ToArray())
        {
            DealDamageToFollower(state, opponentIndex, follower, damage);
            DestroyFollowerIfNeeded(state, opponentIndex, follower);
        }
    }

    private static void DealDamageToAllFollowersByFollowerCount(GameState state)
    {
        // Count once before any damage is dealt, so all followers receive the same value.
        var damage = state.Players.Sum(player => player.BoardInternal.Count);
        if (damage == 0)
        {
            return;
        }

        for (var playerIndex = 0; playerIndex < 2; playerIndex++)
        {
            foreach (var follower in state.Players[playerIndex].BoardInternal.ToArray())
            {
                DealDamageToFollower(state, playerIndex, follower, damage);
                DestroyFollowerIfNeeded(state, playerIndex, follower);
            }
        }
    }

    private static void DealDamageToAllFollowersWithoutTrait(GameState state, string protectedTrait, int damage)
    {
        for (var playerIndex = 0; playerIndex < 2; playerIndex++)
        {
            var player = state.Players[playerIndex];
            foreach (var follower in player.BoardInternal
                         .Where(follower => follower.Definition.Traits?.Contains(protectedTrait, StringComparer.Ordinal) != true)
                         .ToArray())
            {
                DealDamageToFollower(state, playerIndex, follower, damage);
                DestroyFollowerIfNeeded(state, playerIndex, follower);
            }
        }
    }

    /// <summary>
    /// Resolves Worlds Beyond's automatic "distribute damage" wording: the total is
    /// assigned from the opponent's oldest follower to the newest, assigning each
    /// follower up to its current defense before any remainder continues to the next.
    /// </summary>
    private static void ApplyDistributedDamageToEnemyFollowersByEntryOrder(GameState state, int damage)
    {
        var opponentIndex = OtherPlayer(state.ActivePlayer);
        var opponent = state.Players[opponentIndex];
        var remainingDamage = damage;

        // The board list is maintained in entry order. Snapshot it because destroyed followers
        // are removed while resolving the allocation.
        foreach (var target in opponent.BoardInternal.ToArray())
        {
            if (remainingDamage == 0)
            {
                break;
            }

            var assignedDamage = Math.Min(remainingDamage, target.CurrentDefense);
            remainingDamage -= assignedDamage;
            DealDamageToFollower(state, opponentIndex, target, assignedDamage);
            DestroyFollowerIfNeeded(state, opponentIndex, target);
        }
    }

    private static void ReplaceOwnDeckWithApocalypseDeck(GameState state)
    {
        var active = state.Players[state.ActivePlayer];
        active.DeckInternal.Clear();

        foreach (var definition in CardCatalog.ApocalypseDeckCards)
        {
            active.DeckInternal.Add(new CardInstance(state.NextInstanceId++, definition));
        }

        Shuffle(state, state.ActivePlayer);
    }

    private static void ApplyEvolve(GameState state, EvolveAction action)
    {
        if (!CanEvolve(state))
        {
            throw new InvalidOperationException("The active player cannot evolve this turn.");
        }

        var active = state.Players[state.ActivePlayer];
        var follower = GetUnevolvedFollower(active, action.FollowerInstanceId);

        active.EvolutionPoints--;
        active.UsedEvolutionOrSuperEvolutionThisTurn = true;
        StrengthenFollower(follower, 2, EvolutionState.Evolved);
        ApplyEvolutionKeywordChanges(follower);
        ApplyOnEvolveEffects(state, follower);
        ApplyEvolutionEffects(
            state,
            follower,
            action.ModeChoiceIndex,
            action.OwnHandCardTargetInstanceIds,
            action.EnemyFollowerTargetInstanceId);
    }

    private static void ApplyEvolutionEffects(
        GameState state,
        FollowerInstance evolvedFollower,
        int? modeChoiceIndex,
        IReadOnlyList<int>? ownHandCardTargetInstanceIds,
        int? enemyFollowerTargetInstanceId,
        IReadOnlyList<CardEffect>? overridingEffects = null)
    {
        var evolutionEffects = overridingEffects ?? GetEvolutionEffects(evolvedFollower.Definition);
        foreach (var effect in evolutionEffects)
        {
            ApplyEvolutionEffect(state, evolvedFollower, effect, ownHandCardTargetInstanceIds, enemyFollowerTargetInstanceId);
        }

        // 【进化时】【模式】 resolves after the printed evolution effects, and the same modes are
        // reused when the follower is super evolved without its own super-evolution effects.
        foreach (var effect in GetEvolutionModeEffects(evolvedFollower.Definition, modeChoiceIndex))
        {
            ApplyEvolutionEffect(state, evolvedFollower, effect, ownHandCardTargetInstanceIds, enemyFollowerTargetInstanceId);
        }
    }

    private static void ApplyEvolutionEffect(
        GameState state,
        FollowerInstance evolvedFollower,
        CardEffect effect,
        IReadOnlyList<int>? ownHandCardTargetInstanceIds,
        int? enemyFollowerTargetInstanceId)
    {
        switch (effect.Kind)
        {
            case CardEffectKind.SummonFollower:
                SummonFollowers(state, effect.ReferencedCardId!, effect.Amount);
                break;
            case CardEffectKind.SummonFollowerWithoutLastWords:
                SummonFollowers(state, effect.ReferencedCardId!, effect.Amount, suppressLastWords: true);
                break;
            case CardEffectKind.GainStatsToOtherAlliedFollowers:
                IncreaseOtherAlliedFollowerStats(
                    state,
                    evolvedFollower.InstanceId,
                    effect.Amount,
                    effect.ReferencedCardId);
                break;
            case CardEffectKind.AddCopyToHand:
                AddCopiesToHand(state, state.ActivePlayer, effect.ReferencedCardId!, effect.Amount);
                break;
            case CardEffectKind.DiscardOwnHandCardsUpTo:
                DiscardOwnHandCardsUpTo(state, ownHandCardTargetInstanceIds, effect.Amount);
                break;
            case CardEffectKind.DrawCards:
                DrawCards(state, state.ActivePlayer, effect.Amount);
                break;
            case CardEffectKind.RestoreOwnLeaderHealth:
                RestoreLeaderHealth(state, state.ActivePlayer, effect.Amount);
                break;
            case CardEffectKind.DealDamageToEnemyFollower:
                ApplyTargetedFollowerDamage(state, enemyFollowerTargetInstanceId, effect.Amount);
                break;
            case CardEffectKind.DealDamageToEnemyLeader:
                DealDamageToLeader(state, OtherPlayer(state.ActivePlayer), effect.Amount);
                break;
            case CardEffectKind.DealDamageToAllEnemyFollowers:
                ApplyDamageToAllEnemyFollowers(state, effect.Amount);
                break;
            case CardEffectKind.DecreaseAllEnemyFollowersDefense:
                DecreaseAllEnemyFollowersDefense(state, effect.Amount);
                break;
            case CardEffectKind.RestoreOwnPlayPoints:
                RestorePlayPoints(state.Players[state.ActivePlayer], effect.Amount);
                break;
            case CardEffectKind.RestoreOwnEvolutionPoints:
                RestoreEvolutionPoints(state.Players[state.ActivePlayer], effect.Amount);
                break;
            default:
                throw new InvalidOperationException($"Unsupported evolution effect: {effect.Kind}.");
        }
    }

    private static void SummonFollowers(GameState state, string followerCardId, int count)
        => SummonFollowers(state, state.ActivePlayer, followerCardId, count, suppressLastWords: false);

    private static void SummonFollowers(GameState state, string followerCardId, int count, bool suppressLastWords)
        => SummonFollowers(state, state.ActivePlayer, followerCardId, count, suppressLastWords);

    private static void SummonFollowers(GameState state, int playerIndex, string followerCardId, int count)
        => SummonFollowers(state, playerIndex, followerCardId, count, suppressLastWords: false);

    private static void SummonFollowers(
        GameState state,
        int playerIndex,
        string followerCardId,
        int count,
        bool suppressLastWords)
    {
        var definition = CardCatalog.Get(followerCardId);
        if (definition.Type != CardType.Follower)
        {
            throw new InvalidOperationException($"Only followers can be summoned, but {followerCardId} is not a follower.");
        }

        var player = state.Players[playerIndex];
        var summonedCount = Math.Min(count, PlayerState.BoardLimit - player.OccupiedBoardSlots);
        for (var index = 0; index < summonedCount; index++)
        {
            var card = new CardInstance(state.NextInstanceId++, definition, suppressLastWords);
            var follower = new FollowerInstance(card, state.TurnNumber);
            player.BoardInternal.Add(follower);
            ApplyEnteringFollowerPassiveGrants(state, playerIndex, follower);
        }
    }

    /// <summary>
    /// 【谢幕曲】召唤1个『X』，使其+N/+0且获得【突进】，使其失去【谢幕曲】: the copies arrive with the
    /// attack bonus, 【突进】 and no Last Words, so the chain stops after the first generation.
    /// </summary>
    private static void SummonEmpoweredFollowersWithoutLastWords(
        GameState state,
        int ownerIndex,
        string followerCardId,
        int count,
        int attackBonus)
    {
        var definition = CardCatalog.Get(followerCardId);
        if (definition.Type != CardType.Follower)
        {
            throw new InvalidOperationException($"Only followers can be summoned, but {followerCardId} is not a follower.");
        }

        var player = state.Players[ownerIndex];
        var summonedCount = Math.Min(count, PlayerState.BoardLimit - player.OccupiedBoardSlots);
        for (var index = 0; index < summonedCount; index++)
        {
            var card = new CardInstance(state.NextInstanceId++, definition, HasSuppressedLastWords: true);
            var follower = new FollowerInstance(card, state.TurnNumber);
            follower.Attack += attackBonus;
            GrantFollowerKeywords(follower, CardKeyword.Rush);
            player.BoardInternal.Add(follower);
            ApplyEnteringFollowerPassiveGrants(state, ownerIndex, follower);
        }
    }

    /// <summary>
    /// Continuous abilities such as 「自己的其他梦魇·随从进入战场时，使其获得【突进】」: every other allied
    /// follower already in play may hand a keyword to the follower that just entered.
    /// </summary>
    private static void ApplyEnteringFollowerPassiveGrants(
        GameState state,
        int playerIndex,
        FollowerInstance entering)
    {
        foreach (var source in state.Players[playerIndex].BoardInternal)
        {
            if (source.InstanceId == entering.InstanceId || source.Definition.PassiveEffects is null)
            {
                continue;
            }

            foreach (var effect in source.Definition.PassiveEffects)
            {
                switch (effect.Kind)
                {
                    case CardEffectKind.GrantRushToEnteringAlliedFollowers:
                        if (MatchesProfessionFilter(entering.Definition, effect.ReferencedCardId))
                        {
                            GrantFollowerKeywords(entering, CardKeyword.Rush);
                        }

                        break;
                    case CardEffectKind.EmpowerEnteringAlliedTraitFollowers:
                        // 「若为自己的回合」: the whole empowerment only happens on the owner's turn.
                        if (state.ActivePlayer != playerIndex ||
                            !MatchesTraitFilter(entering.Definition, effect.ReferencedCardId))
                        {
                            break;
                        }

                        entering.Attack += effect.Amount;
                        GrantFollowerKeywords(entering, CardKeyword.Rush | CardKeyword.Ward);
                        DealDamageToLeader(state, OtherPlayer(playerIndex), 1);
                        break;
                }
            }
        }
    }

    /// <summary>Trait-sensitive effects store the trait name in ReferencedCardId.</summary>
    private static bool MatchesTraitFilter(CardDefinition definition, string? traitName) =>
        string.IsNullOrWhiteSpace(traitName) ||
        definition.Traits?.Contains(traitName, StringComparer.Ordinal) == true;

    /// <summary>
    /// Summons different kinds of follower taken randomly from the deck. The chosen copies leave the
    /// deck; a kind that does not fit on the board simply stays there, and when the deck holds fewer
    /// eligible kinds than requested the effect summons as many as it can.
    /// </summary>
    private static void SummonRandomDistinctFollowersFromDeck(
        GameState state,
        int playerIndex,
        int kindCount,
        string? professionName,
        int maximumCost)
    {
        var player = state.Players[playerIndex];
        var candidates = player.DeckInternal
            .Where(card => card.Definition.Type == CardType.Follower &&
                           MatchesProfessionFilter(card.Definition, professionName) &&
                           (maximumCost <= 0 || card.Definition.Cost <= maximumCost))
            .GroupBy(card => card.Definition.Id, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToList();

        for (var index = 0; index < kindCount && candidates.Count > 0; index++)
        {
            var choiceIndex = NextInt(state, candidates.Count);
            var chosen = candidates[choiceIndex];
            candidates.RemoveAt(choiceIndex);
            if (player.OccupiedBoardSlots >= PlayerState.BoardLimit ||
                !player.DeckInternal.Remove(chosen))
            {
                continue;
            }

            var follower = new FollowerInstance(chosen, state.TurnNumber);
            player.BoardInternal.Add(follower);
            ApplyEnteringFollowerPassiveGrants(state, playerIndex, follower);
        }
    }

    /// <summary>
    /// Profession-sensitive effects store the <see cref="CardProfession"/> name in ReferencedCardId; a
    /// missing name matches every profession.
    /// </summary>
    private static bool MatchesProfessionFilter(CardDefinition definition, string? professionName) =>
        string.IsNullOrWhiteSpace(professionName) ||
        (Enum.TryParse<CardProfession>(professionName, out var profession) && definition.Profession == profession);

    private static void IncreaseOtherAlliedFollowerStats(GameState state, int sourceFollowerInstanceId, int amount)
        => IncreaseOtherAlliedFollowerStats(state, sourceFollowerInstanceId, amount, professionName: null);

    private static void IncreaseOtherAlliedFollowerStats(
        GameState state,
        int sourceFollowerInstanceId,
        int amount,
        string? professionName)
    {
        foreach (var follower in state.Players[state.ActivePlayer].BoardInternal
                     .Where(follower => follower.InstanceId != sourceFollowerInstanceId &&
                                        MatchesProfessionFilter(follower.Definition, professionName)))
        {
            IncreaseFollowerStats(follower, amount);
        }
    }

    /// <summary>
    /// 【入场曲】“随机1个其他随从和本随从+X/+X”里负责随机那半的部分。When the caster stands alone
    /// this does nothing and the other half of the effect still applies to the caster itself.
    /// </summary>
    private static void IncreaseRandomOtherAlliedFollowerStats(
        GameState state,
        int sourceFollowerInstanceId,
        int amount)
    {
        var otherFollowers = state.Players[state.ActivePlayer].BoardInternal
            .Where(follower => follower.InstanceId != sourceFollowerInstanceId)
            .ToArray();
        if (otherFollowers.Length == 0)
        {
            return;
        }

        IncreaseFollowerStats(otherFollowers[NextInt(state, otherFollowers.Length)], amount);
    }

    private static void ApplySuperEvolve(GameState state, SuperEvolveAction action)
    {
        if (!CanSuperEvolve(state))
        {
            throw new InvalidOperationException("The active player cannot super evolve this turn.");
        }

        var active = state.Players[state.ActivePlayer];
        var follower = GetUnevolvedFollower(active, action.FollowerInstanceId);

        active.SuperEvolutionPoints--;
        active.UsedEvolutionOrSuperEvolutionThisTurn = true;
        StrengthenFollower(follower, 3, EvolutionState.SuperEvolved);
        ApplyEvolutionKeywordChanges(follower);
        ApplyOnEvolveEffects(state, follower);
        ApplyEvolutionEffects(
            state,
            follower,
            action.ModeChoiceIndex,
            action.OwnHandCardTargetInstanceIds,
            action.EnemyFollowerTargetInstanceId,
            GetEvolutionEffectsForSuperEvolution(follower.Definition));
        ApplySuperEvolutionEffects(state, action, follower);
    }

    private static void ApplySuperEvolutionEffects(
        GameState state,
        SuperEvolveAction action,
        FollowerInstance superEvolvedFollower)
    {
        foreach (var effect in GetSuperEvolutionEffects(superEvolvedFollower.Definition))
        {
            switch (effect.Kind)
            {
                case CardEffectKind.SuperEvolveAnotherUnevolvedFollower:
                    ApplySuperEvolveAnotherUnevolvedFollower(
                        state,
                        superEvolvedFollower.InstanceId,
                        action.OtherFollowerTargetInstanceId);
                    break;
                case CardEffectKind.DrawCards:
                    DrawCards(state, state.ActivePlayer, effect.Amount);
                    break;
                case CardEffectKind.GainStatsToOtherAlliedFollowers:
                    IncreaseOtherAlliedFollowerStats(
                        state,
                        superEvolvedFollower.InstanceId,
                        effect.Amount,
                        effect.ReferencedCardId);
                    break;
                case CardEffectKind.GiveEnemyCrest:
                    GiveCrest(state.Players[OtherPlayer(state.ActivePlayer)], CrestCatalog.Get(effect.ReferencedCardId!));
                    break;
                case CardEffectKind.GiveSelfCrest:
                    GiveCrest(state.Players[state.ActivePlayer], CrestCatalog.Get(effect.ReferencedCardId!));
                    break;
                default:
                    throw new InvalidOperationException($"Unsupported super-evolution effect: {effect.Kind}.");
            }
        }
    }

    private static void ApplySuperEvolveAnotherUnevolvedFollower(
        GameState state,
        int sourceFollowerInstanceId,
        int? targetFollowerInstanceId)
    {
        var active = state.Players[state.ActivePlayer];
        var hasSelectableTarget = active.BoardInternal.Any(follower =>
            follower.InstanceId != sourceFollowerInstanceId &&
            follower.EvolutionState == EvolutionState.Unevolved);

        if (targetFollowerInstanceId is null)
        {
            if (hasSelectableTarget)
            {
                throw new InvalidOperationException(
                    "This super-evolution effect requires selecting another unevolved allied follower.");
            }

            return;
        }

        var target = active.BoardInternal.SingleOrDefault(follower =>
                         follower.InstanceId == targetFollowerInstanceId.Value &&
                         follower.InstanceId != sourceFollowerInstanceId)
                     ?? throw new InvalidOperationException("The selected super-evolution target is not another allied follower.");
        if (target.EvolutionState != EvolutionState.Unevolved)
        {
            throw new InvalidOperationException("The selected super-evolution target must be unevolved.");
        }

        // The copied super evolution does not spend SEP, so it does not fire
        // “when super evolved by spending SEP” effects again.
        StrengthenFollower(target, 3, EvolutionState.SuperEvolved);
        ApplyOnEvolveEffects(state, target);
    }

    /// <summary>
    /// Evolves a follower through an ability instead of an evolution point. It gains +2/+2 and
    /// becomes evolved, so 「本随从进化时」 fires while the manual-only 【进化时】 does not.
    /// Keyword changes such as 失去【守护】 / 获得【威慑】 belong to a card's 【进化时】 ability, so
    /// they are deliberately skipped here for the same reason Olivia's ability super evolution
    /// skips them.
    /// </summary>
    private static void EvolveFollowerByAbility(GameState state, int followerInstanceId)
    {
        var follower = state.Players[state.ActivePlayer].BoardInternal
            .SingleOrDefault(candidate => candidate.InstanceId == followerInstanceId);
        if (follower is null || follower.EvolutionState != EvolutionState.Unevolved)
        {
            return;
        }

        StrengthenFollower(follower, 2, EvolutionState.Evolved);
        ApplyOnEvolveEffects(state, follower);
    }

    /// <summary>
    /// 「本随从进化时」 fires for every evolution of the follower, whether it was paid for with an
    /// evolution point or caused by an ability.
    /// </summary>
    private static void ApplyOnEvolveEffects(GameState state, FollowerInstance follower)
    {
        // Every evolution path in this prototype evolves a follower of the active player.
        var ownerIndex = state.ActivePlayer;
        foreach (var effect in follower.Definition.OnEvolveEffects ?? [])
        {
            switch (effect.Kind)
            {
                case CardEffectKind.AddCopyToHand:
                    AddCopiesToHand(state, ownerIndex, effect.ReferencedCardId!, effect.Amount);
                    break;
                case CardEffectKind.AddCopyToHandWithoutLastWords:
                    AddCopiesToHand(state, ownerIndex, effect.ReferencedCardId!, effect.Amount, suppressLastWords: true);
                    break;
                default:
                    throw new InvalidOperationException(
                        $"Unsupported \"when this follower evolves\" effect: {effect.Kind}.");
            }
        }
    }

    private static void ApplyExtraPlayPoint(GameState state)
    {
        if (!CanUseExtraPlayPoint(state))
        {
            throw new InvalidOperationException("The active player cannot use an extra play point now.");
        }

        var active = state.Players[state.ActivePlayer];
        if (active.OwnTurnNumber <= 5)
        {
            active.UsedEarlyExtraPlayPoint = true;
        }
        else
        {
            active.UsedLateExtraPlayPoint = true;
        }

        // Extra PP may temporarily take a player from 10 to 11 PP. It resets at the next turn start.
        active.CurrentPlayPoints++;
    }

    private static void ApplyAttackLeader(GameState state, AttackLeaderAction action)
    {
        var attacker = GetReadyLeaderAttacker(state, action.AttackerInstanceId);
        var opponent = state.Players[OtherPlayer(state.ActivePlayer)];

        if (opponent.Board.Any(follower => follower.HasWard && !follower.HasIntimidate) && !attacker.CanIgnoreWard)
        {
            throw new InvalidOperationException("A Ward follower must be attacked first.");
        }

        ApplyAttackEffects(state, attacker);
        if (state.IsGameOver)
        {
            // An attack trigger such as 可爱恶魔·莉莉姆's can end the game before combat damage.
            return;
        }

        attacker.HasAttacked = true;
        if (DealDamageToLeader(state, OtherPlayer(state.ActivePlayer), attacker.Attack))
        {
            ApplyDrainHeal(state, attacker, attacker.Attack);
        }

        state.Players[state.ActivePlayer].AttackedEnemyLeaderThisTurn = true;
    }

    private static void ApplyAttackFollower(GameState state, AttackFollowerAction action)
    {
        var attacker = GetReadyFollowerAttacker(state, action.AttackerInstanceId);
        var opponent = state.Players[OtherPlayer(state.ActivePlayer)];
        var defender = opponent.BoardInternal.SingleOrDefault(candidate => candidate.InstanceId == action.DefenderInstanceId)
            ?? throw new InvalidOperationException("The selected defender is not on the opponent's board.");

        if (defender.HasIntimidate)
        {
            throw new InvalidOperationException("An Intimidate follower cannot be attacked by an enemy follower.");
        }

        if (opponent.Board.Any(follower => follower.HasWard && !follower.HasIntimidate) &&
            !attacker.CanIgnoreWard &&
            !defender.HasWard)
        {
            throw new InvalidOperationException("A Ward follower must be attacked first.");
        }

        ApplyAttackEffects(state, attacker);
        if (state.IsGameOver)
        {
            return;
        }

        attacker.HasAttacked = true;
        var damageDealt = DealDamageToFollower(
            state,
            OtherPlayer(state.ActivePlayer),
            defender,
            attacker.Attack);
        if (damageDealt)
        {
            ApplyDrainHeal(state, attacker, attacker.Attack);
        }

        if (attacker.HasBane)
        {
            defender.CurrentDefense = 0;
        }

        DealDamageToFollower(
            state,
            state.ActivePlayer,
            attacker,
            defender.Attack);
        if (defender.HasBane)
        {
            attacker.CurrentDefense = 0;
        }

        // Combat damage is simultaneous, then both destroyed followers enter their owner's graveyard.
        var defenderWasDestroyed = defender.CurrentDefense <= 0;
        DestroyFollowerIfNeeded(state, state.ActivePlayer, attacker);
        DestroyFollowerIfNeeded(state, OtherPlayer(state.ActivePlayer), defender);

        if (attacker.IsSuperEvolved && defenderWasDestroyed)
        {
            var opponentIndex = OtherPlayer(state.ActivePlayer);
            DealDamageToLeader(state, opponentIndex, 1);
        }
    }

    private static void ApplyEndTurn(GameState state)
    {
        var endingPlayer = state.Players[state.ActivePlayer];
        ApplyEndOfOwnTurnFollowerEffects(state);
        if (state.IsGameOver)
        {
            return;
        }

        ApplyEndOfOwnTurnAmuletEffects(state);
        if (state.IsGameOver)
        {
            return;
        }

        ApplyHandCostReductions(endingPlayer);
        ApplyEndOfOwnTurnCrestEffects(state, state.ActivePlayer);
        if (state.IsGameOver)
        {
            return;
        }

        RemoveEndOfTurnBonuses(endingPlayer);
        RemoveTimedLeaderEffectsExpiringAtEndOfTurn(state, state.ActivePlayer);
        endingPlayer.AttackedEnemyLeaderOnPreviousTurn = endingPlayer.AttackedEnemyLeaderThisTurn;
        endingPlayer.AttackedEnemyLeaderThisTurn = false;
        state.ActivePlayer = OtherPlayer(state.ActivePlayer);
        StartTurn(state);
    }

    private static void ApplyAttackEffects(GameState state, FollowerInstance attacker)
    {
        foreach (var effect in attacker.Definition.AttackEffects ?? [])
        {
            switch (effect.Kind)
            {
                case CardEffectKind.GainTemporaryAttackIfOwnFollowersAttackedEnemyLeaderPreviousTurn:
                    if (state.Players[state.ActivePlayer].AttackedEnemyLeaderOnPreviousTurn)
                    {
                        IncreaseTemporaryAttack(attacker, effect.Amount);
                    }

                    break;
                case CardEffectKind.DealDamageToAllLeaders:
                    // 【攻击时】对所有主战者造成伤害：both leaders take the damage, including ours.
                    // The damage is simultaneous, so we only stop early when the first hit already
                    // finished the game; otherwise the second hit would overwrite the winner.
                    DealDamageToLeader(state, OtherPlayer(state.ActivePlayer), effect.Amount);
                    if (!state.IsGameOver)
                    {
                        DealDamageToLeader(state, state.ActivePlayer, effect.Amount);
                    }

                    break;
                default:
                    throw new InvalidOperationException($"Unsupported attack effect: {effect.Kind}.");
            }
        }
    }

    /// <summary>
    /// 【虹吸】restores the controller's leader by the damage the follower dealt with its attack.
    /// </summary>
    private static void ApplyDrainHeal(GameState state, FollowerInstance attacker, int damageDealt)
    {
        if (!attacker.HasDrain || damageDealt <= 0)
        {
            return;
        }

        RestoreLeaderHealth(state, state.ActivePlayer, damageDealt);
    }

    /// <summary>
    /// 选择对手的战场上的1个随从，对其造成伤害（【入场曲】与【进化时】共用）。The action carries the
    /// target; when the opponent had no follower at that moment the rest of the card still resolves.
    /// </summary>
    private static void ApplyTargetedFollowerDamage(GameState state, int? targetInstanceId, int damage)
    {
        if (targetInstanceId is null)
        {
            return;
        }

        var opponentIndex = OtherPlayer(state.ActivePlayer);
        var target = state.Players[opponentIndex].BoardInternal
            .SingleOrDefault(follower => follower.InstanceId == targetInstanceId.Value)
            ?? throw new InvalidOperationException("The chosen evolution target is not on the opponent's board.");

        DealDamageToFollower(state, opponentIndex, target, damage);
        DestroyFollowerIfNeeded(state, opponentIndex, target);
    }

    private static void StartTurn(GameState state)
    {
        state.TurnNumber++;
        var active = state.Players[state.ActivePlayer];
        active.OwnTurnNumber++;
        active.MaxPlayPoints = Math.Min(10, active.MaxPlayPoints + 1);
        active.CurrentPlayPoints = active.MaxPlayPoints;
        active.UsedEvolutionOrSuperEvolutionThisTurn = false;

        foreach (var follower in active.BoardInternal)
        {
            follower.HasAttacked = false;
        }

        ApplyStartOfOwnTurnCrestEffects(state);
        if (state.IsGameOver)
        {
            return;
        }

        DecreaseOwnAmuletCountdowns(state);
        if (state.IsGameOver)
        {
            return;
        }

        DrawCards(state, state.ActivePlayer, 1);
    }

    private static void ApplyEndOfOwnTurnAmuletEffects(GameState state)
    {
        var ownerIndex = state.ActivePlayer;
        var owner = state.Players[ownerIndex];
        foreach (var amulet in owner.AmuletsInternal.ToArray())
        {
            foreach (var effect in amulet.Definition.EndOfOwnTurnEffects ?? [])
            {
                switch (effect.Kind)
                {
                    case CardEffectKind.DealDamageToAllFollowersWithoutTrait:
                        DealDamageToAllFollowersWithoutTrait(state, effect.ReferencedCardId!, effect.Amount);
                        break;
                    default:
                        throw new InvalidOperationException($"Unsupported amulet end-of-turn effect: {effect.Kind}.");
                }

                if (state.IsGameOver)
                {
                    return;
                }
            }
        }
    }

    private static void ApplyEndOfOwnTurnFollowerEffects(GameState state)
    {
        var ownerIndex = state.ActivePlayer;
        var owner = state.Players[ownerIndex];
        foreach (var follower in owner.BoardInternal.ToArray())
        {
            var conditionalEffects = follower.IsEvolved
                ? follower.Definition.EvolvedEndOfOwnTurnEffects ?? []
                : follower.Definition.UnevolvedEndOfOwnTurnEffects ?? [];
            foreach (var effect in (follower.Definition.EndOfOwnTurnEffects ?? []).Concat(conditionalEffects))
            {
                switch (effect.Kind)
                {
                    case CardEffectKind.DealDamageToUpToTwoRandomEnemyFollowers:
                        ApplyDamageToUpToTwoRandomEnemyFollowers(state, effect.Amount);
                        break;
                    case CardEffectKind.RestoreOwnLeaderHealth:
                        RestoreLeaderHealth(state, ownerIndex, effect.Amount);
                        break;
                    case CardEffectKind.DealDamageToEnemyLeader:
                        DealDamageToLeader(state, OtherPlayer(ownerIndex), effect.Amount);
                        break;
                    case CardEffectKind.BanishSelf:
                        // 【怨灵】自己的回合结束时消失：banish removes it outright, so it bypasses
                        // the destruction rules and never reaches a graveyard.
                        BanishFollower(state, ownerIndex, follower);
                        break;
                    default:
                        throw new InvalidOperationException($"Unsupported follower end-of-turn effect: {effect.Kind}.");
                }

                if (state.IsGameOver)
                {
                    return;
                }
            }
        }
    }

    private static void DecreaseOwnAmuletCountdowns(GameState state)
    {
        var ownerIndex = state.ActivePlayer;
        var owner = state.Players[ownerIndex];
        foreach (var amulet in owner.AmuletsInternal.ToArray())
        {
            if (amulet.Countdown is not { } countdown)
            {
                continue;
            }

            amulet.Countdown = countdown - 1;
            if (amulet.Countdown == 0)
            {
                DestroyAmulet(state, ownerIndex, amulet);
            }
        }
    }

    /// <summary>Resolves 「自己的回合结束时」 effects printed on the ending player's crests.</summary>
    private static void ApplyEndOfOwnTurnCrestEffects(GameState state, int ownerIndex)
    {
        foreach (var crest in state.Players[ownerIndex].CrestsInternal.ToArray())
        {
            if (crest.Definition.EndOfOwnTurnEffects is null)
            {
                continue;
            }

            foreach (var effect in crest.Definition.EndOfOwnTurnEffects)
            {
                switch (effect.Kind)
                {
                    case CardEffectKind.ShatterRandomLastWordsCardAndEnemyFollower:
                        ShatterRandomLastWordsCardAndRandomEnemyFollower(state, ownerIndex);
                        break;
                    default:
                        throw new InvalidOperationException(
                            $"Unsupported crest end-of-turn effect: {effect.Kind}.");
                }

                if (state.IsGameOver)
                {
                    return;
                }
            }
        }
    }

    /// <summary>
    /// 自己的回合结束时，若自己的战场上有拥有【谢幕曲】的卡牌，则破坏自己的战场上的随机1张拥有
    /// 【谢幕曲】的卡牌和对手的战场上的随机1个随从. The trigger only needs a card of the owner's own,
    /// so the enemy half simply does nothing when the opponent has no follower.
    /// </summary>
    private static void ShatterRandomLastWordsCardAndRandomEnemyFollower(GameState state, int ownerIndex)
    {
        var owner = state.Players[ownerIndex];
        var ownFollowers = owner.BoardInternal
            .Where(follower => follower.Definition.LastWordsEffects is { Count: > 0 })
            .ToArray();
        var ownAmulets = owner.AmuletsInternal
            .Where(amulet => amulet.LastWordsEffects is { Count: > 0 })
            .ToArray();
        var candidateCount = ownFollowers.Length + ownAmulets.Length;
        if (candidateCount == 0)
        {
            return;
        }

        var pick = NextInt(state, candidateCount);
        if (pick < ownFollowers.Length)
        {
            DestroyFollower(state, ownerIndex, ownFollowers[pick]);
        }
        else
        {
            DestroyAmulet(state, ownerIndex, ownAmulets[pick - ownFollowers.Length]);
        }

        DestroyRandomEnemyFollower(state, ownerIndex);
    }

    /// <summary>
    /// 【亡者召回_N】 summons a copy of a follower from the owner's graveyard. It prefers the highest
    /// cost that is at most N (N first, then N-1, and so on) and picks uniformly among the cards of that
    /// cost, so two 2-cost cards next to three other 2-cost cards give a 2/5 and a 3/5 chance. The
    /// graveyard keeps its cards; what appears is a fresh copy of the printed card.
    /// </summary>
    private static void RecallFollowersFromGraveyard(GameState state, int ownerIndex, int count, int maximumCost)
    {
        var player = state.Players[ownerIndex];
        for (var index = 0; index < count; index++)
        {
            var eligible = player.GraveyardInternal
                .Where(card => card.Definition.Type == CardType.Follower &&
                               (maximumCost <= 0 || CurrentCost(card) <= maximumCost))
                .ToArray();
            if (eligible.Length == 0 || player.OccupiedBoardSlots >= PlayerState.BoardLimit)
            {
                return;
            }

            // 优先召唤费用正好为 N 的随从；没有就退到 N-1，以此类推。
            var highestCost = eligible.Max(CurrentCost);
            var candidates = eligible.Where(card => CurrentCost(card) == highestCost).ToArray();
            var chosen = candidates[NextInt(state, candidates.Length)];
            var copy = new CardInstance(state.NextInstanceId++, chosen.Definition)
            {
                CostReduction = chosen.CostReduction
            };
            var follower = new FollowerInstance(copy, state.TurnNumber);
            player.BoardInternal.Add(follower);
            ApplyEnteringFollowerPassiveGrants(state, ownerIndex, follower);
        }
    }

    private static void ApplyStartOfOwnTurnCrestEffects(GameState state)
    {
        var ownerIndex = state.ActivePlayer;
        var owner = state.Players[ownerIndex];
        foreach (var crest in owner.CrestsInternal.ToArray())
        {
            foreach (var effect in crest.Definition.StartOfOwnTurnEffects ?? [])
            {
                switch (effect.Kind)
                {
                    case CardEffectKind.DealDamageToOwnLeader:
                        DealDamageToLeader(state, ownerIndex, effect.Amount);
                        break;
                    default:
                        throw new InvalidOperationException($"Unsupported crest start-of-turn effect: {effect.Kind}.");
                }

                if (state.IsGameOver)
                {
                    return;
                }
            }
        }
    }

    private static FollowerInstance GetReadyLeaderAttacker(GameState state, int attackerId)
    {
        var attacker = state.Players[state.ActivePlayer].BoardInternal
            .SingleOrDefault(candidate => candidate.InstanceId == attackerId)
            ?? throw new InvalidOperationException("The selected attacker is not on the active player's board.");

        if (!CanAttackLeaderThisTurn(state, attacker))
        {
            throw new InvalidOperationException("The selected follower cannot attack the leader this turn.");
        }

        return attacker;
    }

    private static FollowerInstance GetReadyFollowerAttacker(GameState state, int attackerId)
    {
        var attacker = state.Players[state.ActivePlayer].BoardInternal
            .SingleOrDefault(candidate => candidate.InstanceId == attackerId)
            ?? throw new InvalidOperationException("The selected attacker is not on the active player's board.");

        if (!CanAttackFollowerThisTurn(state, attacker))
        {
            throw new InvalidOperationException("The selected follower cannot attack followers this turn.");
        }

        return attacker;
    }

    private static bool CanAttackFollowerThisTurn(GameState state, FollowerInstance follower)
    {
        return !follower.HasAttacked &&
               (follower.HasStorm || follower.HasRush || follower.IsEvolved || follower.SummonedOnTurn != state.TurnNumber);
    }

    private static bool CanAttackLeaderThisTurn(GameState state, FollowerInstance follower)
    {
        return !follower.HasAttacked &&
               (follower.HasStorm || follower.SummonedOnTurn != state.TurnNumber);
    }

    private static bool CanEvolve(GameState state)
    {
        var active = state.Players[state.ActivePlayer];
        var requiredTurn = state.ActivePlayer == state.StartingPlayer ? 5 : 4;

        return active.EvolutionPoints > 0 &&
               !active.UsedEvolutionOrSuperEvolutionThisTurn &&
               active.OwnTurnNumber >= requiredTurn;
    }

    private static bool CanSuperEvolve(GameState state)
    {
        var active = state.Players[state.ActivePlayer];
        var requiredTurn = state.ActivePlayer == state.StartingPlayer ? 7 : 6;

        return active.SuperEvolutionPoints > 0 &&
               !active.UsedEvolutionOrSuperEvolutionThisTurn &&
               active.OwnTurnNumber >= requiredTurn;
    }

    private static bool CanUseExtraPlayPoint(GameState state)
    {
        // Worlds Beyond grants only the player going second one use through their fifth turn,
        // then one fresh use from their sixth turn onward.
        if (state.ActivePlayer == state.StartingPlayer)
        {
            return false;
        }

        var active = state.Players[state.ActivePlayer];
        return active.OwnTurnNumber <= 5
            ? !active.UsedEarlyExtraPlayPoint
            : !active.UsedLateExtraPlayPoint;
    }

    private static FollowerInstance GetUnevolvedFollower(PlayerState player, int followerInstanceId)
    {
        var follower = player.BoardInternal.SingleOrDefault(candidate => candidate.InstanceId == followerInstanceId)
            ?? throw new InvalidOperationException("The selected follower is not on the active player's board.");

        if (follower.EvolutionState != EvolutionState.Unevolved)
        {
            throw new InvalidOperationException("Only an unevolved follower may evolve or super evolve.");
        }

        return follower;
    }

    private static void StrengthenFollower(FollowerInstance follower, int bonus, EvolutionState evolutionState)
    {
        follower.Attack += bonus;
        follower.MaxDefense += bonus;
        follower.CurrentDefense += bonus;
        follower.EvolutionState = evolutionState;
    }

    private static void IncreaseFollowerStats(FollowerInstance follower, int amount)
    {
        follower.Attack += amount;
        follower.MaxDefense += amount;
        follower.CurrentDefense += amount;
    }

    /// <summary>
    /// Applies a -0/-X modifier. This is deliberately separate from damage: it reduces
    /// both current and maximum defense, does not trigger damage handling, and therefore
    /// cannot be healed back above the reduced maximum defense.
    /// </summary>
    private static void DecreaseAllEnemyFollowersDefense(GameState state, int amount)
    {
        var opponentIndex = OtherPlayer(state.ActivePlayer);
        var opponent = state.Players[opponentIndex];

        // Snapshot before removals so every follower present at resolution receives the modifier.
        foreach (var follower in opponent.BoardInternal.ToArray())
        {
            follower.MaxDefense -= amount;
            follower.CurrentDefense -= amount;
            DestroyFollowerIfNeeded(state, opponentIndex, follower);
        }
    }

    private static void GrantFollowerKeywords(FollowerInstance follower, CardKeyword keywords) =>
        follower.GrantedKeywords |= keywords;

    private static void ApplyEvolutionKeywordChanges(FollowerInstance follower)
    {
        follower.ConsumedKeywords |= follower.Definition.EvolutionRemovedKeywords;
        GrantFollowerKeywords(follower, follower.Definition.EvolutionGrantedKeywords);
    }

    private static void IncreaseTemporaryAttack(FollowerInstance follower, int amount)
    {
        follower.Attack += amount;
        follower.TemporaryAttackBonus += amount;
    }

    private static void RemoveEndOfTurnBonuses(PlayerState player)
    {
        foreach (var follower in player.BoardInternal.Where(follower => follower.TemporaryAttackBonus > 0))
        {
            follower.Attack -= follower.TemporaryAttackBonus;
            follower.TemporaryAttackBonus = 0;
        }
    }

    private static void RestoreLeaderHealth(GameState state, int playerIndex, int amount)
    {
        var player = state.Players[playerIndex];
        var healthBefore = player.Health;
        player.Health = Math.Min(player.MaxHealth, player.Health + amount);
        if (player.Health > healthBefore)
        {
            ApplyOwnLeaderRestoredCrestEffects(state, playerIndex);
        }
    }

    private static void SetLeaderMaxHealth(PlayerState player, int maximumHealth)
    {
        player.MaxHealth = maximumHealth;
        player.Health = Math.Min(player.Health, player.MaxHealth);
    }

    private static void RestorePlayPoints(PlayerState player, int amount)
    {
        player.CurrentPlayPoints = Math.Min(player.MaxPlayPoints, player.CurrentPlayPoints + amount);
    }

    /// <summary>
    /// 回复自己进化点：evolution points never exceed the usual maximum of two.
    /// </summary>
    private static void RestoreEvolutionPoints(PlayerState player, int amount)
    {
        player.EvolutionPoints = Math.Min(PlayerState.StartingEvolutionPoints, player.EvolutionPoints + amount);
    }

    private static void IncreaseOwnMaxPlayPoints(PlayerState player, int amount)
    {
        player.MaxPlayPoints = Math.Min(10, player.MaxPlayPoints + amount);
    }

    private static void IncreaseOwnMaxPlayPointsAndDrawIfAtTen(GameState state, int amount)
    {
        var active = state.Players[state.ActivePlayer];
        IncreaseOwnMaxPlayPoints(active, amount);

        // The condition is checked after the increase. It also applies when the player was
        // already at 10 PP, because their maximum PP is still 10 after the effect resolves.
        if (active.MaxPlayPoints == 10)
        {
            DrawCards(state, state.ActivePlayer, 1);
        }
    }

    private static bool DealDamageToFollower(
        GameState state,
        int ownerIndex,
        FollowerInstance follower,
        int damage)
    {
        // Barrier prevents the next instance of damage entirely, then disappears.
        // It is consumed even when another effect later changes that damage to zero.
        if (follower.HasBarrier)
        {
            follower.ConsumedKeywords |= CardKeyword.Barrier;
            return false;
        }

        // A super-evolved follower ignores all damage during its controller's own turn.
        if (follower.IsSuperEvolved && ownerIndex == state.ActivePlayer)
        {
            return false;
        }

        follower.CurrentDefense -= damage;
        return true;
    }

    /// <summary>Applies leader damage. Returns false when a timed effect prevented it entirely.</summary>
    private static bool DealDamageToLeader(GameState state, int playerIndex, int damage)
    {
        if (state.Players[playerIndex].TimedLeaderEffectsInternal.Any(effect =>
                effect.Kind == TimedLeaderEffectKind.PreventAllDamage))
        {
            return false;
        }

        state.Players[playerIndex].Health -= damage;
        CheckLeaderDefeat(state, playerIndex);
        return true;
    }

    private static void ApplyOwnLeaderRestoredCrestEffects(GameState state, int ownerIndex)
    {
        // “自己的每回合中可触发1次” only permits the trigger while that player's own
        // turn is active, and shares one use across all effects on the same crest.
        if (state.ActivePlayer != ownerIndex)
        {
            return;
        }

        var owner = state.Players[ownerIndex];
        foreach (var crest in owner.CrestsInternal.ToArray())
        {
            if (crest.LastOwnLeaderRestoreTriggerTurn == owner.OwnTurnNumber ||
                crest.Definition.OwnLeaderRestoredEffects is null)
            {
                continue;
            }

            crest.LastOwnLeaderRestoreTriggerTurn = owner.OwnTurnNumber;
            foreach (var effect in crest.Definition.OwnLeaderRestoredEffects)
            {
                switch (effect.Kind)
                {
                    case CardEffectKind.DealDamageToOwnLeader:
                        DealDamageToLeader(state, ownerIndex, effect.Amount);
                        break;
                    default:
                        throw new InvalidOperationException($"Unsupported crest leader-restore effect: {effect.Kind}.");
                }

                if (state.IsGameOver)
                {
                    return;
                }
            }
        }
    }

    private static void GiveCrest(PlayerState player, CrestDefinition definition)
    {
        // The official leader area allows at most five crests/faiths total. A named crest
        // is unique, so trying to grant an existing one does not create a duplicate copy.
        if (player.CrestsInternal.Count >= PlayerState.LeaderAreaLimit ||
            player.CrestsInternal.Any(crest => crest.Definition.Id == definition.Id))
        {
            return;
        }

        player.CrestsInternal.Add(new CrestInstance(definition));
    }

    private static void RemoveTimedLeaderEffectsExpiringAtEndOfTurn(GameState state, int endingPlayerIndex)
    {
        foreach (var player in state.Players)
        {
            player.TimedLeaderEffectsInternal.RemoveAll(effect =>
                effect.ExpiresAtEndOfPlayerTurn == endingPlayerIndex);
        }
    }

    private static void DrawCards(GameState state, int playerIndex, int count)
    {
        for (var draw = 0; draw < count; draw++)
        {
            var player = state.Players[playerIndex];
            if (player.DeckInternal.Count == 0)
            {
                state.Winner = OtherPlayer(playerIndex);
                state.Phase = GamePhase.GameOver;
                return;
            }

            var card = player.DeckInternal[0];
            player.DeckInternal.RemoveAt(0);

            AddCardToHandOrGrave(player, card);
        }
    }

    private static void SearchDeckForFollowerWithMinimumCostToHand(GameState state, int minimumCost)
    {
        var player = state.Players[state.ActivePlayer];
        var candidates = player.DeckInternal
            .Where(card => card.Definition.Type == CardType.Follower && card.Definition.Cost >= minimumCost)
            .ToArray();
        if (candidates.Length == 0)
        {
            return;
        }

        var card = candidates[NextInt(state, candidates.Length)];
        player.DeckInternal.Remove(card);
        AddCardToHandOrGrave(player, card);
    }

    private static void AddCardToHandOrGrave(PlayerState player, CardInstance card)
    {
        if (player.HandInternal.Count >= PlayerState.HandLimit)
        {
            player.GraveyardInternal.Add(card);
        }
        else
        {
            player.HandInternal.Add(card);
        }
    }

    private static void DestroyFollowerIfNeeded(GameState state, int playerIndex, FollowerInstance follower)
    {
        if (follower.CurrentDefense > 0)
        {
            return;
        }

        DestroyFollower(state, playerIndex, follower);
    }

    private static bool DestroyFollower(GameState state, int playerIndex, FollowerInstance follower)
    {
        // 【怨灵】离场时消失：the follower leaves play without entering a graveyard or triggering Last Words.
        if (follower.Definition.BanishesWhenLeavingBoard)
        {
            return BanishFollower(state, playerIndex, follower);
        }

        // During their controller's own turn, a super-evolved follower cannot be
        // destroyed by an ability. Combat and ordinary destruction on later turns
        // still work normally.
        if (follower.IsSuperEvolved && playerIndex == state.ActivePlayer)
        {
            return false;
        }

        var owner = state.Players[playerIndex];
        if (owner.BoardInternal.Remove(follower))
        {
            owner.GraveyardInternal.Add(follower.Card);
            ApplyLastWordsEffects(state, playerIndex, follower.Card);
            return true;
        }

        return false;
    }

    /// <summary>
    /// 【消失】Removes a follower from play without destroying it: no graveyard entry, no Last Words,
    /// and no interaction with effects that trigger when a follower is destroyed.
    /// </summary>
    private static bool BanishFollower(GameState state, int playerIndex, FollowerInstance follower)
    {
        return state.Players[playerIndex].BoardInternal.Remove(follower);
    }

    /// <summary>
    /// Destroys an amulet, sends it to its owner's graveyard and resolves its Last Words. A 结晶 amulet
    /// resolves the crystallized Last Words instead of the ones printed on the follower.
    /// </summary>
    private static bool DestroyAmulet(GameState state, int ownerIndex, AmuletInstance amulet)
    {
        var owner = state.Players[ownerIndex];
        if (!owner.AmuletsInternal.Remove(amulet))
        {
            return false;
        }

        owner.GraveyardInternal.Add(amulet.Card);
        ApplyAmuletLastWordsEffects(state, ownerIndex, amulet);
        return true;
    }

    private static void ApplyAmuletLastWordsEffects(GameState state, int ownerIndex, AmuletInstance amulet)
    {
        var effects = amulet.LastWordsEffects;
        if (amulet.Card.HasSuppressedLastWords || effects is null)
        {
            return;
        }

        foreach (var effect in effects)
        {
            switch (effect.Kind)
            {
                case CardEffectKind.SummonFollower:
                    SummonFollowers(state, ownerIndex, effect.ReferencedCardId!, effect.Amount);
                    break;
                case CardEffectKind.SummonFollowerWithoutLastWords:
                    SummonFollowers(state, ownerIndex, effect.ReferencedCardId!, effect.Amount, suppressLastWords: true);
                    break;
                case CardEffectKind.SummonFollowerWithBonusWithoutLastWords:
                    SummonEmpoweredFollowersWithoutLastWords(
                        state,
                        ownerIndex,
                        effect.ReferencedCardId!,
                        effect.Amount,
                        effect.SecondaryAmount);
                    break;
                case CardEffectKind.DrawCards:
                    DrawCards(state, ownerIndex, effect.Amount);
                    break;
                case CardEffectKind.RestoreOwnLeaderHealth:
                    RestoreLeaderHealth(state, ownerIndex, effect.Amount);
                    break;
                case CardEffectKind.DestroyRandomEnemyFollower:
                    DestroyRandomEnemyFollower(state, ownerIndex);
                    break;
                default:
                    throw new InvalidOperationException($"Unsupported amulet Last Words effect: {effect.Kind}.");
            }
        }
    }

    /// <summary>
    /// 破坏对手的战场上的随机1个随从。A random pick may land on a follower with 【灵气】, because that
    /// keyword only stops an ability from designating a target.
    /// </summary>
    private static void DestroyRandomEnemyFollower(GameState state, int actingPlayerIndex)
    {
        var opponentIndex = OtherPlayer(actingPlayerIndex);
        var opponent = state.Players[opponentIndex];
        if (opponent.BoardInternal.Count == 0)
        {
            return;
        }

        DestroyFollower(state, opponentIndex, opponent.BoardInternal[NextInt(state, opponent.BoardInternal.Count)]);
    }

    private static void ApplyLastWordsEffects(GameState state, int ownerIndex, CardInstance source)
    {
        if (source.HasSuppressedLastWords || source.Definition.LastWordsEffects is null)
        {
            return;
        }

        foreach (var effect in source.Definition.LastWordsEffects)
        {
            switch (effect.Kind)
            {
                case CardEffectKind.AddCopyToHandWithoutLastWords:
                    AddCopiesToHand(state, ownerIndex, effect.ReferencedCardId!, effect.Amount, suppressLastWords: true);
                    break;
                case CardEffectKind.AddCopyToHand:
                    AddCopiesToHand(state, ownerIndex, effect.ReferencedCardId!, effect.Amount);

                    break;
                case CardEffectKind.SummonFollower:
                    SummonFollowers(state, ownerIndex, effect.ReferencedCardId!, effect.Amount);
                    break;
                case CardEffectKind.SummonFollowerWithoutLastWords:
                    SummonFollowers(state, ownerIndex, effect.ReferencedCardId!, effect.Amount, suppressLastWords: true);
                    break;
                case CardEffectKind.SummonFollowerWithBonusWithoutLastWords:
                    SummonEmpoweredFollowersWithoutLastWords(
                        state,
                        ownerIndex,
                        effect.ReferencedCardId!,
                        effect.Amount,
                        effect.SecondaryAmount);
                    break;
                case CardEffectKind.DestroyRandomEnemyFollower:
                    DestroyRandomEnemyFollower(state, ownerIndex);
                    break;
                case CardEffectKind.RestoreOwnLeaderHealth:
                    RestoreLeaderHealth(state, ownerIndex, effect.Amount);
                    break;
                default:
                    throw new InvalidOperationException($"Unsupported Last Words effect: {effect.Kind}.");
            }
        }
    }

    private static void AddCardToHand(GameState state, int playerIndex, CardInstance card)
    {
        var player = state.Players[playerIndex];
        if (player.HandInternal.Count >= PlayerState.HandLimit)
        {
            player.GraveyardInternal.Add(card);
            return;
        }

        player.HandInternal.Add(card);
    }

    private static void AddCopiesToHand(
        GameState state,
        int playerIndex,
        string cardId,
        int count,
        bool suppressLastWords = false)
    {
        var definition = CardCatalog.Get(cardId);
        for (var copy = 0; copy < count; copy++)
        {
            AddCardToHand(
                state,
                playerIndex,
                new CardInstance(state.NextInstanceId++, definition, suppressLastWords));
        }
    }

    private static void CheckLeaderDefeat(GameState state, int defeatedPlayer)
    {
        if (state.Players[defeatedPlayer].Health > 0)
        {
            return;
        }

        state.Winner = OtherPlayer(defeatedPlayer);
        state.Phase = GamePhase.GameOver;
    }

    private static void AddDeckCards(GameState state, int playerIndex, DeckDefinition deck)
    {
        foreach (var definition in deck.Cards)
        {
            state.Players[playerIndex].DeckInternal.Add(new CardInstance(state.NextInstanceId++, definition));
        }
    }

    private static void Shuffle(GameState state, int playerIndex)
    {
        ShuffleCards(state, state.Players[playerIndex].DeckInternal);
    }

    private static void ShuffleCards(GameState state, IList<CardInstance> cards)
    {
        for (var index = cards.Count - 1; index > 0; index--)
        {
            var swapIndex = NextInt(state, index + 1);
            (cards[index], cards[swapIndex]) = (cards[swapIndex], cards[index]);
        }
    }

    private static int NextInt(GameState state, int exclusiveMax)
    {
        if (exclusiveMax <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(exclusiveMax));
        }

        // SplitMix64: deterministic and easy to clone as one ulong in GameState.
        state.RandomState += 0x9E3779B97F4A7C15UL;
        var value = state.RandomState;
        value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL;
        value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;
        value ^= value >> 31;
        return (int)(value % (uint)exclusiveMax);
    }

    private static PlayerView ToPlayerView(PlayerState player)
    {
        return new PlayerView(
            player.Health,
            player.MaxHealth,
            player.CurrentPlayPoints,
            player.MaxPlayPoints,
            player.OwnTurnNumber,
            player.EvolutionPoints,
            player.SuperEvolutionPoints,
            player.UsedEarlyExtraPlayPoint,
            player.UsedLateExtraPlayPoint,
            player.AttackedEnemyLeaderOnPreviousTurn,
            player.Hand.Count,
            player.Deck.Count,
            player.Graveyard.Count,
            player.Board.Select(follower => new VisibleFollower(
                follower.InstanceId,
                follower.Definition.Id,
                follower.Definition.Name,
                follower.Attack,
                follower.CurrentDefense,
                follower.MaxDefense,
                follower.Keywords,
                follower.EvolutionState,
                follower.HasAttacked,
                follower.Definition.Traits ?? [])).ToArray(),
            player.Amulets.Select(amulet => new VisibleAmulet(
                amulet.InstanceId,
                amulet.Definition.Id,
                amulet.Definition.Name,
                amulet.Countdown)).ToArray(),
            player.Crests.Select(crest => new VisibleCrest(
                crest.Definition.Id,
                crest.Definition.Name,
                crest.Definition.EffectText)).ToArray(),
            player.RevealedCardIds.ToArray());
    }

    /// <summary>
    /// Records the catalog IDs of the cards an action plays in public. Only a play reveals a card;
    /// drawing, discarding, shuffling and the deck itself never do.
    /// </summary>
    private static void RecordRevealedCards(GameState state, GameAction action)
    {
        var cardInstanceId = action switch
        {
            PlayFollowerAction play => play.CardInstanceId,
            PlayAmuletAction playAmulet => playAmulet.CardInstanceId,
            PlayCrystallizeAction crystallize => crystallize.CardInstanceId,
            PlayAccelerateAction accelerate => accelerate.CardInstanceId,
            PlaySpellAction playSpell => playSpell.CardInstanceId,
            _ => (int?)null
        };
        if (cardInstanceId is null)
        {
            return;
        }

        var active = state.Players[state.ActivePlayer];
        var card = active.HandInternal.FirstOrDefault(candidate =>
            candidate.InstanceId == cardInstanceId.Value);
        if (card is not null)
        {
            // One entry per play, so the count of copies already spent stays observable.
            active.RevealedCardIdsInternal.Add(card.Definition.Id);
        }
    }

    private static int OtherPlayer(int playerIndex)
    {
        ValidatePlayerIndex(playerIndex);
        return playerIndex == 0 ? 1 : 0;
    }

    private static void ValidatePlayerIndex(int playerIndex)
    {
        if (playerIndex is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(playerIndex), "Only player 0 and player 1 exist.");
        }
    }
}
