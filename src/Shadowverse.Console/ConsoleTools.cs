using System.Text;
using System.Globalization;
using System.Diagnostics;
using System.Security.Cryptography;
using Shadowverse.Engine.Agents;
using Shadowverse.Engine.Cards;
using Shadowverse.Engine.Decks;
using Shadowverse.Engine.Game;
using Shadowverse.Engine.Models;
using Shadowverse.Engine.Simulation;
namespace Shadowverse.ConsoleApp;

/// <summary>
/// 命令行辅助与工具：参数解析、卡牌/卡组打印、对局基准的卡组解析、采样与拟合。
/// 自检代码在 <see cref="AgentSelfTests"/>。
/// </summary>
internal static class ConsoleTools
{
internal static void PrintHelp()
{
    Console.WriteLine("影之诗原型 - 控制台");
    Console.WriteLine();
    Console.WriteLine("用法：Shadowverse.Console [选项]");
    Console.WriteLine();
    Console.WriteLine("对局：");
    Console.WriteLine("  （无参数）           打印一局完整战报；P1 为规则牌手，P2 为随机牌手");
    Console.WriteLine("  --lookahead          把 P1 换成前瞻牌手");
    Console.WriteLine("  --rollouts <1-500>   前瞻推演次数（默认 60）");
    Console.WriteLine("  --p2-rollouts <1-500> 只给第二牌手设推演次数；用来做");
    Console.WriteLine("                       同一个牌手两种搜索量对打的对照实验");
    Console.WriteLine("  --p2-horizon <1-10>  只给第二牌手设推演视野；同理，用来做");
    Console.WriteLine("                       同一个牌手两种视野对打的对照实验");
    Console.WriteLine("  --rail-margin <0-1>  回退闸下限默认值（默认 0.035）：前瞻的优势不到这个值就交给规则牌手");
    Console.WriteLine("  --p2-rail-margin <0-1> 只覆盖第二牌手的闸门，用来做闸门开/关的对照");
    Console.WriteLine("  --selection <rail|argmax|robust>  决策规则（默认 rail，即 1.0 的行为）");
    Console.WriteLine("  --p2-selection <rail|argmax|robust>  只覆盖第二牌手，用来做选择规则的配对对照");
    Console.WriteLine("  --robustness <z>   robust 模式下扣减跨世界标准差的系数（默认 1.0）");
    Console.WriteLine("  --p2-robustness <z>  只覆盖第二牌手的稳健系数");
    Console.WriteLine("  --confidence <z>   rail 模式下回退闸统计项的强度（默认 1.0，越大越常让位给规则牌手）");
    Console.WriteLine("  --p2-confidence <z>  只覆盖第二牌手的统计强度");
    Console.WriteLine("  --rollout <rule|value>  推演里由谁打完剩余动作（默认 rule，即 1.0 的行为）");
    Console.WriteLine("  --p2-rollout <rule|value>  只覆盖第二牌手，用来做 rollout 策略的配对对照");
    Console.WriteLine("  --mulligan-horizon <1-10>  换牌专用视野（默认 0 = 跟随主回合视野）");
    Console.WriteLine("  --p2-mulligan-horizon <1-10>  只覆盖第二牌手的换牌视野");
    Console.WriteLine("  --alternate-horizon <1-10>  交替视野：奇数号推演用它，和主视野对冲（默认 0 = 不交替）");
    Console.WriteLine("  --p2-alternate-horizon <1-10>  只覆盖第二牌手的交替视野");
    Console.WriteLine("  --third-horizon <1-10>  第三档视野，构成三档循环 [主,交替,第三]（默认 0 = 不用）");
    Console.WriteLine("  --p2-third-horizon <1-10>  只覆盖第二牌手的第三档视野");
    Console.WriteLine("  --alt-rollout <rule|value>  策略循环：一半推演改用这个 rollout 策略（默认同 --rollout = 不集成）");
    Console.WriteLine("  --p2-alt-rollout <rule|value>  只覆盖第二牌手");
    Console.WriteLine("  --extra-pp <search|rule|never>  额外PP 的候选权：搜索自由决定 / 只有规则牌手也愿意才允许 / 完全禁止");
    Console.WriteLine("  --p2-extra-pp <search|rule|never>  只覆盖第二牌手");
    Console.WriteLine("  --opponent-rollout <rule|value|nested>  rollout 里对手用什么策略（默认 rule = 1.0/2.0 行为；");
    Console.WriteLine("                       value = 把对手当成爬山评估函数的优化者；nested = 对手是真前瞻搜索牌手）");
    Console.WriteLine("  --p2-opponent-rollout <rule|value|nested>  只覆盖第二牌手");
    Console.WriteLine("  --opponent-rollouts <1-500>  嵌套对手的推演次数（默认 8；嵌套搜索代价是乘法，别开大）");
    Console.WriteLine("  --opponent-horizon <1-10>  嵌套对手的视野（默认 0 = 跟随主视野）");
    Console.WriteLine("  --opponent-first-action-only  嵌套对手只搜「它怎么回应我」的第一手（省掉约 4 倍成本）");
    Console.WriteLine("  --own-rollouts <1-500>  嵌套**我方**（--rollout nested）的推演次数（默认 8）");
    Console.WriteLine("  --own-horizon <1-10>  嵌套我方的视野（默认 0 = 跟随主视野）");
    Console.WriteLine("  --alt-weights-file <路径>  载入集成用的第二套评估权重（不覆盖主权重）");
    Console.WriteLine("  --evaluator-ensemble  第一牌手开启评估函数集成（需配合 --alt-weights-file）");
    Console.WriteLine("  --p2-evaluator-ensemble  第二牌手开启");
    Console.WriteLine("  --train-neural <样本.csv>  训练神经网络叶子评估（--hidden / --epochs / --out / --value-source）");
    Console.WriteLine("  --value-source <outcome|search>  训练标签来源：最终胜负（默认）或旁路文件的搜索估值（蒸馏）");
    Console.WriteLine("  --diagnose-search-value <样本.csv>  诊断搜索估值能不能预测胜负（AUC；不跑对局，只读样本）");
    Console.WriteLine("  --neural-weights <路径>  装载神经网络叶子评估，取代线性评估");
    Console.WriteLine("  2.0 预设：--horizon 1 --alternate-horizon 3 --rollouts 60");
    Console.WriteLine("    中速梦镜像：决定性胜率 76.3%（总胜率 62.2%），vs 1.0，p < 0.0001");
    Console.WriteLine("    郭龙 vs 中速梦：决定性胜率 76.0%（总胜率 59.0%），vs 1.0，p < 0.0001");
    Console.WriteLine("  1.0 基线：--p2 lookahead-v1 --p2-rollouts 10 --p2-horizon 3（缺一不可）");
    Console.WriteLine("  注意：--horizon/--rollouts 的默认值仍是 3 / 60，这样不带参数跑 lookahead-v1 才等于 1.0");
    Console.WriteLine("  --horizon <1-10>     前瞻推演回合数（默认 3，仅 --lookahead 时生效）");
    Console.WriteLine("  --random             每局改用随机种子；省略时使用固定种子，便于复现");
    Console.WriteLine("  --deck1 <卡组编号>   P1 使用的卡组（--stats 默认 DECK-003 中速梦；其他模式默认卡组库第一副）");
    Console.WriteLine("  --deck2 <卡组编号>   P2 使用的卡组（同上）");
    Console.WriteLine("                       注：剑斗士卡组（DECK-001）禁止用于任何牌手对战");
    Console.WriteLine();
    Console.WriteLine("统计与校验：");
    Console.WriteLine("  --stats [局数]       并行批量对局，输出胜率与 Wilson 95% 置信区间（默认 1000 局）");
    Console.WriteLine("  --bo10 <N>          主要评判口径：跑 N 个 BO10（= 10N 局），输出 BO10 得分、平手数、");
    Console.WriteLine("                      等强零假设期望分与单侧 p 值（给了 --bo10 时以它为准）");
    Console.WriteLine("  --p1 <牌手>          第一牌手：random、greedy、baseline、lookahead、lookahead-v2（默认 greedy）");
    Console.WriteLine("  --p2 <牌手>          第二牌手：同上（默认 random）");
    Console.WriteLine("                       baseline = 2026-09-12 冻结的规则牌手快照，用来衡量 greedy 的改动是否真的变强");
    Console.WriteLine("  --jobs <1-256>       跑批并行度（默认处理器核数；每局互相独立，可放心并行）");
    Console.WriteLine("  --random-decks       每对牌局从合法卡组里随机抽两副（用来测对手卡组识别）");
    Console.WriteLine("  --no-swap            关闭换边对开；默认每个对阵在两个座位方向各打一半，消除座位与发牌偏置");
    Console.WriteLine("  --quiet-stats        只打印胜率与置信区间，跳过逐项行为统计");
    Console.WriteLine("  --smoke-test         同一随机种子对局两次，校验完整行动记录一致");
    Console.WriteLine("  --effect-test        运行卡牌效果与牌手行为的全部自检");
    Console.WriteLine();
    Console.WriteLine("评估权重拟合（牌手 3.0）：");
    Console.WriteLine("  --collect-selfplay <局数>  让牌手自对弈，采集（局面特征, 最终胜负）样本");
    Console.WriteLine("  --collect-agent <牌手>     采样用的牌手（默认 lookahead）");
    Console.WriteLine("  --collect-deck <卡组编号>  把采样限定在该卡组的镜像局上（做某套牌专精用）");
    Console.WriteLine("  --fit-weights <样本文件>   对样本做 L2 正则 logistic 回归，输出拟合权重");
    Console.WriteLine("  --weights-file <权重文件>  用拟合权重跑基准（在跑批前生效，用于 A/B）");
    Console.WriteLine("  --matchup-weights <文件>  按对局切换权重：每行「我方卡组 对手卡组 权重文件路径」");
    Console.WriteLine("  --epochs / --learning-rate / --l2 / --out  拟合参数与输出路径");
    Console.WriteLine();
    Console.WriteLine("数据查询：");
    Console.WriteLine("  --cards              打印卡牌库（含编号、职业、稀有度、类型、费用、身材、效果）");
    Console.WriteLine("  --decks              打印卡组库");
    Console.WriteLine("  --help, -h           显示本说明");
}

internal static void PrintCardCatalog()
{
    Console.WriteLine("========== 当前卡牌库 ==========");
    Console.WriteLine("格式：编号 - 卡牌名称 - 职业 - 稀有度 - 类型（随从、法术、护符） - 费用 - 身材（法术或护符留空） - 效果");
    Console.WriteLine($"编号规则：已使用编号不再复用；下一张卡从 {CardCatalog.NextCardId} 起。");
    Console.WriteLine("注：衍生卡会保留在卡牌库，但不能直接加入普通卡组。\n");

    if (CardCatalog.All.Count == 0)
    {
        Console.WriteLine("（暂无已录入卡牌）");
        return;
    }

    foreach (var card in CardCatalog.All)
    {
        var body = card.Type == CardType.Follower ? $"{card.Attack}/{card.Defense}" : "";
        var generatedMark = card.IsCollectible ? string.Empty : "【衍生卡】";
        Console.WriteLine(
            $"{card.Id} - {card.Name} - {CardProfessionName(card.Profession)} - {CardRarityName(card.Rarity)} - {CardTypeName(card.Type)} - {card.Cost}费 - {body} - {generatedMark}{EffectDescription(card)}");
    }
}

internal static string CardTypeName(CardType type) => type switch
{
    CardType.Follower => "随从",
    CardType.Spell => "法术",
    CardType.Amulet => "护符",
    _ => throw new InvalidOperationException("Unknown card type.")
};

internal static string CardRarityName(CardRarity rarity) => rarity switch
{
    CardRarity.Bronze => "铜卡",
    CardRarity.Silver => "银卡",
    CardRarity.Gold => "金卡",
    CardRarity.Rainbow => "虹卡",
    _ => throw new InvalidOperationException("Unknown card rarity.")
};

internal static string CardProfessionName(CardProfession profession) => profession switch
{
    CardProfession.Elf => "精灵",
    CardProfession.Royal => "皇家护卫",
    CardProfession.Witch => "巫师",
    CardProfession.Dragon => "龙族",
    CardProfession.Nightmare => "梦魇",
    CardProfession.Bishop => "主教",
    CardProfession.Nemesis => "超越者",
    CardProfession.Neutral => "中立",
    _ => throw new InvalidOperationException("Unknown card profession.")
};

internal static string EffectDescription(CardDefinition card)
{
    var effects = new List<string>();
    if (card.Keywords.HasFlag(CardKeyword.Ward) && !card.EffectText.Contains("【守护】", StringComparison.Ordinal))
    {
        effects.Add("守护（Ward）");
    }

    if (card.Keywords.HasFlag(CardKeyword.Storm) && !card.EffectText.Contains("【疾驰】", StringComparison.Ordinal))
    {
        effects.Add("疾驰（Storm）");
    }

    if (card.Keywords.HasFlag(CardKeyword.Bane) && !card.EffectText.Contains("【毁灭】", StringComparison.Ordinal))
    {
        effects.Add("毁灭（Bane）");
    }

    if (card.Keywords.HasFlag(CardKeyword.Intimidate) && !card.EffectText.Contains("【威慑】", StringComparison.Ordinal))
    {
        effects.Add("威慑（Intimidate）");
    }

    if (card.Keywords.HasFlag(CardKeyword.Rush) && !card.EffectText.Contains("【突进】", StringComparison.Ordinal))
    {
        effects.Add("突进（Rush）");
    }

    if (card.Keywords.HasFlag(CardKeyword.IgnoreWard) && !card.EffectText.Contains("无视【守护】", StringComparison.Ordinal))
    {
        effects.Add("无视守护攻击");
    }

    if (card.Keywords.HasFlag(CardKeyword.Barrier) && !card.EffectText.Contains("【屏障】", StringComparison.Ordinal))
    {
        effects.Add("屏障（Barrier）");
    }

    if (card.Keywords.HasFlag(CardKeyword.Aura) && !card.EffectText.Contains("【灵气】", StringComparison.Ordinal))
    {
        effects.Add("灵气（Aura）");
    }

    if (card.Keywords.HasFlag(CardKeyword.Drain) && !card.EffectText.Contains("【虹吸】", StringComparison.Ordinal))
    {
        effects.Add("虹吸（Drain）");
    }

    if (!string.IsNullOrWhiteSpace(card.EffectText))
    {
        effects.Add(card.EffectText);
    }

    return effects.Count == 0 ? "无" : string.Join("；", effects);
}

internal static void PrintDeckCatalog()
{
    Console.WriteLine("========== 当前卡组库 ==========");
    Console.WriteLine("编号规则：DECK-001 起；已使用的编号永不复用，新卡组继续递增。\n");

    if (DeckCatalog.All.Count == 0)
    {
        Console.WriteLine("（暂无已保存卡组）");
        return;
    }

    foreach (var deck in DeckCatalog.All)
    {
        Console.WriteLine($"{deck.Id} - {deck.Name}");
        Console.WriteLine($"  {deck.Description}");
        Console.WriteLine($"  共 {deck.Entries.Sum(entry => entry.Count)} 张：");
        foreach (var entry in deck.Entries)
        {
            Console.WriteLine($"  {entry.CardId} ×{entry.Count} - {CardCatalog.Get(entry.CardId).Name}");
        }

        Console.WriteLine();
    }
}

/// <summary>
/// The deck agent benchmarks use when neither --deck1 nor --deck2 is given. It prefers the
/// 中速梦 list by ID, then by name, and only falls back to the library's first deck when neither
/// exists, so a renamed or reordered library cannot silently change what is being measured.
/// </summary>
internal static string ResolveDefaultBenchmarkDeckId(string fallbackDeckId)
{
    const string preferredId = "DECK-003";
    const string preferredName = "中速梦";
    var decks = DeckCatalog.All;

    return decks.FirstOrDefault(deck => string.Equals(deck.Id, preferredId, StringComparison.OrdinalIgnoreCase))?.Id
        ?? decks.FirstOrDefault(deck => string.Equals(deck.Name, preferredName, StringComparison.Ordinal))?.Id
        ?? fallbackDeckId;
}

/// <summary>
/// 剑斗士卡组只有 6 种卡，其中两张各放了 10 张，胜负基本由同一张卡堆叠决定。用它衡量牌手强弱
/// 得到的是那副牌的镜像对局结果，不是策略质量，因此所有牌手对战都禁止使用它。
/// </summary>
internal static bool IsForbiddenAgentMatchDeck(DeckListDefinition deck) =>
    string.Equals(deck.Id, "DECK-001", StringComparison.OrdinalIgnoreCase) ||
    deck.Name.Contains("剑斗士", StringComparison.Ordinal);

internal static DeckListDefinition RequireAllowedAgentMatchDeck(string deckId, string optionName)
{
    var deck = DeckCatalog.Get(deckId);
    if (IsForbiddenAgentMatchDeck(deck))
    {
        throw new ArgumentException(
            $"{optionName} 指定的「{deck.Name}」（{deck.Id}）禁止用于牌手对战：" +
            "它只有 6 种卡、其中两张各 10 张，胜负由单卡堆叠决定，无法衡量牌手强弱。" +
            "请改用其他卡组，或加 --random-decks 从合法卡组里随机抽取。");
    }

    return deck;
}

internal static AgentBenchmark.BenchmarkDeck ToBenchmarkDeck(DeckListDefinition deck)
{
    var storedCount = deck.Entries.Sum(entry => entry.Count);
    if (storedCount != DeckDefinition.RequiredCardCount)
    {
        throw new InvalidOperationException(
            $"{deck.Id}（{deck.Name}）目前只有 {storedCount} 张卡，未满 {DeckDefinition.RequiredCardCount} 张，无法用于对局。");
    }

    return new AgentBenchmark.BenchmarkDeck(deck.Id, deck.Name, DeckCatalog.Create(deck.Id, deck.Name));
}

internal static IReadOnlyList<AgentBenchmark.BenchmarkDeck> ResolveBenchmarkDecks(
    string[] args,
    string fallbackDeckId,
    string explicitFirstDeckId,
    string explicitSecondDeckId,
    bool randomDecks)
{
    if (HasOption(args, "--deck1") || HasOption(args, "--deck2"))
    {
        // An explicit pair is honoured, but each side is still checked against the ban.
        var firstDeck = RequireAllowedAgentMatchDeck(
            HasOption(args, "--deck1") ? explicitFirstDeckId : ResolveDefaultBenchmarkDeckId(fallbackDeckId),
            "--deck1");
        var secondDeck = RequireAllowedAgentMatchDeck(
            HasOption(args, "--deck2") ? explicitSecondDeckId : ResolveDefaultBenchmarkDeckId(fallbackDeckId),
            "--deck2");
        return [ToBenchmarkDeck(firstDeck), ToBenchmarkDeck(secondDeck)];
    }

    if (!randomDecks)
    {
        return [ToBenchmarkDeck(RequireAllowedAgentMatchDeck(ResolveDefaultBenchmarkDeckId(fallbackDeckId), "--stats"))];
    }

    var pool = DeckCatalog.All.Where(deck => !IsForbiddenAgentMatchDeck(deck)).ToArray();
    if (pool.Length < 2)
    {
        throw new InvalidOperationException(
            $"随机抽卡组需要至少两副合法卡组，当前只有 {pool.Length} 副（剑斗士卡组已被排除）。");
    }

    return pool.Select(ToBenchmarkDeck).ToArray();
}

internal static AgentKind? ParseAgentOption(string[] args, string optionName)
{
    var value = ReadOptionValue(args, optionName);
    return value is null ? null : AgentBenchmark.ParseAgent(value);
}

/// <summary>
/// 解析额外PP 候选权。默认 search，所以不指定时行为不变。
/// </summary>
internal static LookaheadExtraPlayPointPolicy ParseExtraPlayPointPolicy(string[] args, string optionName)
{
    var value = ReadOptionValue(args, optionName);
    if (value is null)
    {
        return LookaheadExtraPlayPointPolicy.Search;
    }

    return value.Trim().ToLowerInvariant() switch
    {
        "search" or "free" or "off" => LookaheadExtraPlayPointPolicy.Search,
        "rule" or "gated" or "rule-gated" => LookaheadExtraPlayPointPolicy.RuleGated,
        "never" or "none" or "ban" => LookaheadExtraPlayPointPolicy.Never,
        _ => throw new ArgumentException(
            $"未知额外PP 候选权“{value}”。可选：search、rule、never")
    };
}

/// <summary>
/// 解析 rollout 策略。默认是 1.0 的 RuleAgent，所以不指定时行为不变。
/// </summary>
internal static LookaheadRolloutPolicy ParseRolloutPolicy(string[] args, string optionName)
{
    var value = ReadOptionValue(args, optionName);
    if (value is null)
    {
        return LookaheadRolloutPolicy.RuleAgent;
    }

    return value.Trim().ToLowerInvariant() switch
    {
        "rule" or "greedy" or "v1" or "1.0" => LookaheadRolloutPolicy.RuleAgent,
        "value" or "evaluator" or "hill" => LookaheadRolloutPolicy.EvaluatorGreedy,
        "nested" or "lookahead" or "search" => LookaheadRolloutPolicy.NestedLookahead,
        _ => throw new ArgumentException(
            $"未知 rollout 策略“{value}”。可选：rule（规则牌手）、value（爬山评估函数）、" +
            "nested（真前瞻搜索，只对 --opponent-rollout 有意义）")
    };
}

/// <summary>
/// 解析决策规则。默认是 1.0 的 RuleAgentFallback，所以不指定时行为不变。
/// </summary>
internal static LookaheadSelectionMode ParseSelectionMode(string[] args, string optionName)
{
    var value = ReadOptionValue(args, optionName);
    if (value is null)
    {
        return LookaheadSelectionMode.RuleAgentFallback;
    }

    return value.Trim().ToLowerInvariant() switch
    {
        "rail" or "fallback" or "v1" or "1.0" => LookaheadSelectionMode.RuleAgentFallback,
        "argmax" or "pure" or "none" => LookaheadSelectionMode.PureArgmax,
        "robust" or "variance" or "lcb" => LookaheadSelectionMode.VariancePenalized,
        _ => throw new ArgumentException(
            $"未知选择规则“{value}”。可选：rail、argmax、robust")
    };
}

internal static bool HasOption(string[] args, string optionName) =>
    args.Contains(optionName, StringComparer.OrdinalIgnoreCase);

internal static string? ReadOptionValue(string[] args, string optionName)
{
    var optionIndex = Array.FindIndex(args, argument =>
        string.Equals(argument, optionName, StringComparison.OrdinalIgnoreCase));
    if (optionIndex == -1)
    {
        return null;
    }

    if (optionIndex == args.Length - 1)
    {
        throw new ArgumentException($"{optionName} 后必须提供取值。");
    }

    return args[optionIndex + 1];
}

internal static ulong CreateRandomSeed()
{
    var bytes = RandomNumberGenerator.GetBytes(sizeof(ulong));
    return BitConverter.ToUInt64(bytes);
}

internal static string ProjectRoot() =>
    Path.GetDirectoryName(Path.GetDirectoryName(DeckCatalog.StoragePath))
    ?? AppContext.BaseDirectory;

/// <summary>
/// 让牌手自己下棋，把每一次决策的局面特征连同"这个牌手最后赢了没有"写进样本文件，
/// 供 --fit-weights 拟合叶子评估的权重。这是把手调常数换成数据驱动常数的唯一入口。
/// </summary>
/// <summary>
/// 打印每套牌的"延迟收益"画像。
/// <para>
/// 这是为了设计"视野随卡组自适应"的判据：报告第 12 节发现视野和卡组有真实交互
/// （短视野在中速梦上赚、在郭龙这种斜坡卡组上赔），但**判据必须是量出来的，不能拍脑袋**。
/// 如果中速梦也有大量吟唱/谢幕曲，那"数延迟收益的牌"就区分不开这两套牌，
/// 规则就是白设计的 —— 所以先把它打出来看。
/// </para>
/// </summary>
internal static void PrintDeckProfile()
{
    Console.WriteLine("卡组「延迟收益」画像（每套牌 40 张）");
    Console.WriteLine();
    Console.WriteLine(
        $"{"卡组",-18}{"均费",6}{"最高费",7}{"斜坡",6}{"吟唱",6}{"回合末",7}{"谢幕曲",7}{"被动",6}{"合计*",7}");

    foreach (var entry in DeckCatalog.All)
    {
        var deck = DeckCatalog.Create(entry.Id);
        var cards = deck.Cards;

        var ramp = cards.Count(card => EffectsOf(card).Any(effect => effect.Kind
            is CardEffectKind.IncreaseOwnMaxPlayPoints
            or CardEffectKind.IncreaseOwnMaxPlayPointsAndDrawIfAtTen));
        var countdown = cards.Count(card => card.Countdown is not null);
        var endOfTurn = cards.Count(card => card.EndOfOwnTurnEffects is { Count: > 0 }
            || card.UnevolvedEndOfOwnTurnEffects is { Count: > 0 }
            || card.EvolvedEndOfOwnTurnEffects is { Count: > 0 });
        var lastWords = cards.Count(card => card.LastWordsEffects is { Count: > 0 });
        var passive = cards.Count(card => card.PassiveEffects is { Count: > 0 });

        // "合计*" 是五类的并集（同一张牌同时命中多项只算一次），也就是"延迟收益牌占比"的分子。
        var anyDelayed = cards.Count(card =>
            card.Countdown is not null ||
            card.EndOfOwnTurnEffects is { Count: > 0 } ||
            card.UnevolvedEndOfOwnTurnEffects is { Count: > 0 } ||
            card.EvolvedEndOfOwnTurnEffects is { Count: > 0 } ||
            card.LastWordsEffects is { Count: > 0 } ||
            card.PassiveEffects is { Count: > 0 } ||
            EffectsOf(card).Any(effect => effect.Kind
                is CardEffectKind.IncreaseOwnMaxPlayPoints
                or CardEffectKind.IncreaseOwnMaxPlayPointsAndDrawIfAtTen));

        Console.WriteLine(
            $"{deck.Name,-18}{cards.Average(card => card.Cost),6:F2}{cards.Max(card => card.Cost),7}" +
            $"{ramp,6}{countdown,6}{endOfTurn,7}{lastWords,7}{passive,6}" +
            $"{$"{anyDelayed}/40",7}");
    }
}

/// <summary>一张牌所有会产生效果的地方，去重前的原始枚举（用于统计"这张牌有没有某类效果"）。</summary>
private static IEnumerable<CardEffect> EffectsOf(CardDefinition card)
{
    var lists = new[]
    {
        card.FanfareEffects,
        card.SpellEffects,
        card.EvolutionEffects,
        card.SuperEvolutionEvolutionEffects,
        card.LastWordsEffects,
        card.EndOfOwnTurnEffects,
        card.AttackEffects,
        card.OnEvolveEffects,
        card.PassiveEffects
    };

    foreach (var list in lists)
    {
        if (list is null)
        {
            continue;
        }

        foreach (var effect in list)
        {
            yield return effect;
        }
    }
}

/// <summary>
/// 载入"按对局切换"的权重表。
/// <para>
/// 文件每行一条：<c>我方卡组编号 对手卡组编号 权重文件路径</c>，<c>#</c> 开头是注释，
/// 路径相对于仓库根目录（和 outputs/ 一致）。
/// </para>
/// <para>
/// 存在的理由：实测同一套 21 项特征、**只换"权重是在哪种对局的数据上拟合的"**，
/// 交叉对局上的 BO10 从 37.5 跳到 65.0，镜像上从 57.5 跳到 65.0 ——
/// **每一列都是"在本对局上拟合的那一套"赢**。所以按对局选一份是有价值的。
/// 没配到的对局回退全局默认权重。
/// </para>
/// </summary>
internal static void LoadMatchupWeights(string path)
{
    var map = new Dictionary<(string Own, string Opponent), (double[] Weights, double Scale)>();
    foreach (var rawLine in File.ReadAllLines(path))
    {
        var line = rawLine.Trim();
        if (line.Length == 0 || line.StartsWith('#'))
        {
            continue;
        }

        var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 3)
        {
            throw new InvalidOperationException(
                $"对局权重文件 {path} 的每一行应为「我方卡组 对手卡组 权重文件路径」，收到：{line}");
        }

        var weightsPath = Path.IsPathRooted(parts[2]) ? parts[2] : Path.Combine(ProjectRoot(), parts[2]);
        var (weights, scale) = WeightTools.ReadWeights(weightsPath);
        map[(parts[0], parts[1])] = (weights, scale);
    }

