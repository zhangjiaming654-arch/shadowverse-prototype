using System.Globalization;
using System.Text;
using Shadowverse.Engine.Agents;
using Shadowverse.Engine.Game;
using Shadowverse.Engine.Simulation;

namespace Shadowverse.ConsoleApp;

/// <summary>
/// Self-play collection and logistic fitting for the lookahead evaluator.
/// <para>
/// The evaluator shipped with twelve hand-tuned weights and an arbitrary logistic scale. These
/// tools replace that guesswork with weights fitted on the outcomes of real self-play games:
/// <c>--collect-selfplay</c> writes one row per decision (the evaluator's feature vector from the
/// deciding player's side, labelled with whether that player went on to win), and
/// <c>--fit-weights</c> runs a regularised logistic regression over those rows.
/// </para>
/// <para>
/// A fit produces a single weight vector that already absorbs the scale, so a fitted model is
/// applied with a scale of 1.
/// </para>
/// </summary>
public static class WeightTools
{
    /// <summary>
    /// A fitted evaluator plus its quality on data the fit never saw.
    /// <para>
    /// The validation numbers are the ones that matter: a 12-parameter fit on hundreds of thousands
    /// of rows will always look good on its own training data, so quoting the training log-loss as
    /// evidence would be meaningless. Rows are written match by match, so a split by row index is a
    /// split by game, which also keeps correlated decisions from the same game out of both halves.
    /// </para>
    /// </summary>
    public sealed record FitResult(
        double[] Weights,
        double TrainLogLoss,
        double TrainAccuracy,
        double ValidationLogLoss,
        double ValidationAccuracy,
        double HandTunedTrainLogLoss,
        double HandTunedTrainAccuracy,
        double HandTunedValidationLogLoss,
        double HandTunedValidationAccuracy,
        int TrainSampleCount,
        int ValidationSampleCount);

    /// <summary>
    /// Plays matches and writes one CSV row per main-phase decision, labelled with whether the
    /// player who was deciding at that moment went on to win. Returns the number of rows written.
    /// </summary>
    public static int Collect(
        IReadOnlyList<AgentBenchmark.BenchmarkDeck> decks,
        AgentKind agentKind,
        int matchCount,
        ulong seedBase,
        int rollouts,
        int horizon,
        int maxDegreeOfParallelism,
        string outputPath,
        /// <summary>
        /// 只记录**用这套牌那一方**的决策。默认 null = 两边都记。
        /// <para>
        /// 为什么需要它：现有的样本只记了"局面特征 + 谁赢"，**没记这一行是哪个牌手在决策**。
        /// 于是"中速梦面对郭龙时的判断"和"中速梦面对中速梦时的判断"混在同一个文件里，分不开 ——
        /// 而"牌手要根据对手卡组换策略"这件事，恰恰只能靠这个对照来验证。
        /// </para>
        /// </summary>
        string? perspectiveDeckId = null)
    {
        ArgumentNullException.ThrowIfNull(decks);
        if (decks.Count == 0)
        {
            throw new ArgumentException("采样至少需要一副卡组。", nameof(decks));
        }

        var perMatch = new List<(double[] Features, int Label)>[matchCount];
        var completed = 0;
        var progressGate = new object();
        var progressEvery = Math.Max(1, matchCount / 20);

        Parallel.For(
            0,
            matchCount,
            new ParallelOptions { MaxDegreeOfParallelism = maxDegreeOfParallelism },
            index =>
            {
                var seed = seedBase + (ulong)index;
                var (deckA, deckB) = SelectDeckPair(decks, seedBase, index);

                var firstAgent = AgentBenchmark.CreateAgent(
                    agentKind,
                    seed ^ 0x5DEECE66DUL,
                    rollouts,
                    horizon);
                var secondAgent = AgentBenchmark.CreateAgent(
                    agentKind,
                    seed ^ 0xBADC0FFEEUL,
                    rollouts,
                    horizon);

                var samples = new List<(double[] Features, int ActingPlayer)>();
                var result = MatchRunner.PlayToEnd(
                    GameEngine.CreateGame(deckA.Definition, deckB.Definition, seed),
                    firstAgent,
                    secondAgent,
                    onStep: step =>
                    {
                        // The mulligan has no board to weigh, so it carries no usable sample.
                        if (step.BeforeState.Phase != GamePhase.Main)
                        {
                            return;
                        }

                        // 只看指定那一方的决策。用它才能把"同一套牌、不同对手"的数据分开采。
                        if (perspectiveDeckId is not null &&
                            (step.ActingPlayer == 0 ? deckA.Id : deckB.Id) != perspectiveDeckId)
                        {
                            return;
                        }

                        samples.Add((
                            LookaheadPlayerAgent.PositionFeatures(step.BeforeState, step.ActingPlayer),
                            step.ActingPlayer));
                    });

                // The winner is only known once the match ends, so the labels are attached here.
                perMatch[index] = samples
                    .Select(sample => (sample.Features, sample.ActingPlayer == result.Winner ? 1 : 0))
                    .ToList();

                var finished = Interlocked.Increment(ref completed);
                if (finished % progressEvery == 0)
                {
                    lock (progressGate)
                    {
                        var rows = 0;
                        foreach (var match in perMatch)
                        {
                            rows += match?.Count ?? 0;
                        }

                        Console.WriteLine($"  进度 {finished}/{matchCount} 局 ｜ 已采集 {rows} 条决策样本");
                    }
                }
            });

        return WriteSamples(outputPath, perMatch);
    }

