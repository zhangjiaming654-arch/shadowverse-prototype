using Shadowverse.Engine.Decks;
using Shadowverse.Engine.Models;

namespace Shadowverse.ConsoleApp;

/// <summary>
/// Compatibility placeholder retained for older callers. The starter deck was
/// removed when the deck library was reset.
/// </summary>
public static class BasicDeckFactory
{
    public static DeckDefinition Create(string name)
    {
        throw new InvalidOperationException(
            "当前没有基础牌组。请先在卡组编辑器中建立并保存一副 40 张卡组。");
    }
}
