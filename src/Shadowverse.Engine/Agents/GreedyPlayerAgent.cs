using Shadowverse.Engine.Game;
using Shadowverse.Engine.Cards;
using Shadowverse.Engine.Models;

namespace Shadowverse.Engine.Agents;

/// <summary>
/// First non-random baseline. It uses only the visible observation and a transparent hand-written score.
/// It is intentionally simple: the next iteration can replace this with rollout search or a learned policy.
/// </summary>
public sealed class GreedyPlayerAgent : IPlayerAgent
{
    // These are deliberately visible, hand-tuned policy values. They make this baseline
    // useful for testing a Royal deck centred on Starchium; they are not learned values.
    private const int StarchiumMulliganKeepScore = 100;
    private const int StarchiumPlayPriority = 90;
    private const int StarchiumEvolutionPriority = 100;
    private const int NormalEvolutionBaseScore = 75;
    private const int SuperEvolutionBaseScore = 115;
    private const int DragonRainbowPlayPriority = 16;
    private const int LethalScore = 100_000;

    // 进化 / 超进化的身材加成，与 GameEngine 里 StrengthenFollower 的调用保持一致。
    private const int EvolutionStatBonus = 2;
    private const int SuperEvolutionStatBonus = 3;

    // 攻击估值：打脸基准权重、击杀随从基准权重，以及落后时的攻防倾斜幅度。
    private const double FaceDamageWeight = 6.0;
    private const int FollowerKillWeight = 8;
    private const double RaceDiscount = 0.35;
    private const double DefensePremium = 0.5;

    // 是否把"由对手已公开的牌推断出的卡组、及其潜在手牌爆发"计入威胁模型。
    // 关掉它就只按对手场面的攻击力估算威胁，用来做对照测量。
    private const bool UseInferredReach = true;

    public GameAction ChooseAction(GameObservation observation, IReadOnlyList<GameAction> legalActions)
    {
        if (legalActions.Count == 0)
        {
            throw new InvalidOperationException("An agent was asked to act without any legal actions.");
        }

        var lethal = AnalyzeLethal(observation, legalActions);

        return legalActions
            .Select((action, index) => new ScoredAction(action, Score(action, observation, lethal), index))
            .OrderByDescending(candidate => candidate.Score)
            .ThenByDescending(candidate => TieBreakPriority(candidate.Action))
            .ThenBy(candidate => candidate.Index)
            .First()
            .Action;
    }

    /// <summary>
    /// Scores one action. Once the face damage still available this turn would kill the opponent,
    /// every action that contributes to that kill outranks any amount of ordinary value, so the
    /// agent commits to the lethal line instead of developing its board or trading followers.
    /// </summary>
    private static int Score(GameAction action, GameObservation observation, LethalContext lethal)
    {
        // The two attack scorers need the opponent's reach, which is estimated once per decision
        // rather than once per scored action, so they are handled here instead of in ScoreBase.
        var score = action switch
        {
            AttackLeaderAction attackLeader =>
                ScoreLeaderAttack(attackLeader, observation, lethal.IncomingPressure),
            AttackFollowerAction attackFollower =>
                ScoreFollowerAttack(attackFollower, observation, lethal.IncomingPressure),
            _ => ScoreBase(action, observation)
        };

        return lethal.IsLethal && AdvancesLethal(action, observation, lethal)
            ? Math.Max(score, LethalScore)
            : score;
    }

    private static int ScoreBase(GameAction action, GameObservation observation)
    {
        return action switch
        {
            MulliganAction mulligan => ScoreMulligan(mulligan, observation),
            PlayFollowerAction play => ScorePlay(play, observation),
            PlayAmuletAction playAmulet => ScoreAmulet(playAmulet, observation),
            PlayCrystallizeAction crystallize => ScoreCrystallize(crystallize, observation),
            PlayAccelerateAction accelerate => ScoreAccelerate(accelerate, observation),
            PlaySpellAction playSpell => ScoreSpell(playSpell, observation),
            EvolveAction evolve => ScoreEvolution(
                evolve.FollowerInstanceId,
                observation,
                isSuperEvolution: false,
                modeChoiceIndex: evolve.ModeChoiceIndex,
                enemyFollowerTargetInstanceId: evolve.EnemyFollowerTargetInstanceId),
            SuperEvolveAction superEvolve => ScoreEvolution(
                superEvolve.FollowerInstanceId,
                observation,
                isSuperEvolution: true,
                superEvolve.OtherFollowerTargetInstanceId,
                enemyFollowerTargetInstanceId: superEvolve.EnemyFollowerTargetInstanceId),
            UseExtraPlayPointAction => ScoreExtraPlayPoint(observation),
            EndTurnAction => 0,
            _ => throw new InvalidOperationException("Unsupported action type.")
        };
    }

    /// <summary>
    /// What the current turn can still deliver to the enemy leader.
    /// <para>
    /// Face attacks are summed exactly from the legal attack actions: each attacker attacks once
    /// and attacking costs no play points, so the whole sum is reachable. One hand play is added
    /// on top because it is legal, therefore affordable, and does not consume an attack. Adding a
    /// second play would double-count a single play-point pool and could report a kill that does
    /// not exist. Evolution only adds its stat bonus to a follower that can already reach the
    /// leader, because evolving grants 突进 rather than 疾驰.
    /// </para>
    /// </summary>
    private static LethalContext AnalyzeLethal(
        GameObservation observation,
        IReadOnlyList<GameAction> legalActions)
    {
        var faceAttackers = new HashSet<int>();
        var attackDamage = 0;
        foreach (var action in legalActions)
        {
            if (action is AttackLeaderAction attackLeader)
            {
                faceAttackers.Add(attackLeader.AttackerInstanceId);
                attackDamage += FaceDamageOfAttack(attackLeader, observation);
            }
        }

        var bestPlayDamage = 0;
        foreach (var action in legalActions)
        {
            if (action is PlaySpellAction or PlayFollowerAction)
            {
                bestPlayDamage = Math.Max(bestPlayDamage, FaceDamageOf(action, observation));
            }
        }

        var evolutionGain = 0;
        foreach (var action in legalActions)
        {
            evolutionGain = Math.Max(evolutionGain, action switch
            {
                EvolveAction evolve when faceAttackers.Contains(evolve.FollowerInstanceId) =>
                    EvolutionStatBonus,
                SuperEvolveAction superEvolve when faceAttackers.Contains(superEvolve.FollowerInstanceId) =>
                    SuperEvolutionStatBonus,
                _ => 0
            });
        }

        return new LethalContext(
            faceAttackers,
            attackDamage + bestPlayDamage + evolutionGain >= observation.Opponent.Health,
            IncomingLethalPressure(observation));
    }