    private static int WriteSamples(
        string outputPath,
        List<(double[] Features, int Label)>[] perMatch)
    {
        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var featureCount = LookaheadPlayerAgent.PositionWeights.Length;
        var rows = 0;
        using var writer = new StreamWriter(outputPath, append: false, Encoding.UTF8);
        writer.WriteLine(
            $"# label,{string.Join(',', Enumerable.Range(0, featureCount).Select(i => $"f{i}"))}");
        foreach (var samples in perMatch)
        {
            if (samples is null)
            {
                continue;
            }

            foreach (var (features, label) in samples)
            {
                writer.Write(label.ToString(CultureInfo.InvariantCulture));
                foreach (var value in features)
                {
                    writer.Write(',');
                    writer.Write(value.ToString("R", CultureInfo.InvariantCulture));
                }

                writer.WriteLine();
                rows++;
            }
        }

        return rows;
    }

    /// <summary>
    /// Regularised logistic regression over a collected self-play file. The reported hand-tuned
    /// numbers come from scoring the same rows with the weights the evaluator shipped with, so the
    /// comparison is like for like.
    /// </summary>
    public static FitResult Fit(
        string path,
        int epochs,
        double learningRate,
        double l2,
        Action<string> report)
    {
        ArgumentNullException.ThrowIfNull(report);
        var (features, labels) = ReadSamples(path);
        if (labels.Count == 0)
        {
            throw new InvalidOperationException($"样本文件里没有可用数据：{path}");
        }

        var featureCount = features[0].Length;

        // Rows are written match by match, so splitting by row index splits by game. The last fifth
        // is held out entirely: it never touches the normalisation scales or the fitted weights.
        var validationCount = Math.Max(1, labels.Count / 5);
        var trainCount = labels.Count - validationCount;
        var trainFeatures = features.GetRange(0, trainCount);
        var trainLabels = labels.GetRange(0, trainCount);
        var validationFeatures = features.GetRange(trainCount, validationCount);
        var validationLabels = labels.GetRange(trainCount, validationCount);

        // Features live on wildly different scales (health is ±20, hand size ±9, play points ±10),
        // so plain gradient descent on the raw vectors diverges. Each feature is divided by its
        // RMS. It is deliberately not centred: every feature is a difference between the two sides,
        // and subtracting a mean would destroy the antisymmetry the evaluator relies on.
        // The scales come from the training half only, so no validation information leaks in.
        var scales = FeatureScales(trainFeatures, featureCount);
        var standardizedTrain = Standardize(trainFeatures, scales);
        var standardizedValidation = Standardize(validationFeatures, scales);

        var weights = new double[featureCount];
        var order = Enumerable.Range(0, trainLabels.Count).ToArray();
        var random = new Random(20_260_912);

        for (var epoch = 0; epoch < epochs; epoch++)
        {
            // A decaying rate keeps the early epochs fast without letting the fit oscillate later.
            var rate = learningRate / (1.0 + (epoch / 50.0));
            Shuffle(order, random);
            foreach (var index in order)
            {
                var vector = standardizedTrain[index];
                var predicted = Sigmoid(Dot(weights, vector));
                var error = predicted - trainLabels[index];
                for (var feature = 0; feature < featureCount; feature++)
                {
                    var gradient = (error * vector[feature]) + (l2 * weights[feature]);
                    weights[feature] -= rate * gradient;
                }
            }

            if ((epoch + 1) % Math.Max(1, epochs / 5) == 0)
            {
                report(
                    $"  epoch {epoch + 1}/{epochs}：" +
                    $"训练 {LogLoss(weights, 1.0, standardizedTrain, trainLabels):F5} ｜ " +
                    $"验证 {LogLoss(weights, 1.0, standardizedValidation, validationLabels):F5}");
            }
        }

        // Map the standardized weights back onto the raw features the evaluator actually sees.
        var fitted = new double[featureCount];
        for (var feature = 0; feature < featureCount; feature++)
        {
            fitted[feature] = weights[feature] / scales[feature];
        }

        var handTuned = LookaheadPlayerAgent.PositionWeights.ToArray();
        var handScale = LookaheadPlayerAgent.ScoreScale;

        return new FitResult(
            fitted,
            LogLoss(fitted, 1.0, trainFeatures, trainLabels),
            Accuracy(fitted, 1.0, trainFeatures, trainLabels),
            LogLoss(fitted, 1.0, validationFeatures, validationLabels),
            Accuracy(fitted, 1.0, validationFeatures, validationLabels),
            LogLoss(handTuned, handScale, trainFeatures, trainLabels),
            Accuracy(handTuned, handScale, trainFeatures, trainLabels),
            LogLoss(handTuned, handScale, validationFeatures, validationLabels),
            Accuracy(handTuned, handScale, validationFeatures, validationLabels),
            trainLabels.Count,
            validationLabels.Count);
    }

