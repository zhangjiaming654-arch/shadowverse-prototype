using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Shadowverse.Engine.Agents;
using Shadowverse.Engine.Decks;
using Shadowverse.Engine.Game;
using Shadowverse.Engine.Models;
using Shadowverse.Engine.Simulation;

namespace Shadowverse.ConsoleApp;

/// <summary>
/// [DIR-9] **V4 因果测量路径**：与 3.0 的因果实验（<see cref="CausalContinuationProbe"/>）结构相同，
/// 但影子牌手换成 4.0 原型；产物文件名独立（<c>v4-causal*</c>），**不覆盖** 3.0 的任何产物。
///
/// <para><b>协议</b>：源对局 = 冻结 2.0 对冻结 2.0；两个座位的**每个决策点**都问三者
/// （实战 2.0 / 影子 2.0 / 影子 V4），包括换牌与唯一合法动作；影子 2.0 必须与实战 2.0 逐动作 100% 同步；
/// 收集全部真实分歧（<c>v2FinalAction != v4Action</c>），每个分歧做 K=2 完整交叉续局，
/// 两条分支后续都交给冻结 2.0，且**同一局面的两个动作共享同一对续局种子**。</para>
///
/// <para><b>采样器知识</b>：影子 V4 使用 <see cref="InformationSetTurnMctsAgentV4.DeterminizationKnowledge"/>
/// （legacy_true_hidden_pool）。与冻结 2.0 使用同一引擎接口，所以本测量只做"同信息条件下的架构比较"。</para>
/// </summary>
public static class V4CausalProbe
{
    public const string DeterminizationKnowledge = InformationSetTurnMctsAgentV4.DeterminizationKnowledge;

    /// <summary>V4 候选配置（[RES-13] 冻结）。</summary>
    public const int V4Iterations = 256;
    public const int V4MaxDepth = 8;
    public const double V4Exploration = 0.7;
    public const double V4Widening = 1.0;
    public const int V4LeafHorizonTurns = 3;

    private const ulong FrozenSeatSalt = 7UL;
    private const int K = 2;

    public sealed record Config(
        int GamesPerMatchup,
        ulong SourceSeedBase,
        ulong V4ProbeSeed,
        IReadOnlyList<ulong> ContinuationSeeds,
        string OutputDirectory,
        bool Resume);