    /// <summary>Whether this action is part of the kill that this turn already has available.</summary>
    private static bool AdvancesLethal(
        GameAction action,
        GameObservation observation,
        LethalContext lethal) =>
        FaceDamageOf(action, observation) > 0 ||
        (action switch
        {
            EvolveAction evolve => lethal.FaceAttackers.Contains(evolve.FollowerInstanceId),
            SuperEvolveAction superEvolve => lethal.FaceAttackers.Contains(superEvolve.FollowerInstanceId),
            _ => false
        });

    private static int FaceDamageOf(GameAction action, GameObservation observation) => action switch
    {
        AttackLeaderAction attackLeader => FaceDamageOfAttack(attackLeader, observation),
        PlaySpellAction playSpell => FaceDamageOfPlay(playSpell, observation),
        PlayFollowerAction playFollower => FaceDamageOfPlay(playFollower, observation),
        _ => 0
    };

    private static int FaceDamageOfAttack(AttackLeaderAction action, GameObservation observation)
    {
        var attacker = observation.Self.Board.Single(follower =>
            follower.InstanceId == action.AttackerInstanceId);
        return attacker.Attack + AttackTriggerBonus(attacker, observation);
    }

    private static int FaceDamageOfPlay(GameAction action, GameObservation observation)
    {
        var cardInstanceId = action switch
        {
            PlaySpellAction playSpell => playSpell.CardInstanceId,
            PlayFollowerAction playFollower => playFollower.CardInstanceId,
            _ => throw new InvalidOperationException("Only a spell or a follower play can deal face damage.")
        };
        var card = observation.OwnHand.Single(card => card.InstanceId == cardInstanceId);

        var effects = action is PlaySpellAction
            ? GetSpellEffects(card.Definition)
            : GetFanfareEffects(card.Definition);

        var damage = 0;
        foreach (var effect in effects)
        {
            // 边狱的邪崇 only damages the leader as part of a Fanfare that chose targets; without
            // chosen targets that half of the effect never resolves.
            if (effect.Kind == CardEffectKind.DealDamageToUpToTwoEnemyFollowersAndLeader &&
                action is PlayFollowerAction { EnemyFollowerTargetInstanceIds: null })
            {
                continue;
            }

            damage += LeaderDamageOfEffect(effect, observation);
        }

        return damage;
    }

    /// <summary>
    /// The leader damage an effect is guaranteed to deal. Effects that damage both leaders are
    /// deliberately excluded: they resolve simultaneously, so they are not a safe way to close a
    /// game and could lose it instead.
    /// </summary>
    private static int LeaderDamageOfEffect(CardEffect effect, GameObservation observation) => effect.Kind switch
    {
        CardEffectKind.DealDamageToEnemyLeader => effect.Amount,
        CardEffectKind.DealDamageToEnemyLeaderIfAwakened =>
            observation.Self.MaxPlayPoints >= 7 ? effect.Amount : 0,
        CardEffectKind.DealDamageToAllEnemyFollowersAndLeader => effect.Amount,
        CardEffectKind.DealDamageToRandomEnemyFollowerAndLeader => effect.Amount,
        CardEffectKind.DealDamageToUpToTwoEnemyFollowersAndLeader => effect.Amount,
        _ => 0
    };

    /// <summary>
    /// How many of the cards a draw effect produces actually reach the hand. Cards drawn past the
    /// nine-card hand limit are destroyed, so they never become a resource; scoring them as full
    /// cards made the agent happily draw into a full hand and burn its own deck.
    /// </summary>
    private static int CardsReachingHand(GameObservation observation, int amount) =>
        Math.Min(Math.Max(0, PlayerState.HandLimit - observation.Self.HandCount), amount);

    private static int ScoreMulligan(MulliganAction action, GameObservation observation)
    {
        var replaced = action.ReplaceInstanceIds.ToHashSet();
        var hasDragonDeckSignal = observation.OwnHand.Any(card =>
            card.Definition.Profession == CardProfession.Dragon);
        if (hasDragonDeckSignal)
        {
            return observation.OwnHand
                .Where(card => !replaced.Contains(card.InstanceId))
                .Sum(card => ScoreDragonMulliganCard(card.Definition));
        }

        var hasStarchium = observation.OwnHand.Any(card =>
            card.Definition.Id == CardIds.RoyalSeveringHeavenStarchium);

        return observation.OwnHand
            .Where(card => !replaced.Contains(card.InstanceId))
            .Sum(card => ScoreKeptMulliganCard(card.Definition, hasStarchium));
    }

    /// <summary>
    /// 泛用的起手保留评分。
    /// <para>
    /// 原来这里在"手牌里没有斯塔奇乌姆"时给每张牌 −10 分，那是专为剑斗士卡组写的找牌策略：
    /// 找不到那张牌就整副起手全换。剑斗士已禁赛，而这条规则对任何既没有龙族卡、也没有那张牌的
    /// 卡组就退化成"永远全换四张"——实测中速梦的换牌张数恒为 4.00，连 2 费优质节奏牌也一起扔掉。
    /// </para>
    /// 现在改成一条与卡组无关的费用曲线：低费留、高费换。
    /// </summary>
    private static int ScoreKeptMulliganCard(CardDefinition card, bool hasStarchium)
    {
        if (card.Id == CardIds.RoyalSeveringHeavenStarchium)
        {
            return StarchiumMulliganKeepScore;
        }

        // hasStarchium 仍然保留：围着斯塔奇乌姆的卡组会希望起手多留低费配合牌，
        // 但这不再改变"留还是换"的方向，只影响低费牌的相对优先级。
        var lowCostBonus = hasStarchium ? 4 : 0;
        return card.Cost switch
        {
            <= 1 => 20 + lowCostBonus,
            2 => 18 + lowCostBonus,
            3 => 12 + lowCostBonus,
            4 => 4,
            5 => -4,
            6 => -10,
            7 => -15,
            _ => -20
        };
    }

    private static int ScoreDragonMulliganCard(CardDefinition card)
    {
        var score = card.Cost switch
        {
            <= 1 => 28,
            2 => 26,
            3 => 22,
            4 => 12,
            5 => 8,
            6 => -3,
            7 => -10,
            8 => -15,
            _ => -22
        };

        score += card.Rarity switch
        {
            CardRarity.Rainbow => 20,
            CardRarity.Gold => 7,
            CardRarity.Silver => 2,
            _ => 0
        };

        // Dragon Oracle is the deck's main way to bring its rainbow endgame forward.
        if (card.Id == CardIds.DragonOracle)
        {
            score += 32;
        }

        return score;
    }