    private static List<double[]> Standardize(List<double[]> features, double[] scales)
    {
        var standardized = new List<double[]>(features.Count);
        foreach (var vector in features)
        {
            var scaled = new double[vector.Length];
            for (var feature = 0; feature < vector.Length; feature++)
            {
                scaled[feature] = vector[feature] / scales[feature];
            }

            standardized.Add(scaled);
        }

        return standardized;
    }

    /// <summary>The root-mean-square of each feature across the sample, used to normalize it.</summary>
    private static double[] FeatureScales(List<double[]> features, int featureCount)
    {
        var scales = new double[featureCount];
        for (var feature = 0; feature < featureCount; feature++)
        {
            var sumSquares = 0.0;
            foreach (var vector in features)
            {
                sumSquares += vector[feature] * vector[feature];
            }

            var rms = Math.Sqrt(sumSquares / features.Count);
            scales[feature] = rms < 1e-9 ? 1.0 : rms;
        }

        return scales;
    }

    /// <summary>
    /// Writes the fitted weights in the format <c>--weights-file</c> reads: the scale first, then
    /// one weight per feature, whitespace separated.
    /// </summary>
    public static void SaveWeights(string path, double[] weights, double scoreScale)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var text = new StringBuilder();
        text.AppendLine("# 由 --fit-weights 生成：第一行是分数尺度，第二行起是各特征权重");
        text.AppendLine(scoreScale.ToString("R", CultureInfo.InvariantCulture));
        foreach (var weight in weights)
        {
            text.AppendLine(weight.ToString("R", CultureInfo.InvariantCulture));
        }

