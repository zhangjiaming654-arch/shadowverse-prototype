using System.Globalization;
using Shadowverse.Engine.Agents;

namespace Shadowverse.ConsoleApp;

/// <summary>
/// 用 <c>--collect-selfplay</c> 采到的样本训练 <see cref="NeuralPositionEvaluator"/>。
/// <para>
/// <b>两种标签来源</b>（<see cref="ValueSource"/>）：
/// </para>
/// <list type="bullet">
/// <item><b>Outcome</b>（默认）：这一局最后谁赢了 —— 和线性拟合一样。第四轮的教训是
/// "预测胜负 ≠ 动作排序对不对"，这条路已经失败过 9 次。</item>
/// <item><b>Search</b>：搜索自己给这个局面的估值（旁路文件 <c>&lt;样本&gt;.search.csv</c>，
/// 与样本<b>逐行对齐</b>）。这是<b>蒸馏</b>：把搜索的前瞻结论压进一个不搜索的评估函数里，
/// 也就是迭代回路里"用搜索结果反过来改进牌手"那一步。搜索的估值是 0..1 的连续值，
/// 所以损失从交叉熵换成平方误差，评价指标从"准确率"换成"相关系数 / R²"。</item>
/// </list>
/// <para>
/// 归一化用逐特征 RMS、不做中心化（特征都是两侧之差，反对称性必须保住）。
/// 训练/验证按**行序 80/20 切分**，而行是按对局顺序写入的，所以等价于按对局切分。
/// </para>
/// </summary>
public static class NeuralTrainer
{
    /// <summary>监督信号从哪来。见类注释。</summary>
    public enum ValueSource
    {
        /// <summary>主文件第一列：这一局最终谁赢（0/1）。</summary>
        Outcome,

        /// <summary>旁路文件里的搜索估值（0..1 连续值）。逐行对齐，空行 = 这一行没有搜索估值。</summary>
        Search
    }

    public sealed record TrainResult(
        NeuralPositionEvaluator Network,
        ValueSource Source,
        int TrainRows,
        int ValidationRows,
        /// <summary>因为旁路文件那一行为空而被丢掉的样本数（<see cref="ValueSource.Outcome"/> 恒为 0）。</summary>
        int SkippedRows,
        /// <summary>交出的是第几轮的权重。验证损失会跳而训练损失一直在降，
        /// 所以返回的**不是**最后一轮，而是验证最好的那一轮。</summary>
        int BestEpoch,
        double TrainLogLoss,
        double TrainAccuracy,
        double ValidationLogLoss,
        double ValidationAccuracy,
        double HandTunedValidationLogLoss,
        double HandTunedValidationAccuracy,
        /// <summary>验证集上预测值的标准差。**诊断"网络是不是在输出常数"用**：
        /// 接近 0 说明它只学会了偏置项，训练信号没进到权重里。</summary>
        double ValidationPredictionStd,
        /// <summary>验证集的平方误差（<see cref="ValueSource.Search"/> 的"对数损失"对应物）。</summary>
        double ValidationSquaredError,
        /// <summary>验证集上"预测值 vs 老师估值"的皮尔逊相关系数。
        /// 这是蒸馏成没成的主要判据：接近 0 就是没学到，再低的 MSE 也只是在输出平均数。</summary>
        double ValidationCorrelation,
        /// <summary>验证集上老师估值的均值（对照用：把它当常数预测器的水平）。</summary>
        double TeacherMean,
        /// <summary>验证集上老师估值的标准差。</summary>
        double TeacherStd);