    private static int ScorePlay(PlayFollowerAction action, GameObservation observation)
    {
        var card = observation.OwnHand.Single(card => card.InstanceId == action.CardInstanceId);
        // Spending PP creates board value; this is a tie-breaker, not a replacement for combat scoring.
        var score = card.Definition.Cost;

        if (card.Definition.Id == CardIds.RoyalSeveringHeavenStarchium)
        {
            // It is important to have this follower on board before the evolution turns.
            // This intentionally loses to an immediate lethal action, but generally beats
            // ordinary plays of the same turn.
            score += StarchiumPlayPriority;
        }

        if (card.Definition.Id == CardIds.ShatteredBandit)
        {
            // A one-time Last Words replacement makes this early follower less disposable.
            score += 8;
        }

        if (card.Definition.Profession == CardProfession.Dragon &&
            card.Definition.Rarity == CardRarity.Rainbow)
        {
            // 郭龙的核心威胁集中在虹卡；这只作为节奏相近时的偏好，仍让斩杀、
            // 清场和即时解场的分数优先决定行动。
            score += DragonRainbowPlayPriority;
        }

        score += ScoreFollowerEndOfTurnEffects(card.Definition, isEvolved: false, observation);

        score += ScoreEnhance(card.Definition, observation.Self.CurrentPlayPoints);

        foreach (var effect in GetFanfareEffects(card.Definition))
        {
            switch (effect.Kind)
            {
                case CardEffectKind.ReturnOwnHandCardToDeckThenDrawCards:
                    score += ScoreReturnHandCard(action, observation);
                    break;
                case CardEffectKind.DealDamageToUpToTwoEnemyFollowersAndLeader:
                    score += ScoreUpToTwoFollowerDamageFanfare(action, effect.Amount, observation);
                    break;
                case CardEffectKind.ReplaceOwnDeckWithApocalypseDeck:
                    // Replacing a dwindling deck with ten exceptionally powerful cards is
                    // valuable, but should not override an immediate winning attack.
                    score += observation.Self.DeckCount == 0 ? -100_000 : 35;
                    break;
                case CardEffectKind.GainStormIfOwnFollowersAttackedEnemyLeaderPreviousTurn:
                    score += observation.Self.AttackedEnemyLeaderOnPreviousTurn ? 30 : 0;
                    break;
                case CardEffectKind.DealDamageToRandomEnemyFollower:
                    score += ScoreRandomFollowerDamage(effect.Amount, observation);
                    break;
                case CardEffectKind.DealDamageToRandomEnemyFollowerIfOwnFollowersAttackedEnemyLeaderPreviousTurn:
                    if (observation.Self.AttackedEnemyLeaderOnPreviousTurn)
                    {
                        score += ScoreRandomFollowerDamage(effect.Amount, observation);
                    }

                    break;
                case CardEffectKind.DiscardOwnHandCards:
                    score += ScoreDiscardOwnHandCards(action.OwnHandCardTargetInstanceIds, observation);
                    break;
                case CardEffectKind.DiscardOwnHandCardsUpTo:
                    score += ScoreDiscardOwnHandCards(action.OwnHandCardTargetInstanceIds, observation);
                    break;
                case CardEffectKind.DealDamageToAllEnemyFollowersAndLeader:
                    score += ScoreAllEnemyFollowersAndLeaderDamage(effect.Amount, observation);
                    break;
                case CardEffectKind.DealDamageToAllEnemyFollowers:
                    score += ScoreAllEnemyFollowersDamage(effect.Amount, observation);
                    break;
                case CardEffectKind.IncreaseOwnMaxPlayPoints:
                    score += ScoreIncreaseMaxPlayPoints(observation);
                    break;
            }
        }

        score += ScoreSelectedFanfareMode(card.Definition, action.ModeChoiceIndex, observation);

        return score;
    }

    private static int ScoreAccelerate(PlayAccelerateAction action, GameObservation observation)
    {
        var card = observation.OwnHand.Single(card => card.InstanceId == action.CardInstanceId);
        var accelerate = card.Definition.Accelerate
            ?? throw new InvalidOperationException("An Accelerate action requires an Accelerate definition.");

        return accelerate.Effects.Sum(effect => effect.Kind switch
        {
            CardEffectKind.IncreaseOwnMaxPlayPoints => ScoreIncreaseMaxPlayPoints(observation),
            _ => throw new InvalidOperationException($"Unsupported Accelerate effect: {effect.Kind}.")
        });
    }

    /// <summary>
    /// 结晶: the card goes down cheaply as an amulet now and turns into the follower a few turns later,
    /// so it is valued a little below simply playing the follower, with a bonus for leaving room.
    /// </summary>
    private static int ScoreCrystallize(PlayCrystallizeAction action, GameObservation observation)
    {
        var card = observation.OwnHand.Single(card => card.InstanceId == action.CardInstanceId);
        var crystallize = card.Definition.Crystallize
            ?? throw new InvalidOperationException("A Crystallize action requires a Crystallize definition.");
        var bodyValue = card.Definition.Attack + card.Definition.Defense;
        var roomBonus = observation.Self.Board.Count < PlayerState.BoardLimit ? 4 : -6;
        return 6 + (bodyValue * 2) - (crystallize.Cost * 2) + roomBonus;
    }

    private static int ScoreAmulet(PlayAmuletAction action, GameObservation observation)
    {
        var card = observation.OwnHand.Single(card => card.InstanceId == action.CardInstanceId);
        var score = card.Definition.Cost;
        foreach (var effect in card.Definition.EndOfOwnTurnEffects ?? [])
        {
            if (effect.Kind != CardEffectKind.DealDamageToAllFollowersWithoutTrait)
            {
                continue;
            }

            var protectedTrait = effect.ReferencedCardId!;
            var enemyValue = observation.Opponent.Board
                .Where(follower => follower.Traits?.Contains(protectedTrait, StringComparer.Ordinal) != true)
                .Sum(follower => follower.CurrentDefense <= effect.Amount
                    ? FollowerValue(follower) * 7
                    : effect.Amount * 2);
            var ownRisk = observation.Self.Board
                .Where(follower => follower.Traits?.Contains(protectedTrait, StringComparer.Ordinal) != true)
                .Sum(follower => follower.CurrentDefense <= effect.Amount
                    ? FollowerValue(follower) * 7
                    : effect.Amount * 2);

            // An amulet with 吟唱 N resolves its end-of-turn effect N times before it is destroyed.
            // Later triggers are worth less because the board can change before they happen, so
            // they are weighted 1, 1/2, 1/4 ... which sums to 2*(1 - 2^-N). The old code hard-coded
            // N = 2 instead of reading the card's actual Countdown.
            var triggers = card.Definition.Countdown ?? 1;
            var weight = 2.0 * (1.0 - Math.Pow(0.5, triggers));
            score += (int)Math.Round((enemyValue - ownRisk) * weight);
        }

        return score;
    }

