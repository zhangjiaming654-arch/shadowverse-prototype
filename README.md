# Shadowverse prototype

This Visual Studio solution implements the current prototype ruleset:

- editable 40-card decks, backed by a shared card catalog;
- 4-card starting hand and one redraw for any chosen cards;
- leader health 20, PP grows by one each own turn (up to 10), and one draw each turn;
- deck exhaustion loses; hand limit is 9 and overflow cards enter the graveyard;
- followers, spells and amulets share a five-slot board; followers fight each other and attack
  leaders, using Ward, Storm, Bane, Rush, Barrier, Intimidate, Aura, Drain and Ward-ignoring;
- standard Worlds Beyond evolution: 2 EP and 2 SEP per player, ordinary evolution, super
  evolution, and the second player's two staged extra-PP uses;
- Fanfare, Evolution, Super-evolution, Last Words, discard, attack and end-of-turn effects,
  Fanfare mode choices, evolution effects that choose an enemy follower, Enhance and Accelerate
  costs, countdown amulets, timed leader effects, crests, banished followers that leave play
  without a graveyard, summons that have lost their Last Words, and 【唤灵】 costs paid from the
  graveyard;
- deck summons that take random cards out of the deck, continuous abilities that grant keywords to
  followers as they enter, and profession-filtered stat buffs;
- 【结晶】, the amulet-shaped sibling of 【激奏】: while the card's own cost cannot be paid it can be
  played as an amulet with its own Countdown and Last Words;
- 【模式】 cards may print their own evolution modes, next to cards that simply repeat their Fanfare
  modes; restoring evolution points stops at two and restoring play points stops at the maximum PP;
- cards can carry an end-of-turn cost reduction while they sit in hand (it stacks and stays), and
  passive auras can empower the owner's followers of a given trait as they enter;
- 【亡者召回】 summons a copy of a follower from the graveyard, preferring the highest cost up to the
  printed limit and leaving the graveyard untouched, and 【超进化时】 can hand the player a crest whose
  end-of-turn effect shatters a random own card with Last Words plus a random enemy follower;
- 【进化时】 / 【超进化时】 only fire for an evolution paid with an evolution point, while
  「本随从进化时」 fires for every evolution, including the ones an ability causes;
- Keyword changes such as 失去【守护】 / 获得【威慑】 are printed as part of a card's 【进化时】
  ability, so they follow that trigger: an evolution caused by an ability grants the stats but
  neither the keyword changes nor the 【进化时】 effects;
- one random baseline and three playable agents: the rule-based `GreedyPlayerAgent` and the
  `LookaheadPlayerAgent` / `LookaheadPlayerAgentV2` planners, which run rollouts on determinized
  copies of the game instead of reading hidden cards.

## Projects

- `Shadowverse.Engine`: deterministic game rules, legal-action generation, observations, and the
  agents. `CardCatalog` holds every card definition, `DeckCatalog` persists saved deck lists, and
  `MatchRunner` plays a match to the end.
- `Shadowverse.Console`: a command-line entry point for playing matches, batch statistics, the
  card and deck catalogs, and the self-tests.
- `Shadowverse.DeckEditor`: a Windows window for creating and editing saved decks, plus a replay
  viewer that steps through machine-generated matches.

## Open in Visual Studio

Open `ShadowversePrototype.sln`.

- To simulate matches, select `Shadowverse.Console` as the startup project, then run it.
- To edit decks, select `Shadowverse.DeckEditor` as the startup project, then run it. The editor
  has the deck name at the top, the current deck in the middle, and the card catalog at the
  bottom. In the current-deck table, edit the copy-count cell directly or use the `＋`/`－`
  buttons. It shows the 40-card progress, card-type totals, and mana curve. The card catalog
  supports name search and profession, rarity, type, cost, and keyword filters; click a result to
  add one copy. Saving requires exactly 40 cards.
- The editor's `对局回放` button opens the replay viewer: choose a deck and an agent for each
  side, generate 1–30 matches, then step through the action log with the arrow keys (space toggles
  auto-play). A single match, or a whole batch, can be exported as JSON.

## Console commands

Running the console with no arguments prints one full match report; everything else is an option.

| Option | Effect |
| --- | --- |
| *(no arguments)* | Full report of one match, with Player 1 as the rule-based agent and Player 2 as the random agent. |
| `--lookahead` | Gives Player 1 to a lookahead agent instead of the rule-based agent. |
| `--rollouts 1-500` | Rollouts per action for the lookahead agent (default 60). |
| `--horizon 1-10` | Turns simulated per rollout (default 3). |
| `--random` | Uses a fresh seed instead of the fixed, reproducible default seed. |
| `--deck1 ID` / `--deck2 ID` | Selects the saved deck for Player 1 / Player 2. Without either option both sides use the first saved deck. |
| `--stats [matches]` | Plays silent matches (default 1000) and reports win counts, average final health, average cards left in deck, first/second-player win rates, win methods, average game length, and per-agent behavioral statistics. |
| `--smoke-test` | Plays the same seeded match twice and verifies that the full action log is identical, and that a mulligan never redraws a card the player kept. |
| `--effect-test` | Runs every card-effect and agent self-test in the console. |
| `--cards` | Prints every catalog card with its ID, profession, rarity, type, cost, stats, and effect text. |
| `--decks` | Prints every saved deck and its card entries. |
| `--help`, `-h` | Prints the option list. |

## Current data

- The catalog holds 59 cards, `BASE-001` through `BASE-059`: 44 followers, 14 spells, and 1
  amulet. 45 are collectible; the other 14 are generated cards that stay in the catalog but cannot
  be built into a normal deck.
- Card IDs are never reused, and the console prints the ID the next card would receive
  (`BASE-060` today).
- A saved deck may hold fewer than 40 cards while it is being assembled, and the editor can save it;
  a match still requires a complete 40-card deck, and the console says so instead of starting one.
- `CardCatalog` validates itself the first time it is used: every definition, duplicate IDs, and
  every effect that names another card, a crest, or a trait. A bad entry fails immediately with
  the offending card ID instead of failing halfway through a match.
- Saved decks live in `data/deck-library.json`. It currently holds three complete 40-card decks
  (`DECK-001` 剑斗士卡组 / `DECK-002` 郭龙 / `DECK-003` 中速梦); `DECK-001` is banned from every
  match because it stacks ten copies of a single card, and the next new deck starts from the highest
  number in use.
- Snapshots taken before each maintenance pass live in `artifacts/maintenance/`.

## Adding a card

The catalog is `src/Shadowverse.Engine/Cards/CardCatalog.cs`. Add a constant to `CardIds`, then a
`CardDefinition` using the format `编号 - 卡牌名称 - 职业 - 稀有度 - 类型 - 费用 - 身材 - 效果`.
Professions are 精灵、皇家护卫、巫师、龙族、梦魇、主教、超越者、中立; rarities are 铜卡、银卡、金卡、虹卡.
Effects are structured `CardEffect` values rather than parsed display text, so behavior the engine
does not implement yet has to be added to `CardEffectKind` and `GameEngine` before a card can use
it. Run the console with `--effect-test` after adding or changing a card.