    public static TrainResult Train(
        string csvPath,
        int hiddenCount,
        int epochs,
        double learningRate,
        double l2,
        ulong seed,
        Action<string> report,
        ValueSource source = ValueSource.Outcome)
    {
        ArgumentNullException.ThrowIfNull(report);
        var (features, labels, searchTargets) = ReadSamples(csvPath, source);

        // Search 模式下没有估值的行必须丢掉，但**切分要在丢之前按完整行序算** ——
        // 否则"训练/验证按对局切分"这个性质会变（同一局的行会被拆到两侧）。
        var validationCount = Math.Max(1, labels.Count / 5);
        var trainCount = labels.Count - validationCount;
        var skipped = 0;

        List<double> trainTargets;
        List<double> validationTargets;
        List<double[]> trainFeatures;
        List<double[]> validationFeatures;
        List<int> trainLabels;
        List<int> validationLabels;

        if (source == ValueSource.Search)
        {
            // 按"保留与否"把特征/标签/估值对齐着筛出来。三份列表必须用**同一个条件**筛，
            // 否则特征和标签会错位 —— 那是这份代码最危险的失败模式。
            bool Keep(int index) => !double.IsNaN(searchTargets[index]);

            List<T> Filter<T>(int from, int count, List<T> values)
            {
                var kept = new List<T>(count);
                for (var index = from; index < from + count; index++)
                {
                    if (Keep(index))
                    {
                        kept.Add(values[index]);
                    }
                }

                return kept;
            }

            trainTargets = Filter(0, trainCount, searchTargets);
            validationTargets = Filter(trainCount, validationCount, searchTargets);
            trainFeatures = Filter(0, trainCount, features);
            validationFeatures = Filter(trainCount, validationCount, features);
            trainLabels = Filter(0, trainCount, labels);
            validationLabels = Filter(trainCount, validationCount, labels);
            skipped = labels.Count - trainTargets.Count - validationTargets.Count;

            if (trainFeatures.Count == 0 || validationFeatures.Count == 0)
            {
                throw new InvalidOperationException(
                    $"样本 {csvPath} 的旁路文件里没有可用的搜索估值（一个都没读到）。" +
                    $"旁路文件应是 {WeightTools.SearchValuePath(csvPath)}。");
            }
        }
        else
        {
            // Outcome 模式不看旁路文件：这里刻意保持"一行都不丢"，和改动前的行为逐位一致。
            trainFeatures = features.GetRange(0, trainCount);
            validationFeatures = features.GetRange(trainCount, validationCount);
            trainLabels = labels.GetRange(0, trainCount);
            validationLabels = labels.GetRange(trainCount, validationCount);
            trainTargets = new List<double>();
            validationTargets = new List<double>();
        }

        if (features.Count == 0 || trainFeatures.Count == 0 || validationFeatures.Count == 0)
        {
            throw new InvalidOperationException($"样本文件 {csvPath} 里没有可用数据。");
        }

        var inputCount = features[0].Length;
        report(source == ValueSource.Search
            ? $"载入 {features.Count} 行、{inputCount} 项特征；标签来自搜索估值（{WeightTools.SearchValuePath(csvPath)}），" +
              $"可用 {trainFeatures.Count + validationFeatures.Count} 行、丢弃 {skipped} 行。"
            : $"载入 {features.Count} 行、{inputCount} 项特征；网络 {inputCount}→{hiddenCount}→{hiddenCount}→1。");

        // 只用训练集算 RMS 尺度。
        var scales = new double[inputCount];
        for (var index = 0; index < inputCount; index++)
        {
            var sumSquares = 0.0;
            foreach (var row in trainFeatures)
            {
                sumSquares += row[index] * row[index];
            }

            var rms = Math.Sqrt(sumSquares / trainFeatures.Count);
            scales[index] = rms < 1e-9 ? 1.0 : rms;
        }

        var random = new Random(unchecked((int)seed));
        var net = RandomNetwork(inputCount, hiddenCount, scales, random);

        // 每层的速度缓冲（动量）
        var vW1 = new double[net.W1.Length];
        var vB1 = new double[net.B1.Length];
        var vW2 = new double[net.W2.Length];
        var vB2 = new double[net.B2.Length];
        var vW3 = new double[net.W3.Length];
        var vB3 = 0.0;
        const double Momentum = 0.9;

        // 注意这里用的是**筛过之后**的行数：trainFeatures/trainTargets 已经把没有搜索估值的行
        // 丢掉了，用切分前的 trainCount 会越界。
        var order = Enumerable.Range(0, trainFeatures.Count).ToArray();
        var h1 = new double[hiddenCount];
        var h2 = new double[hiddenCount];

        // 每一轮都算验证损失，并留下**验证最好的那一份快照**。
        // 实测：训练损失一路降到 0.51 而验证损失在 0.61~0.65 之间跳（过拟合已经开始），
        // 这时候返回"最后一轮"等于抽签 —— 返回最好的一轮才是唯一合理的选择。
        NeuralPositionEvaluator Snapshot() => new(
            inputCount,
            hiddenCount,
            (double[])net.Scales.Clone(),
            (double[])net.W1.Clone(),
            (double[])net.B1.Clone(),
            (double[])net.W2.Clone(),
            (double[])net.B2.Clone(),
            (double[])net.W3.Clone(),
            net.B3);

        var best = Snapshot();
        var bestValidationLoss = double.MaxValue;
        var bestEpoch = 0;

        for (var epoch = 0; epoch < epochs; epoch++)
        {
            // 每个 epoch 打乱一次
            for (var i = order.Length - 1; i > 0; i--)
            {
                var j = random.Next(i + 1);
                (order[i], order[j]) = (order[j], order[i]);
            }

            foreach (var row in order)
            {
                var x = trainFeatures[row];
                // 两种标签来源共用同一条反向传播，只有**输出层的误差项**不一样：
                //   Outcome = 0/1 + 交叉熵  → dL/dz = p − y
                //   Search  = 0..1 + 平方误差 → dL/dz = (p − y)·p·(1−p)
                // 平方误差那一项里多出来的 p·(1−p) 不能省：网络的输出层是 sigmoid，
                // 省掉它等于假装输出层是线性的，梯度会朝错误的方向走。
                var y = source == ValueSource.Search ? trainTargets[row] : trainLabels[row];

                // ---- 前向 ----
                for (var unit = 0; unit < hiddenCount; unit++)
                {
                    var sum = net.B1[unit];
                    var offset = unit * inputCount;
                    for (var index = 0; index < inputCount; index++)
                    {
                        sum += net.W1[offset + index] * NeuralPositionEvaluator.Normalize(x[index], scales[index]);
                    }

                    h1[unit] = sum > 0 ? sum : 0;
                }

                for (var unit = 0; unit < hiddenCount; unit++)
                {
                    var sum = net.B2[unit];
                    var offset = unit * hiddenCount;
                    for (var index = 0; index < hiddenCount; index++)
                    {
                        sum += net.W2[offset + index] * h1[index];
                    }

                    h2[unit] = sum > 0 ? sum : 0;
                }

                var output = net.B3;
                for (var index = 0; index < hiddenCount; index++)
                {
                    output += net.W3[index] * h2[index];
                }

                var prediction = 1.0 / (1.0 + Math.Exp(-output));

                // ---- 反向 ----
                // **先把所有层的梯度算完，再统一更新。**
                // 原来只注意到"dH2 必须在更新 W3 之前算"，却把 W2 更新排在了 dH1 之前 ——
                // 于是 dH1 用的是**已经更新过**的 W2，梯度不一致。同一种错，只是深了一层。
                // 先算完再更新，这类顺序问题就整体不存在了。
                var dOutput = source == ValueSource.Search
                    ? (prediction - y) * prediction * (1.0 - prediction)
                    : prediction - y;

                var dH2 = new double[hiddenCount];
                for (var index = 0; index < hiddenCount; index++)
                {
                    dH2[index] = h2[index] > 0 ? dOutput * net.W3[index] : 0.0;
                }

                var dH1 = new double[hiddenCount];
                for (var index = 0; index < hiddenCount; index++)
                {
                    if (h1[index] <= 0)
                    {
                        continue;
                    }

                    var sum = 0.0;
                    for (var unit = 0; unit < hiddenCount; unit++)
                    {
                        sum += net.W2[(unit * hiddenCount) + index] * dH2[unit];
                    }

                    dH1[index] = sum;
                }

                for (var index = 0; index < hiddenCount; index++)
                {
                    vW3[index] = (Momentum * vW3[index]) - (learningRate * (dOutput * h2[index]));
                    net.W3[index] += vW3[index];
                }

                vB3 = (Momentum * vB3) - (learningRate * dOutput);
                net.B3 += vB3;

                for (var unit = 0; unit < hiddenCount; unit++)
                {
                    var offset = unit * hiddenCount;
                    for (var index = 0; index < hiddenCount; index++)
                    {
                        vW2[offset + index] =
                            (Momentum * vW2[offset + index]) - (learningRate * (dH2[unit] * h1[index]));
                        net.W2[offset + index] += vW2[offset + index];
                    }

                    vB2[unit] = (Momentum * vB2[unit]) - (learningRate * dH2[unit]);
                    net.B2[unit] += vB2[unit];
                }

                for (var unit = 0; unit < hiddenCount; unit++)
                {
                    var offset = unit * inputCount;
                    for (var index = 0; index < inputCount; index++)
                    {
                        var gradient = (dH1[unit] * NeuralPositionEvaluator.Normalize(x[index], scales[index]))
                                       + (l2 * net.W1[offset + index]);
                        vW1[offset + index] = (Momentum * vW1[offset + index]) - (learningRate * gradient);
                        net.W1[offset + index] += vW1[offset + index];
                    }

                    vB1[unit] = (Momentum * vB1[unit]) - (learningRate * dH1[unit]);
                    net.B1[unit] += vB1[unit];
                }
            }

            var trainLoss = source == ValueSource.Search
                ? MeanSquaredError(net, trainFeatures, trainTargets)
                : LogLoss(net, trainFeatures, trainLabels);
            var validationLoss = source == ValueSource.Search
                ? MeanSquaredError(net, validationFeatures, validationTargets)
                : LogLoss(net, validationFeatures, validationLabels);
            if (validationLoss < bestValidationLoss)
            {
                bestValidationLoss = validationLoss;
                bestEpoch = epoch + 1;
                best = Snapshot();
            }

            if ((epoch + 1) % Math.Max(1, epochs / 5) == 0)
            {
                report(source == ValueSource.Search
                    ? $"  epoch {epoch + 1}/{epochs}：训练 MSE {trainLoss:F5} ｜ 验证 MSE {validationLoss:F5}"
                    : $"  epoch {epoch + 1}/{epochs}：训练 {trainLoss:F5} ｜ 验证 {validationLoss:F5}");
            }
        }

        // 交出去的是验证最好的那一份，不是最后一轮。
        net = best;
        report(source == ValueSource.Search
            ? $"  取第 {bestEpoch} 轮的权重（验证 MSE 最好 {bestValidationLoss:F5}）。"
            : $"  取第 {bestEpoch} 轮的权重（验证损失最好 {bestValidationLoss:F5}）。");

        // 手调线性评估作为对照：把网络和它放在同一把尺子上。
        // 注意 Search 模式下这个对照只是在给出"线性模型在同样输入上能把这批标签拟到多好"，
        // 不是最终判据 —— 判据是下面的相关系数。
        var handTunedWeights = LookaheadPlayerAgent.PositionWeights;
        var handTunedScale = LookaheadPlayerAgent.ScoreScale;
        return new TrainResult(
            net,
            source,
            trainFeatures.Count,
            validationFeatures.Count,
            skipped,
            bestEpoch,
            LogLoss(net, trainFeatures, trainLabels),
            Accuracy(net, trainFeatures, trainLabels),
            LogLoss(net, validationFeatures, validationLabels),
            Accuracy(net, validationFeatures, validationLabels),
            LinearLogLoss(handTunedWeights, handTunedScale, validationFeatures, validationLabels),
            LinearAccuracy(handTunedWeights, handTunedScale, validationFeatures, validationLabels),
            PredictionStd(net, validationFeatures),
            MeanSquaredError(net, validationFeatures, validationTargets),
            Correlation(net, validationFeatures, validationTargets),
            Mean(validationTargets),
            Std(validationTargets));
    }