    private static int ScoreSelectedFanfareMode(
        CardDefinition definition,
        int? modeChoiceIndex,
        GameObservation observation)
    {
        if (definition.FanfareModeOptions is null)
        {
            return modeChoiceIndex is null ? 0 : -100_000;
        }

        return ScoreSelectedMode(definition.FanfareModeOptions, modeChoiceIndex, observation);
    }

    /// <summary>The modes the card chooses from when it evolves: its own list, or the Fanfare modes.</summary>
    private static IReadOnlyList<ModeDefinition>? EvolutionModes(CardDefinition definition) =>
        definition.EvolutionModeOptions ??
        (definition.EvolutionRepeatsFanfareMode ? definition.FanfareModeOptions : null);

    private static int ScoreSelectedEvolutionMode(
        CardDefinition definition,
        int? modeChoiceIndex,
        GameObservation observation)
    {
        var modes = EvolutionModes(definition);
        return modes is null
            ? (modeChoiceIndex is null ? 0 : -100_000)
            : ScoreSelectedMode(modes, modeChoiceIndex, observation);
    }

    private static int ScoreSelectedMode(
        IReadOnlyList<ModeDefinition> modes,
        int? modeChoiceIndex,
        GameObservation observation)
    {
        if (modeChoiceIndex is null || modeChoiceIndex.Value < 0 || modeChoiceIndex.Value >= modes.Count)
        {
            return -100_000;
        }

        return modes[modeChoiceIndex.Value].Effects
            .Sum(effect => effect.Kind switch
            {
                CardEffectKind.DrawCards => observation.Self.DeckCount < effect.Amount
                    ? -100_000
                    : 20 + (CardsReachingHand(observation, effect.Amount) * 5),
                CardEffectKind.RestoreOwnLeaderHealth => Math.Min(
                    effect.Amount,
                    PlayerState.StartingHealth - observation.Self.Health) * 5,
                CardEffectKind.DecreaseAllEnemyFollowersDefense =>
                    ScoreAllEnemyFollowerDefenseDecrease(effect.Amount, observation),
                CardEffectKind.GainStats => effect.Amount * 8,
                CardEffectKind.GainStatsToRandomOtherAlliedFollower =>
                    observation.Self.Board.Count > 0 ? effect.Amount * 8 : 0,
                CardEffectKind.DealDamageToRandomEnemyFollower =>
                    ScoreRandomFollowerDamage(effect.Amount, observation),
                CardEffectKind.DealDamageToEnemyLeader => ScoreLeaderDamage(effect.Amount, observation),
                CardEffectKind.DealDamageToAllEnemyFollowers =>
                    ScoreAllEnemyFollowersDamage(effect.Amount, observation),
                CardEffectKind.RestoreOwnEvolutionPoints =>
                    Math.Min(effect.Amount, PlayerState.StartingEvolutionPoints - observation.Self.EvolutionPoints) * 12,
                CardEffectKind.RestoreOwnPlayPoints => effect.Amount * 8,
                CardEffectKind.GainStorm => 8,
                CardEffectKind.GainWard => 6,
                CardEffectKind.DealDamageToOwnLeader => -effect.Amount * 4,
                CardEffectKind.SummonFollower => 16,
                CardEffectKind.SummonFollowerWithoutLastWords => 14,
                CardEffectKind.SummonFollowerWithBonusWithoutLastWords => 16,
                CardEffectKind.RecallFollowerFromGraveyard =>
                    observation.Self.Board.Count >= PlayerState.BoardLimit ? 0 : 18,
                _ => throw new InvalidOperationException($"Unsupported Fanfare mode effect: {effect.Kind}.")
            });
    }

    private static int ScoreAllEnemyFollowerDefenseDecrease(int amount, GameObservation observation) =>
        observation.Opponent.Board.Sum(follower =>
            follower.CurrentDefense <= amount
                ? FollowerValue(follower) * 7
                : (amount * 3) + 2);

    private static int ScoreAllEnemyFollowersAndLeaderDamage(int damage, GameObservation observation) =>
        ScoreLeaderDamage(damage, observation) + observation.Opponent.Board.Sum(follower =>
            follower.CurrentDefense <= damage
                ? FollowerValue(follower) * 7
                : damage * 2);

    private static int ScoreAllEnemyFollowersDamage(int damage, GameObservation observation) =>
        observation.Opponent.Board.Sum(follower =>
            follower.CurrentDefense <= damage
                ? FollowerValue(follower) * 7
                : damage * 2);

    private static int ScoreEnhance(CardDefinition definition, int currentPlayPoints)
    {
        var resolvedEnhance = definition.EnhanceEffects?
            .Where(enhance => enhance.Cost <= currentPlayPoints)
            .OrderByDescending(enhance => enhance.Cost)
            .FirstOrDefault();
        if (resolvedEnhance is null || definition.EnhanceEffects is null)
        {
            return 0;
        }

        return definition.EnhanceEffects
            .Where(enhance => enhance.Cost <= resolvedEnhance.Cost)
            .SelectMany(enhance => enhance.Effects)
            .Sum(effect => effect.Kind switch
            {
                CardEffectKind.GainStats => effect.Amount * 5,
                CardEffectKind.SearchDeckForFollowerWithMinimumCostToHand => 24,
                CardEffectKind.RestoreOwnPlayPoints => effect.Amount * 3,
                CardEffectKind.SummonFollower => effect.Amount * 8,
                CardEffectKind.GainStorm => 20,
                CardEffectKind.SetOwnLeaderMaxHealth => effect.Amount == 1 ? -45 : 0,
                CardEffectKind.GrantOwnLeaderDamageImmunityUntilEndOfOpponentTurn => 90,
                _ => 0
            });
    }

    private static int ScoreReturnHandCard(PlayFollowerAction action, GameObservation observation)
    {
        if (action.HandCardTargetInstanceId is null)
        {
            return -5;
        }

        var returnedCard = observation.OwnHand.Single(card =>
            card.InstanceId == action.HandCardTargetInstanceId.Value);
        // For a redraw effect, prefer returning a card that is currently less useful to keep.
        return returnedCard.Definition.Cost switch
        {
            >= 6 => 12,
            >= 4 => 8,
            3 => 4,
            2 => 0,
            _ => -4
        };
    }

