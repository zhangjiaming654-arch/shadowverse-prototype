using System.Globalization;
using System.Text.Json;
using Shadowverse.Engine.Models;

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

    private static readonly Dictionary<string, double> _hand = new(StringComparer.Ordinal)
    {
        // ⚠️ 实测否决：曾设 BASE-079 = 12.0（留在手里的溢价），结果**明显更差** ——
        //   宇宙鱼 64.0% → 58.0%，决定性牌局 38:13 → 25:15，BO10 80 → 55
        //   （中速梦无变化，62%/60，符合"只有宇宙鱼吃这张表"）。
        // 原因判断：+12 相对 ScoreScale=12 是极大的常数项，而且罪人在手里的回合很多，
        //   **每个回合都加 12** 把其他特征全淹没了；同时窗口因子用"有进化点"判定，
        //   而进化点几乎整局都有 → 窗口几乎恒开 → 确实变成了囤牌。
        // 机制（双标量 + 窗口）保留在代码里，默认**全 0 关闭**；要重新启用必须先标定量级，
        // 不能直接拍 12。当前最优配置是只挂 crestValue[CREST-005] = 24.0。
        ["BASE-079"] = 2.0
    };

    /// <summary>打出价值 = "留着的代价"。罪人打出去实测只有 46% 胜率（等于没用），故为 0。</summary>
    private static readonly Dictionary<string, double> _played = new(StringComparer.Ordinal);

    private static string? _loadedFrom;
    private static bool _ensured;

    /// <summary>默认查找位置：仓库根的 outputs/learned-values.json。</summary>
    public static string DefaultPath { get; } = ResolveDefaultPath();

    public static double Crest(string? crestId) =>
        crestId is not null && _crest.TryGetValue(crestId, out var value) ? value : 0.0;

    public static double HandCard(string? cardId) =>
        cardId is not null && _hand.TryGetValue(cardId, out var value) ? value : 0.0;

    public static double PlayedCard(string? cardId) =>
        cardId is not null && _played.TryGetValue(cardId, out var value) ? value : 0.0;

    /// <summary>
    /// **使用窗口**：这张牌"留着才有、出了就没了"的溢价，现在还兑现得了吗？返回 0~1 的折扣。
    /// <para>
    /// 这是**防止囤牌的关键机制**。若在手价值是个常数，那么"留着"永远优于"出掉"，
    /// 牌手会囤牌空过 —— 本项目已经踩过这个坑：<c>DragonRainbowBoardValue</c> 的注释记录了
    /// 旧评估函数"宁愿囤昂贵卡、也不肯花能量点"。所以窗口一关，溢价必须归零。
    /// </para>
    /// <para>
    /// **窗口从卡面规则算，不靠学**：数据管"这张牌值多少"，规则管"什么时候它才有用"。
    /// </para>
    /// </summary>
    public static double HeldWindowFactor(CardDefinition card, int evolutionPoints)
    {
        var hasEvolutionPayoff =
            (card.OnEvolveEffects?.Count ?? 0) > 0 || (card.EvolutionEffects?.Count ?? 0) > 0;
        if (hasEvolutionPayoff)
        {
            // 窗口 = 我方还进化得起它。进化点用完 →【进化时】收益永远拿不到 → 溢价归零，
            // 牌手于是会老老实实把它打出去，而不是烂在手里。
            return evolutionPoints > 0 ? 1.0 : 0.0;
        }

        // 【奥义】类：【解放奥义】的槽会自己涨，窗口一直开着（等到满槽才兑现），不做折扣。
        // 普通牌：留着没有额外溢价。
        return 0.0;
    }

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