    /// <summary>
    /// 搜索估值是 0..1 的连续值，所以用平方误差而不是交叉熵。
    /// <para>
    /// 空的 <paramref name="targets"/> 返回 NaN：<see cref="ValueSource.Outcome"/> 模式下
    /// 压根没有搜索估值，这时候"平方误差"没有定义 —— 不能拿它去索引一个空列表。
    /// </para>
    /// </summary>
    private static double MeanSquaredError(
        NeuralPositionEvaluator net,
        List<double[]> features,
        List<double> targets)
    {
        if (features.Count == 0 || targets.Count == 0)
        {
            return double.NaN;
        }

        var total = 0.0;
        for (var row = 0; row < features.Count; row++)
        {
            var difference = net.Evaluate(features[row]) - targets[row];
            total += difference * difference;
        }

        return total / features.Count;
    }

    /// <summary>
    /// 预测值与老师估值的皮尔逊相关系数。**蒸馏成没成看这个**：
    /// MSE 低但相关系数是 0，说明网络只是在输出这批数据的平均值，一个特征都没用上。
    /// </summary>
    private static double Correlation(
        NeuralPositionEvaluator net,
        List<double[]> features,
        List<double> targets)
    {
        if (features.Count < 2 || targets.Count == 0)
        {
            return double.NaN;
        }

        var predictions = new List<double>(features.Count);
        foreach (var row in features)
        {
            predictions.Add(net.Evaluate(row));
        }

        var predictionMean = Mean(predictions);
        var targetMean = Mean(targets);
        var covariance = 0.0;
        var predictionVariance = 0.0;
        var targetVariance = 0.0;
        for (var index = 0; index < predictions.Count; index++)
        {
            var predictionDelta = predictions[index] - predictionMean;
            var targetDelta = targets[index] - targetMean;
            covariance += predictionDelta * targetDelta;
            predictionVariance += predictionDelta * predictionDelta;
            targetVariance += targetDelta * targetDelta;
        }

        var denominator = Math.Sqrt(predictionVariance * targetVariance);
        return denominator < 1e-12 ? double.NaN : covariance / denominator;
    }

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