    private static int ScoreUpToTwoFollowerDamageFanfare(
        PlayFollowerAction action,
        int damage,
        GameObservation observation)
    {
        var targets = action.EnemyFollowerTargetInstanceIds;
        if (targets is null)
        {
            return 0;
        }

        var score = observation.Opponent.Health <= damage ? 100_000 : damage * 6;
        foreach (var targetId in targets)
        {
            var follower = observation.Opponent.Board.Single(card => card.InstanceId == targetId);
            score += follower.CurrentDefense <= damage
                ? FollowerValue(follower) * 8
                : damage * 2;
        }

        return score;
    }

    private static int ScoreSpell(PlaySpellAction action, GameObservation observation)
    {
        var card = observation.OwnHand.Single(card => card.InstanceId == action.CardInstanceId);
        var effects = GetSpellEffects(card.Definition);
        if (effects.Count == 0)
        {
            throw new InvalidOperationException($"Spell {card.Definition.Id} has no resolvable effect.");
        }

        var score = effects.Sum(effect => ScoreSpellEffect(action, effect, observation));

        return score + ScoreSpellEnhance(card.Definition, observation);
    }

    private static int ScoreSpellEffect(PlaySpellAction action, CardEffect effect, GameObservation observation) =>
        effect.Kind switch
        {
            CardEffectKind.DealDamageToEnemyFollowerOrLeader => ScoreDamageSpell(action.Target, effect.Amount, observation),
            CardEffectKind.DrawCards => observation.Self.DeckCount < effect.Amount
                ? -100_000
                : 20 + (CardsReachingHand(observation, effect.Amount) * 5),
            CardEffectKind.SetEnemyLeaderMaxHealth => observation.Opponent.MaxHealth <= effect.Amount
                ? -5
                : 120,
            CardEffectKind.SummonFollower => ScoreSummonSpell(effect, observation),
            CardEffectKind.DealDamageToRandomEnemyFollower => ScoreRandomFollowerDamage(effect.Amount, observation),
            CardEffectKind.DiscardOwnHandCards => ScoreDiscardOwnHandCards(action.OwnHandCardTargetInstanceIds, observation),
            CardEffectKind.DealDamageToRandomEnemyFollowerAndLeader =>
                ScoreRandomFollowerDamage(effect.Amount, observation) + ScoreLeaderDamage(effect.Amount, observation),
            CardEffectKind.DistributeDamageAmongEnemyFollowersByEntryOrder => ScoreDistributedDamage(effect, observation),
            CardEffectKind.IncreaseOwnMaxPlayPointsAndDrawIfAtTen =>
                ScoreIncreaseMaxPlayPointsAndDrawIfAtTen(observation),
            CardEffectKind.DestroyRandomEnemyFollowerWithHighestAttack =>
                ScoreDestroyRandomHighestAttackFollower(observation),
            CardEffectKind.DealDamageToEnemyLeader => ScoreLeaderDamage(effect.Amount, observation),
            CardEffectKind.DealDamageToEnemyLeaderIfAwakened => observation.Self.MaxPlayPoints >= 7
                ? ScoreLeaderDamage(effect.Amount, observation)
                : 0,
            CardEffectKind.RestoreOwnLeaderHealth => Math.Min(
                effect.Amount,
                observation.Self.MaxHealth - observation.Self.Health) * 5,
            CardEffectKind.DestroyEnemyFollower => ScoreDestroyFollower(action.Target, observation),
            CardEffectKind.DealDamageToAllFollowersByFollowerCount =>
                ScoreAllFollowersByFollowerCount(observation),
            _ => throw new InvalidOperationException($"Unsupported spell effect: {effect.Kind}.")
        };

    private static int ScoreDestroyFollower(SpellTarget? target, GameObservation observation) =>
        target is EnemyFollowerTarget followerTarget
            ? FollowerValue(observation.Opponent.Board.Single(follower =>
                follower.InstanceId == followerTarget.FollowerInstanceId)) * 8
            : -100_000;

    private static int ScoreAllFollowersByFollowerCount(GameObservation observation)
    {
        var damage = observation.Self.Board.Count + observation.Opponent.Board.Count;
        if (damage == 0)
        {
            return -5;
        }

        var enemyValue = observation.Opponent.Board.Sum(follower =>
            follower.CurrentDefense <= damage ? FollowerValue(follower) * 7 : damage * 2);
        var ownRisk = observation.Self.Board.Sum(follower =>
            follower.CurrentDefense <= damage ? FollowerValue(follower) * 7 : damage * 2);
        return enemyValue - ownRisk;
    }

    private static int ScoreDestroyRandomHighestAttackFollower(GameObservation observation)
    {
        if (observation.Opponent.Board.Count == 0)
        {
            return -5;
        }

        var highestAttack = observation.Opponent.Board.Max(follower => follower.Attack);
        var targets = observation.Opponent.Board.Where(follower => follower.Attack == highestAttack).ToArray();
        return (int)Math.Round(targets.Average(follower => FollowerValue(follower) * 7));
    }

    private static int ScoreIncreaseMaxPlayPointsAndDrawIfAtTen(GameObservation observation)
    {
        if (observation.Self.MaxPlayPoints < 10)
        {
            // Early PP acceleration is especially valuable because it makes future turns
            // reach a card's cost threshold one turn sooner.
            return 34 + ((10 - observation.Self.MaxPlayPoints) * 2);
        }

        return observation.Self.DeckCount == 0 ? -100_000 : 18;
    }

    private static int ScoreIncreaseMaxPlayPoints(GameObservation observation) =>
        observation.Self.MaxPlayPoints >= 10 ? 0 :
        34 + ((10 - observation.Self.MaxPlayPoints) * 2);

    private static int ScoreDiscardOwnHandCards(
        IReadOnlyList<int>? targetInstanceIds,
        GameObservation observation)
    {
        if (targetInstanceIds is null)
        {
            return -100_000;
        }

        // Discarding a lower-cost card generally preserves more future options than discarding
        // an expensive late-game card. 郭龙还要特别保护虹卡：它们是加速后的主要
        // 收束资源，因此只有效果本身的收益足够高时才值得舍弃。
        return -targetInstanceIds
            .Select(instanceId => observation.OwnHand.Single(card => card.InstanceId == instanceId).Definition)
            .Sum(card => 2 + (card.Cost * 2) + DiscardRarityPenalty(card.Rarity));
    }

    private static int DiscardRarityPenalty(CardRarity rarity) => rarity switch
    {
        CardRarity.Rainbow => 14,
        CardRarity.Gold => 5,
        CardRarity.Silver => 2,
        _ => 0
    };

    private static int ScoreLeaderDamage(int damage, GameObservation observation) =>
        observation.Opponent.Health <= damage ? 100_000 : damage * 6;