    LookaheadPlayerAgent.ConfigureMatchupWeights(map);
    Console.WriteLine($"已按对局载入 {map.Count} 份评估权重（来自 {path}）；没配到的对局回退默认权重。");
}

internal static void RunSelfPlayCollection(    string[] args,
    int rollouts,
    int horizon,
    int maxDegreeOfParallelism,
    AgentBenchmark.SideConfig? rolloutConfig = null)
{
    var matchCount = ParseIntegerOption(
        args,
        "--collect-selfplay",
        defaultValue: 200,
        minimum: 1,
        maximum: 50_000);
    var agentKind = ParseAgentOption(args, "--collect-agent") ?? AgentKind.Lookahead;
    var outputPath = ReadOptionValue(args, "--out")
        ?? Path.Combine(ProjectRoot(), "outputs", "selfplay-samples.csv");
    // --collect-deck 把采样限定在单副卡组的镜像局上，用来做"某套牌专精"的权重拟合。
    var forcedDeckId = ReadOptionValue(args, "--collect-deck");
    // --collect-perspective-deck 只记这套牌那一方的决策：
    // 这样"同一套牌、不同对手"的数据才能分开采，用来验证"牌手要对不同对手换策略"。
    var perspectiveDeckId = ReadOptionValue(args, "--collect-perspective-deck");
    var pool = forcedDeckId is not null
        ? new[] { ToBenchmarkDeck(RequireAllowedAgentMatchDeck(forcedDeckId, "--collect-deck")) }
        : DeckCatalog.All
            .Where(deck => !IsForbiddenAgentMatchDeck(deck))
            .Select(ToBenchmarkDeck)
            .ToArray();

    Console.WriteLine(
        $"自对弈采样：{matchCount} 局 ｜ 牌手 {AgentBenchmark.DisplayName(agentKind)} ｜ " +
        $"{rollouts} 次推演 × {horizon} 回合 ｜ 卡组池 {string.Join("、", pool.Select(deck => deck.Name))}");
    Console.WriteLine(
        perspectiveDeckId is null
            ? "视角：双方决策都记"
            : $"视角：只记 {perspectiveDeckId} 那一方的决策");
    if (rolloutConfig is not null &&
        (rolloutConfig.RolloutPolicy != LookaheadRolloutPolicy.RuleAgent ||
         rolloutConfig.OpponentRolloutPolicy != LookaheadRolloutPolicy.RuleAgent))
    {
        // 采样的配置必须跟着输出走，否则读样本的人会以为这是基线数据。
        Console.WriteLine(
            $"rollout 配置：我方 {rolloutConfig.RolloutPolicy}（{rolloutConfig.OwnNestedRollouts} 次推演）｜ " +
            $"对手 {rolloutConfig.OpponentRolloutPolicy}（{rolloutConfig.OpponentRollouts} 次推演" +
            (rolloutConfig.OpponentFirstActionOnly ? "、只搜第一手" : string.Empty) + "）");
    }

    var stopwatch = Stopwatch.StartNew();
    var rows = WeightTools.Collect(
        pool,
        agentKind,
        matchCount,
        20_260_901UL,
        rollouts,
        horizon,
        maxDegreeOfParallelism,
        outputPath,
        perspectiveDeckId,
        rolloutConfig);
    stopwatch.Stop();
    Console.WriteLine($"写入 {rows} 条决策样本 → {outputPath}（{stopwatch.Elapsed.TotalSeconds:F1} 秒）");
}

