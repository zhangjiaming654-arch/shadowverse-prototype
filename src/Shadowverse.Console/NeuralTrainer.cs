using System.Globalization;
using Shadowverse.Engine.Agents;

namespace Shadowverse.ConsoleApp;

/// <summary>
/// 用 <c>--collect-selfplay</c> 采到的样本训练 <see cref="NeuralPositionEvaluator"/>。
/// <para>
/// 标签仍然是"这一局最后谁赢了" —— 和线性拟合一样。**这不是我们最终想要的目标函数**
/// （第四轮的教训：预测胜负 ≠ 动作排序对不对），但它是唯一现成的监督信号，
/// 所以这一步的定位是**跑通管线 + 便宜的证伪**：如果连价值头都毫无用处，
/// 那么真正该做的是策略头（直接给出动作先验），而不是继续改评估函数。
/// </para>
/// <para>
/// 归一化用逐特征 RMS、不做中心化（特征都是两侧之差，反对称性必须保住）。
/// 训练/验证按**行序 80/20 切分**，而行是按对局顺序写入的，所以等价于按对局切分。
/// </para>
/// </summary>
public static class NeuralTrainer
{
    public sealed record TrainResult(
        NeuralPositionEvaluator Network,
        int TrainRows,
        int ValidationRows,
        double TrainLogLoss,
        double TrainAccuracy,
        double ValidationLogLoss,
        double ValidationAccuracy,
        double HandTunedValidationLogLoss,
        double HandTunedValidationAccuracy,
        /// <summary>验证集上预测值的标准差。**诊断"网络是不是在输出常数"用**：
        /// 接近 0 说明它只学会了偏置项，训练信号没进到权重里。</summary>
        double ValidationPredictionStd);

    public static TrainResult Train(
        string csvPath,
        int hiddenCount,
        int epochs,
        double learningRate,
        double l2,
        ulong seed,
        Action<string> report)
    {
        ArgumentNullException.ThrowIfNull(report);
        var (features, labels) = ReadSamples(csvPath);
        if (features.Count == 0)
        {
            throw new InvalidOperationException($"样本文件 {csvPath} 里没有数据。");
        }

        var inputCount = features[0].Length;
        report($"载入 {features.Count} 行、{inputCount} 项特征；网络 {inputCount}→{hiddenCount}→{hiddenCount}→1。");

        // 按行序 80/20 切分：行是按对局顺序写入的，所以这等价于按对局切分。
        var validationCount = Math.Max(1, labels.Count / 5);
        var trainCount = labels.Count - validationCount;
        var trainFeatures = features.GetRange(0, trainCount);
        var trainLabels = labels.GetRange(0, trainCount);
        var validationFeatures = features.GetRange(trainCount, validationCount);
        var validationLabels = labels.GetRange(trainCount, validationCount);

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

        var order = Enumerable.Range(0, trainCount).ToArray();
        var h1 = new double[hiddenCount];
        var h2 = new double[hiddenCount];

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
                var y = trainLabels[row];

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
                var dOutput = prediction - y;

                // 先算 dH2，再更新 W3。反过来的话 dH2 会用**更新后**的 W3，梯度就不一致了。
                var dH2 = new double[hiddenCount];
                for (var index = 0; index < hiddenCount; index++)
                {
                    dH2[index] = h2[index] > 0 ? dOutput * net.W3[index] : 0.0;
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

            if ((epoch + 1) % Math.Max(1, epochs / 5) == 0)
            {
                report(
                    $"  epoch {epoch + 1}/{epochs}：" +
                    $"训练 {LogLoss(net, trainFeatures, trainLabels):F5} ｜ " +
                    $"验证 {LogLoss(net, validationFeatures, validationLabels):F5}");
            }
        }

        // 手调线性评估作为对照：把网络和它放在同一把尺子上。
        var handTunedWeights = LookaheadPlayerAgent.PositionWeights;
        var handTunedScale = LookaheadPlayerAgent.ScoreScale;
        return new TrainResult(
            net,
            trainCount,
            validationCount,
            LogLoss(net, trainFeatures, trainLabels),
            Accuracy(net, trainFeatures, trainLabels),
            LogLoss(net, validationFeatures, validationLabels),
            Accuracy(net, validationFeatures, validationLabels),
            LinearLogLoss(handTunedWeights, handTunedScale, validationFeatures, validationLabels),
            LinearAccuracy(handTunedWeights, handTunedScale, validationFeatures, validationLabels),
            PredictionStd(net, validationFeatures));
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
        var sum = 0.0;
        for (var index = 0; index < features.Length; index++)
        {
            sum += features[index] * weights[index];
        }

        return 1.0 / (1.0 + Math.Exp(-sum / scale));
    }

    private static (List<double[]> Features, List<int> Labels) ReadSamples(string path)
    {
        var features = new List<double[]>();
        var labels = new List<int>();
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

        return (features, labels);
    }
}