        File.WriteAllText(path, text.ToString(), Encoding.UTF8);
    }

    /// <summary>Loads a weights file written by <see cref="SaveWeights"/> and applies it.</summary>
    public static void LoadWeights(string path, Action<string> report)
    {
        ArgumentNullException.ThrowIfNull(report);
        var numbers = File.ReadAllLines(path)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith('#'))
            .Select(line => double.Parse(line, CultureInfo.InvariantCulture))
            .ToArray();

        var featureCount = LookaheadPlayerAgent.PositionWeights.Length;
        if (numbers.Length != featureCount + 1)
        {
            throw new InvalidOperationException(
                $"权重文件 {path} 应有 {featureCount + 1} 个数（1 个尺度 + {featureCount} 个权重），" +
                $"实际 {numbers.Length} 个。");
        }

        LookaheadPlayerAgent.ConfigureWeights(numbers[1..], numbers[0]);
        report($"已从 {path} 载入评估权重（尺度 {numbers[0]:R}，{featureCount} 项）。");
    }

    /// <summary>
    /// 载入集成用的第二套评估权重。和 <see cref="LoadWeights"/> 同样的文件格式，
    /// 但装到集成槽位，不覆盖主权重。
    /// </summary>
    public static void LoadEnsembleWeights(string path, Action<string> report)
    {
        ArgumentNullException.ThrowIfNull(report);
        var numbers = File.ReadAllLines(path)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith('#'))
            .Select(line => double.Parse(line, CultureInfo.InvariantCulture))
            .ToArray();

        var featureCount = LookaheadPlayerAgent.PositionWeights.Length;
        if (numbers.Length != featureCount + 1)
        {
            throw new InvalidOperationException(
                $"集成权重文件 {path} 应有 {featureCount + 1} 个数（1 个尺度 + {featureCount} 个权重），" +
                $"实际 {numbers.Length} 个。");
        }

        LookaheadPlayerAgent.ConfigureEnsembleWeights(numbers[1..], numbers[0]);
        report($"已从 {path} 载入【集成】评估权重（尺度 {numbers[0]:R}，{featureCount} 项）。");
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
            if (parts.Length < 2)
            {
                continue;
            }

            labels.Add(int.Parse(parts[0], CultureInfo.InvariantCulture));
            var vector = new double[parts.Length - 1];
            for (var index = 1; index < parts.Length; index++)
            {
                vector[index - 1] = double.Parse(parts[index], CultureInfo.InvariantCulture);
            }

            features.Add(vector);
        }

        return (features, labels);
    }

    private static double LogLoss(
        double[] weights,
        double scale,
        IReadOnlyList<double[]> features,
        List<int> labels)
    {
        var total = 0.0;
        for (var index = 0; index < labels.Count; index++)
        {
            var probability = Sigmoid(Dot(weights, features[index]) / scale);
            var clamped = Math.Clamp(probability, 1e-9, 1.0 - 1e-9);
            total -= (labels[index] * Math.Log(clamped)) + ((1 - labels[index]) * Math.Log(1.0 - clamped));
        }

        return total / labels.Count;
    }

    private static double Accuracy(
        double[] weights,
        double scale,
        IReadOnlyList<double[]> features,
        List<int> labels)
    {
        var correct = 0;
        for (var index = 0; index < labels.Count; index++)
        {
            var predicted = Sigmoid(Dot(weights, features[index]) / scale) >= 0.5 ? 1 : 0;
            if (predicted == labels[index])
            {
                correct++;
            }
        }

        return (double)correct / labels.Count;
    }

    private static double Dot(double[] weights, double[] vector)
    {
        var total = 0.0;
        for (var index = 0; index < weights.Length && index < vector.Length; index++)
        {
            total += weights[index] * vector[index];
        }

        return total;
    }

    private static double Sigmoid(double value) => 1.0 / (1.0 + Math.Exp(-value));

    private static void Shuffle(int[] order, Random random)
    {
        for (var index = order.Length - 1; index > 0; index--)
        {
            var swap = random.Next(index + 1);
            (order[index], order[swap]) = (order[swap], order[index]);
        }
    }

    private static (AgentBenchmark.BenchmarkDeck A, AgentBenchmark.BenchmarkDeck B) SelectDeckPair(
        IReadOnlyList<AgentBenchmark.BenchmarkDeck> decks,
        ulong seedBase,
        int matchIndex)
    {
        if (decks.Count < 2)
        {
            return (decks[0], decks[^1]);
        }

        var draw = Mix(seedBase + (ulong)matchIndex);
        var first = (int)(draw % (ulong)decks.Count);
        var second = (first + 1 + (int)((draw >> 32) % (ulong)(decks.Count - 1))) % decks.Count;
        return (decks[first], decks[second]);
    }

    private static ulong Mix(ulong value)
    {
        value ^= value >> 30;
        value *= 0xBF58476D1CE4E5B9UL;
        value ^= value >> 27;
        value *= 0x94D049BB133111EBUL;
        return value ^ (value >> 31);
    }
}