/// <summary>对自对弈样本做带 L2 正则的 logistic 回归，并和现有手调权重做同数据对比。</summary>
internal static void RunNeuralTraining(string[] args)
{
    var inputPath = ReadOptionValue(args, "--train-neural")
        ?? throw new ArgumentException("--train-neural 后必须提供样本文件路径。");
    var hidden = ParseIntegerOption(args, "--hidden", defaultValue: 64, minimum: 4, maximum: 512);
    var epochs = ParseIntegerOption(args, "--epochs", defaultValue: 12, minimum: 1, maximum: 10_000);
    var learningRate = double.Parse(
        ReadOptionValue(args, "--learning-rate") ?? "0.001",
        CultureInfo.InvariantCulture);
    var l2 = double.Parse(ReadOptionValue(args, "--l2") ?? "0.00001", CultureInfo.InvariantCulture);
    var seed = (ulong)ParseIntegerOption(args, "--seed", defaultValue: 12345, minimum: 1, maximum: int.MaxValue);
    var valueSource = ParseValueSourceOption(args, "--value-source");
    var outputPath = ReadOptionValue(args, "--out")
        ?? Path.Combine(ProjectRoot(), "outputs", "neural-leaf.txt");

    Console.WriteLine($"训练神经网络叶子评估：{inputPath}（{epochs} 轮，隐层 {hidden}，学习率 {learningRate}）");
    Console.WriteLine(valueSource == NeuralTrainer.ValueSource.Search
        ? $"标签来源：搜索估值（蒸馏）—— 旁路文件 {WeightTools.SearchValuePath(inputPath)}"
        : "标签来源：最终胜负（和线性拟合同一个目标）");
    var result = NeuralTrainer.Train(
        inputPath,
        hidden,
        epochs,
        learningRate,
        l2,
        seed,
        Console.WriteLine,
        valueSource);
    Console.WriteLine();
    if (valueSource == NeuralTrainer.ValueSource.Search)
    {
        Console.WriteLine(
            $"样本数：训练 {result.TrainRows} ｜ 验证 {result.ValidationRows}（按行序切分 = 按对局切分）" +
            $" ｜ 因缺搜索估值丢弃 {result.SkippedRows} 行");
        Console.WriteLine($"采用第 {result.BestEpoch} 轮的权重（验证 MSE 最低那一轮，不是最后一轮）");
        Console.WriteLine();
        Console.WriteLine("                       平方误差   相关系数");
        Console.WriteLine($"神经网络  验证集       {result.ValidationSquaredError:F5}    {result.ValidationCorrelation:F4}");
        Console.WriteLine(
            $"老师（搜索估值）      —          —        均值 {result.TeacherMean:F4} ｜ 标准差 {result.TeacherStd:F4}");
        Console.WriteLine(
            $"预测值标准差 {result.ValidationPredictionStd:F4}" +
            "（接近 0 说明网络只学会了老师估值的平均数 —— 蒸馏没成）");
        Console.WriteLine();
        Console.WriteLine(result.ValidationCorrelation >= 0.5
            ? $"结论：网络确实学到了搜索估值（相关系数 {result.ValidationCorrelation:F4}）。" +
              "但**这还不是验收** —— 判据只有 BO10 得分，见 BO10-JUDGEMENT.md。"
            : $"结论：网络没能复现搜索估值（相关系数只有 {result.ValidationCorrelation:F4}），" +
              "蒸馏这一步就没成，换进叶子评估不会有意义。");
    }
    else
    {
        Console.WriteLine($"样本数：训练 {result.TrainRows} ｜ 验证 {result.ValidationRows}（按行序切分 = 按对局切分）");
        Console.WriteLine($"采用第 {result.BestEpoch} 轮的权重（验证损失最低那一轮，不是最后一轮）");
        Console.WriteLine();
        Console.WriteLine("                   对数损失    准确率");
        Console.WriteLine($"神经网络  训练集   {result.TrainLogLoss:F5}     {result.TrainAccuracy:P2}");
        Console.WriteLine($"神经网络  验证集   {result.ValidationLogLoss:F5}     {result.ValidationAccuracy:P2}");
        Console.WriteLine($"线性手调  验证集   {result.HandTunedValidationLogLoss:F5}     {result.HandTunedValidationAccuracy:P2}");
        Console.WriteLine(
            $"预测值标准差 {result.ValidationPredictionStd:F4}" +
            "（接近 0 说明网络只学会了偏置、没学到任何特征 —— 训练信号没进权重）");
        Console.WriteLine();
        Console.WriteLine(result.ValidationLogLoss < result.HandTunedValidationLogLoss
            ? $"结论：神经网络在验证集上更好（对数损失低 {result.HandTunedValidationLogLoss - result.ValidationLogLoss:F5}）。"
            : "结论：神经网络在验证集上没有超过线性手调值。");
    }

    result.Network.Save(outputPath);
    Console.WriteLine($"已写入 {outputPath}");
}

