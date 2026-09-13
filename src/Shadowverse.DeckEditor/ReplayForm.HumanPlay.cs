using System.Globalization;
using Shadowverse.Engine.Agents;
using Shadowverse.Engine.Cards;
using Shadowverse.Engine.Decks;
using Shadowverse.Engine.Game;
using Shadowverse.Engine.Models;
using Shadowverse.Engine.Simulation;

namespace Shadowverse.DeckEditor;

/// <summary>
/// 人机对战模式。**没有新建窗体** —— 实时对局本质上就是"一份还在增长的回放"，
/// 所以直接复用 <see cref="ReplayForm"/> 已有的战场/手牌/箭头渲染，
/// 只是把 <c>_steps</c> 换成一根后台线程边打边追加的列表。
/// <para>
/// 真人固定坐 Player 0（界面下方）。对手用 <c>_secondAgent</c> 下拉里选中的牌手。
/// </para>
/// <para>
/// **公平性**：真人只拿得到 <see cref="GameObservation"/>，和 AI 拿到的是同一个东西 ——
/// 看不到对手手牌、看不到牌库顺序。
/// </para>
/// </summary>
public sealed partial class ReplayForm
{
    private readonly Panel _humanPanel = new()
    {
        Dock = DockStyle.Right,
        Width = 260,
        BackColor = Color.FromArgb(24, 34, 46),
        Padding = new Padding(8)
    };

    private readonly Button _humanPlayButton = new() { Dock = DockStyle.Top, Height = 34, Text = "开始人机对战" };
    private readonly Label _humanStatus = new()
    {
        Dock = DockStyle.Top,
        Height = 76,
        ForeColor = Color.FromArgb(198, 214, 232),
        Text = "选好你用的卡组（P1）和对手牌手（P2），然后开始。"
    };

    /// <summary>
    /// 版本图例。之前下拉框里把"本会话之前的老快照"叫成"1.0"，而且 2.0 根本不在列表里，
    /// 让人没法判断自己在打哪个版本 —— 所以把定义直接写在界面上。
    /// </summary>
    private readonly Label _agentLegend = new()
    {
        Dock = DockStyle.Top,
        Height = 92,
        ForeColor = Color.FromArgb(150, 172, 196),
        Font = new Font("Consolas", 8F),
        Text =
            "3.0 视野循环{1,3} × 60 推演（活的）\r\n" +
            "2.0 冻结：视野循环{1,3} × 60\r\n" +
            "1.0 冻结：视野 3 × 10 推演\r\n" +
            "旧基线 本会话之前的老快照\r\n" +
            "冻结版本忽略推演下拉框"
    };

    private readonly FlowLayoutPanel _humanActions = new()
    {
        Dock = DockStyle.Fill,
        FlowDirection = FlowDirection.TopDown,
        WrapContents = false,
        AutoScroll = true
    };

    private readonly object _liveGate = new();
    private List<MatchStep>? _liveSteps;
    private TaskCompletionSource<GameAction>? _humanChoice;
    private bool _liveMatchRunning;

    private void BuildHumanPlayPanel()
    {
        var title = new Label
        {
            Dock = DockStyle.Top,
            Height = 26,
            Text = "人机对战",
            Font = new Font(Font, FontStyle.Bold),
            ForeColor = Color.White
        };

        StyleToolbarButton(_humanPlayButton, isPrimary: true);
        _humanPlayButton.Click += (_, _) => StartHumanMatch();

        _humanPanel.Controls.Add(_humanActions);
        _humanPanel.Controls.Add(_humanStatus);
        _humanPanel.Controls.Add(_agentLegend);
        _humanPanel.Controls.Add(_humanPlayButton);
        _humanPanel.Controls.Add(title);
        Controls.Add(_humanPanel);

        // 用户中途关窗时，后台线程会永远等在 TaskCompletionSource 上，必须在这里放它走。
        FormClosing += (_, _) => CancelHumanMatch();
    }

    private void CancelHumanMatch()
    {
        _humanChoice?.TrySetCanceled();
        _humanChoice = null;
    }

