using System.Globalization;
using Shadowverse.Engine.Agents;
using Shadowverse.Engine.Decks;
using Shadowverse.Engine.Game;
using Shadowverse.Engine.Models;
using Shadowverse.Engine.Simulation;

namespace Shadowverse.ConsoleApp;

/// <summary>
/// [DIR-9 第 4 条] V4 因果 CSV 的**跨进程重放**：按指纹定位到那条分歧行对应的真实决策点，
/// 重建局面、复算两个牌手的动作、并用 CSV 里记录的同一对续局种子重跑 K=2 交叉续局，
/// 逐项比对 CSV 记录。**证明 CSV 足以重建源对局并复现结果**。
/// </summary>
internal static class V4CausalReplay
{
    private const ulong FrozenSeatSalt = 7UL;

    public static bool Run(string csvPath, int requestedRows, ulong sourceSeedBase, ulong v4ProbeSeed, Action<string> report)
    {
        ArgumentNullException.ThrowIfNull(report);
        if (!File.Exists(csvPath))
        {
            throw new InvalidOperationException($"找不到 {csvPath}");
        }

        var lines = File.ReadAllLines(csvPath);
        var rows = new List<ReplayRow>();
        for (var index = 1; index < lines.Length; index++)
        {
            if (!string.IsNullOrWhiteSpace(lines[index]))
            {
                rows.Add(Parse(lines[index]));
            }
        }

        var disagreements = rows.Where(row => !row.Agreed).ToList();
        report($"CSV 行数 {rows.Count} ｜ 分歧行 {disagreements.Count} ｜ 目标重放条数 {requestedRows}");

        // 尽量"每种对局各一条"
        var picked = new List<ReplayRow>();
        foreach (var matchup in CausalContinuationProbe.Matchups)
        {
            var candidate = disagreements.FirstOrDefault(row => row.Matchup == matchup.Name && picked.All(existing => existing.Matchup != row.Matchup));
            if (candidate is not null)
            {
                picked.Add(candidate);
            }
        }

        foreach (var row in disagreements.Where(row => picked.All(existing => existing != row)))
        {
            if (picked.Count >= requestedRows)
            {
                break;
            }

            picked.Add(row);
        }

        var decks = new Dictionary<string, DeckDefinition>(StringComparer.Ordinal);
        DeckDefinition Deck(string id)
        {
            if (!decks.TryGetValue(id, out var deck))
            {
                deck = AgentSelfTests.CreateMatchDeck(id, "v4-causal-" + id);
                decks[id] = deck;
            }

            return deck;
        }

        var allPassed = true;
        var number = 0;
        foreach (var row in picked.Take(requestedRows))
        {
            number++;
            var matchup = CausalContinuationProbe.Matchups.First(item => item.Name == row.Matchup);
            var seatSeeds = new[] { row.SourceGameSeed, Mix(row.SourceGameSeed, FrozenSeatSalt) };
            var real2 = new[]
            {
                new LookaheadPlayerAgentV2(seatSeeds[0], CausalContinuationProbe.FrozenV2Rollouts),
                new LookaheadPlayerAgentV2(seatSeeds[1], CausalContinuationProbe.FrozenV2Rollouts)
            };
            var shadowV4 = new[]
            {
                new InformationSetTurnMctsAgentV4(Mix(v4ProbeSeed, seatSeeds[0]), V4CausalProbe.V4Iterations, V4CausalProbe.V4MaxDepth, V4CausalProbe.V4Exploration, V4CausalProbe.V4LeafHorizonTurns, V4CausalProbe.V4Widening),
                new InformationSetTurnMctsAgentV4(Mix(v4ProbeSeed, seatSeeds[1]), V4CausalProbe.V4Iterations, V4CausalProbe.V4MaxDepth, V4CausalProbe.V4Exploration, V4CausalProbe.V4LeafHorizonTurns, V4CausalProbe.V4Widening)
            };

            var ordinals = new[] { 0, 0 };
            GameState? located = null;
            GameAction locatedV2 = null!;
            GameAction locatedV4 = null!;

            MatchRunner.PlayToEnd(
                GameEngine.CreateGame(Deck(matchup.Deck1), Deck(matchup.Deck2), row.SourceGameSeed),
                real2[0],
                real2[1],
                onStep: step =>
                {
                    var seat = step.ActingPlayer;
                    var ordinal = ordinals[seat]++;
                    var legal = GameEngine.GetLegalActions(step.BeforeState);
                    var observation = GameEngine.ToObservation(step.BeforeState, seat);
                    var v4Action = shadowV4[seat].ChooseAction(step.BeforeState, observation, legal);
                    if (seat == row.Seat && ordinal == row.SeatDecisionOrdinal)
                    {
                        located = step.BeforeState;
                        locatedV2 = step.Action;
                        locatedV4 = v4Action;
                    }
                });

            if (located is null)
            {
                report($"  重放#{number} 未能定位决策点（{row.Matchup} 种子 {row.SourceGameSeed} 座位 {row.Seat} 序 {row.SeatDecisionOrdinal}）");
                allPassed = false;
                continue;
            }

            var fingerprintOk = string.Equals(GameEngine.StateFingerprint(located), row.Fingerprint, StringComparison.OrdinalIgnoreCase);
            var v2Ok = string.Equals(InformationSetTurnMctsAgentV4.ActionKey(locatedV2), row.V2FinalAction, StringComparison.Ordinal);
            var v4Ok = string.Equals(InformationSetTurnMctsAgentV4.ActionKey(locatedV4), row.V4FinalAction, StringComparison.Ordinal);

            var a2 = RunBranch(located, locatedV2, row.S1Seed!.Value).Winner == row.Seat ? 1 : 0;
            var a3 = RunBranch(located, locatedV4, row.S1Seed.Value).Winner == row.Seat ? 1 : 0;
            var b2 = RunBranch(located, locatedV2, row.S2Seed!.Value).Winner == row.Seat ? 1 : 0;
            var b3 = RunBranch(located, locatedV4, row.S2Seed.Value).Winner == row.Seat ? 1 : 0;
            var resultsOk = a2 == row.S1Result2 && a3 == row.S1Result3 && b2 == row.S2Result2 && b3 == row.S2Result3;

            var ok = fingerprintOk && v2Ok && v4Ok && resultsOk;
            allPassed &= ok;
            report($"  重放#{number} {row.Matchup} 种子 {row.SourceGameSeed} 座位 {row.Seat} 序 {row.SeatDecisionOrdinal}"
                + $" ｜ 指纹={fingerprintOk} v2动作={v2Ok} v4动作={v4Ok} K=2结果={resultsOk} ｜ 判定通过={ok}");
            report($"    CSV: v2={row.V2FinalAction} v4={row.V4FinalAction} ｜ 重放: v2={InformationSetTurnMctsAgentV4.ActionKey(locatedV2)} v4={InformationSetTurnMctsAgentV4.ActionKey(locatedV4)}");
            report($"    CSV 结果 s1=({row.S1Result2},{row.S1Result3}) s2=({row.S2Result2},{row.S2Result3}) ｜ 重放 s1=({a2},{a3}) s2=({b2},{b3})");
        }

        report($"重放合计 {number} 条 ｜ 全部通过 = {allPassed}");
        return allPassed;
    }