    private static double Std(List<double> values)
    {
        if (values.Count == 0)
        {
            return double.NaN;
        }

        var mean = Mean(values);
        var total = 0.0;
        foreach (var value in values)
        {
            var delta = value - mean;
            total += delta * delta;
        }

        return Math.Sqrt(total / values.Count);
    }

    private static NeuralPositionEvaluator RandomNetwork(
        int inputCount,
        int hiddenCount,
        double[] scales,
        Random random)
    {
        double[] RandomArray(int count, double limit)
        {
            var values = new double[count];
            for (var i = 0; i < count; i++)
            {
                values[i] = ((random.NextDouble() * 2.0) - 1.0) * limit;
            }

            return values;
        }

        // He 初始化：为 N(0,1) 输入设计。这和 NeuralPositionEvaluator.MaxNormalizedInput = 3
        // 是配套的 —— 如果那边把输入夹到 ±8，这里就必须缩小 8 倍，否则预激活爆炸成 NaN。
        return new NeuralPositionEvaluator(
            inputCount,
            hiddenCount,
            scales,
            RandomArray(hiddenCount * inputCount, 1.0 / Math.Sqrt(inputCount)),
            Enumerable.Repeat(0.05, hiddenCount).ToArray(),
            RandomArray(hiddenCount * hiddenCount, 1.0 / Math.Sqrt(hiddenCount)),
            Enumerable.Repeat(0.05, hiddenCount).ToArray(),
            RandomArray(hiddenCount, 1.0 / Math.Sqrt(hiddenCount)),
            0.0);
    }