    /// <summary>开一局人机对战。真人坐 Player 0，对手用 P2 下拉里选的牌手。</summary>
    private void StartHumanMatch()
    {
        if (_liveMatchRunning)
        {
            return;
        }

        if (_firstDeck.SelectedItem is not ReplayDeckChoice myDeck ||
            _secondDeck.SelectedItem is not ReplayDeckChoice opponentDeck ||
            _secondAgent.SelectedItem is not string opponentAgent)
        {
            _humanStatus.Text = "请先在工具栏选好双方的卡组和对手牌手。";
            return;
        }

        try
        {
            var seed = CreateSeed();
            var rollouts = int.Parse((string)_secondRollouts.SelectedItem!, CultureInfo.InvariantCulture);
            var initial = GameEngine.CreateGame(
                DeckCatalog.Create(myDeck.Id, $"{myDeck.Name} 我"),
                DeckCatalog.Create(opponentDeck.Id, $"{opponentDeck.Name} 对手"),
                seed);

            var steps = new List<MatchStep>();
            lock (_liveGate)
            {
                _liveSteps = steps;
            }

            // 把实时对局接到现有回放上：_initialState + 不断增长的 _steps 就是回放的数据结构。
            _initialState = initial;
            _steps = steps;
            _currentStepIndex = -1;
            _liveMatchRunning = true;
            _humanPlayButton.Enabled = false;
            _humanStatus.Text = $"对手：{opponentAgent}（{rollouts} 次推演）\n对局进行中…";
            ClearHumanActions();
            UpdatePlayback();

            var human = new HumanPlayerAgent(AskHuman);
            var opponent = CreateAgent(opponentAgent, seed + 2, rollouts);

            Task.Run(() =>
            {
                try
                {
                    var result = MatchRunner.PlayToEnd(initial, human, opponent, onStep: AppendLiveStep);
                    RunOnUi(() =>
                    {
                        _liveMatchRunning = false;
                        _humanPlayButton.Enabled = true;
                        ClearHumanActions();
                        _humanStatus.Text = result.Winner == 0
                            ? "你赢了。"
                            : $"你输了（Player {result.Winner + 1} 获胜）。";
                    });
                }
                catch (Exception exception)
                {
                    RunOnUi(() =>
                    {
                        _liveMatchRunning = false;
                        _humanPlayButton.Enabled = true;
                        ClearHumanActions();
                        _humanStatus.Text = "对局中断：" + exception.Message;
                    });
                }
            });
        }
        catch (Exception exception)
        {
            _liveMatchRunning = false;
            _humanPlayButton.Enabled = true;
            MessageBox.Show(exception.Message, "无法开始对局", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>
    /// 后台线程在这里阻塞，等界面把用户点的那一下送回来。
    /// **必须切到 UI 线程**：这个方法是后台线程调的，直接碰控件会抛跨线程异常。
    /// </summary>
    private GameAction AskHuman(GameObservation observation, IReadOnlyList<GameAction> legalActions)
    {
        var completion = new TaskCompletionSource<GameAction>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _humanChoice = completion;

        RunOnUi(() =>
        {
            if (!_liveMatchRunning)
            {
                completion.TrySetCanceled();
                return;
            }

            PresentChoices(observation, legalActions, completion);
        });

        return completion.Task.GetAwaiter().GetResult();
    }

    /// <summary>
    /// 在 UI 线程上把合法动作铺成按钮。
    /// **刻意不自己判断合法性** —— 只把引擎给的 <paramref name="legalActions"/> 翻译成可点的东西。
    /// 自己写一套规则判断迟早会和引擎分叉。
    /// </summary>
    private void PresentChoices(
        GameObservation observation,
        IReadOnlyList<GameAction> legalActions,
        TaskCompletionSource<GameAction> completion)
    {
        ClearHumanActions();

        var header = observation.Phase == GamePhase.Mulligan
            ? "换牌：选择要换掉的牌（不换也点一个）"
            : $"你的回合（第 {observation.TurnNumber} 回合）";
        _humanStatus.Text = header + "\n" + DescribeOwnState(observation);

        foreach (var action in legalActions)
        {
            var button = new Button
            {
                Text = DescribeAction(observation, action),
                Width = _humanActions.ClientSize.Width - 24,
                Height = 30,
                TextAlign = ContentAlignment.MiddleLeft,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(46, 66, 88),
                ForeColor = Color.White
            };
            button.Click += (_, _) =>
            {
                ClearHumanActions();
                completion.TrySetResult(action);
            };
            _humanActions.Controls.Add(button);
        }
    }

    private void ClearHumanActions()
    {
        foreach (Control control in _humanActions.Controls)
        {
            control.Dispose();
        }

        _humanActions.Controls.Clear();
    }

    /// <summary>后台线程每走完一步就追加一次，并让界面跳到最新一步。</summary>
    private void AppendLiveStep(MatchStep step)
    {
        int count;
        lock (_liveGate)
        {
            if (_liveSteps is null)
            {
                return;
            }

            _liveSteps.Add(step);
            count = _liveSteps.Count;
        }

        RunOnUi(() =>
        {
            _currentStepIndex = count - 1;
            UpdatePlayback();
        });
    }

    private void RunOnUi(Action action)
    {
        if (IsDisposed || Disposing)
        {
            return;
        }

        if (InvokeRequired)
        {
            BeginInvoke(action);
        }
        else
        {
            action();
        }
    }

    private static string DescribeOwnState(GameObservation observation)
    {
        var self = observation.Self;
        var opponent = observation.Opponent;
        return $"你 {self.Health} 血 ｜ PP {self.CurrentPlayPoints}/{self.MaxPlayPoints}" +
               $" ｜ 进化 {self.EvolutionPoints}+{self.SuperEvolutionPoints}\n" +
               $"对手 {opponent.Health} 血 ｜ 手牌 {opponent.HandCount} 张";
    }

    /// <summary>把一个动作翻译成人话。认不出来的动作退回 ToString()，不隐藏任何选项。</summary>
    private static string DescribeAction(GameObservation observation, GameAction action)
    {
        string CardName(int instanceId) =>
            observation.OwnHand.FirstOrDefault(card => card.InstanceId == instanceId)?.Definition.Name
            ?? $"#{instanceId}";

        string FollowerName(int instanceId) =>
            observation.Self.Board.FirstOrDefault(follower => follower.InstanceId == instanceId)?.CardId is { } id
                ? CardCatalog.Get(id).Name
                : $"#{instanceId}";

        string TargetName(int instanceId) =>
            observation.Opponent.Board.FirstOrDefault(follower => follower.InstanceId == instanceId)?.CardId
                is { } id
                ? CardCatalog.Get(id).Name
                : $"#{instanceId}";

        return action switch
        {
            MulliganAction mulligan => mulligan.ReplaceInstanceIds.Count == 0
                ? "不换牌"
                : "换掉：" + string.Join("、", mulligan.ReplaceInstanceIds.Select(CardName)),
            PlayFollowerAction play => $"打出随从 {CardName(play.CardInstanceId)}",
            PlayAmuletAction amulet => $"打出护符 {CardName(amulet.CardInstanceId)}",
            PlaySpellAction spell => $"打出法术 {CardName(spell.CardInstanceId)}",
            PlayCrystallizeAction crystallize => $"结晶 {CardName(crystallize.CardInstanceId)}",
            PlayAccelerateAction accelerate => $"加速 {CardName(accelerate.CardInstanceId)}",
            EvolveAction evolve => $"进化 {FollowerName(evolve.FollowerInstanceId)}",
            SuperEvolveAction superEvolve => $"超进化 {FollowerName(superEvolve.FollowerInstanceId)}",
            AttackLeaderAction attackLeader => $"{FollowerName(attackLeader.AttackerInstanceId)} 攻击对方主战者",
            AttackFollowerAction attackFollower =>
                $"{FollowerName(attackFollower.AttackerInstanceId)} 攻击 {TargetName(attackFollower.DefenderInstanceId)}",
            UseExtraPlayPointAction => "使用额外 PP",
            EndTurnAction => "结束回合",
            _ => action.ToString() ?? "未知动作"
        };
    }
}
