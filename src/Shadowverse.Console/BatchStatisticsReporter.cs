using Shadowverse.Engine.Game;
using Shadowverse.Engine.Simulation;

namespace Shadowverse.ConsoleApp;

/// <summary>Aggregates many silent matches into a readable comparison between the two player agents.</summary>
public sealed class BatchStatisticsReporter
{
    private readonly SideStatistics[] _sides = [new(), new()];
    private int _matchCount;
    private int _firstPlayerWins;
    private int _leaderDefeatWins;
    private int _deckExhaustionWins;
    private long _totalTurns;
    private long _totalActions;

    public void Add(MatchResult match)
    {
        _matchCount++;
        _totalTurns += match.FinalState.TurnNumber;
        _totalActions += match.ActionCount;

        for (var player = 0; player < 2; player++)
        {
            var finalPlayerState = match.FinalState.Players[player];
            _sides[player].TotalFinalHealth += finalPlayerState.Health;
            _sides[player].TotalRemainingDeckCards += finalPlayerState.Deck.Count;

            if (player == match.FinalState.StartingPlayer)
            {
                _sides[player].StartedFirstCount++;
            }
            else
            {
                _sides[player].StartedSecondCount++;
            }
        }

        _sides[match.Winner].Wins++;
        if (match.Winner == match.FinalState.StartingPlayer)
        {
            _firstPlayerWins++;
            _sides[match.Winner].WinsWhenFirst++;
        }
        else
        {
            _sides[match.Winner].WinsWhenSecond++;
        }

        var loser = OtherPlayer(match.Winner);
        if (match.FinalState.Players[loser].Health <= 0)
        {
            _leaderDefeatWins++;
        }
        else
        {
            _deckExhaustionWins++;
        }

        foreach (var entry in match.Actions)
        {
            var side = _sides[entry.Player];
            side.TotalActions++;

            switch (entry.Action)
            {
                case PlayFollowerAction:
                    side.TotalPlays++;
                    break;
                case PlayAmuletAction:
                    side.TotalAmuletPlays++;
                    break;
                case PlaySpellAction:
                    side.TotalSpellCasts++;
                    break;
                case EvolveAction:
                    side.TotalEvolutions++;
                    break;
                case SuperEvolveAction:
                    side.TotalSuperEvolutions++;
                    break;
                case UseExtraPlayPointAction:
                    side.TotalExtraPlayPointUses++;
                    break;
                case AttackLeaderAction:
                    side.TotalLeaderAttacks++;
                    break;
                case AttackFollowerAction:
                    side.TotalFollowerTrades++;
                    break;
                case MulliganAction mulligan:
                    side.TotalReplacedCards += mulligan.ReplaceInstanceIds.Count;
                    break;
            }
        }
    }

    public void Print(string firstPlayerLabel, string secondPlayerLabel)
    {
        Console.WriteLine("========== 批量对局统计 ==========");
        Console.WriteLine($"总对局数：{_matchCount}");
        Console.WriteLine();
        PrintSide(firstPlayerLabel, _sides[0]);
        PrintSide(secondPlayerLabel, _sides[1]);

        Console.WriteLine();
        Console.WriteLine($"先手胜局：{_firstPlayerWins}（{Percent(_firstPlayerWins)}）");
        Console.WriteLine($"后手胜局：{_matchCount - _firstPlayerWins}（{Percent(_matchCount - _firstPlayerWins)}）");
        Console.WriteLine($"领袖生命归零结束：{_leaderDefeatWins}（{Percent(_leaderDefeatWins)}）");
        Console.WriteLine($"牌库耗尽结束：{_deckExhaustionWins}（{Percent(_deckExhaustionWins)}）");

        Console.WriteLine();
        Console.WriteLine($"平均回合数：{Average(_totalTurns):F2}");
        Console.WriteLine($"平均动作数：{Average(_totalActions):F2}");
        Console.WriteLine("说明：最终生命为原始数值，可能因斩杀伤害低于 0。\n");
    }

    private void PrintSide(string label, SideStatistics side)
    {
        Console.WriteLine($"{label}");
        Console.WriteLine($"  胜局：{side.Wins}（{Percent(side.Wins)}）");
        Console.WriteLine($"  平均最终生命：{Average(side.TotalFinalHealth):F2}");
        Console.WriteLine($"  平均剩余牌库：{Average(side.TotalRemainingDeckCards):F2}");
        Console.WriteLine($"  先手战绩：{side.WinsWhenFirst}/{side.StartedFirstCount}");
        Console.WriteLine($"  后手战绩：{side.WinsWhenSecond}/{side.StartedSecondCount}");
        Console.WriteLine($"  平均动作数：{Average(side.TotalActions):F2}");
        Console.WriteLine($"  平均出随从次数：{Average(side.TotalPlays):F2}");
        Console.WriteLine($"  平均出护符次数：{Average(side.TotalAmuletPlays):F2}");
        Console.WriteLine($"  平均使用法术次数：{Average(side.TotalSpellCasts):F2}");
        Console.WriteLine($"  平均普通进化次数：{Average(side.TotalEvolutions):F2}");
        Console.WriteLine($"  平均超进化次数：{Average(side.TotalSuperEvolutions):F2}");
        Console.WriteLine($"  平均额外 PP 使用次数：{Average(side.TotalExtraPlayPointUses):F2}");
        Console.WriteLine($"  平均攻击领袖次数：{Average(side.TotalLeaderAttacks):F2}");
        Console.WriteLine($"  平均随从交换次数：{Average(side.TotalFollowerTrades):F2}");
        Console.WriteLine($"  打脸占全部攻击：{AttackLeaderShare(side):F1}%");
        Console.WriteLine($"  平均起手换牌张数：{Average(side.TotalReplacedCards):F2}");
    }

    private double Average(long total) => _matchCount == 0 ? 0 : (double)total / _matchCount;

    private string Percent(int value) => _matchCount == 0
        ? "0.0%"
        : $"{100.0 * value / _matchCount:F1}%";

    private double AttackLeaderShare(SideStatistics side)
    {
        var allAttacks = side.TotalLeaderAttacks + side.TotalFollowerTrades;
        return allAttacks == 0 ? 0 : 100.0 * side.TotalLeaderAttacks / allAttacks;
    }

    private static int OtherPlayer(int player) => player == 0 ? 1 : 0;

    private sealed class SideStatistics
    {
        public int Wins { get; set; }
        public int StartedFirstCount { get; set; }
        public int StartedSecondCount { get; set; }
        public int WinsWhenFirst { get; set; }
        public int WinsWhenSecond { get; set; }
        public long TotalFinalHealth { get; set; }
        public long TotalRemainingDeckCards { get; set; }
        public long TotalActions { get; set; }
        public long TotalPlays { get; set; }
        public long TotalAmuletPlays { get; set; }
        public long TotalSpellCasts { get; set; }
        public long TotalEvolutions { get; set; }
        public long TotalSuperEvolutions { get; set; }
        public long TotalExtraPlayPointUses { get; set; }
        public long TotalLeaderAttacks { get; set; }
        public long TotalFollowerTrades { get; set; }
        public long TotalReplacedCards { get; set; }
    }
}