/// <summary>
/// 由已解析好的参数拼一份 <see cref="AgentBenchmark.SideConfig"/>。
/// <para>
/// 为什么要把这些值当参数传进来而不是在这里重新解析 <c>args</c>：
/// 重解析等于把同一套开关读两遍，两遍一旦不一致（比如默认值改了只改一处），
/// 采样用的牌手和报告里写的配置就会**悄悄不是同一个** —— 那是很难查的一类错。
/// </para>
/// </summary>
internal static AgentBenchmark.SideConfig BuildSideConfig(
    int rollouts,
    int horizon,
    double railMargin,
    LookaheadSelectionMode selectionMode,
    double robustnessPenalty,
    double statisticalConfidence,
    LookaheadRolloutPolicy rolloutPolicy,
    int mulliganHorizon,
    int alternateHorizon,
    int thirdHorizon,
    LookaheadRolloutPolicy alternateRolloutPolicy,
    LookaheadExtraPlayPointPolicy extraPlayPointPolicy,
    LookaheadRolloutPolicy opponentRolloutPolicy,
    int opponentRollouts,
    int opponentHorizon,
    bool opponentFirstActionOnly,
    int ownNestedRollouts,
    int ownNestedHorizon,
    bool evaluatorEnsemble) => new(
        AgentKind.Lookahead,
        rollouts,
        horizon,
        railMargin,
        selectionMode,
        robustnessPenalty,
        statisticalConfidence,
        rolloutPolicy,
        mulliganHorizon,
        alternateHorizon,
        thirdHorizon,
        alternateRolloutPolicy,
        extraPlayPointPolicy,
        opponentRolloutPolicy,
        opponentRollouts,
        opponentHorizon,
        opponentFirstActionOnly,
        ownNestedRollouts,
        ownNestedHorizon,
        evaluatorEnsemble);