    private static int ScoreDistributedDamage(CardEffect effect, GameObservation observation)
    {
        var remainingDamage = effect.Amount +
            (observation.Self.AttackedEnemyLeaderOnPreviousTurn
                ? effect.BonusAmountIfOwnFollowersAttackedEnemyLeaderPreviousTurn
                : 0);
        var score = 0;

        foreach (var follower in observation.Opponent.Board)
        {
            if (remainingDamage == 0)
            {
                break;
            }

            var assignedDamage = Math.Min(remainingDamage, follower.CurrentDefense);
            remainingDamage -= assignedDamage;
            score += assignedDamage >= follower.CurrentDefense
                ? FollowerValue(follower) * 6
                : assignedDamage * 2;
        }

        return score;
    }

    private static int ScoreSummonSpell(CardEffect effect, GameObservation observation)
    {
        if (OccupiedBoardSlots(observation.Self) >= PlayerState.BoardLimit)
        {
            return -10;
        }

        var summoned = CardCatalog.Get(effect.ReferencedCardId!);
        return 10 + (summoned.Attack * 2) + summoned.Defense;
    }

    private static int ScoreSpellEnhance(CardDefinition definition, GameObservation observation)
    {
        var resolvedEnhance = definition.EnhanceEffects?
            .Where(enhance => enhance.Cost <= observation.Self.CurrentPlayPoints)
            .OrderByDescending(enhance => enhance.Cost)
            .FirstOrDefault();
        if (resolvedEnhance is null || definition.EnhanceEffects is null)
        {
            return 0;
        }

        return definition.EnhanceEffects
            .Where(enhance => enhance.Cost <= resolvedEnhance.Cost)
            .SelectMany(enhance => enhance.Effects)
            .Sum(effect => effect.Kind switch
            {
                CardEffectKind.DealDamageToRandomEnemyFollower => ScoreRandomFollowerDamage(effect.Amount, observation),
                CardEffectKind.DealDamageToAllEnemyFollowersAndLeader =>
                    ScoreAllEnemyFollowersAndLeaderDamage(effect.Amount, observation),
                _ => 0
            });
    }

    private static int ScoreRandomFollowerDamage(int damage, GameObservation observation)
    {
        if (observation.Opponent.Board.Count == 0)
        {
            return -8;
        }

        return (int)Math.Round(observation.Opponent.Board
            .Select(follower => follower.CurrentDefense <= damage
                ? FollowerValue(follower) * 6
                : damage * 2)
            .Average());
    }

    private static int ScoreUpToTwoRandomFollowerDamage(int damage, GameObservation observation)
    {
        if (observation.Opponent.Board.Count == 0)
        {
            return 0;
        }

        var averageTargetValue = observation.Opponent.Board
            .Select(follower => follower.CurrentDefense <= damage
                ? FollowerValue(follower) * 6
                : damage * 2)
            .Average();
        return (int)Math.Round(averageTargetValue * Math.Min(2, observation.Opponent.Board.Count));
    }

