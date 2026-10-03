using System.Globalization;
using System.Text.Json;

namespace Shadowverse.Engine.Agents;

/// <summary>
/// **可学习价值表** —— 给每个纹章、每张手牌一个"待学的位置"，初始为 0，可由文件覆盖。
/// <para>
/// 为什么要这个东西（2026-10-03 实测，产物 <c>outputs/crest-card-signal.txt</c>）：
/// 宇宙鱼镜像 40 局里 ——
/// </para>
/// <list type="bullet">
///   <item>拿到「纹章：束刃的罪人」(CREST-005) 的一方，胜率 <b>77.8%</b>（基准 50%）</item>
///   <item>而「罪人本体(BASE-079)出过场」的一方，胜率只有 <b>46.0%</b> —— 等于没用</item>
/// </list>
/// <para>
/// 两者差 31.8 个百分点。所以价值必须挂在**纹章**上，**不能挂在卡上** ——
/// 否则等于告诉牌手"罪人这张牌很强"，反而鼓励它把罪人当普通随从乱出。
/// 挂在纹章上，牌手会自己推出"要为了拿纹章而把罪人留到进化回合"。
/// </para>
/// <para>
/// 全程**不需要**告诉牌手哪张牌重要：这里只提供"待学的位置"，具体数值由实测标定。
/// </para>
/// </summary>
public static class LearnedValues
{
    /// <summary>纹章 id → 价值（正数表示持有它有利）。</summary>
    public static IReadOnlyDictionary<string, double> CrestValues => _crest;

    /// <summary>手牌卡 id → 价值。只用于**自己的**手牌；对手手牌是暗牌，不参与。</summary>
    public static IReadOnlyDictionary<string, double> HandCardValues => _hand;

    private static readonly Dictionary<string, double> _crest = new(StringComparer.Ordinal)
    {
        // 唯一样本量够（18 局）的纹章。取值口径：ScoreScale = 12，logistic 反解
        //   logit(0.778) = 1.253 → rawScore ≈ 15.0 是全胜率口径；
        // 但纹章只在后段持有、且这里只是启发式叶子，故先取半个量级 6.0，由基准实测调整。
        ["CREST-005"] = 24.0
    };

    private static readonly Dictionary<string, double> _hand = new(StringComparer.Ordinal);

    private static string? _loadedFrom;
    private static bool _ensured;

    /// <summary>默认查找位置：仓库根的 outputs/learned-values.json。</summary>
    public static string DefaultPath { get; } = ResolveDefaultPath();

    public static double Crest(string? crestId) =>
        crestId is not null && _crest.TryGetValue(crestId, out var value) ? value : 0.0;

    public static double HandCard(string? cardId) =>
        cardId is not null && _hand.TryGetValue(cardId, out var value) ? value : 0.0;

    /// <summary>
    /// 首次访问时尝试加载 <see cref="DefaultPath"/>。
    /// 文件不存在就用内置表 —— **加载失败绝不能影响对局**。
    /// </summary>
    public static void EnsureLoaded()
    {
        if (_ensured)
        {
            return;
        }

        _ensured = true;
        TryLoad(DefaultPath);
    }

    /// <summary>显式加载（测试用）。格式：{"crest":{"CREST-005":6.0},"hand":{"BASE-079":0}}</summary>
    public static void TryLoad(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return;
            }

            using var document = JsonDocument.Parse(File.ReadAllText(path));
            if (document.RootElement.TryGetProperty("crest", out var crests))
            {
                foreach (var entry in crests.EnumerateObject())
                {
                    _crest[entry.Name] = entry.Value.GetDouble();
                }
            }

            if (document.RootElement.TryGetProperty("hand", out var hand))
            {
                foreach (var entry in hand.EnumerateObject())
                {
                    _hand[entry.Name] = entry.Value.GetDouble();
                }
            }

            _loadedFrom = path;
        }
        catch (Exception)
        {
            // 价值表是附加信息，读失败就用内置默认值。
        }
    }

    /// <summary>已加载的表来自哪个文件；没加载则为 null。</summary>
    public static string? LoadedFrom => _loadedFrom;

    private static string ResolveDefaultPath()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "ShadowversePrototype.sln")))
            {
                return Path.Combine(directory.FullName, "outputs", "learned-values.json");
            }
        }

        return Path.Combine(
            AppContext.BaseDirectory,
            "outputs",
            "learned-values.json");
    }

    /// <summary>导出当前表，便于拟合工具回写与人工核对。</summary>
    public static string ToJson() =>
        JsonSerializer.Serialize(
            new
            {
                crest = _crest,
                hand = _hand,
                note = string.Create(CultureInfo.InvariantCulture, $"loadedFrom={_loadedFrom ?? "(builtin)"}")
            });
}