/// <summary>
/// 【决策质量分层】按决策裕度分层，看搜索的判断正确率怎么随裕度变化。
/// 用途见 <see cref="DecisionQualityProbe"/>：判别"裕度小时搜索是不是接近抛硬币"。
/// </summary>
internal static void RunDecisionQuality(string[] args)
{
    var gameCount = ParseIntegerOption(args, "--quality-games", defaultValue: 200, minimum: 5, maximum: 5000);
    var seed = (ulong)ParseIntegerOption(args, "--quality-seed", defaultValue: 20_260_925, minimum: 1, maximum: int.MaxValue);
    var rollouts = ParseIntegerOption(args, "--quality-rollouts", defaultValue: 10, minimum: 1, maximum: 500);
    var horizon = ParseIntegerOption(args, "--quality-horizon", defaultValue: 1, minimum: 1, maximum: 10);

    var decks = new List<DeckDefinition>
    {
        AgentSelfTests.CreateMatchDeck("DECK-003", "quality-a"),
        AgentSelfTests.CreateMatchDeck("DECK-002", "quality-b")
    };

    Console.WriteLine($"决策质量分层：{gameCount} 局（{rollouts} 次推演 × 视野 {horizon}），种子 {seed}");
    Console.WriteLine("标签 = 这一局最终谁赢；只统计搜索与规则牌手**分歧**的决策。");
    Console.WriteLine();
    var report = DecisionQualityProbe.Run(
        decks, gameCount, seed, rollouts, horizon, Console.WriteLine);

    Console.WriteLine();
    Console.WriteLine("==================== 分层结果 ====================");
    Console.WriteLine($"总决策 {report.Decisions} 个 ｜ 与规则牌手分歧 {report.OverallDisagreementShare:P1}");
    Console.WriteLine(
        $"分歧决策的「搜索那一方最终赢」比例 = **{report.OverallDisagreementWinShare:P1}**" +
        "（0.5 = 抛硬币；自对弈基线注意这个数不是 0.5 本身，见下）");
    Console.WriteLine($"对照：双方一致的那些决策里，搜索那一方赢的比例 = {report.AgreementWinShare:P1}");
    Console.WriteLine();
    Console.WriteLine("裕度区间          分歧决策数   占分歧比   实际胜率   搜索预测   预测−实际");
    foreach (var stratum in report.Strata)
    {
        var upper = double.IsPositiveInfinity(stratum.MarginUpper) || stratum.MarginUpper > 1e6
            ? "∞"
            : stratum.MarginUpper.ToString("F2", CultureInfo.InvariantCulture);
        Console.WriteLine(
            $"  [{stratum.MarginLower:F2},{upper,-4})   {stratum.Decisions,7}   " +
            $"{stratum.DisagreementShare,8:P1}   {stratum.DisagreementWinShare,8:P1}   " +
            $"{stratum.MeanEstimate,8:P1}   {stratum.MeanEstimate - stratum.DisagreementWinShare,+9:P1}");
    }

    Console.WriteLine();
    Console.WriteLine(
        "**关键看最后两列**：\"搜索预测\"是搜索自己给被选动作的估值均值。");
    Console.WriteLine(
        "如果\"预测−实际\"在各层都接近 0，说明搜索**校准良好、而且和裕度无关** —— " +
        "那加算力/降噪都不会有用，必须改结构。");
    Console.WriteLine(
        "如果只在**小裕度**那几层出现大的正偏差，说明搜索在没把握时系统性地高估自己" +
        "（那才是可以靠降噪或校准去修的）。");
}