    private static double PredictionStd(NeuralPositionEvaluator net, List<double[]> features)
    {
        var total = 0.0;
        var totalSquares = 0.0;
        foreach (var row in features)
        {
            var value = net.Evaluate(row);
            total += value;
            totalSquares += value * value;
        }

        var mean = total / features.Count;
        return Math.Sqrt(Math.Max(0.0, (totalSquares / features.Count) - (mean * mean)));
    }

    private static double LogLoss(
        NeuralPositionEvaluator net,
        List<double[]> features,
        List<int> labels)
    {
        var total = 0.0;
        for (var row = 0; row < features.Count; row++)
        {
            var predicted = Math.Clamp(net.Evaluate(features[row]), 1e-12, 1.0 - 1e-12);
            total -= (labels[row] * Math.Log(predicted)) + ((1 - labels[row]) * Math.Log(1.0 - predicted));
        }

        return total / features.Count;
    }

    private static double Accuracy(
        NeuralPositionEvaluator net,
        List<double[]> features,
        List<int> labels)
    {
        var correct = 0;
        for (var row = 0; row < features.Count; row++)
        {
            if ((net.Evaluate(features[row]) >= 0.5 ? 1 : 0) == labels[row])
            {
                correct++;
            }
        }

        return (double)correct / features.Count;
    }

    private static double LinearLogLoss(
        double[] weights,
        double scale,
        List<double[]> features,
        List<int> labels)
    {
        var total = 0.0;
        for (var row = 0; row < features.Count; row++)
        {
            var predicted = Math.Clamp(Linear(weights, scale, features[row]), 1e-12, 1.0 - 1e-12);
            total -= (labels[row] * Math.Log(predicted)) + ((1 - labels[row]) * Math.Log(1.0 - predicted));
        }

        return total / features.Count;
    }

    private static double LinearAccuracy(
        double[] weights,
        double scale,
        List<double[]> features,
        List<int> labels)
    {
        var correct = 0;
        for (var row = 0; row < features.Count; row++)
        {
            if ((Linear(weights, scale, features[row]) >= 0.5 ? 1 : 0) == labels[row])
            {
                correct++;
            }
        }

        return (double)correct / features.Count;
    }

    private static double Linear(double[] weights, double scale, double[] features)
    {
        // 特征数可能**多于**权重数：例如数据里多了一列"对局上下文"（对手是哪套牌）。
        // 手调线性值在这里只是给拟合结果当参照物，让它因为多了一列就崩掉没有意义。
        // 所以按**公共长度**算 —— 多出来的列，手调权重本来就没有对应系数。
        var count = Math.Min(weights.Length, features.Length);
        var sum = 0.0;
        for (var index = 0; index < count; index++)
        {
            sum += features[index] * weights[index];
        }

        return 1.0 / (1.0 + Math.Exp(-sum / scale));
    }

