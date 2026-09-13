using System.Globalization;

namespace Shadowverse.Engine.Agents;

/// <summary>
/// 一个小型前馈网络，用来替代"线性特征 × 权重 + logistic"的叶子评估。
/// <para>
/// 动机：手调/拟合的线性评估在本项目里连续失败（对数损失能从 0.78 降到 0.63，胜率一动不动）。
/// 这不是调参问题，是**表达力**问题 —— 21 维线性函数表达不了"这个局面好不好"。
/// </para>
/// <para>
/// 结构刻意做得很小（21 输入 → 64 → 64 → 1，约 5.6k 参数），因为：
/// ① 叶子评估在一局里要被调用约 3 万次，模型必须便宜；
/// ② 笔记本上 5.6k 参数的前向只要几微秒，对局耗时的增加可以忽略；
/// ③ 数据量只有几十万行，大模型只会过拟合。
/// </para>
/// <para>
/// 归一化用**逐特征 RMS**（不做中心化）—— 和线性拟合那边一致：
/// 每个特征都是两侧之差，是反对称的，中心化会破坏这个性质。
/// </para>
/// </summary>
public sealed class NeuralPositionEvaluator
{
    private readonly int _inputCount;
    private readonly int _hiddenCount;
    private readonly double[] _w1;
    private readonly double[] _b1;
    private readonly double[] _w2;
    private readonly double[] _b2;
    private readonly double[] _w3;
    private double _b3;
    private readonly double[] _scales;

    public NeuralPositionEvaluator(
        int inputCount,
        int hiddenCount,
        double[] scales,
        double[] w1,
        double[] b1,
        double[] w2,
        double[] b2,
        double[] w3,
        double b3)
    {
        _inputCount = inputCount;
        _hiddenCount = hiddenCount;
        _scales = scales;
        _w1 = w1;
        _b1 = b1;
        _w2 = w2;
        _b2 = b2;
        _w3 = w3;
        _b3 = b3;

        if (w1.Length != hiddenCount * inputCount || b1.Length != hiddenCount ||
            w2.Length != hiddenCount * hiddenCount || b2.Length != hiddenCount ||
            w3.Length != hiddenCount || scales.Length != inputCount)
        {
            throw new ArgumentException("神经网络权重数组的尺寸不匹配。");
        }
    }

    public int InputCount => _inputCount;

    /// <summary>
    /// 归一化后输入的上限，取 3（三个标准差）。
    /// **这个值必须和初始化配套**：RMS 归一化后输入近似 N(0,1)，而 He 初始化正是为
    /// N(0,1) 输入设计的。夹到 ±8 会让预激活放大 8 倍、梯度爆成 NaN；
    /// 夹到 ±3 才和初始化对得上。
    /// </summary>
    public const double MaxNormalizedInput = 3.0;

    /// <summary>把一项特征归一化并夹紧。前向和反向必须用同一个函数，否则梯度不一致。</summary>
    public static double Normalize(double value, double scale) =>
        Math.Clamp(value / scale, -MaxNormalizedInput, MaxNormalizedInput);

    // 训练器需要原地更新这些数组。暴露成只读引用（数组内容可变，引用不可换），
    // 这样推理路径仍然拿不到"换一个数组"的能力。
    public double[] W1 => _w1;
    public double[] B1 => _b1;
    public double[] W2 => _w2;
    public double[] B2 => _b2;
    public double[] W3 => _w3;
    public double B3 { get => _b3; set => _b3 = value; }
    public double[] Scales => _scales;

    /// <summary>前向传播，返回 0..1 的胜率估计。</summary>
    public double Evaluate(IReadOnlyList<double> features)
    {
        if (features.Count != _inputCount)
        {
            throw new ArgumentException($"特征个数应为 {_inputCount}，收到 {features.Count}。");
        }

        var hidden1 = new double[_hiddenCount];
        for (var unit = 0; unit < _hiddenCount; unit++)
        {
            var sum = _b1[unit];
            var offset = unit * _inputCount;
            for (var index = 0; index < _inputCount; index++)
            {
                sum += _w1[offset + index] * Normalize(features[index], _scales[index]);
            }

            hidden1[unit] = sum > 0 ? sum : 0;
        }

        var hidden2 = new double[_hiddenCount];
        for (var unit = 0; unit < _hiddenCount; unit++)
        {
            var sum = _b2[unit];
            var offset = unit * _hiddenCount;
            for (var index = 0; index < _hiddenCount; index++)
            {
                sum += _w2[offset + index] * hidden1[index];
            }

            hidden2[unit] = sum > 0 ? sum : 0;
        }

        var output = _b3;
        for (var index = 0; index < _hiddenCount; index++)
        {
            output += _w3[index] * hidden2[index];
        }

        return 1.0 / (1.0 + Math.Exp(-output));
    }

    /// <summary>把网络写成一个纯文本文件：第一行是维度，之后是顺序排列的权重。</summary>
    public void Save(string path)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using var writer = new StreamWriter(path);
        writer.WriteLine("# 由 --train-neural 生成的小型前馈网络");
        writer.WriteLine($"{_inputCount} {_hiddenCount}");
        WriteArray(writer, "scales", _scales);
        WriteArray(writer, "w1", _w1);
        WriteArray(writer, "b1", _b1);
        WriteArray(writer, "w2", _w2);
        WriteArray(writer, "b2", _b2);
        WriteArray(writer, "w3", _w3);
        writer.WriteLine(_b3.ToString("R", CultureInfo.InvariantCulture));
    }

    public static NeuralPositionEvaluator Load(string path)
    {
        var numbers = File.ReadAllLines(path)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith('#'))
            .ToArray();
        if (numbers.Length < 2)
        {
            throw new InvalidOperationException($"神经网络文件 {path} 格式不对。");
        }

        var header = numbers[0].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var inputCount = int.Parse(header[0], CultureInfo.InvariantCulture);
        var hiddenCount = int.Parse(header[1], CultureInfo.InvariantCulture);

        // 第二个文件行是 "scales n"，以此类推；每段的第一个 token 是名字。
        var index = 1;
        double[] Take(string expectedName, int count)
        {
            var parts = numbers[index].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts[0] != expectedName || parts.Length != count + 1)
            {
                throw new InvalidOperationException(
                    $"神经网络文件 {path} 第 {index + 1} 行应为 {expectedName} 加 {count} 个数。");
            }

            var values = new double[count];
            for (var i = 0; i < count; i++)
            {
                values[i] = double.Parse(parts[i + 1], CultureInfo.InvariantCulture);
            }

            index++;
            return values;
        }

        var scales = Take("scales", inputCount);
        var w1 = Take("w1", hiddenCount * inputCount);
        var b1 = Take("b1", hiddenCount);
        var w2 = Take("w2", hiddenCount * hiddenCount);
        var b2 = Take("b2", hiddenCount);
        var w3 = Take("w3", hiddenCount);
        var b3 = double.Parse(numbers[index], CultureInfo.InvariantCulture);

        return new NeuralPositionEvaluator(inputCount, hiddenCount, scales, w1, b1, w2, b2, w3, b3);
    }

    private static void WriteArray(StreamWriter writer, string name, double[] values)
    {
        writer.Write(name);
        foreach (var value in values)
        {
            writer.Write(' ');
            writer.Write(value.ToString("R", CultureInfo.InvariantCulture));
        }

        writer.WriteLine();
    }
}