/// <summary>
/// 【决策敏感度】把一批固定局面喂给同一族牌手的不同配置，数"选的动作变了多少次"。
/// <para>
/// 用途见 <see cref="DecisionSensitivityProbe"/> 的注释：这个项目试过十几个方向全都不产生强度，
/// 一个可能的统一解释是**搜索的输出对自身内部不敏感**。这个命令几分钟就能量出来，
/// 而且它对任何杠杆都能测 —— 比"预测指标"可靠（AUC 那个筛子已被证明是恒等式，报告 §18.4）。
/// </para>
/// </summary>
internal static void RunDecisionSensitivity(string[] args)
{    var gameCount = ParseIntegerOption(
        args, "--sensitivity-games", defaultValue: 60, minimum: 5, maximum: 5000);
    var seed = (ulong)ParseIntegerOption(
        args, "--sensitivity-seed", defaultValue: 20_260_920, minimum: 1, maximum: int.MaxValue);
    var neuralPath = ReadOptionValue(args, "--sensitivity-neural");

    // 只用两副允许对战的卡组采局面（中速梦 + 郭龙），和验收的卡组池一致。
    var decks = new List<DeckDefinition>
    {
        AgentSelfTests.CreateMatchDeck("DECK-003", "sensitivity-a"),
        AgentSelfTests.CreateMatchDeck("DECK-002", "sensitivity-b")
    };

    Console.WriteLine($"决策敏感度探针：{gameCount} 个局面，种子 {seed}");
    Console.WriteLine("基线 = 3.0 的默认配置（手调 21 项权重 + 线性叶子 + 视野循环 {1,3} + 60 次推演）");
    Console.WriteLine();

    var baseline = new DecisionSensitivityProbe.Variant(
        "基线（默认）",
        () => new LookaheadPlayerAgent(
            rolloutsPerAction: 60,
            futureTurnHorizon: 1,
            alternateHorizon: 3,
            minimumPracticalAdvantage: 0.0));

    var variants = new List<DecisionSensitivityProbe.Variant>
    {
        // 0. 噪声地板：同一个基线配置再建一个实例。它变了多少，后面的数字就只能解读到那个精度。
        new("【噪声地板】基线重建一次", () => new LookaheadPlayerAgent(
            rolloutsPerAction: 60,
            futureTurnHorizon: 1,
            alternateHorizon: 3,
            minimumPracticalAdvantage: 0.0)),

        // 1. 推演次数 60 → 10（六分之一，项目历史上"10 次之后饱和"的那条曲线）
        new("推演次数 60 → 10", () => new LookaheadPlayerAgent(
            rolloutsPerAction: 10,
            futureTurnHorizon: 1,
            alternateHorizon: 3,
            minimumPracticalAdvantage: 0.0)),

        // 2. 视野：去掉交替视野，退成纯 H1（这是"唯一成功过的机制"，敏感度应该高）
        new("视野 {1,3} → 纯 1", () => new LookaheadPlayerAgent(
            rolloutsPerAction: 60,
            futureTurnHorizon: 1,
            minimumPracticalAdvantage: 0.0)),

        // 3. 回退闸：统计项强度 1.0 → 0（把闸门完全打开）
        new("回退闸统计强度 1.0 → 0", () => new LookaheadPlayerAgent(
            rolloutsPerAction: 60,
            futureTurnHorizon: 1,
            alternateHorizon: 3,
            statisticalConfidence: 0.0,
            minimumPracticalAdvantage: 0.0)),

        // 4. 叶子：线性 → 蒸馏出来的神经网络
        new("叶子 线性 → 神经网络(蒸馏)", () => new LookaheadPlayerAgent(
            rolloutsPerAction: 60,
            futureTurnHorizon: 1,
            alternateHorizon: 3,
            minimumPracticalAdvantage: 0.0)),

        // 5. rollout 策略：规则牌手 → 真前瞻（对手座位，只搜第一手，成本 2.7×）
        new("rollout 对手 规则 → 嵌套前瞻", () => new LookaheadPlayerAgent(
            rolloutsPerAction: 60,
            futureTurnHorizon: 1,
            alternateHorizon: 3,
            minimumPracticalAdvantage: 0.0,
            opponentRolloutPolicy: LookaheadRolloutPolicy.NestedLookahead,
            opponentRollouts: 1,
            opponentFirstActionOnly: true)),

        // 6. 视野：{1,3} → {1,3,8}（项目里"加到第三档"的配置；长视野实测更差）
        new("视野 {1,3} → {1,3,8}", () => new LookaheadPlayerAgent(
            rolloutsPerAction: 60,
            futureTurnHorizon: 1,
            alternateHorizon: 3,
            thirdHorizon: 8,
            minimumPracticalAdvantage: 0.0)),

        // 7. 推演次数 60 → 240（"加预算"的配置；实测到顶回落）
        new("推演次数 60 → 240", () => new LookaheadPlayerAgent(
            rolloutsPerAction: 240,
            futureTurnHorizon: 1,
            alternateHorizon: 3,
            minimumPracticalAdvantage: 0.0)),

        // 8. 选择规则：RuleAgentFallback → PureArgmax（实测纯 argmax 更差，65:96）
        new("选择规则 闸门 → 纯 argmax", () => new LookaheadPlayerAgent(
            rolloutsPerAction: 60,
            futureTurnHorizon: 1,
            alternateHorizon: 3,
            selectionMode: LookaheadSelectionMode.PureArgmax,
            minimumPracticalAdvantage: 0.0)),

        // 9. 额外PP 候选权：Search → Never（实测中性）
        new("额外PP 候选权 Search → Never", () => new LookaheadPlayerAgent(
            rolloutsPerAction: 60,
            futureTurnHorizon: 1,
            alternateHorizon: 3,
            extraPlayPointPolicy: LookaheadExtraPlayPointPolicy.Never,
            minimumPracticalAdvantage: 0.0)),

        // 10. 视野 {1,3} → {1,2,3}（中间档：看改变率是不是跟着视野差走）
        new("视野 {1,3} → {1,2,3}", () => new LookaheadPlayerAgent(
            rolloutsPerAction: 60,
            futureTurnHorizon: 1,
            alternateHorizon: 3,
            thirdHorizon: 2,
            minimumPracticalAdvantage: 0.0)),

        // ---- 以下四条是**已知中性**的旋钮（记录在案），用来检验"裕度对比"判据 ----
        // 用途：如果中性旋钮普遍落在"负对比"一侧（和额外PP 同侧），这个判据就能当预检用；
        // 如果它们也落在正侧，判据就废掉。见 OVERNIGHT-REPORT §3.5。

        // 11. 稳健惩罚 1.0 → 0（`--confidence` 那一路的历史"中性"记录）
        new("【已知中性】稳健惩罚 1.0 → 0", () => new LookaheadPlayerAgent(
            rolloutsPerAction: 60,
            futureTurnHorizon: 1,
            alternateHorizon: 3,
            robustnessPenalty: 0.0,
            minimumPracticalAdvantage: 0.0)),

        // 12. 闸门统计强度 1.0 → 3.0（记录：强度 2.0 实测是 72:96 更差）
        new("【已知更差】闸门统计强度 1.0 → 3.0", () => new LookaheadPlayerAgent(
            rolloutsPerAction: 60,
            futureTurnHorizon: 1,
            alternateHorizon: 3,
            statisticalConfidence: 3.0,
            minimumPracticalAdvantage: 0.0)),

        // 13. 我方 rollout 换成爬山评估函数（方向 C，已关闭：12:21 更差）
        new("【已知更差】我方 rollout → 爬山", () => new LookaheadPlayerAgent(
            rolloutsPerAction: 60,
            futureTurnHorizon: 1,
            alternateHorizon: 3,
            rolloutPolicy: LookaheadRolloutPolicy.EvaluatorGreedy,
            minimumPracticalAdvantage: 0.0)),

        // 14. 对手 rollout 换成爬山（记录：中性，42:32 p=0.41）
        new("【已知中性】对手 rollout → 爬山", () => new LookaheadPlayerAgent(
            rolloutsPerAction: 60,
            futureTurnHorizon: 1,
            alternateHorizon: 3,
            opponentRolloutPolicy: LookaheadRolloutPolicy.EvaluatorGreedy,
            minimumPracticalAdvantage: 0.0))
    };

    // 注意：**没有"换权重文件"这条轴**。3.0 的权重是**静态全局**的
    // （`LookaheadPlayerAgent.PositionWeights`，只能整体替换），没有实例级构造参数，
    // 所以一份进程里换不了权重 —— 硬塞进去会让所有变体一起被改掉，测出来是假的。
    // 其余五条轴都是实例级旋钮，可以在一个进程里干净对比。

    // 神经网络叶子也是全局槽位，装一次；只有走 PositionWeights 的配置会用到它。
    if (neuralPath is not null)
    {
        var network = NeuralPositionEvaluator.Load(neuralPath);
        LookaheadPlayerAgent.ConfigureNeuralEvaluator(network);
        Console.WriteLine($"已装载神经网络叶子：{neuralPath}（第 4 条轴会用它）");
        Console.WriteLine();
    }

    var report = DecisionSensitivityProbe.Run(
        decks, gameCount, seed, baseline, variants, Console.WriteLine);

    Console.WriteLine();
    Console.WriteLine("==================== 小结 ====================");
    Console.WriteLine($"局面数 {report.Positions}；**噪声地板**（基线自己重问一遍）改了 "
        + $"{report.BaselineSelfChanged}/{report.Positions}");
    if (report.BaselineSelfChanged > 0)
    {
        Console.WriteLine(
            "⚠ 噪声地板不为 0 —— 基线自己重问都会变，说明这个牌手有跨局状态，"
            + "下面的百分比要和噪声地板一起读。");
    }

    foreach (var variant in report.Variants)
    {
        Console.WriteLine(
            $"  {variant.Name,-34} {variant.Changed,4}/{variant.Decisions}  "
            + $"({variant.ChangedShare:P1})  "
            + $"｜ 裕度对比 {variant.MarginContrast:+0.0000;-0.0000}");
    }

    Console.WriteLine();
    Console.WriteLine("裕度对比 = 未改变处的裕度 − 改变处的裕度。**正数**表示改动集中在");
    Console.WriteLine("「基线本来就没把握（裕度小）」的决策上 —— 那是「改动打在薄弱环节」的证据；");
    Console.WriteLine("接近 0 表示改动和裕度无关。用来判断「有效改动改的是不是薄弱决策」这个假设。");
}