    /// <summary>
    /// 读主样本文件；<see cref="ValueSource.Search"/> 时再读一份**逐行对齐**的旁路文件，
    /// 取里面的搜索估值当标签。
    /// <para>
    /// <b>对齐的语义：一个数据行对一个数据行。</b>两边都跳过注释行（<c>#</c> 开头）和空行，
    /// 所以注释行数量不同、文件末尾多一个换行符都不会错位。旁路文件里的**空白数据行**是
    /// 合法且有意义的：它表示"这个决策没有搜索估值"（规则牌手不搜索、换牌阶段没有棋盘），
    /// 读成 <see cref="double.NaN"/>，由调用方在切分之后丢掉。
    /// </para>
    /// <para>
    /// <b>为什么必须双向核对数量</b>：只检查"主文件读完了、旁路文件还有剩"只能抓住一个方向 ——
    /// 旁路文件少几行时，多出来的样本会被配到**后面**的估值上（或者配到 NaN），
    /// 于是每个样本都拿到了错误的标签，而所有指标都还"看着正常"。
    /// 实际采出来的旁路文件里空行占比不低（换牌阶段的决策行），所以这种错位**很容易发生**，
    /// 一旦发生又是静默的 —— 这正是这份代码最危险的失败模式。
    /// </para>
    /// </summary>
    private static (List<double[]> Features, List<int> Labels, List<double> SearchTargets) ReadSamples(
        string path,
        ValueSource source)
    {
        var features = new List<double[]>();
        var labels = new List<int>();
        var searchTargets = new List<double>();

        // 先把旁路文件的估值全部读出来，再和主文件按数据行号对上。
        // 边读边配会很脆：任意一边多一个空行就整体错位，而且要等到训练完才会发现。
        List<double>? sidecar = null;
        if (source == ValueSource.Search)
        {
            var sidecarPath = WeightTools.SearchValuePath(path);
            if (!File.Exists(sidecarPath))
            {
                throw new InvalidOperationException(
                    $"找不到搜索估值旁路文件 {sidecarPath}。" +
                    "它应由 --collect-selfplay 和样本一起写出；先重新采一份样本。");
            }

            sidecar = new List<double>();
            foreach (var line in File.ReadLines(sidecarPath))
            {
                var sidecarLine = line.Trim();
                if (sidecarLine.StartsWith('#'))
                {
                    continue;
                }

                // 空行 = 这一行没有搜索估值（**不等于"值是 0"**）。
                if (sidecarLine.Length == 0)
                {
                    sidecar.Add(double.NaN);
                    continue;
                }

                sidecar.Add(double.Parse(sidecarLine, CultureInfo.InvariantCulture));
            }
        }

        foreach (var line in File.ReadLines(path))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#'))
            {
                continue;
            }

            var parts = trimmed.Split(',');
            labels.Add(int.Parse(parts[0], CultureInfo.InvariantCulture));
            var row = new double[parts.Length - 1];
            for (var index = 1; index < parts.Length; index++)
            {
                row[index - 1] = double.Parse(parts[index], CultureInfo.InvariantCulture);
            }

            features.Add(row);
        }

        if (sidecar is not null)
        {
            var sidecarPath = WeightTools.SearchValuePath(path);
            if (sidecar.Count < features.Count)
            {
                throw new InvalidOperationException(
                    $"搜索估值旁路文件 {sidecarPath} 的数据行少于样本文件" +
                    $"（估值 {sidecar.Count} 行、样本 {features.Count} 行）。" +
                    "两个文件必须一个数据行对一个数据行 —— 少行会让后面的样本全部配到错误的估值上，而且是静默的。");
            }

            if (sidecar.Count > features.Count)
            {
                throw new InvalidOperationException(
                    $"搜索估值旁路文件 {sidecarPath} 的数据行多于样本文件" +
                    $"（估值 {sidecar.Count} 行、样本 {features.Count} 行）。" +
                    "两个文件必须一个数据行对一个数据行。注意空行也是**数据行**（表示这一行没有搜索估值），" +
                    "所以不能拿空行来当分隔或排版。");
            }

            // 数量相等，可以安全地按行号取。
            searchTargets.AddRange(sidecar);
        }

        return (features, labels, searchTargets);
    }
}
