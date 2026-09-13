using System.Globalization;

namespace Shadowverse.ConsoleApp;

/// <summary>
/// 诊断"搜索自己的估值"里到底有没有信息 —— 也就是它**能不能预测这一局的胜负**。
/// <para>
/// 为什么需要这个：一整条迭代回路（蒸馏搜索估值）的前提是"搜索的判断比叶子强"。
/// 但如果这个判断和最终胜负根本无关（AUC ≈ 0.5），那么
/// ① 拿它当标签去训叶子必然学不到东西，
/// ② 换任何 rollout 策略来"改善搜索估值"都是在提升一个不存在的信号。
/// 所以这是一个**极便宜的证伪**：不用跑任何对局，直接用已经采好的样本算。
/// </para>
/// <para>
/// 输入：<c>--collect-selfplay</c> 写出的主样本文件 + 逐行对齐的旁路文件
/// （主文件第一列是"这一局谁赢"，旁路文件是"搜索当时给自己的估值"）。
/// </para>
/// <para>
/// 判据：**AUC**（Mann-Whitney U 统计量）= 随便抽一个"赢的决策"和一个"输的决策"，
/// 前者搜索估值更高的概率。0.5 = 纯噪声，1.0 = 完美预测。
/// 注意样本内是相关的（一局里几十个决策共享同一个胜负），所以这**不是**一个独立样本的
/// 显著性检验，只能当"有没有信号"的量级判断。
/// </para>
/// </summary>
public static class SearchValueDiagnostics
{
    public sealed record Report(
        int Rows,
        int Wins,
        int Losses,
        /// <summary>搜索估值均值：赢的那些决策 vs 输的那些决策。</summary>
        double MeanValueWhenWin,
        double MeanValueWhenLoss,
        /// <summary>0.5 = 没有区分能力。</summary>
        double Auc,
        /// <summary>估值本身的均值与标准差（和 0.5 的差就是"乐观程度"）。</summary>
        double MeanValue,
        double StdValue,
        /// <summary>按搜索估值分十档，每档的实际胜率 —— 单调性看这个。</summary>
        IReadOnlyList<(double LowerBound, int Count, double WinRate)> Buckets,
        /// <summary>
        /// 暴力 AUC：随机抽若干"赢/输"配对，数"赢的那边估值更高"的比例。
        /// **和排序法完全独立**，专门用来交叉验证 <see cref="Auc"/>（两者不一致就说明有一边错了）。
        /// </summary>
        double BruteForceAuc);

    public static Report Analyze(string samplePath)
    {
        var sidecarPath = WeightTools.SearchValuePath(samplePath);
        if (!File.Exists(sidecarPath))
        {
            throw new InvalidOperationException(
                $"找不到搜索估值旁路文件 {sidecarPath}。" +
                "它由 --collect-selfplay 和样本一起写出；先采一份样本。");
        }

        // 旁路文件里空行 = 这一行没有搜索估值（规则牌手不搜索），丢掉。
        var values = new List<double>();
        foreach (var line in File.ReadLines(sidecarPath))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#'))
            {
                continue;
            }

            values.Add(double.Parse(trimmed, CultureInfo.InvariantCulture));
        }

        // 主文件第一列是最终胜负。
        var labels = new List<int>();
        foreach (var line in File.ReadLines(samplePath))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#'))
            {
                continue;
            }

            var comma = trimmed.IndexOf(',');
            if (comma <= 0)
            {
                continue;
            }