/// <summary>
/// 诊断"搜索自己的估值"能多好地预测最终胜负 —— 这是**不跑对局**的证伪：
/// 如果它没有信息（AUC ≈ 0.5），那么"蒸馏搜索估值"和"改 rollout 策略去改善搜索"两条路
/// 都是在提升一个不存在的信号，可以立刻划掉，省下几个小时的对局。
/// </summary>
internal static void RunSearchValueDiagnostics(string[] args)
{
    var inputPath = ReadOptionValue(args, "--diagnose-search-value")
        ?? throw new ArgumentException("--diagnose-search-value 后必须提供样本文件路径。");

    Console.WriteLine($"诊断搜索估值的信息量：{inputPath}");
    Console.WriteLine($"旁路文件：{WeightTools.SearchValuePath(inputPath)}");
    Console.WriteLine();
    var report = SearchValueDiagnostics.Analyze(inputPath);

    Console.WriteLine($"样本 {report.Rows} 条决策（赢 {report.Wins} ｜ 输 {report.Losses}）");
    Console.WriteLine($"搜索估值：均值 {report.MeanValue:F4} ｜ 标准差 {report.StdValue:F4}");
    Console.WriteLine(
        $"  赢的决策上均值 {report.MeanValueWhenWin:F4} ｜ 输的决策上均值 {report.MeanValueWhenLoss:F4}" +
        $" ｜ 差值 {report.MeanValueWhenWin - report.MeanValueWhenLoss:F4}");
    Console.WriteLine();
    Console.WriteLine("按搜索估值分十档，看实际胜率是否单调：");
    Console.WriteLine("  区间          样本数    实际胜率");
    foreach (var (lowerBound, count, winRate) in report.Buckets)
    {
        Console.WriteLine(
            $"  [{lowerBound:F1},{lowerBound + 0.1:F1})   {count,7}    {winRate,7:P2}");
    }

    Console.WriteLine();
    Console.WriteLine($"★ AUC（排序法）    = {report.Auc:F4}");
    Console.WriteLine($"  AUC（采样交叉验证）= {report.BruteForceAuc:F4}   ← 两条独立算法应当一致");
    if (SearchValueDiagnostics.LastRankDiagnostics is { } diagnostics)
    {
        Console.WriteLine($"  {diagnostics}");
    }
    Console.WriteLine(report.Auc switch
    {
        < 0.53 => "结论：**几乎没有信息**。搜索自己的判断和最终胜负基本无关 —— " +
                  "那么蒸馏它、或者改 rollout 去改善它，都是在提升一个不存在的信号。方向可以划掉。",
        < 0.60 => "结论：信息很弱。比噪声强一点，但不足以支撑「它比叶子强」这个前提。",
        < 0.70 => "结论：有中等信息。值得继续，但要记住它离「可靠」还很远。",
        _ => "结论：**有明确信息**。搜索的判断确实能预测胜负，那么改善它是有意义的目标。"
    });
    Console.WriteLine();
    Console.WriteLine(
        "注意：同一局里几十个决策共享同一个胜负，样本是相关的，所以这个 AUC 不是独立样本检验，" +
        "只能用来看「有没有信号」的量级，不能当显著性。");
}

/// <summary>
/// 解析 <c>--value-source</c>。/// <para>
/// **默认必须是 outcome**：这个项目里"拟合最终胜负"是失败过 9 次的目标，蒸馏（search）是
/// 新东西，不能让它悄悄变成默认行为把旧结论污染掉。
/// </para>
/// </summary>
private static NeuralTrainer.ValueSource ParseValueSourceOption(string[] args, string name)
{
    var raw = ReadOptionValue(args, name);
    if (raw is null)
    {
        return NeuralTrainer.ValueSource.Outcome;
    }

    return raw.ToLowerInvariant() switch
    {
        "outcome" or "win" => NeuralTrainer.ValueSource.Outcome,
        "search" or "distill" => NeuralTrainer.ValueSource.Search,
        _ => throw new ArgumentException(
            $"{name} 只支持 outcome（最终胜负，默认）或 search（旁路文件的搜索估值），收到：{raw}")
    };
}