    private static (int Winner, int Actions) RunBranch(GameState decisionState, GameAction forced, ulong continuationSeed)
    {
        var after = GameEngine.Apply(decisionState, forced);
        var first = new LookaheadPlayerAgentV2(continuationSeed, CausalContinuationProbe.FrozenV2Rollouts);
        // [RES-14 第 2 条] 两个座位必须使用**不同**的随机流：正式实验是 seed 与 Mix(seed, FrozenSeatSalt)。
        var second = new LookaheadPlayerAgentV2(Mix(continuationSeed, FrozenSeatSalt), CausalContinuationProbe.FrozenV2Rollouts);
        var result = MatchRunner.PlayToEnd(after, first, second);
        return (result.Winner, result.ActionCount);
    }

    private sealed record ReplayRow(
        string Matchup,
        ulong SourceGameSeed,
        int Seat,
        int SeatDecisionOrdinal,
        string Fingerprint,
        string V2FinalAction,
        string V4FinalAction,
        bool Agreed,
        ulong? S1Seed,
        ulong? S2Seed,
        int S1Result2,
        int S1Result3,
        int S2Result2,
        int S2Result3);

    private static ReplayRow Parse(string line)
    {
        var fields = SplitCsv(line);
        return new ReplayRow(
            fields[0],
            ulong.Parse(fields[1], CultureInfo.InvariantCulture),
            int.Parse(fields[2], CultureInfo.InvariantCulture),
            int.Parse(fields[5], CultureInfo.InvariantCulture),
            fields[8],
            fields[9],
            fields[10],
            fields[11] == "1",
            ParseNullableUlong(fields[12]),
            ParseNullableUlong(fields[13]),
            ParseInt(fields[14]),
            ParseInt(fields[15]),
            ParseInt(fields[17]),
            ParseInt(fields[18]));
    }

    private static ulong? ParseNullableUlong(string text) =>
        string.IsNullOrEmpty(text) ? null : ulong.Parse(text, CultureInfo.InvariantCulture);

    private static int ParseInt(string text) =>
        string.IsNullOrEmpty(text) ? 0 : int.Parse(text, CultureInfo.InvariantCulture);

    private static List<string> SplitCsv(string line)
    {
        var fields = new List<string>();
        var current = new System.Text.StringBuilder();
        var quoted = false;
        for (var index = 0; index < line.Length; index++)
        {
            var ch = line[index];
            if (quoted)
            {
                if (ch == '"')
                {
                    if (index + 1 < line.Length && line[index + 1] == '"')
                    {
                        current.Append('"');
                        index++;
                    }
                    else
                    {
                        quoted = false;
                    }
                }
                else
                {
                    current.Append(ch);
                }
            }
            else if (ch == '"')
            {
                quoted = true;
            }
            else if (ch == ',')
            {
                fields.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(ch);
            }
        }

        fields.Add(current.ToString());
        return fields;
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
