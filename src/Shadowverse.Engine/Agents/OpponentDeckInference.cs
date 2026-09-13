using System.Collections.Concurrent;
using Shadowverse.Engine.Cards;
using Shadowverse.Engine.Decks;
using Shadowverse.Engine.Game;
using Shadowverse.Engine.Models;

namespace Shadowverse.Engine.Agents;

/// <summary>A saved deck that is still consistent with every card the opponent has revealed.</summary>
public sealed record OpponentDeckCandidate(
    string DeckId,
    string DeckName,
    int RevealedCardsMatched,
    int DeckCardCount);

/// <summary>
/// Works out which saved deck the opponent is on from the cards they have played in public, and
/// turns that into an estimate of the damage they could still be holding.
/// <para>
/// This is the only way to model an opponent's hidden reach without reading their hand. Board
/// contents are already visible, so the value here is twofold: remembering cards that have since
/// left the board, and knowing which cards the opponent can <em>never</em> have because their deck
/// does not contain them.
/// </para>
/// <para>
/// When no saved deck explains what was seen, the inference reports that instead of guessing: an
/// invented deck would silently inject wrong reach numbers into every later decision.
/// </para>
/// </summary>
public static class OpponentDeckInference
{
    // DeckCatalog.All re-reads and re-validates the JSON file on every call, and this runs once per
    // scored action, so the library is read once per process instead.
    private static readonly Lazy<IReadOnlyList<DeckListDefinition>> CachedLibrary =
        new(() => DeckCatalog.All, LazyThreadSafetyMode.ExecutionAndPublication);

    private static readonly Lazy<IReadOnlyDictionary<string, DeckListDefinition>> CachedDecksById =
        new(
            () => CachedLibrary.Value.ToDictionary(deck => deck.Id, StringComparer.Ordinal),
            LazyThreadSafetyMode.ExecutionAndPublication);

    // The reach estimate runs once per rollout leaf inside the lookahead planner, which is tens of
    // thousands of calls per match. The answer depends only on public information that changes
    // slowly, so it is memoised; the cap keeps a long run from growing without bound.
    private const int BurstCacheCapacity = 8192;
    private static readonly ConcurrentDictionary<string, double> BurstCache = new(StringComparer.Ordinal);

    /// <summary>The saved decks that could produce every card the opponent has revealed.</summary>
    public static IReadOnlyList<OpponentDeckCandidate> Identify(IEnumerable<string> revealedCardIds)
    {
        var revealed = DistinctCards(revealedCardIds);
        if (revealed.Length == 0)
        {
            return [];
        }

        var candidates = new List<OpponentDeckCandidate>();
        foreach (var deck in CachedLibrary.Value)
        {
            var deckCardIds = deck.Entries.Select(entry => entry.CardId).ToHashSet(StringComparer.Ordinal);
            // A single revealed card the deck cannot contain rules that deck out entirely.
            if (revealed.Any(id => !deckCardIds.Contains(id)))
            {
                continue;
            }

            candidates.Add(new OpponentDeckCandidate(
                deck.Id,
                deck.Name,
                revealed.Length,
                deck.Entries.Sum(entry => entry.Count)));
        }

        return candidates
            .OrderByDescending(candidate => candidate.RevealedCardsMatched)
            .ThenBy(candidate => candidate.DeckId, StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>
    /// Every card the opponent could still be holding, given the decks that remain possible. With
    /// several candidates this is their union, which is deliberately conservative: it over-states
    /// the opponent's options rather than under-stating them.
    /// </summary>
    public static IReadOnlyList<CardDefinition> PossibleCards(IEnumerable<string> revealedCardIds)
    {
        var candidates = Identify(revealedCardIds);
        if (candidates.Count == 0)
        {
            return [];
        }

        var possibleIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var candidate in candidates)
        {
            if (!CachedDecksById.Value.TryGetValue(candidate.DeckId, out var deck))
            {
                continue;
            }

            foreach (var entry in deck.Entries)
            {
                possibleIds.Add(entry.CardId);
            }
        }

        return possibleIds.Select(CardCatalog.Get).ToArray();
    }

    /// <summary>
    /// The largest single hit the opponent could add from hand this turn, weighted by how likely
    /// they are to still be holding it. Add their board's attack to get their total reach.
    /// <para>
    /// Two corrections matter here. Copies the opponent has already played are subtracted, so the
    /// estimate stops expecting burst that has been spent. And the probability is drawn from the
    /// cards they have not shown yet — their hand plus what is left of their deck — rather than
    /// from a fresh 40-card deck, which would badly over-state reach once the game is under way.
    /// </para>
    /// </summary>
    public static double EstimatedHandBurst(
        IReadOnlyList<string> revealedCardIds,
        int opponentHandCount,
        int opponentMaxPlayPoints,
        int opponentDeckCount)
    {
        if (opponentHandCount <= 0)
        {
            return 0.0;
        }

        var unseenPool = opponentHandCount + opponentDeckCount;
        if (unseenPool <= 0)
        {
            return 0.0;
        }

        var cacheKey = string.Concat(
            opponentHandCount,
            "|",
            opponentMaxPlayPoints,
            "|",
            opponentDeckCount,
            "|",
            string.Join(',', revealedCardIds.OrderBy(id => id, StringComparer.Ordinal)));
        if (BurstCache.TryGetValue(cacheKey, out var cached))
        {
            return cached;
        }

        var candidates = Identify(revealedCardIds);
        if (candidates.Count == 0)
        {
            return 0.0;
        }

        var revealedCounts = CountByCardId(revealedCardIds);
        var copiesAvailable = MaxCopiesPerCard(candidates);
        var best = 0.0;

        foreach (var (cardId, copiesInDeck) in copiesAvailable)
        {
            var alreadyPlayed = revealedCounts.TryGetValue(cardId, out var played) ? played : 0;
            var remainingCopies = Math.Max(0, copiesInDeck - alreadyPlayed);
            if (remainingCopies == 0)
            {
                continue;
            }

            var card = CardCatalog.Get(cardId);
            var damage = MaxLeaderDamage(card);
            if (damage <= 0 || card.Cost > opponentMaxPlayPoints)
            {
                continue;
            }

            var probability = ProbabilityOfHoldingAtLeastOne(
                remainingCopies,
                unseenPool,
                opponentHandCount);
            best = Math.Max(best, damage * probability);
        }

        if (BurstCache.Count < BurstCacheCapacity)
        {
            BurstCache[cacheKey] = best;
        }

        return best;
    }

    /// <summary>The most copies of each card any surviving candidate deck carries.</summary>
    private static Dictionary<string, int> MaxCopiesPerCard(IReadOnlyList<OpponentDeckCandidate> candidates)
    {
        var copies = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var candidate in candidates)
        {
            if (!CachedDecksById.Value.TryGetValue(candidate.DeckId, out var deck))
            {
                continue;
            }

            foreach (var entry in deck.Entries)
            {
                copies[entry.CardId] = copies.TryGetValue(entry.CardId, out var current)
                    ? Math.Max(current, entry.Count)
                    : entry.Count;
            }
        }

        return copies;
    }

