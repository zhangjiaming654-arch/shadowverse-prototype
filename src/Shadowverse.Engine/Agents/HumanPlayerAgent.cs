using Shadowverse.Engine.Game;

namespace Shadowverse.Engine.Agents;

/// <summary>
/// 让真人来出一个动作。
/// <para>
/// 引擎层刻意不认识任何 UI 框架：它只接受一个"给我这个局面和合法动作、还我一个选择"的委托。
/// WinForms 那边负责把这个委托变成"切到 UI 线程、等用户点一下、再把结果传回来"。
/// 这样引擎可以照常在没有界面的环境（基准测试、采样）里编译，也不会被拖进 UI 依赖。
/// </para>
/// <para>
/// **隐藏信息**：真人拿到的只有 <see cref="GameObservation"/>，和 AI 牌手拿到的是同一个东西 ——
/// 看不到对手手牌、看不到牌库顺序。所以"人机对战"是在和 AI 完全相同的规则下打的，
/// 用来体感牌手强度是公平的。
/// </para>
/// </summary>
public sealed class HumanPlayerAgent : IPlayerAgent
{
    private readonly Func<GameObservation, IReadOnlyList<GameAction>, GameAction> _chooseAction;

    public HumanPlayerAgent(
        Func<GameObservation, IReadOnlyList<GameAction>, GameAction> chooseAction)
    {
        ArgumentNullException.ThrowIfNull(chooseAction);
        _chooseAction = chooseAction;
    }

    public GameAction ChooseAction(
        GameObservation observation,
        IReadOnlyList<GameAction> legalActions)
    {
        ArgumentNullException.ThrowIfNull(observation);
        ArgumentNullException.ThrowIfNull(legalActions);
        if (legalActions.Count == 0)
        {
            throw new InvalidOperationException("没有合法动作时不该调用真人牌手。");
        }

        var chosen = _chooseAction(observation, legalActions);

        // 界面可能因为窗口被关掉等原因返回一个不在列表里的动作；宁可炸在这里，
        // 也不要让引擎拿着非法动作去 Apply —— 那样报出来的错会离真正的原因很远。
        if (!legalActions.Contains(chosen))
        {
            throw new InvalidOperationException("真人牌手返回了一个不在合法动作列表里的动作。");
        }

        return chosen;
    }
}
