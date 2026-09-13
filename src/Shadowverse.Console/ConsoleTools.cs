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
    Console.WriteLine("  --opponent-rollout <rule|value>  rollout 里对手用什么策略（默认 rule；value = 把对手当成优化者）");
    Console.WriteLine("  --p2-opponent-rollout <rule|value>  只覆盖第二牌手");
    Console.WriteLine("  --alt-weights-file <路径>  载入集成用的第二套评估权重（不覆盖主权重）");
    Console.WriteLine("  --evaluator-ensemble  第一牌手开启评估函数集成（需配合 --alt-weights-file）");
    Console.WriteLine("  --p2-evaluator-ensemble  第二牌手开启");
    Console.WriteLine("  --train-neural <样本.csv>  训练神经网络叶子评估（--hidden / --epochs / --out）");
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
        _ => throw new ArgumentException(
            $"未知 rollout 策略“{value}”。可选：rule、value")
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

internal static void RunSelfPlayCollection(    string[] args,
    int rollouts,
    int horizon,
    int maxDegreeOfParallelism)
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
        perspectiveDeckId);
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
    var outputPath = ReadOptionValue(args, "--out")
        ?? Path.Combine(ProjectRoot(), "outputs", "neural-leaf.txt");

    Console.WriteLine($"训练神经网络叶子评估：{inputPath}（{epochs} 轮，隐层 {hidden}，学习率 {learningRate}）");
    var result = NeuralTrainer.Train(inputPath, hidden, epochs, learningRate, l2, seed, Console.WriteLine);
    Console.WriteLine();
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
    result.Network.Save(outputPath);
    Console.WriteLine($"已写入 {outputPath}");
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