    private static Dictionary<string, int> CountByCardId(IEnumerable<string> revealedCardIds)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var id in revealedCardIds)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                continue;
            }

            counts[id] = counts.TryGetValue(id, out var current) ? current + 1 : 1;
        }

        return counts;
    }

    private static string[] DistinctCards(IEnumerable<string> revealedCardIds) =>
        revealedCardIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    private static int MaxLeaderDamage(CardDefinition card)
    {
        var best = 0;
        foreach (var effect in EnumerateEffects(card))
        {
            var damage = effect.Kind switch
            {
                CardEffectKind.DealDamageToEnemyLeader => effect.Amount,
                CardEffectKind.DealDamageToEnemyLeaderIfAwakened => effect.Amount,
                CardEffectKind.DealDamageToAllEnemyFollowersAndLeader => effect.Amount,
                CardEffectKind.DealDamageToRandomEnemyFollowerAndLeader => effect.Amount,
                CardEffectKind.DealDamageToUpToTwoEnemyFollowersAndLeader => effect.Amount,
                CardEffectKind.DealDamageToEnemyFollowerOrLeader => effect.Amount,
                _ => 0
            };
            best = Math.Max(best, damage);
        }

        return best;
    }

    private static IEnumerable<CardEffect> EnumerateEffects(CardDefinition card)
    {
        foreach (var effect in card.FanfareEffects ?? [])
        {
            yield return effect;
        }

        foreach (var effect in card.SpellEffects ?? [])
        {
            yield return effect;
        }

        if (card.Effect is not null)
        {
            yield return card.Effect;
        }

        foreach (var mode in card.FanfareModeOptions ?? [])
        {
            foreach (var effect in mode.Effects)
            {
                yield return effect;
            }
        }
    }

    /// <summary>
    /// The chance a hand of <paramref name="handSize"/> cards drawn without replacement from
    /// <paramref name="poolSize"/> unseen cards contains at least one of
    /// <paramref name="copies"/> remaining copies.
    /// </summary>
    private static double ProbabilityOfHoldingAtLeastOne(int copies, int poolSize, int handSize)
    {
        if (copies <= 0)
        {
            return 0.0;
        }

        if (copies >= poolSize || handSize >= poolSize)
        {
            return 1.0;
        }

        var none = 1.0;
        for (var drawn = 0; drawn < handSize; drawn++)
        {
            var remaining = poolSize - copies - drawn;
            if (remaining <= 0)
            {
                return 1.0;
            }

            none *= (double)remaining / (poolSize - drawn);
        }

        return 1.0 - none;
    }
}