            labels.Add(int.Parse(trimmed[..comma], CultureInfo.InvariantCulture));
        }

        if (labels.Count != values.Count)
        {
            throw new InvalidOperationException(
                $"样本 {labels.Count} 行、搜索估值 {values.Count} 行，两个文件必须逐行对齐。");
        }

        if (labels.Count == 0)
        {
            throw new InvalidOperationException($"样本文件 {samplePath} 里没有可用数据。");
        }

        var winValues = new List<double>();
        var lossValues = new List<double>();
        var total = 0.0;
        var totalSquares = 0.0;
        for (var index = 0; index < labels.Count; index++)
        {
            total += values[index];
            totalSquares += values[index] * values[index];
            if (labels[index] == 1)
            {
                winValues.Add(values[index]);
            }
            else
            {
                lossValues.Add(values[index]);
            }
        }

        var mean = total / labels.Count;
        var std = Math.Sqrt(Math.Max(0.0, (totalSquares / labels.Count) - (mean * mean)));

        // 分十档看单调性 —— AUC 是单个数字，分档能看出"是不是只有高端有信息"。
        var buckets = new List<(double LowerBound, int Count, double WinRate)>();
        for (var bucket = 0; bucket < 10; bucket++)
        {
            var lower = bucket / 10.0;
            var upper = (bucket + 1) / 10.0;
            var count = 0;
            var wins = 0;
            for (var index = 0; index < labels.Count; index++)
            {
                var value = values[index];
                var inBucket = bucket == 9
                    ? value >= lower && value <= upper
                    : value >= lower && value < upper;
                if (!inBucket)
                {
                    continue;
                }

                count++;
                wins += labels[index];
            }

            buckets.Add((lower, count, count == 0 ? double.NaN : (double)wins / count));
        }

        return new Report(
            labels.Count,
            winValues.Count,
            lossValues.Count,
            Mean(winValues),
            Mean(lossValues),
            Auc(winValues, lossValues),
            mean,
            std,
            buckets,
            BruteForceAuc(winValues, lossValues));
    }

    /// <summary>
    /// 随机抽配对来估 AUC。故意用固定种子，保证可复现。
    /// 这是给 <see cref="Auc"/> 的独立交叉验证：一个是 O(n log n) 排序法，一个是 O(samples) 采样法，
    /// 两条路走到同一个数才敢信。
    /// </summary>
    private static double BruteForceAuc(List<double> winValues, List<double> lossValues)
    {
        if (winValues.Count == 0 || lossValues.Count == 0)
        {
            return double.NaN;
        }

        var random = new Random(20_260_915);
        const int samples = 400_000;
        var greater = 0;
        var ties = 0;
        for (var index = 0; index < samples; index++)
        {
            var winValue = winValues[random.Next(winValues.Count)];
            var lossValue = lossValues[random.Next(lossValues.Count)];
            if (winValue > lossValue)
            {
                greater++;
            }
            else if (winValue.Equals(lossValue))
            {
                ties++;
            }
        }

        return (greater + (0.5 * ties)) / samples;
    }

    /// <summary>赢的估值均值 vs 输的估值均值。</summary>
    private static double Mean(List<double> values)
    {
        if (values.Count == 0)
        {
            return double.NaN;
        }

        var total = 0.0;
        foreach (var value in values)
        {
            total += value;
        }

        return total / values.Count;
    }

    /// <summary>
    /// Mann-Whitney U / ROC AUC。
    /// <para>
    /// 平手按平均名次计（标准做法），否则大量相同的估值会把面积推歪。
    /// </para>
    /// <para>
    /// 之前这里有一版"先排序、再回到原序列上按 IsWin 累加"的写法，算出了 AUC = 1.35
    /// 这种**数学上不可能**的值（面积必须 ≤ 1），而且名次范围检查还是通过的 ——
    /// 说明那个写法里有个我没能一眼看出来的错。现在的写法把**胜方标记和名次放在一起排序**，
    /// 名次在排序过程中直接写进同一条记录，不再依赖"排序后回到原序列"这一步。
    /// </para>
    /// </summary>
    private static double Auc(List<double> winValues, List<double> lossValues)
    {
        if (winValues.Count == 0 || lossValues.Count == 0)
        {
            return double.NaN;
        }

        var combined = new List<(double Value, bool IsWin, double Rank)>(winValues.Count + lossValues.Count);
        foreach (var value in winValues)
        {
            combined.Add((value, true, 0.0));
        }

        foreach (var value in lossValues)
        {
            combined.Add((value, false, 0.0));
        }

        combined.Sort((left, right) => left.Value.CompareTo(right.Value));

        var position = 0;
        while (position < combined.Count)
        {
            var end = position;
            while (end + 1 < combined.Count && combined[end + 1].Value.Equals(combined[position].Value))
            {
                end++;
            }

            // 名次从 1 开始；同值取这一段名次的平均。
            var averageRank = ((position + 1) + (end + 1)) / 2.0;
            for (var index = position; index <= end; index++)
            {
                combined[index] = combined[index] with { Rank = averageRank };
            }

            position = end + 1;
        }

        var rankSum = 0.0;
        var rankTotal = 0.0;
        var winCount = 0;
        var rankMin = double.MaxValue;
        var rankMax = double.MinValue;
        foreach (var (_, isWin, rank) in combined)
        {
            rankTotal += rank;
            if (rank < rankMin)
            {
                rankMin = rank;
            }

            if (rank > rankMax)
            {
                rankMax = rank;
            }

            if (isWin)
            {
                winCount++;
                rankSum += rank;
            }
        }

        var wins = winValues.Count;
        var losses = lossValues.Count;

        // 恒等式核对：全部名次之和必须正好等于 n(n+1)/2。
        // 这是数学上的硬约束，不依赖我对 U 统计量公式的记忆 —— 所以它能独立地判定名次阶段对不对。
        var n = combined.Count;
        var expectedRankTotal = (double)n * (n + 1) / 2.0;

        var u = rankSum - (wins * (double)(wins + 1) / 2.0);
        var area = u / ((double)wins * losses);
        LastRankDiagnostics =
            $"名次法：n={n}（赢 {winCount} ｜ 输 {n - winCount}）" +
            $"｜ 名次范围 [{rankMin:F1}, {rankMax:F1}]" +
            $"｜ 名次总和 {rankTotal:F0} vs 恒等式要求 {expectedRankTotal:F0}" +
            $"（{(Math.Abs(rankTotal - expectedRankTotal) < 1.0 ? "一致 ✓" : "不一致 ✗ —— 名次阶段就是错的")}）" +
            $"｜ rankSum(赢)={rankSum:F0}｜ U={u:F0}｜ wins*losses={(double)wins * losses:F0}" +
            $"｜ 面积={area:F4}";
        return area;
    }

    /// <summary>最近一次 <see cref="Auc"/> 的中间量，用来在两种算法不一致时定位。</summary>
    public static string? LastRankDiagnostics { get; private set; }
}