internal static void RunWeightFitting(string[] args)
{
    var inputPath = ReadOptionValue(args, "--fit-weights")
        ?? throw new ArgumentException("--fit-weights 后必须提供样本文件路径。");
    var epochs = ParseIntegerOption(args, "--epochs", defaultValue: 300, minimum: 1, maximum: 100_000);
    var learningRate = double.Parse(
        ReadOptionValue(args, "--learning-rate") ?? "0.05",
        CultureInfo.InvariantCulture);
    var l2 = double.Parse(ReadOptionValue(args, "--l2") ?? "0.00001", CultureInfo.InvariantCulture);
    var outputPath = ReadOptionValue(args, "--out")
        ?? Path.Combine(ProjectRoot(), "outputs", "fitted-weights.txt");

    Console.WriteLine($"拟合评估权重：{inputPath}（{epochs} 轮，学习率 {learningRate}，L2 {l2}）");
    var result = WeightTools.Fit(inputPath, epochs, learningRate, l2, Console.WriteLine);
    Console.WriteLine();
    Console.WriteLine($"样本数：训练 {result.TrainSampleCount} ｜ 验证 {result.ValidationSampleCount}（按牌局切分）");
    Console.WriteLine();
    Console.WriteLine("                   对数损失    准确率");
    Console.WriteLine($"拟合   训练集      {result.TrainLogLoss:F5}     {result.TrainAccuracy:P2}");
    Console.WriteLine($"手调   训练集      {result.HandTunedTrainLogLoss:F5}     {result.HandTunedTrainAccuracy:P2}");
    Console.WriteLine($"拟合   验证集      {result.ValidationLogLoss:F5}     {result.ValidationAccuracy:P2}");
    Console.WriteLine($"手调   验证集      {result.HandTunedValidationLogLoss:F5}     {result.HandTunedValidationAccuracy:P2}");
    Console.WriteLine();
    Console.WriteLine(result.ValidationLogLoss < result.HandTunedValidationLogLoss
        ? $"结论：拟合权重在验证集上更好（对数损失低 {result.HandTunedValidationLogLoss - result.ValidationLogLoss:F5}）。"
        : $"结论：拟合权重在验证集上没有超过手调值——这套特征或这份数据还不足以支撑替换。");
    WeightTools.SaveWeights(outputPath, result.Weights, 1.0);
    Console.WriteLine($"已写入 {outputPath}（尺度 1）");
    Console.WriteLine("拟合权重：");
    Console.WriteLine(
        "  " + string.Join(", ", result.Weights.Select(weight => weight.ToString("F4", CultureInfo.InvariantCulture))));
}

internal static void RunGreedyVsRandomMatch(
    string firstDeckId,
    string secondDeckId,
    ulong gameSeed,
    bool usesRandomSeed)
{
    var deckA = AgentSelfTests.CreateMatchDeck(firstDeckId, "A");
    var deckB = AgentSelfTests.CreateMatchDeck(secondDeckId, "B");
    var initialState = GameEngine.CreateGame(deckA, deckB, gameSeed);
    var reporter = new BattleConsoleReporter();

    Console.WriteLine(
        $"Player 1：规则牌手（{firstDeckId}）｜ Player 2：随机牌手（{secondDeckId}）");
    Console.WriteLine(usesRandomSeed ? "种子模式：随机（本局可从战报顶部查看种子）" : "种子模式：固定（可复现）");
    reporter.PrintOpening(initialState, gameSeed);
    var match = MatchRunner.PlayToEnd(
        initialState,
        new GreedyPlayerAgent(),
        new RandomPlayerAgent(seed: 202),
        onStep: reporter.PrintStep);

    reporter.PrintConclusion(match);
}

internal static void RunLookaheadVsGreedyMatch(
    string firstDeckId,
    string secondDeckId,
    int rolloutsPerAction,
    int futureTurnHorizon,
    ulong gameSeed,
    bool usesRandomSeed)
{
    var deckA = AgentSelfTests.CreateMatchDeck(firstDeckId, "A");
    var deckB = AgentSelfTests.CreateMatchDeck(secondDeckId, "B");
    var initialState = GameEngine.CreateGame(deckA, deckB, gameSeed);
    var reporter = new BattleConsoleReporter();
    var lookahead = new LookaheadPlayerAgent(rolloutsPerAction, futureTurnHorizon, seed: 90_002);

    Console.WriteLine(
        $"Player 1：前瞻牌手（{firstDeckId}，每个操作 {rolloutsPerAction} 次模拟／向前 {futureTurnHorizon} 回合）｜ Player 2：规则牌手（{secondDeckId}）");
    Console.WriteLine(usesRandomSeed ? "种子模式：随机（本局可从战报顶部查看种子）" : "种子模式：固定（可复现）");
    reporter.PrintOpening(initialState, gameSeed);
    var match = MatchRunner.PlayToEnd(
        initialState,
        lookahead,
        new GreedyPlayerAgent(),
        onStep: step =>
        {
            if (step.ActingPlayer == 0 && lookahead.LastDecision is not null)
            {
                reporter.PrintLookaheadDecision(step, lookahead.LastDecision);
            }

            reporter.PrintStep(step);
        });

    reporter.PrintConclusion(match);
}

internal static string ActionSignature(ActionLogEntry entry)
{
    var action = entry.Action switch
    {
        MulliganAction mulligan => $"M:{string.Join(',', mulligan.ReplaceInstanceIds.Order())}",
        PlayFollowerAction play =>
            $"P:{play.CardInstanceId}:{play.HandCardTargetInstanceId?.ToString() ?? "NONE"}:{string.Join(',', play.EnemyFollowerTargetInstanceIds ?? [])}:{play.ModeChoiceIndex?.ToString() ?? "NONE"}:{string.Join(',', play.OwnHandCardTargetInstanceIds ?? [])}",
        PlayAmuletAction playAmulet => $"A:{playAmulet.CardInstanceId}",
        PlayAccelerateAction accelerate => $"ACC:{accelerate.CardInstanceId}",
        PlaySpellAction playSpell => $"S:{playSpell.CardInstanceId}:{SpellTargetSignature(playSpell.Target)}",
        EvolveAction evolve =>
            $"EVO:{evolve.FollowerInstanceId}:{evolve.ModeChoiceIndex?.ToString() ?? "NONE"}:{string.Join(',', evolve.OwnHandCardTargetInstanceIds ?? [])}",
        SuperEvolveAction superEvolve =>
            $"SUPER:{superEvolve.FollowerInstanceId}:{superEvolve.OtherFollowerTargetInstanceId?.ToString() ?? "NONE"}:{superEvolve.ModeChoiceIndex?.ToString() ?? "NONE"}:{string.Join(',', superEvolve.OwnHandCardTargetInstanceIds ?? [])}",
        UseExtraPlayPointAction => "XPP",
        AttackLeaderAction attackLeader => $"AL:{attackLeader.AttackerInstanceId}",
        AttackFollowerAction attackFollower => $"AF:{attackFollower.AttackerInstanceId}:{attackFollower.DefenderInstanceId}",
        EndTurnAction => "E",
        _ => throw new InvalidOperationException("Unsupported action in replay signature.")
    };

    return $"{entry.TurnNumber}:{entry.Player}:{action}";
}

internal static string SpellTargetSignature(SpellTarget? target) => target switch
{
    null => "NONE",
    EnemyLeaderTarget => "LEADER",
    EnemyFollowerTarget follower => $"FOLLOWER:{follower.FollowerInstanceId}",
    _ => throw new InvalidOperationException("Unsupported spell target in replay signature.")
};

internal static string ParseDeckId(string[] args, string optionName, string defaultDeckId)
{
    var optionIndex = Array.FindIndex(args, argument =>
        string.Equals(argument, optionName, StringComparison.OrdinalIgnoreCase));

    if (optionIndex == -1)
    {
        return defaultDeckId;
    }

    if (optionIndex == args.Length - 1)
    {
        throw new ArgumentException($"{optionName} 后必须提供卡组编号。例如：{optionName} {defaultDeckId}");
    }

    return args[optionIndex + 1];
}

internal static int ParseStatisticsMatchCount(string[] args)
{
    const int defaultMatchCount = 1_000;
    const int maximumMatchCount = 100_000;
    var statsIndex = Array.FindIndex(args, argument =>
        string.Equals(argument, "--stats", StringComparison.OrdinalIgnoreCase));

    if (statsIndex == args.Length - 1)
    {
        return defaultMatchCount;
    }

    if (!int.TryParse(args[statsIndex + 1], out var matchCount) || matchCount is < 1 or > maximumMatchCount)
    {
        throw new ArgumentException(
            $"统计局数必须是 1 到 {maximumMatchCount} 的整数。例如：--stats 1000");
    }

    return matchCount;
}

internal static int ParseIntegerOption(string[] args, string optionName, int defaultValue, int minimum, int maximum)
{
    var optionIndex = Array.FindIndex(args, argument =>
        string.Equals(argument, optionName, StringComparison.OrdinalIgnoreCase));
    if (optionIndex == -1)
    {
        return defaultValue;
    }

    if (optionIndex == args.Length - 1 ||
        !int.TryParse(args[optionIndex + 1], out var value) ||
        value < minimum || value > maximum)
    {
        throw new ArgumentException(
            $"{optionName} 必须是 {minimum} 到 {maximum} 的整数。例：{optionName} {defaultValue}");
    }

    return value;
}
}