    /// <summary>CSV 一行的字段（足以重建源对局、恢复 V4 决策序号、重放动作与 K=2 结果）。</summary>
    public sealed record Row(
        string Matchup,
        ulong SourceGameSeed,
        int Seat,
        int TurnNumber,
        string Phase,
        int SeatDecisionOrdinal,
        bool Eligible,
        int LegalCount,
        string StateFingerprint,
        string V2FinalAction,
        string V4FinalAction,
        bool Agreed,
        ulong? S1Seed,
        ulong? S2Seed,
        int? S1Result2,
        int? S1Result3,
        double? S1Delta,
        int? S2Result2,
        int? S2Result3,
        double? S2Delta,
        int V4RootVisits,
        int V4MaxDepthReached)
    {
        public const string Header =
            "matchup,sourceGameSeed,seat,turnNumber,phase,seatDecisionOrdinal,eligible,legalCount,stateFingerprint,"
            + "v2FinalAction,v4FinalAction,agreed,s1_seed,s2_seed,s1_result2,s1_result3,s1_delta,s2_result2,s2_result3,s2_delta,"
            + "v4RootVisits,v4MaxDepthReached";

        public double Delta => S1Delta is null || S2Delta is null ? 0.0 : (S1Delta.Value + S2Delta.Value) / 2.0;

        public string ToCsv() => string.Join(',',
            Quote(Matchup),
            SourceGameSeed.ToString(CultureInfo.InvariantCulture),
            Seat.ToString(CultureInfo.InvariantCulture),
            TurnNumber.ToString(CultureInfo.InvariantCulture),
            Quote(Phase),
            SeatDecisionOrdinal.ToString(CultureInfo.InvariantCulture),
            Eligible ? "1" : "0",
            LegalCount.ToString(CultureInfo.InvariantCulture),
            Quote(StateFingerprint),
            Quote(V2FinalAction),
            Quote(V4FinalAction),
            Agreed ? "1" : "0",
            S1Seed?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            S2Seed?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            S1Result2?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            S1Result3?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            S1Delta?.ToString("0.####", CultureInfo.InvariantCulture) ?? string.Empty,
            S2Result2?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            S2Result3?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            S2Delta?.ToString("0.####", CultureInfo.InvariantCulture) ?? string.Empty,
            V4RootVisits.ToString(CultureInfo.InvariantCulture),
            V4MaxDepthReached.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>跑正式测量（逐源对局落盘 + 检查点 + 严格续跑）。</summary>
    public static int RunFormal(Config config, Action<string> report)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(report);

        if (config.ContinuationSeeds.Count != K)
        {
            throw new InvalidOperationException($"V4 因果路径要求恰好 {K} 个续局种子，当前 {config.ContinuationSeeds.Count} 个。");
        }

        var partialPath = Path.Combine(config.OutputDirectory, "v4-causal.partial.csv");
        var finalPath = Path.Combine(config.OutputDirectory, "v4-causal.csv");
        var checkpointPath = Path.Combine(config.OutputDirectory, "v4-causal.checkpoint.json");
        var manifestPath = Path.Combine(config.OutputDirectory, "v4-causal-manifest.json");
        var configHash = ConfigHash(config);
        var consoleHash = Sha256(typeof(V4CausalProbe).Assembly.Location);
        var engineHash = Sha256(typeof(InformationSetTurnMctsAgentV4).Assembly.Location);

        var rows = new List<Row>();
        var completedSeeds = new HashSet<ulong>();

        if (!config.Resume)
        {
            File.WriteAllText(partialPath, Row.Header + Environment.NewLine);
        }
        else
        {
            if (!File.Exists(checkpointPath) || !File.Exists(partialPath))
            {
                throw new InvalidOperationException("断点续跑被拒绝：找不到检查点或 partial CSV。");
            }

            var checkpoint = File.ReadAllText(checkpointPath);
            foreach (var (label, expected) in new[]
                     {
                         ("configHash", configHash),
                         ("consoleAssemblySha256", consoleHash),
                         ("engineAssemblySha256", engineHash)
                     })
            {
                if (!checkpoint.Contains($"\"{label}\": \"{expected}\"", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException($"断点续跑被拒绝：{label} 与检查点不一致（配置或代码已变）。");
                }
            }

            var loaded = LoadRows(partialPath, out var groups);
            // [DIR-9 第 3 条] 尾部残缺组整组重跑；非尾部损坏一律拒绝。
            var lastKey = groups.Count == 0 ? null : groups[^1].Key.Key;
            foreach (var group in groups)
            {
                var complete = group.Rows.Count > 0
                    && group.Rows.All(row => row.S1Delta is not null || row.Agreed);
                if (complete)
                {
                    rows.AddRange(group.Rows);
                    completedSeeds.Add(group.Key.Seed);
                }
                else if (!string.Equals(group.Key.Key, lastKey, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"断点续跑被拒绝：非尾部源对局损坏（{group.Key.Matchup} / {group.Key.Seed}）。");
                }
            }

            var kept = rows.Select(row => row.ToCsv()).ToList();
            var temporary = partialPath + ".tmp";
            File.WriteAllText(temporary, Row.Header + Environment.NewLine + string.Join(Environment.NewLine, kept)
                + (kept.Count > 0 ? Environment.NewLine : string.Empty));
            File.Move(temporary, partialPath, overwrite: true);
            report($"断点续跑：校验通过，载入 {rows.Count} 行 / {completedSeeds.Count} 个已完成源对局");
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

        var totalRows = rows.Count;
        var syncChecks = 0;
        var syncMismatch = 0;

        for (var roundIndex = 0; roundIndex < config.GamesPerMatchup; roundIndex++)
        {
            for (var matchupIndex = 0; matchupIndex < CausalContinuationProbe.Matchups.Count; matchupIndex++)
            {
                var matchup = CausalContinuationProbe.Matchups[matchupIndex];
                var sourceSeed = Mix(config.SourceSeedBase, (ulong)((matchupIndex * 1_000_003) + roundIndex));
                if (completedSeeds.Contains(sourceSeed))
                {
                    continue;
                }

                var seatSeeds = new[] { sourceSeed, Mix(sourceSeed, FrozenSeatSalt) };
                var real2 = new[]
                {
                    new LookaheadPlayerAgentV2(seatSeeds[0], CausalContinuationProbe.FrozenV2Rollouts),
                    new LookaheadPlayerAgentV2(seatSeeds[1], CausalContinuationProbe.FrozenV2Rollouts)
                };
                var shadow2 = new[]
                {
                    new LookaheadPlayerAgentV2(seatSeeds[0], CausalContinuationProbe.FrozenV2Rollouts),
                    new LookaheadPlayerAgentV2(seatSeeds[1], CausalContinuationProbe.FrozenV2Rollouts)
                };
                var shadowV4 = new[]
                {
                    new InformationSetTurnMctsAgentV4(Mix(config.V4ProbeSeed, seatSeeds[0]), V4Iterations, V4MaxDepth, V4Exploration, V4LeafHorizonTurns, V4Widening),
                    new InformationSetTurnMctsAgentV4(Mix(config.V4ProbeSeed, seatSeeds[1]), V4Iterations, V4MaxDepth, V4Exploration, V4LeafHorizonTurns, V4Widening)
                };

                var gameRows = new List<Row>();
                var ordinals = new[] { 0, 0 };
                var aborted = false;

                var result = MatchRunner.PlayToEnd(
                    GameEngine.CreateGame(Deck(matchup.Deck1), Deck(matchup.Deck2), sourceSeed),
                    real2[0],
                    real2[1],
                    onStep: step =>
                    {
                        if (aborted)
                        {
                            return;
                        }

                        var seat = step.ActingPlayer;
                        var state = step.BeforeState;
                        var observation = GameEngine.ToObservation(state, seat);
                        var legal = GameEngine.GetLegalActions(state);
                        var ordinal = ordinals[seat]++;

                        // 影子 2.0 必须与实战 2.0 逐动作 100% 同步（任何一步不一致即整批作废）
                        var shadowAction = shadow2[seat].ChooseAction(state, observation, legal);
                        syncChecks++;
                        if (!string.Equals(Canonical(shadowAction), Canonical(step.Action), StringComparison.Ordinal))
                        {
                            syncMismatch++;
                            aborted = true;
                            return;
                        }

                        // 影子 V4：每个决策点都问（含换牌与唯一合法动作），走生产路径
                        var v4Action = shadowV4[seat].ChooseAction(state, observation, legal);
                        var v4Stats = shadowV4[seat].LastStats;

                        var v2Key = Canonical(step.Action);
                        var v4Key = Canonical(v4Action);
                        var agreed = string.Equals(v2Key, v4Key, StringComparison.Ordinal);

                        ulong? seedA = null, seedB = null;
                        int? a2 = null, a3 = null, b2 = null, b3 = null;
                        double? dA = null, dB = null;
                        if (!agreed)
                        {
                            // 同一局面的两个动作**共享同一对续局种子**；种子按局面派生，不同局面不复用。
                            seedA = DeriveContinuationSeed(config.ContinuationSeeds[0], sourceSeed, seat, ordinal, 0);
                            seedB = DeriveContinuationSeed(config.ContinuationSeeds[1], sourceSeed, seat, ordinal, 1);
                            var w2A = RunBranch(state, step.Action, seedA.Value).Winner;
                            var w3A = RunBranch(state, v4Action, seedA.Value).Winner;
                            var w2B = RunBranch(state, step.Action, seedB.Value).Winner;
                            var w3B = RunBranch(state, v4Action, seedB.Value).Winner;
                            a2 = w2A == seat ? 1 : 0;
                            a3 = w3A == seat ? 1 : 0;
                            b2 = w2B == seat ? 1 : 0;
                            b3 = w3B == seat ? 1 : 0;
                            dA = a3 - a2;
                            dB = b3 - b2;
                        }

                        gameRows.Add(new Row(
                            matchup.Name, sourceSeed, seat, state.TurnNumber, state.Phase.ToString(), ordinal,
                            legal.Count >= 2, legal.Count,
                            GameEngine.StateFingerprint(state), v2Key, v4Key, agreed,
                            seedA, seedB, a2, a3, dA, b2, b3, dB,
                            v4Stats?.RootVisits ?? 0, v4Stats?.MaxDepth ?? 0));
                    });

                if (aborted)
                {
                    throw new InvalidOperationException($"影子同步失败 {syncMismatch}/{syncChecks}：本批作废。");
                }

                rows.AddRange(gameRows);
                completedSeeds.Add(sourceSeed);

                var lines = gameRows.Select(row => row.ToCsv()).ToList();
                if (lines.Count > 0)
                {
                    File.AppendAllLines(partialPath, lines);
                }

                WriteCheckpoint(checkpointPath, configHash, consoleHash, engineHash, completedSeeds, rows.Count);
                report($"  {matchup.Name} 源种子 {sourceSeed}：决策 {gameRows.Count}，"
                    + $"分歧 {gameRows.Count(row => !row.Agreed)}，胜者 {result.Winner}");
                totalRows += gameRows.Count;
            }
        }

        // 收尾：写最终 CSV + manifest + 汇总
        var allLines = new List<string> { Row.Header };
        allLines.AddRange(rows.Select(row => row.ToCsv()));
        File.WriteAllLines(finalPath, allLines);

        var t = rows.Count;
        var m = 0;
        var d = 0;
        var sum = 0.0;
        foreach (var row in rows)
        {
            if (!row.Agreed)
            {
                d++;
                sum += row.Delta;
            }
        }

        m = rows.Count(row => row.Eligible);

        var deltaCall = t == 0 ? double.NaN : sum / t;
        var deltaEligible = m == 0 ? double.NaN : sum / m;
        var deltaCond = d == 0 ? double.NaN : sum / d;

        WriteManifest(manifestPath, config, configHash, consoleHash, engineHash, t, m, d);
        report($"V4 影子同步 {syncChecks - syncMismatch}/{syncChecks}");
        report($"T={t}（全部决策）  M={m}（合格，含≥2 合法动作）  D={d}（分歧）");
        report($"Δ_call={deltaCall:+0.0000;-0.0000;0.0000}  Δ_eligible={deltaEligible:+0.0000;-0.0000;0.0000}  "
            + $"Δ_cond={deltaCond:+0.0000;-0.0000;0.0000}");
        report($"CSV：{finalPath}");
        return syncMismatch > 0 ? 1 : 0;
    }

    /// <summary>合格决策判定：只有"该决策点有 ≥2 个合法动作"才算合格。
    /// CSV 里没有直接列 eligible，所以按"是否可能分歧"近似——这里用 δ 是否被计算来判断不够；
    /// 因此 M 的精确值由采集脚本从 CSV 的 v4RootVisits 之外的字段重算，见 [REQ-14] 的独立复算。</summary>
    // （已废弃）此前用 CanonicalEligible 近似合格性，导致 M==T；现在 CSV 直接记录 eligible。

    // ------------------------------------------------------------------ 续局

    private static (int Winner, int Actions) RunBranch(GameState decisionState, GameAction forced, ulong continuationSeed)
    {
        var after = GameEngine.Apply(decisionState, forced);
        var first = new LookaheadPlayerAgentV2(continuationSeed, CausalContinuationProbe.FrozenV2Rollouts);
        // [RES-14 第 2 条] 两个座位必须使用**不同**的随机流：正式实验是 seed 与 Mix(seed, FrozenSeatSalt)。
        var second = new LookaheadPlayerAgentV2(Mix(continuationSeed, FrozenSeatSalt), CausalContinuationProbe.FrozenV2Rollouts);
        var result = MatchRunner.PlayToEnd(after, first, second);
        return (result.Winner, result.ActionCount);
    }

    // ------------------------------------------------------------------ 落盘 / 续跑 / 身份

    private sealed record GroupKey(string Matchup, ulong Seed)
    {
        public string Key => Matchup + "|" + Seed.ToString(CultureInfo.InvariantCulture);
    }

    private static List<Row> LoadRows(string path, out List<(GroupKey Key, List<Row> Rows)> groups)
    {
        var lines = File.ReadAllLines(path);
        var rows = new List<Row>();
        for (var index = 1; index < lines.Length; index++)
        {
            if (string.IsNullOrWhiteSpace(lines[index]))
            {
                continue;
            }

            rows.Add(ParseRow(lines[index]));
        }

        groups = rows
            .GroupBy(row => new GroupKey(row.Matchup, row.SourceGameSeed))
            .Select(group => (group.Key, group.ToList()))
            .ToList();
        return rows;
    }

    private static Row ParseRow(string line)
    {
        var fields = SplitCsv(line);
        if (fields.Count != 22)
        {
            throw new InvalidOperationException($"v4-causal CSV 字段数 {fields.Count}（期望 22）：{line[..Math.Min(80, line.Length)]}");
        }

        return new Row(
            fields[0], ulong.Parse(fields[1], CultureInfo.InvariantCulture), int.Parse(fields[2], CultureInfo.InvariantCulture),
            int.Parse(fields[3], CultureInfo.InvariantCulture), fields[4], int.Parse(fields[5], CultureInfo.InvariantCulture),
            fields[6] == "1", int.Parse(fields[7], CultureInfo.InvariantCulture),
            fields[8], fields[9], fields[10], fields[11] == "1",
            ParseNullableUlong(fields[12]), ParseNullableUlong(fields[13]),
            ParseNullableInt(fields[14]), ParseNullableInt(fields[15]), ParseNullableDouble(fields[16]),
            ParseNullableInt(fields[17]), ParseNullableInt(fields[18]), ParseNullableDouble(fields[19]),
            int.Parse(fields[20], CultureInfo.InvariantCulture), int.Parse(fields[21], CultureInfo.InvariantCulture));
    }

    private static ulong? ParseNullableUlong(string text) =>
        string.IsNullOrEmpty(text) ? null : ulong.Parse(text, CultureInfo.InvariantCulture);

    private static int? ParseNullableInt(string text) =>
        string.IsNullOrEmpty(text) ? null : int.Parse(text, CultureInfo.InvariantCulture);

    private static double? ParseNullableDouble(string text) =>
        string.IsNullOrEmpty(text) ? null : double.Parse(text, CultureInfo.InvariantCulture);

    private static List<string> SplitCsv(string line)
    {
        var fields = new List<string>();
        var current = new StringBuilder();
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

    private static void WriteCheckpoint(
        string path, string configHash, string consoleHash, string engineHash, IReadOnlySet<ulong> seeds, int rowCount)
    {
        var json = string.Join('\n',
            "{",
            $"  \"configHash\": \"{configHash}\",",
            $"  \"consoleAssemblySha256\": \"{consoleHash}\",",
            $"  \"engineAssemblySha256\": \"{engineHash}\",",
            $"  \"rowCount\": {rowCount},",
            $"  \"completedSourceGameCount\": {seeds.Count},",
            $"  \"completedSourceGameSeeds\": [{string.Join(", ", seeds.OrderBy(seed => seed))}]",
            "}");
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, json);
        File.Move(temporary, path, overwrite: true);
    }

    private static void WriteManifest(
        string path, Config config, string configHash, string consoleHash, string engineHash, int t, int m, int d)
    {
        var json = string.Join('\n',
            "{",
            $"  \"generatedAt\": \"{DateTime.Now:O}\",",
            $"  \"probe\": \"v4-causal-formal\",",
            $"  \"determinizationKnowledge\": \"{DeterminizationKnowledge}\",",
            $"  \"v4Config\": {{ \"iterations\": {V4Iterations}, \"maxDepth\": {V4MaxDepth}, \"exploration\": {V4Exploration}, \"widening\": {V4Widening}, \"leafHorizonTurns\": {V4LeafHorizonTurns} }},",
            $"  \"gamesPerMatchup\": {config.GamesPerMatchup},",
            $"  \"sourceSeedBase\": {config.SourceSeedBase},",
            $"  \"v4ProbeSeed\": {config.V4ProbeSeed},",
            $"  \"continuationSeeds\": [{string.Join(", ", config.ContinuationSeeds)}],",
            $"  \"K\": {K},",
            $"  \"T\": {t},",
            $"  \"M\": {m},",
            $"  \"D\": {d},",
            $"  \"configHash\": \"{configHash}\",",
            $"  \"consoleAssemblySha256\": \"{consoleHash}\",",
            $"  \"engineAssemblySha256\": \"{engineHash}\"",
            "}");
        File.WriteAllText(path, json);
    }

    private static string ConfigHash(Config config) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('|',
            "v4-causal-v1",
            config.GamesPerMatchup,
            config.SourceSeedBase,
            config.V4ProbeSeed,
            string.Join(',', config.ContinuationSeeds),
            V4Iterations,
            V4MaxDepth,
            V4Exploration,
            V4Widening,
            V4LeafHorizonTurns,
            DeterminizationKnowledge,
            string.Join(';', CausalContinuationProbe.Matchups.Select(matchup => $"{matchup.Name}:{matchup.Deck1}:{matchup.Deck2}"))))));

    private static string Sha256(string path) =>
        File.Exists(path) ? Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))) : "(缺失)";

    private static string Quote(string text) => '"' + text.Replace("\"", "\"\"", StringComparison.Ordinal) + '"';

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

    private static ulong DeriveContinuationSeed(ulong baseSeed, ulong sourceSeed, int seat, int ordinal, int branch) =>
        Mix(baseSeed, Mix(sourceSeed, (ulong)((seat * 1_000_003) + ordinal + (branch * 7_919))));

    /// <summary>动作的稳定规范串（与因果实验一致）。</summary>
    internal static string Canonical(GameAction action) => InformationSetTurnMctsAgentV4.ActionKey(action);
}
