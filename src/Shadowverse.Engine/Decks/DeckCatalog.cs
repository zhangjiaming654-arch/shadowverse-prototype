using System.Text.Json;
using Shadowverse.Engine.Cards;
using Shadowverse.Engine.Models;

namespace Shadowverse.Engine.Decks;

/// <summary>A named deck list that refers to catalog card IDs and copy counts.</summary>
public sealed record DeckListDefinition(
    string Id,
    string Name,
    string Description,
    IReadOnlyList<DeckCardEntry> Entries)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Id))
        {
            throw new ArgumentException("A deck ID is required.", nameof(Id));
        }

        if (string.IsNullOrWhiteSpace(Name))
        {
            throw new ArgumentException("A deck name is required.", nameof(Name));
        }

        if (Entries is null)
        {
            throw new ArgumentException("Deck entries are required.", nameof(Entries));
        }

        foreach (var entry in Entries)
        {
            entry.Validate();
        }

        // A stored deck may be a work in progress: it only has to stay within the deck limit. A match
        // still requires a complete deck.
        if (Entries.Sum(entry => entry.Count) > DeckDefinition.RequiredCardCount)
        {
            throw new ArgumentException(
                $"A stored deck list cannot contain more than {DeckDefinition.RequiredCardCount} cards.",
                nameof(Entries));
        }
    }
}

/// <summary>
/// The single source of truth for saved deck lists, persisted in data/deck-library.json.
/// The library may be empty while cards and decks are being assembled.
/// </summary>
public static class DeckCatalog
{
    private static readonly DeckListDefinition[] EmptyLibrary = [];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    /// <summary>Path shared by the console and the Windows deck editor.</summary>
    public static string StoragePath { get; } = FindStoragePath();

    public static IReadOnlyList<DeckListDefinition> All => Load();

    public static DeckListDefinition Get(string deckId)
    {
        if (string.IsNullOrWhiteSpace(deckId))
        {
            throw new ArgumentException("A deck ID is required.", nameof(deckId));
        }

        var deck = Load().SingleOrDefault(candidate =>
            string.Equals(candidate.Id, deckId, StringComparison.Ordinal));

        return deck ?? throw new ArgumentException($"Unknown deck ID: {deckId}", nameof(deckId));
    }

    public static DeckDefinition Create(string deckId, string? displayName = null)
    {
        var deck = Get(deckId);
        return CardCatalog.CreateDeck(displayName ?? deck.Name, deck.Entries);
    }

    public static void Save(IEnumerable<DeckListDefinition> decks)
    {
        ArgumentNullException.ThrowIfNull(decks);
        var validatedDecks = ValidateDecks(decks);
        var directory = Path.GetDirectoryName(StoragePath)
            ?? throw new InvalidOperationException("Deck library has no parent directory.");
        Directory.CreateDirectory(directory);

        var temporaryPath = StoragePath + ".tmp";
        var document = new DeckLibraryDocument { Decks = validatedDecks.ToList() };
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(document, JsonOptions));
        File.Move(temporaryPath, StoragePath, overwrite: true);
    }

    private static IReadOnlyList<DeckListDefinition> Load()
    {
        if (!File.Exists(StoragePath))
        {
            return EmptyLibrary;
        }

        try
        {
            var json = File.ReadAllText(StoragePath);
            var document = JsonSerializer.Deserialize<DeckLibraryDocument>(json, JsonOptions)
                ?? throw new InvalidOperationException("Deck library file is empty.");
            return ValidateDecks(document.Decks);
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException($"Unable to read deck library: {StoragePath}", exception);
        }
    }

    private static IReadOnlyList<DeckListDefinition> ValidateDecks(IEnumerable<DeckListDefinition> decks)
    {
        var deckArray = decks.ToArray();
        var duplicateId = deckArray
            .GroupBy(deck => deck.Id, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateId is not null)
        {
            throw new InvalidOperationException($"Deck ID {duplicateId.Key} appears more than once.");
        }

        foreach (var deck in deckArray)
        {
            deck.Validate();
            CardCatalog.ValidateDeckEntries(deck.Entries);
        }

        return deckArray;
    }

    private static string FindStoragePath()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "ShadowversePrototype.sln")))
            {
                return Path.Combine(directory.FullName, "data", "deck-library.json");
            }
        }

        return Path.Combine(AppContext.BaseDirectory, "data", "deck-library.json");
    }

    private sealed class DeckLibraryDocument
    {
        public List<DeckListDefinition> Decks { get; set; } = [];
    }
}