    private static int ScoreFollowerEndOfTurnEffects(
        CardDefinition definition,
        bool isEvolved,
        GameObservation observation)
    {
        var effects = isEvolved
            ? definition.EvolvedEndOfOwnTurnEffects ?? []
            : definition.UnevolvedEndOfOwnTurnEffects ?? [];
        var score = 0;
        foreach (var effect in effects)
        {
            score += effect.Kind switch
            {
                CardEffectKind.DealDamageToUpToTwoRandomEnemyFollowers =>
                    ScoreUpToTwoRandomFollowerDamage(effect.Amount, observation),
                CardEffectKind.RestoreOwnLeaderHealth => Math.Min(
                    effect.Amount,
                    observation.Self.MaxHealth - observation.Self.Health) * 5,
                CardEffectKind.DealDamageToEnemyLeader => ScoreLeaderDamage(effect.Amount, observation),
                _ => 0
            };
        }

        return score;
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

    private static int ScoreDamageSpell(SpellTarget? target, int damage, GameObservation observation)
    {
        return target switch
        {
            EnemyLeaderTarget => observation.Opponent.Health <= damage ? 100_000 : damage * 6,
            EnemyFollowerTarget followerTarget => ScoreDamageSpellOnFollower(followerTarget, damage, observation),
            _ => throw new InvalidOperationException("Damage spell requires a target.")
        };
    }

    private static int ScoreDamageSpellOnFollower(
        EnemyFollowerTarget target,
        int damage,
        GameObservation observation)
    {
        var follower = observation.Opponent.Board.Single(card => card.InstanceId == target.FollowerInstanceId);
        return follower.CurrentDefense <= damage
            ? FollowerValue(follower) * 8
            : damage * 2;
    }

    private static int ScoreEvolution(
        int followerInstanceId,
        GameObservation observation,
        bool isSuperEvolution,
        int? otherFollowerTargetInstanceId = null,
        int? modeChoiceIndex = null,
        int? enemyFollowerTargetInstanceId = null)
    {
        var follower = observation.Self.Board.Single(card => card.InstanceId == followerInstanceId);
        var definition = CardCatalog.Get(follower.CardId);

        // Evolution now has positive value even on an empty opposing board. This reflects
        // the tempo of gaining stats and keeps the agent alert to cards with evolution text.
        var score = (isSuperEvolution ? SuperEvolutionBaseScore : NormalEvolutionBaseScore) +
                    FollowerValue(follower);
        if (observation.Opponent.Board.Count == 0)
        {
            // Avoid treating evolution as completely free when it creates no immediate
            // interaction, while still leaving it meaningfully above ending the turn.
            score -= isSuperEvolution ? 35 : 25;
        }

        score += ScoreEvolutionEffects(follower, observation, isSuperEvolution, enemyFollowerTargetInstanceId);
        score += ScoreEvolutionKeywordChanges(definition);
        score += ScoreFollowerEndOfTurnEffects(definition, isEvolved: true, observation);
        if (isSuperEvolution)
        {
            score += ScoreSuperEvolutionEffects(definition, observation);
        }
        if (EvolutionModes(definition) is not null)
        {
            score += ScoreSelectedEvolutionMode(definition, modeChoiceIndex, observation);
        }

        if (!isSuperEvolution)
        {
            if (follower.CardId == CardIds.RoyalSeveringHeavenStarchium)
            {
                score += ScoreStarchiumEvolution(follower, observation);
            }
        }
        if (otherFollowerTargetInstanceId is not null)
        {
            var target = observation.Self.Board.Single(card => card.InstanceId == otherFollowerTargetInstanceId.Value);
            score += 30 + FollowerValue(target);
        }

        return score;
    }

    private static int ScoreEvolutionKeywordChanges(CardDefinition definition) =>
        KeywordValue(definition.EvolutionGrantedKeywords) -
        KeywordValue(definition.EvolutionRemovedKeywords);

    private static int KeywordValue(CardKeyword keywords)
    {
        var value = 0;
        if (keywords.HasFlag(CardKeyword.Ward)) value += 4;
        if (keywords.HasFlag(CardKeyword.Storm)) value += 6;
        if (keywords.HasFlag(CardKeyword.Bane)) value += 6;
        if (keywords.HasFlag(CardKeyword.Rush)) value += 3;
        if (keywords.HasFlag(CardKeyword.IgnoreWard)) value += 4;
        if (keywords.HasFlag(CardKeyword.Barrier)) value += 4;
        if (keywords.HasFlag(CardKeyword.Intimidate)) value += 6;
        if (keywords.HasFlag(CardKeyword.Aura)) value += 5;
        if (keywords.HasFlag(CardKeyword.Drain)) value += 3;
        return value;
    }

    private static int ScoreSuperEvolutionEffects(CardDefinition definition, GameObservation observation) =>
        (definition.SuperEvolutionEffects ?? []).Sum(effect => effect.Kind switch
        {
            CardEffectKind.GiveEnemyCrest => observation.Opponent.Crests?.Any(crest =>
                crest.Id == effect.ReferencedCardId) == true
                ? 0
                : 28,
            CardEffectKind.DrawCards => observation.Self.DeckCount == 0
                ? -100_000
                : CardsReachingHand(observation, effect.Amount) * 12,
            _ => 0
        });

    private static int ScoreStarchiumEvolution(VisibleFollower source, GameObservation observation)
    {
        var openSlots = Math.Max(0, PlayerState.BoardLimit - OccupiedBoardSlots(observation.Self));
        var summonedKnightCount = Math.Min(2, openSlots);
        var existingOtherFollowerCount = Math.Max(0, observation.Self.Board.Count - 1);

        // It captures the guaranteed two Knights where there is room and the +1/+1 on
        // all other followers, including the Knights just summoned. The high base value
        // is intentional: this deck's policy is explicitly built around this evolution.
        return StarchiumEvolutionPriority +
               (summonedKnightCount * 12) +
               ((existingOtherFollowerCount + summonedKnightCount) * 12);
    }

    private static int ScoreEvolutionEffects(
        VisibleFollower source,
        GameObservation observation,
        bool isSuperEvolution,
        int? enemyFollowerTargetInstanceId = null)
    {
        var definition = CardCatalog.Get(source.CardId);
        var evolutionEffects = isSuperEvolution
            ? definition.SuperEvolutionEvolutionEffects ?? definition.EvolutionEffects ?? []
            : definition.EvolutionEffects ?? [];
        var score = 0;
        foreach (var effect in evolutionEffects)
        {
            switch (effect.Kind)
            {
                case CardEffectKind.SummonFollower:
                    var openSlots = Math.Max(0, PlayerState.BoardLimit - OccupiedBoardSlots(observation.Self));
                    if (openSlots > 0)
                    {
                        var summoned = CardCatalog.Get(effect.ReferencedCardId!);
                        score += Math.Min(effect.Amount, openSlots) * (summoned.Attack + summoned.Defense) * 3;
                    }

                    break;
                case CardEffectKind.GainStatsToOtherAlliedFollowers:
                    var otherFollowerCount = Math.Max(0, observation.Self.Board.Count - 1);
                    var availableSlots = Math.Max(0, PlayerState.BoardLimit - OccupiedBoardSlots(observation.Self));
                    var summonedCount = evolutionEffects
                        .Where(candidate => candidate.Kind == CardEffectKind.SummonFollower)
                        .Sum(candidate => candidate.Amount);
                    otherFollowerCount += Math.Min(availableSlots, summonedCount);
                    score += otherFollowerCount * effect.Amount * 6;
                    break;
                case CardEffectKind.AddCopyToHand:
                    score += effect.Amount * 12;
                    break;
                case CardEffectKind.DiscardOwnHandCardsUpTo:
                    score -= effect.Amount * 3;
                    break;
                case CardEffectKind.DrawCards:
                    score += observation.Self.DeckCount == 0
                        ? -100_000
                        : 20 + (CardsReachingHand(observation, effect.Amount) * 5);
                    break;
                case CardEffectKind.RestoreOwnLeaderHealth:
                    score += Math.Min(effect.Amount, observation.Self.MaxHealth - observation.Self.Health) * 5;
                    break;
                case CardEffectKind.DealDamageToEnemyFollower:
                    score += ScoreEvolutionFollowerDamage(effect.Amount, enemyFollowerTargetInstanceId, observation);
                    break;
            }
        }

        return score;
    }

    /// <summary>
    /// 【进化时】选择对手的战场上的1个随从，对其造成伤害：destroying the chosen follower is worth
    /// far more than chipping it, and wasted overkill damage counts against the action.
    /// </summary>
    private static int ScoreEvolutionFollowerDamage(int damage, int? targetInstanceId, GameObservation observation)
    {
        if (targetInstanceId is null)
        {
            return 0;
        }

        var target = observation.Opponent.Board
            .FirstOrDefault(follower => follower.InstanceId == targetInstanceId.Value);
        if (target is null)
        {
            return 0;
        }

        var overkill = Math.Max(0, damage - target.CurrentDefense);
        return target.CurrentDefense <= damage
            ? 45 - (overkill * 2)
            : (damage * 8) - (overkill * 2);
    }

    private static int ScoreExtraPlayPoint(GameObservation observation)
    {
        var newlyPlayableCard = observation.OwnHand
            .Where(card => card.Definition.Cost > observation.Self.CurrentPlayPoints &&
                           card.Definition.Cost <= observation.Self.CurrentPlayPoints + 1)
            .OrderByDescending(card => card.Definition.Cost)
            .FirstOrDefault();

        var newlyEnhancedCard = observation.OwnHand
            .Where(card => card.Definition.EnhanceEffects?.Any(enhance =>
                enhance.Cost > observation.Self.CurrentPlayPoints &&
                enhance.Cost <= observation.Self.CurrentPlayPoints + 1) == true)
            .OrderByDescending(card => card.Definition.EnhanceEffects!.Max(enhance => enhance.Cost))
            .FirstOrDefault();

        if (newlyPlayableCard is not null)
        {
            return 20 + newlyPlayableCard.Definition.Cost;
        }

        return newlyEnhancedCard is null
            ? -1
            : 15 + newlyEnhancedCard.Definition.EnhanceEffects!.Max(enhance => enhance.Cost);
    }

    private static int ScoreLeaderAttack(
        AttackLeaderAction action,
        GameObservation observation,
        double incomingPressure)
    {
        var attacker = observation.Self.Board.Single(card => card.InstanceId == action.AttackerInstanceId);
        var attackBonus = AttackTriggerBonus(attacker, observation);
        var faceDamage = attacker.Attack + attackBonus;
        if (faceDamage >= observation.Opponent.Health)
        {
            return LethalScore;
        }

        // Racing is worth less while the opponent's reach could already kill us next turn.
        var discount = incomingPressure * RaceDiscount;
        return (int)Math.Round(faceDamage * FaceDamageWeight * (1.0 - discount));
    }

    private static int ScoreFollowerAttack(
        AttackFollowerAction action,
        GameObservation observation,
        double incomingPressure)
    {
        var attacker = observation.Self.Board.Single(card => card.InstanceId == action.AttackerInstanceId);
        var defender = observation.Opponent.Board.Single(card => card.InstanceId == action.DefenderInstanceId);
        var effectiveAttack = attacker.Attack + AttackTriggerBonus(attacker, observation);

        var defenderDestroyed = effectiveAttack >= defender.CurrentDefense || attacker.Keywords.HasFlag(CardKeyword.Bane);
        var attackerDestroyed = defender.Attack >= attacker.CurrentDefense;
        var score = 0;

        if (defenderDestroyed)
        {
            // Removing a follower also removes the damage it would swing back for, which matters
            // more the closer the opponent already is to killing us.
            var premium = incomingPressure * DefensePremium;
            score += (int)Math.Round(FollowerValue(defender) * FollowerKillWeight * (1.0 + premium));

            // An attack spends the attacker's whole attack for the turn. Damage beyond what the
            // kill needed is therefore thrown away, and is worth about as much as face damage
            // would have been. Without this the agent happily used a 5-attack follower to remove
            // a 1-defense one while a cheaper attacker stood idle.
            var overkill = Math.Max(0, effectiveAttack - defender.CurrentDefense);
            score -= (int)Math.Round(overkill * FaceDamageWeight);
        }
        else
        {
            score += effectiveAttack * 2;
        }

        if (attackerDestroyed)
        {
            score -= FollowerValue(attacker) * FollowerKillWeight;
        }
        else
        {
            score -= defender.Attack * 2;
        }

        // Strongly reject trades such as a 1/1 attacking a 2/2 without destroying it.
        if (attackerDestroyed && !defenderDestroyed)
        {
            score -= 20;
        }

        return score;
    }

    /// <summary>
    /// How close the opponent is to being able to kill the leader: 0 while nothing they could do
    /// reaches, 1 once their reach already covers our remaining health.
    /// <para>
    /// Reach is their visible board plus an estimate of the burst still hidden in their hand. The
    /// hidden part comes from identifying which saved deck they are on by the cards they have
    /// revealed, then asking what that deck could be holding — never from reading their hand.
    /// Before they have played anything the hidden part is correctly zero.
    /// </para>
    /// </summary>
    private static double IncomingLethalPressure(GameObservation observation)
    {
        var boardAttack = observation.Opponent.Board.Sum(follower => follower.Attack);
        var handBurst = UseInferredReach
            ? OpponentDeckInference.EstimatedHandBurst(
                observation.Opponent.RevealedCardIds ?? [],
                observation.Opponent.HandCount,
                observation.Opponent.MaxPlayPoints,
                observation.Opponent.DeckCount)
            : 0.0;
        var reach = boardAttack + handBurst;
        return reach <= 0
            ? 0.0
            : Math.Min(1.0, reach / Math.Max(1, observation.Self.Health));
    }

    private static int AttackTriggerBonus(VisibleFollower attacker, GameObservation observation) =>
        attacker.CardId == CardIds.OverwhelmingAssailant && observation.Self.AttackedEnemyLeaderOnPreviousTurn
            ? 1
            : 0;

    private static int FollowerValue(VisibleFollower follower)
    {
        var definition = CardCatalog.Get(follower.CardId);
        var rarityValue = definition.Rarity switch
        {
            CardRarity.Rainbow => 6,
            CardRarity.Gold => 2,
            _ => 0
        };

        // 挂在随从身上、还没兑现的账：谢幕曲和被动效果。
        // 原来只看攻击力/体力/关键词，所以"一只带谢幕曲的随从"和"一只白板"同分——
        // 这会让规则牌手低估它、也会让它错判"该不该撞死对面带谢幕曲的随从"。
        // 这里只用**效果条数**做代理（不解释具体是什么效果），避免手调"一个谢幕曲值几分"。
        // 2026-09-13 夜间实测过的改动，**已回退，不要重做**：
        // 曾给"带谢幕曲/被动效果的随从"在 FollowerValue 里加 (效果条数 × 3) 分，
        // 想做"待兑现价值"的意识。同一批种子 20000 局对照：
        //   带加分 = 2925:1862（决胜负率 61.1%）｜ 不带 = 3048:1798（62.9%）
        // 差 123 对，z≈2.5，p≈0.012 —— **加了反而显著更差**。
        // 推测原因：FollowerValue 被攻击目标选择、交换判断等多处复用，
        // 一个固定加分扭曲了那些比较。要做这件事必须按用途分开，不能塞进通用估值里。
        return (2 * follower.Attack) + follower.CurrentDefense + rarityValue +
               (follower.Keywords.HasFlag(CardKeyword.Bane) ? 4 : 0) +
               (follower.Keywords.HasFlag(CardKeyword.Intimidate) ? 3 : 0) +
               (follower.Keywords.HasFlag(CardKeyword.Rush) ? 2 : 0) +
               (follower.Keywords.HasFlag(CardKeyword.IgnoreWard) ? 3 : 0) +
               (follower.Keywords.HasFlag(CardKeyword.Barrier) ? 3 : 0) +
               (follower.Keywords.HasFlag(CardKeyword.Aura) ? 4 : 0);
    }

    private static int OccupiedBoardSlots(PlayerView player) =>
        player.Board.Count + (player.Amulets?.Count ?? 0);

    private static int TieBreakPriority(GameAction action) => action switch
    {
        AttackFollowerAction => 4,
        PlaySpellAction => 4,
        PlayAmuletAction => 3,
        PlayAccelerateAction => 4,
        AttackLeaderAction => 3,
        SuperEvolveAction => 3,
        EvolveAction => 3,
        UseExtraPlayPointAction => 2,
        PlayFollowerAction => 2,
        EndTurnAction => 1,
        MulliganAction => 0,
        _ => 0
    };

    private sealed record ScoredAction(GameAction Action, int Score, int Index);

    /// <summary>
    /// The lethal picture for one decision: which followers can already reach the leader, and
    /// whether the damage those attacks plus one hand play can still deliver ends the game.
    /// </summary>
    private sealed record LethalContext(
        HashSet<int> FaceAttackers,
        bool IsLethal,
        double IncomingPressure);
}
