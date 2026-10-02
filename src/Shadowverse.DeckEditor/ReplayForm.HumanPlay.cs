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
/// <b>公平性</b>：真人只拿得到 <see cref="GameObservation"/>，和 AI 拿到的是同一个东西 ——
/// 看不到对手手牌、看不到牌库顺序。
/// </para>
/// <para>
/// <b>操作方式</b>（照抄影之诗/炉石，不自己发明）：
/// 换牌是"点手牌标记 + 一个确认按钮"；出牌和攻击是<b>拖拽</b>；进化是右键。
/// 拖拽只负责在引擎给的 legalActions 里<b>筛</b>，筛不中就让用户从菜单里挑 ——
/// 所以任何合法动作都不会变得点不到，而界面也不会自己发明合法性判断。
/// </para>
/// </summary>
public sealed partial class ReplayForm
{
    /// <summary>拖拽落点属于哪一块区域。</summary>
    private enum HumanZone
    {
        None,
        OwnHand,
        OwnBoard,
        OpponentBoard,
        OpponentLeader,
        OwnLeader
    }

    private readonly record struct HumanHit(HumanZone Zone, int? InstanceId);

    /// <summary>真人固定坐 Player 0（界面下方）。对手是 Player 1。</summary>
    private const int HumanPlayerIndex = 0;

    /// <summary>思考面板最多列几个候选。列表太长会把面板撑满，反而不想看。</summary>
    private const int ThinkingRows = 6;

    /// <summary>行动记录最多保留多少步。再多就得滚，而滚起来就没法"一眼看到刚才发生了什么"。</summary>
    private const int ActionLogLines = 40;

    private readonly Panel _humanPanel = new()
    {
        Dock = DockStyle.Right,
        Width = 340,
        BackColor = Color.FromArgb(24, 34, 46),
        Padding = new Padding(8)
    };

    private readonly Button _humanPlayButton = new() { Dock = DockStyle.Top, Height = 34, Text = "开始人机对战" };
    private readonly Label _humanStatus = new()
    {
        Dock = DockStyle.Top,
        Height = 96,
        ForeColor = Color.FromArgb(198, 214, 232),
        Font = new Font("Microsoft YaHei UI", 8.5F),
        Text = "选好你用的卡组（P1）和对手牌手（P2），然后开始。"
    };

    /// <summary>对手是谁（哪个版本、多少次推演）。每一步都会重画状态栏，所以要把它记下来，不能只写一次。</summary>
    private string _humanOpponentSummary = string.Empty;

    /// <summary>
    /// **纹章区**：显示双方主战者已经获得的纹章。
    /// <para>
    /// 纹章是**公开信息**，所以对手那一侧也要看得见 —— 以前界面上完全没有这块，
    /// 用户看不出谁身上挂了什么纹章、还能触发什么。
    /// </para>
    /// <para>
    /// 没有纹章时高度归零，不占版面（这段面板本来就挤，多一行会顶掉下面的内容）。
    /// </para>
    /// </summary>
    private readonly Label _crestZone = new()
    {
        Dock = DockStyle.Top,
        Height = 0,
        ForeColor = Color.FromArgb(206, 186, 255),
        Font = new Font("Microsoft YaHei UI", 8.5F),
        Text = string.Empty
    };

    /// <summary>纹章的完整能力说明放在悬停提示里（名称短、说明长，全塞进版面会顶掉别的内容）。</summary>
    private readonly ToolTip _crestZoneToolTip = new();

    /// <summary>操作提示。换牌阶段和出牌阶段讲的话不一样，所以单独一个标签。</summary>
    private readonly Label _humanHint = new()
    {
        Dock = DockStyle.Top,
        Height = 78,
        ForeColor = Color.FromArgb(255, 214, 130),
        Font = new Font("Microsoft YaHei UI", 8.5F),
        Text = string.Empty
    };

    /// <summary>
    /// 版本图例。之前下拉框里把"本会话之前的老快照"叫成"1.0"，而且 2.0 根本不在列表里，
    /// 让人没法判断自己在打哪个版本 —— 所以把定义直接写在界面上。
    /// </summary>
    private readonly Label _agentLegend = new()
    {
        Dock = DockStyle.Top,
        Height = 74,
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

    private readonly Panel _thinkingPanel = new()
    {
        Dock = DockStyle.Bottom,
        Height = 190,
        BackColor = Color.FromArgb(18, 27, 38),
        Padding = new Padding(6)
    };

    private readonly Label _thinkingTitle = new()
    {
        Dock = DockStyle.Top,
        Height = 18,
        Text = "对手的思考（只看得到公开信息）",
        ForeColor = Color.FromArgb(255, 214, 130),
        Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Bold)
    };

    /// <summary>
    /// 用只读多行文本框而不是 Panel+Label：这里要能滚。Dock/AutoScroll 和自动高度的组合
    /// 在 WinForms 里很容易出怪样子，而界面我没法自动化验证 —— 文本框自带滚动，最省风险。
    /// </summary>
    private readonly TextBox _thinkingBody = new()
    {
        Dock = DockStyle.Fill,
        Multiline = true,
        ReadOnly = true,
        ScrollBars = ScrollBars.Vertical,
        BackColor = Color.FromArgb(18, 27, 38),
        ForeColor = Color.FromArgb(198, 214, 232),
        BorderStyle = BorderStyle.None,
        Font = new Font("Consolas", 8.5F),
        TabStop = false,
        WordWrap = false
    };

    /// <summary>
    /// 对手每一次决策的思考（步骤下标 → 决策），供界面按当前步回看。
    /// <para>
    /// <b>必须在后台线程上同步抓取</b>：<c>onStep</c> 是在对局线程里同步调用的，而界面更新是排队执行的。
    /// 等界面轮到那一步时，牌手早就做完后面好几个决策了，<c>LastDecision</c> 已经不是这一次的。
    /// </para>
    /// </summary>
    private readonly List<(int StepIndex, LookaheadDecision Decision)> _opponentDecisions = [];

    private IPlayerAgent? _opponentAgentInstance;
    private bool _opponentSupportsThinking;

    private readonly Panel _logPanel = new()
    {
        Dock = DockStyle.Bottom,
        Height = 150,
        BackColor = Color.FromArgb(18, 27, 38),
        Padding = new Padding(6)
    };

    private readonly Label _logTitle = new()
    {
        Dock = DockStyle.Top,
        Height = 18,
        Text = "行动记录（最新在上）",
        ForeColor = Color.FromArgb(255, 214, 130),
        Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Bold)
    };

    /// <summary>
    /// 同上，用只读文本框是为了白拿滚动条 —— Dock / AutoScroll 和自动高度的组合在 WinForms 里
    /// 很难一次写对，而界面我没法自动化验证。
    /// </summary>
    private readonly TextBox _logBody = new()
    {
        Dock = DockStyle.Fill,
        Multiline = true,
        ReadOnly = true,
        ScrollBars = ScrollBars.Vertical,
        BackColor = Color.FromArgb(18, 27, 38),
        ForeColor = Color.FromArgb(198, 214, 232),
        BorderStyle = BorderStyle.None,
        Font = new Font("Consolas", 8.5F),
        TabStop = false
    };

    private readonly object _liveGate = new();
    private List<MatchStep>? _liveSteps;
    private TaskCompletionSource<GameAction>? _humanChoice;
    private bool _liveMatchRunning;

    /// <summary>当前正在等真人决定的那些合法动作。拖拽/点击都在这里面筛，不另起一套判断。</summary>
    private IReadOnlyList<GameAction>? _pendingActions;

    private GameObservation? _pendingObservation;

    /// <summary>换牌阶段被标记"要换掉"的手牌实例。换牌是点子集，所以必须记住整张标记表。</summary>
    private readonly HashSet<int> _mulliganMarks = [];

    /// <summary>
    /// 正在等用户选【模式】的那些候选动作。非空时整个右侧面板换成选模式界面。
    /// <para>
    /// 为什么要单独一个状态：模式卡牌一次手势会筛出"同一张牌、同一个目标、只有模式不同"的好几个动作，
    /// 它们靠通用菜单完全没法区分（文字一模一样）。模式名本身就是完整的能力说明，
    /// 所以必须专门铺出来给用户选。
    /// </para>
    /// </summary>
    private IReadOnlyList<GameAction>? _modeSelection;

    /// <summary>
    /// 整个窗体复用的一个弹出菜单。**不要每次新建再 Dispose** —— 见 <see cref="ShowMenu"/>。
    /// </summary>
    private ContextMenuStrip? _actionMenu;

    /// <summary>已经挂过鼠标处理器的格子。**这是防重复挂的**：格子是每次重画新建的（所以重画后要清空），
    /// 但如果在同一次渲染里对同一个格子挂两遍，一次点击就会触发两次 ——
    /// 换牌标记会点了等于没点。这种 bug 在界面上表现为"没反应"，极难查。
    /// </summary>
    private readonly HashSet<Control> _interactionAttached = [];

    /// <summary>是否展开了"全部动作"兜底列表。</summary>
    private bool _humanShowAllActions;

    private Panel? _dragSource;
    private int _dragInstanceId;
    private bool _dragIsHandCard;
    private Point _dragStartScreen;
    private bool _dragActive;

    /// <summary>被拖动格子原来的外观，松手时要还原。</summary>
    private Color _dragSourceColor;
    private BorderStyle _dragSourceBorder;

    /// <summary>拖拽时被高亮的落点控件，以及它原来的样子（松手/移开时要还原）。</summary>
    private Panel? _highlighted;
    private Color _highlightedColor;
    private BorderStyle _highlightedBorder;

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

        _thinkingPanel.Controls.Add(_thinkingBody);
        _thinkingPanel.Controls.Add(_thinkingTitle);
        _logPanel.Controls.Add(_logBody);
        _logPanel.Controls.Add(_logTitle);

        _humanPanel.Controls.Add(_humanActions);
        // 顺序有讲究：WinForms 的 Dock 布局是"后加进去的先排"。
        // Fill 必须最先加（最后排、吃掉剩下的空间），Bottom 要排在 Fill 之后、各个 Top 之前。
        // 想要"思考在上、记录在下"，就得反着加：先 log 再 thinking。
        _humanPanel.Controls.Add(_logPanel);
        _humanPanel.Controls.Add(_thinkingPanel);
        _humanPanel.Controls.Add(_humanHint);
        _humanPanel.Controls.Add(_humanStatus);
        // 纹章区紧贴状态栏下方；没有纹章时高度为 0，不占版面。
        _humanPanel.Controls.Add(_crestZone);
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
        _pendingActions = null;
        _pendingObservation = null;
        _actionMenu?.Dispose();
        _actionMenu = null;
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
            _mulliganMarks.Clear();
            _humanShowAllActions = false;
            _modeSelection = null;
            _opponentDecisions.Clear();
            _humanPlayButton.Enabled = false;
            _humanOpponentSummary = $"{opponentAgent}（{rollouts} 次推演）";
            _humanStatus.Text = $"对手：{_humanOpponentSummary}\n对局进行中…";
            ClearHumanActions();
            UpdatePlayback();

            var human = new HumanPlayerAgent(AskHuman);
            var opponent = CreateAgent(opponentAgent, seed + 2, rollouts);
            _opponentAgentInstance = opponent;
            // 规则牌手不做搜索，没有估值可看 —— 这时面板要说清楚，而不是一直空着。
            _opponentSupportsThinking = opponent is LookaheadPlayerAgent
                or LookaheadPlayerAgentV1
                or LookaheadPlayerAgentV2
                or BaselineLookaheadPlayerAgent;

            Task.Run(() =>
            {
                try
                {
                    var result = MatchRunner.PlayToEnd(initial, human, opponent, onStep: AppendLiveStep);
                    RunOnUi(() =>
                    {
                        _liveMatchRunning = false;
                        _pendingActions = null;
                        _pendingObservation = null;
                        _humanPlayButton.Enabled = true;
                        _humanHint.Text = string.Empty;
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
                        _pendingActions = null;
                        _pendingObservation = null;
                        _humanPlayButton.Enabled = true;
                        _humanHint.Text = string.Empty;
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

        var scheduled = RunOnUi(() =>
        {
            if (!_liveMatchRunning)
            {
                completion.TrySetCanceled();
                return;
            }

            try
            {
                PresentChoices(observation, legalActions, completion);
            }
            catch (Exception exception)
            {
                // 界面出问题时**绝不能把对局线程永远挂住** —— 它正阻塞在 completion 上，
                // 而用户看到的现象只是"卡死了"，连原因都看不到。
                // 宁可让这一局干净地报错结束。
                _humanStatus.Text = "界面出错，对局已中断：" + exception.Message;
                completion.TrySetException(exception);
            }
        });

        if (!scheduled)
        {
            // 窗体已经关掉，这次询问永远送不到界面上。不在这里解开的话，
            // 对局线程会一直等下去，连进程都退不掉。
            completion.TrySetCanceled();
        }

        return completion.Task.GetAwaiter().GetResult();
    }

    /// <summary>
    /// 在 UI 线程上把这一手的可操作方式摆出来。
    /// **刻意不自己判断合法性** —— 只把引擎给的 <paramref name="legalActions"/> 变成能拖、能点的东西。
    /// </summary>
    private void PresentChoices(
        GameObservation observation,
        IReadOnlyList<GameAction> legalActions,
        TaskCompletionSource<GameAction> completion)
    {
        _pendingObservation = observation;
        _pendingActions = legalActions;
        _humanShowAllActions = false;
        _modeSelection = null;

        if (observation.Phase == GamePhase.Mulligan)
        {
            _mulliganMarks.Clear();
            _humanStatus.Text = $"对手：{_humanOpponentSummary}\n换牌阶段 ｜ {DescribeOwnState(observation)}";
            _humanHint.Text = "点击下方手牌标记要换掉的牌（再点一次取消），选好后按【确认换牌】。";
        }
        else
        {
            _humanStatus.Text =
                $"对手：{_humanOpponentSummary}\n你的回合（第 {observation.TurnNumber} 回合） ｜ {DescribeOwnState(observation)}";
            _humanHint.Text =
                "拖动自己的手牌到己方场上出牌；\n" +
                "拖动自己的随从到敌方随从 / 主战者上 → 攻击，\n或指定【进化时】的目标；\n" +
                "拖到己方另一个随从上 → 超进化时带动它；\n" +
                "点一下自己的随从 → 菜单里选【进化】/【超进化】。";
        }

        RefreshCrestZone(observation);

        _humanChoice = completion;
        ClearHumanActions();
        RebuildHumanActions();
        RefreshHumanInteraction();
    }

    /// <summary>当前是不是换牌阶段。</summary>
    private bool IsMulliganPhase => _pendingObservation?.Phase == GamePhase.Mulligan;

    /// <summary>
    /// 把当前这一手的操作按钮重新铺一遍：常驻按钮 + "全部动作"兜底列表。
    /// 兜底列表是<b>故意保留</b>的：界面没法自动化验证，万一某个手势没接上，
    /// 用户至少还能从完整列表里点到那个动作，不会卡死。
    /// </summary>
    private void RebuildHumanActions()
    {
        if (_pendingActions is not { } actions || _pendingObservation is not { } observation)
        {
            return;
        }

        // 正在等用户选【模式】：这时整个面板换成选模式界面，其它按钮全部让位。
        if (_modeSelection is { } modeCandidates)
        {
            BuildModeChoices(modeCandidates, observation);
            return;
        }

        if (IsMulliganPhase)
        {
            var confirm = CreateHumanButton(
                $"确认换牌（换 {_mulliganMarks.Count} 张）",
                primary: true);
            confirm.Click += (_, _) => SubmitMulligan();
            _humanActions.Controls.Add(confirm);
        }
        else
        {
            foreach (var action in HumanActionResolver.Direct(actions))
            {
                var label = action is EndTurnAction ? "结束回合" : DescribeAction(observation, action);
                var button = CreateHumanButton(label, primary: action is EndTurnAction);
                button.Click += (_, _) => CommitHumanAction(action);
                _humanActions.Controls.Add(button);
            }
        }

        var toggle = CreateHumanButton(
            _humanShowAllActions ? $"收起全部动作（{actions.Count}）" : $"显示全部动作（{actions.Count}）",
            primary: false);
        toggle.Click += (_, _) =>
        {
            _humanShowAllActions = !_humanShowAllActions;
            ClearHumanActions();
            RebuildHumanActions();
            // 刻意**不**在这里调 RefreshHumanInteraction：它会把鼠标处理器再挂一遍，
            // 而这次展开只是换按钮列表、没动卡牌格子。（重复挂 = 一次点击触发两次。）
        };
        _humanActions.Controls.Add(toggle);

        if (!_humanShowAllActions)
        {
            return;
        }

        foreach (var action in actions)
        {
            var button = CreateHumanButton(DescribeAction(observation, action), primary: false);
            button.Click += (_, _) => CommitHumanAction(action);
            _humanActions.Controls.Add(button);
        }
    }

    private Button CreateHumanButton(string text, bool primary)
    {
        var button = new Button
        {
            Text = text,
            Width = Math.Max(180, _humanActions.ClientSize.Width - 24),
            Height = 30,
            TextAlign = ContentAlignment.MiddleLeft,
            FlatStyle = FlatStyle.Flat,
            BackColor = primary ? Color.FromArgb(64, 116, 88) : Color.FromArgb(46, 66, 88),
            ForeColor = Color.White
        };
        return button;
    }

    /// <summary>把用户标记的换牌子集提交给引擎。找不到完全一致的动作就什么都不做并说明原因。</summary>
    private void SubmitMulligan()
    {
        if (_pendingActions is not { } actions)
        {
            return;
        }

        var action = HumanActionResolver.Mulligan(actions, _mulliganMarks);
        if (action is null)
        {
            // 理论上不该发生（引擎会给全部子集）。真发生了就明说，不要静默换一张别的。
            _humanHint.Text = "引擎没有提供这个换牌子集，请用【显示全部动作】里的选项。";
            return;
        }

        CommitHumanAction(action);
    }

    private void CommitHumanAction(GameAction action)
    {
        _pendingActions = null;
        _pendingObservation = null;
        _modeSelection = null;
        _mulliganMarks.Clear();
        ClearHumanActions();
        _humanHint.Text = string.Empty;
        _humanChoice?.TrySetResult(action);
    }

    /// <summary>清掉旧按钮再铺新的。两件事必须成对做，所以包成一个方法。</summary>
    private void RefreshActionButtons()
    {
        ClearHumanActions();
        RebuildHumanActions();
    }

    /// <summary>
    /// 【模式】卡牌的专门选择界面。
    /// <para>
    /// 每个模式一个大按钮，标签直接用模式名 —— 模式名本身就是完整的能力说明
    /// （"对对手所有随从造成5点伤害并回复1点进化点"），不需要再翻译一层。
    /// 通用菜单在这里是不行的：所有候选的文字完全一样，用户没法选。
    /// </para>
    /// </summary>
    private void BuildModeChoices(IReadOnlyList<GameAction> candidates, GameObservation observation)
    {
        var header = new Label
        {
            AutoSize = false,
            Width = Math.Max(180, _humanActions.ClientSize.Width - 24),
            Height = 22,
            Text = "选择要发动的【模式】",
            ForeColor = Color.FromArgb(255, 214, 130),
            Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold)
        };
        _humanActions.Controls.Add(header);

        foreach (var group in candidates.GroupBy(HumanActionResolver.ModeIndexOf))
        {
            var sameMode = group.ToList();
            var index = group.Key ?? 0;
            var modeName = HumanActionResolver.ModeNameOf(observation, sameMode[0]) ?? $"模式 {index + 1}";

            var button = CreateHumanButton($"{index + 1}. {modeName}", primary: true);
            // 模式名可以很长，按钮要够高并且允许折行，不能省略号截断 ——
            // 截断之后各模式看起来又会差不多。
            button.Height = 52;
            button.AutoEllipsis = false;
            button.Click += (_, _) =>
            {
                _modeSelection = null;
                if (sameMode.Count == 1)
                {
                    CommitHumanAction(sameMode[0]);
                    return;
                }

                // 选完模式还有多个变体（例如同一模式下还要选目标），交给通用菜单。
                RefreshActionButtons();
                ShowActionMenu(sameMode);
            };
            _humanActions.Controls.Add(button);
        }

        var cancel = CreateHumanButton("取消（重新选择）", primary: false);
        cancel.Click += (_, _) =>
        {
            _modeSelection = null;
            _humanHint.Text = string.Empty;
            RefreshActionButtons();
        };
        _humanActions.Controls.Add(cancel);
    }

    /// <summary>
    /// 一个手势筛出多个合法变体时的分派。
    /// 只在模式上不同的 → 进专门的选模式界面；其余 → 通用菜单。
    /// </summary>
    private void BeginAmbiguityResolution(IReadOnlyList<GameAction> candidates)
    {
        if (candidates.Count == 1)
        {
            CommitHumanAction(candidates[0]);
            return;
        }

        if (HumanActionResolver.IsModeOnlyChoice(candidates))
        {
            _modeSelection = candidates;
            _humanHint.Text = "这张牌要选一个【模式】发动，在右边选。";
            RefreshActionButtons();
            return;
        }

        ShowActionMenu(candidates);
    }

    /// <summary>
    /// 清空动作列表。
    /// <para>
    /// **释放必须延后一拍**：这个方法经常是从某个按钮自己的 Click 处理器里调用的
    /// （点一下 → 提交动作 → 清空列表）。当场 Dispose 掉正在执行事件的那个控件，
    /// WinForms 处理完事件之后还会去碰它。
    /// </para>
    /// </summary>
    private void ClearHumanActions()
    {
        var stale = _humanActions.Controls.Cast<Control>().ToArray();
        _humanActions.Controls.Clear();
        if (stale.Length == 0)
        {
            return;
        }

        void Release()
        {
            foreach (var control in stale)
            {
                control.Dispose();
            }
        }

        if (IsDisposed || Disposing || !IsHandleCreated)
        {
            Release();
            return;
        }

        BeginInvoke(Release);
    }

    // ───────────────────────────── 换牌：点击标记 ─────────────────────────────

    /// <summary>把一个手牌格子接上"点击切换换牌标记"。</summary>
    private void AttachMulliganToggle(Control tile, int cardInstanceId)
    {
        void Toggle(object? _, MouseEventArgs args)
        {
            if (args.Button != MouseButtons.Left)
            {
                return;
            }

            if (!_mulliganMarks.Remove(cardInstanceId))
            {
                _mulliganMarks.Add(cardInstanceId);
            }

            ApplyMulliganMarks();
            // 按钮上的张数要跟着变，但重建整套控件会把正在处理点击的那个控件销毁掉，
            // 所以这里只改文字。
            foreach (Control control in _humanActions.Controls)
            {
                if (control is Button button && button.Text.StartsWith("确认换牌", StringComparison.Ordinal))
                {
                    button.Text = $"确认换牌（换 {_mulliganMarks.Count} 张）";
                }
            }
        }

        AttachToTile(tile, Toggle, null, null);
    }

    /// <summary>把标记状态画到已有的手牌格子上：换掉的牌变暗、边框变 3D、牌名前加一个 ✕。</summary>
    private void ApplyMulliganMarks()
    {
        foreach (Panel tile in _selfHand.Controls.OfType<Panel>())
        {
            if (tile.Tag is not CardInstance card)
            {
                continue;
            }

            var marked = _mulliganMarks.Contains(card.InstanceId);
            tile.BackColor = marked ? Color.FromArgb(120, 46, 52) : TypeColor(card.Definition.Type);
            tile.BorderStyle = marked ? BorderStyle.Fixed3D : BorderStyle.FixedSingle;

            // 标记画在**卡牌自己的文字标签**上，而不是加一个覆盖用的子标签。
            // 子标签会挡住宿主的鼠标事件 —— 鼠标点在角标上就"没反应"了，
            // 而角标正好压在卡牌上，用户很容易点到。原文存在标签的 Tag 里以便还原。
            var body = tile.Controls.OfType<Label>().FirstOrDefault();
            if (body is not null)
            {
                var original = body.Tag as string ?? body.Text;
                body.Tag = original;
                body.Text = marked ? "✕ " + original : original;
            }
        }
    }

    /// <summary>
    /// 把"现在能做什么"直接画在手牌和场上：能打出的牌正常显示，打不出的变暗；
    /// 能攻击 / 能进化的随从各挂一个角标。
    /// <para>
    /// 依据<b>完全是引擎给的 legalActions</b>，不复制任何规则判断 ——
    /// 所以不会出现"看起来能打、点下去却没反应"这种界面和引擎不一致的情况。
    /// 打不出的牌变暗是炉石的做法，一眼就能看出这一手哪些牌真能用。
    /// </para>
    /// <para>
    /// <b>调用顺序有要求</b>：必须在挂鼠标处理器<b>之前</b>调用。角标是子控件、会挡住宿主格子的鼠标事件，
    /// 只有在这一刻就存在，后面的 <c>AttachToTile</c> 递归才会连角标一起挂上处理器。
    /// 决策期间 legalActions 是固定的、角标不会中途新建，所以这个顺序是够的。
    /// </para>
    /// </summary>
    private void ApplyAvailabilityVisuals()
    {
        if (_pendingActions is not { } actions)
        {
            return;
        }

        foreach (Panel tile in _selfHand.Controls.OfType<Panel>())
        {
            if (tile.Tag is not CardInstance card)
            {
                continue;
            }

            var playable = actions.Any(action =>
                HumanActionResolver.PlayedCardOf(action) == card.InstanceId);
            tile.BackColor = playable ? TypeColor(card.Definition.Type) : Color.FromArgb(46, 52, 60);
            tile.BorderStyle = BorderStyle.FixedSingle;
        }

        foreach (Panel slot in _selfBoard.Controls.OfType<Panel>())
        {
            if (slot.Tag is not FollowerInstance follower)
            {
                continue;
            }

            var canAttack = actions.Any(action => action switch
            {
                AttackLeaderAction attack => attack.AttackerInstanceId == follower.InstanceId,
                AttackFollowerAction attack => attack.AttackerInstanceId == follower.InstanceId,
                _ => false
            });
            var canEvolve = HumanActionResolver.Evolve(actions, follower.InstanceId).Count > 0;

            SetCornerBadge(
                slot, "AttackBadge", canAttack ? "⚔ 可攻击" : null,
                Color.FromArgb(168, 92, 34), rightAligned: false);
            SetCornerBadge(
                slot, "EvolveBadge", canEvolve ? "★ 可进化" : null,
                Color.FromArgb(168, 140, 34), rightAligned: true);
        }
    }

    /// <summary>
    /// 在格子上挂 / 更新 / 摘掉一个小角标。
    /// 用绝对定位而不是 Dock —— 格子里已经有一个 Dock=Fill 的正文标签，
    /// 再塞 Dock 控件就要靠 z 序去抢空间，行为不直观。
    /// </summary>
    private static void SetCornerBadge(
        Panel host,
        string name,
        string? text,
        Color backColor,
        bool rightAligned)
    {
        var existing = host.Controls.OfType<Label>().FirstOrDefault(label => label.Name == name);

        if (text is null)
        {
            if (existing is not null)
            {
                host.Controls.Remove(existing);
                existing.Dispose();
            }

            return;
        }

        if (existing is not null)
        {
            existing.Text = text;
            return;
        }

        const int width = 62;
        var badge = new Label
        {
            Name = name,
            Dock = DockStyle.None,
            Location = new Point(rightAligned ? Math.Max(0, host.ClientSize.Width - width) : 0, 0),
            Size = new Size(width, 15),
            Anchor = rightAligned
                ? AnchorStyles.Top | AnchorStyles.Right
                : AnchorStyles.Top | AnchorStyles.Left,
            Text = text,
            BackColor = backColor,
            ForeColor = Color.White,
            Font = new Font("Microsoft YaHei UI", 7F, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleCenter
        };
        host.Controls.Add(badge);
        badge.BringToFront();
    }

    // ───────────────────────────── 拖拽出牌 / 攻击 ─────────────────────────────

    /// <summary>把一个手牌格子接上"拖出去出牌"。</summary>
    private void AttachHandDrag(Panel tile, int cardInstanceId)
    {
        // 点一下（不拖）给一句提示。手牌不像场上的随从那样有菜单可开，
        // 但如果什么都不做，用户会以为界面坏了。
        BeginDragGestures(
            tile,
            cardInstanceId,
            isHandCard: true,
            onTap: () => _humanHint.Text =
                "出牌要靠拖动：把牌拖到己方场上打出；需要指定目标的牌，拖到敌方随从/主战者上。");
    }

    /// <summary>
    /// 把一个己方场上格子接上"拖出去攻击"和"右键打开进化菜单"。
    /// 左键拖 = 攻击（有方向性，适合拖），右键 = 进化/超进化（是"选一个"而不是"指向一个目标"）。
    /// </summary>
    private void AttachFollowerDrag(Panel slot, int followerInstanceId)
    {
        void OpenEvolveMenu()
        {
            if (_pendingActions is not { } actions || _pendingObservation is not { } observation)
            {
                return;
            }

            var evolve = HumanActionResolver.Evolve(actions, followerInstanceId);
            var alreadyEvolved = slot.Tag is FollowerInstance { IsEvolved: true };

            // 菜单里**永远**同时列出【进化】和【超进化】，不能用的一项灰掉并写明原因。
            // 之前是"不可用就干脆不显示"，结果用户点了没反应，也分不清是缺 EP、
            // 缺 SEP、还是这只随从已经进化过了 —— 那就是"点不动"。
            ShowMenu(menu =>
            {
                menu.Items.Add(BuildEvolveItem("进化", "EP", observation.Self.EvolutionPoints, isSuper: false));
                menu.Items.Add(BuildEvolveItem("超进化", "SEP", observation.Self.SuperEvolutionPoints, isSuper: true));
            });

            ToolStripMenuItem BuildEvolveItem(string title, string pointName, int points, bool isSuper)
            {
                var variants = evolve
                    .Where(action => isSuper ? action is SuperEvolveAction : action is EvolveAction)
                    .ToList();
                var item = new ToolStripMenuItem(title);

                if (variants.Count == 0)
                {
                    item.Text = $"{title}（{EvolveBlockReason(pointName, points, alreadyEvolved)}）";
                    item.Enabled = false;
                    return item;
                }

                // 只有一个变体：点这一项就是它。
                if (variants.Count == 1)
                {
                    var captured = variants[0];
                    item.Click += (_, _) => CommitHumanAction(captured);
                    return item;
                }

                // 多个变体：只在模式上不同就交给专门的选模式界面，否则展开成二级菜单。
                if (HumanActionResolver.IsModeOnlyChoice(variants))
                {
                    item.Click += (_, _) => BeginAmbiguityResolution(variants);
                    return item;
                }

                foreach (var variant in variants)
                {
                    var child = new ToolStripMenuItem(DescribeAction(observation, variant));
                    var captured = variant;
                    child.Click += (_, _) => CommitHumanAction(captured);
                    item.DropDownItems.Add(child);
                }

                return item;
            }
        }

        // 左键拖 = 攻击；左键点一下（没拖动）= 打开菜单；右键 = 打开菜单。
        BeginDragGestures(slot, followerInstanceId, isHandCard: false, onTap: OpenEvolveMenu);
        AttachMouseUpDeep(slot, (_, args) =>
        {
            if (args.Button == MouseButtons.Right)
            {
                OpenEvolveMenu();
            }
        });
    }

    /// <summary>
    /// 这一项为什么不能用。只报能从 observation 里<b>确认</b>的原因；
    /// 确认不了就老实说"当前不可用"，不编一个可能不对的理由。
    /// </summary>
    private static string EvolveBlockReason(string pointName, int points, bool alreadyEvolved)
    {
        if (alreadyEvolved)
        {
            return "这只随从已经进化过";
        }

        return points <= 0 ? $"{pointName} 不足" : "当前不可用";
    }

    /// <summary>一个手势对应多个合法变体时，把引擎给的变体原样列出来让用户选 —— 不猜。</summary>
    private void ShowActionMenu(IReadOnlyList<GameAction> candidates)
    {
        if (_pendingObservation is not { } observation)
        {
            return;
        }

        ShowMenu(menu =>
        {
            foreach (var candidate in candidates)
            {
                var item = new ToolStripMenuItem(DescribeAction(observation, candidate));
                var captured = candidate;
                item.Click += (_, _) => CommitHumanAction(captured);
                menu.Items.Add(item);
            }
        });
    }

    /// <summary>
    /// 弹出菜单。<b>每次都整体换一个新菜单，把上一次的丢掉。</b>
    /// <para>
    /// 这个写法是被两次实测事故逼出来的，两个坑都要绕开：
    /// </para>
    /// <list type="number">
    /// <item>
    /// <b>不能在菜单自己的 <c>Closed</c> 里 Dispose 它</b> —— WinForms 关菜单的后续代码
    /// 还会去碰它，直接抛 <c>ObjectDisposedException</c>，而用户那次点击就没提交，对战卡死。
    /// </item>
    /// <item>
    /// <b>也不能复用一个菜单再遍历着 Dispose 它的 Items</b> —— Dispose 一个 ToolStripItem
    /// 会顺手把它从 <c>Items</c> 里摘掉，边遍历边 Dispose 就是
    /// "Collection was modified"。而且菜单第一次打开时 Items 是空的，
    /// **这个错只在第二次打开菜单时才炸**，很难一次就试出来。
    /// </item>
    /// </list>
    /// <para>
    /// 整体换新则完全没有遍历，两个坑都不存在；同时原生句柄也始终只有一个。
    /// </para>
    /// </summary>
    private void ShowMenu(Action<ContextMenuStrip> build)
    {
        // 此刻上一个菜单早就关掉了（它必须先关闭，用户才可能触发下一次操作），所以释放它是安全的。
        _actionMenu?.Dispose();

        var menu = new ContextMenuStrip { ShowImageMargin = false };
        _actionMenu = menu;
        build(menu);
        menu.Show(this, PointToClient(Cursor.Position));
    }

    private void BeginDragGestures(Panel tile, int instanceId, bool isHandCard, Action? onTap = null)
    {
        // 坐标一律换算成**屏幕坐标**再比较。原因：格子里铺着一个填满的标签，
        // 鼠标事件其实是从标签冒出来的（WinForms 事件不冒泡到父控件，所以同一套处理器
        // 会挂到整棵子树上）。事件里的 Location 是"发出事件那个控件"的坐标，
        // 直接拿它配上格子的 PointToScreen 会算错位置。
        Point ScreenPoint(object? sender, MouseEventArgs args) =>
            ((sender as Control) ?? tile).PointToScreen(args.Location);

        void Down(object? sender, MouseEventArgs args)
        {
            // 【融合】选素材期间，左键属于"点选素材"，不再触发出牌拖拽。
            if (args.Button != MouseButtons.Left || _pendingActions is null ||
                _fusionCardInstanceId is not null)
            {
                return;
            }

            _dragSource = tile;
            _dragInstanceId = instanceId;
            _dragIsHandCard = isHandCard;
            _dragStartScreen = ScreenPoint(sender, args);
            _dragActive = false;
        }

        void Move(object? sender, MouseEventArgs args)
        {
            if (_dragSource != tile)
            {
                return;
            }

            var screenPoint = ScreenPoint(sender, args);
            if (!_dragActive)
            {
                if (Math.Abs(screenPoint.X - _dragStartScreen.X) < 4 &&
                    Math.Abs(screenPoint.Y - _dragStartScreen.Y) < 4)
                {
                    return;
                }

                _dragActive = true;
                _cardToolTip.Hide(tile);
                LiftDragSource();
                // 鼠标捕获放在 tile 上：捕获之后 MouseMove/MouseUp 全部由 tile 收到，
                // 光标移出控件也不会丢事件。
                tile.Capture = true;
            }

            UpdateDrag(screenPoint);
        }

        void Up(object? sender, MouseEventArgs args)
        {
            if (_dragSource != tile || args.Button != MouseButtons.Left)
            {
                return;
            }

            tile.Capture = false;
            var dropPoint = ScreenPoint(sender, args);
            var wasActive = _dragActive;
            FinishDrag();
            if (wasActive)
            {
                ResolveDrop(dropPoint);
                return;
            }

            // 没拖动 = 就是点了一下。场上的随从借这一下打开进化菜单，
            // 这样不知道有右键的人也能操作。
            onTap?.Invoke();
        }

        AttachToTile(tile, Down, Move, Up);
    }

    /// <summary>
    /// 把同一套鼠标处理器挂到格子和它**整棵子树**上。
    /// WinForms 的鼠标事件不会从子控件冒泡到父控件，而卡牌格子里铺着一个填满的标签
    /// （场上格子还多一层：格子 → 卡牌格子 → 标签），鼠标其实总是点在标签上。
    /// 只挂一层的话，拖拽会完全没反应。
    /// </summary>
    private static void AttachToTile(Control tile, MouseEventHandler down, MouseEventHandler? move, MouseEventHandler? up)
    {
        tile.MouseDown += down;
        if (move is not null)
        {
            tile.MouseMove += move;
        }

        if (up is not null)
        {
            tile.MouseUp += up;
        }

        // 手牌/场上格子都表示可以操作，光标给出提示。
        tile.Cursor = Cursors.Hand;

        foreach (Control child in tile.Controls)
        {
            AttachToTile(child, down, move, up);
        }
    }

    /// <summary>
    /// 右键菜单要挂在格子和它内部的标签上 —— 标签铺满格子，鼠标事件根本到不了格子本身。
    /// </summary>
    private static void AttachMouseUpDeep(Control root, MouseEventHandler handler)
    {
        root.MouseUp += handler;
        foreach (Control child in root.Controls)
        {
            AttachMouseUpDeep(child, handler);
        }
    }

    /// <summary>拖拽过程中更新落点高亮、箭头和提示文字。</summary>
    private void UpdateDrag(Point screenPoint)
    {
        if (_pendingActions is not { } actions)
        {
            return;
        }

        var hit = HitTestHuman(screenPoint);
        var valid = DragCandidates(actions, hit).Count > 0;

        HighlightDropTarget(hit, valid);
        UpdateDragArrow(hit);
        _actionLabel.Text = DescribeDragIntent(hit, valid);
        if (_dragSource is { } source)
        {
            source.Cursor = valid ? Cursors.Hand : Cursors.No;
        }
    }

    /// <summary>
    /// 当前这个拖拽手势落在 <paramref name="hit"/> 上时，所有匹配的合法动作。
    /// <para>
    /// 手牌和场上随从的语义不同，所以分开走：
    /// 手牌的落点决定"打出后指定谁"；随从的落点决定"指向谁" ——
    /// 那不只是攻击，还包括【进化时】指定敌方随从、超进化指定己方随从。
    /// </para>
    /// </summary>
    private IReadOnlyList<GameAction> DragCandidates(IReadOnlyList<GameAction> actions, HumanHit hit)
    {
        if (_dragIsHandCard)
        {
            return HumanActionResolver.FromHand(
                actions,
                _dragInstanceId,
                hit.Zone == HumanZone.OpponentBoard ? hit.InstanceId : null,
                hit.Zone == HumanZone.OpponentLeader);
        }

        return HumanActionResolver.FromFollower(
            actions,
            _dragInstanceId,
            enemyFollowerTargetInstanceId: hit.Zone == HumanZone.OpponentBoard ? hit.InstanceId : null,
            enemyLeaderTarget: hit.Zone == HumanZone.OpponentLeader,
            allyFollowerTargetInstanceId: hit.Zone == HumanZone.OwnBoard ? hit.InstanceId : null);
    }

    /// <summary>攻击时用回放里已有的那根箭头指一下打谁 —— 和真实游戏的手感一致。</summary>
    private void UpdateDragArrow(HumanHit hit)
    {
        if (_dragIsHandCard)
        {
            _arrowCanvas.Scene = null;
            return;
        }

        var sourceSlot = SlotOfFollower(_selfBoard, _dragInstanceId);
        if (sourceSlot is not int from)
        {
            _arrowCanvas.Scene = null;
            return;
        }

        if (hit.Zone == HumanZone.OpponentLeader)
        {
            _arrowCanvas.Scene = new ReplayArrow(0, from, PlayerState.BoardLimit / 2, TargetsLeader: true);
            return;
        }

        if (hit.Zone == HumanZone.OpponentBoard && hit.InstanceId is int defender)
        {
            var targetSlot = SlotOfFollower(_opponentBoard, defender);
            _arrowCanvas.Scene = targetSlot is int to
                ? new ReplayArrow(0, from, to, TargetsLeader: false)
                : null;
            return;
        }

        _arrowCanvas.Scene = null;
    }

    private static int? SlotOfFollower(TableLayoutPanel board, int instanceId)
    {
        foreach (Control control in board.Controls)
        {
            if (control.Tag is FollowerInstance follower && follower.InstanceId == instanceId)
            {
                return board.GetColumn(control);
            }
        }

        return null;
    }

    private void HighlightDropTarget(HumanHit hit, bool valid)
    {
        var target = hit.Zone switch
        {
            HumanZone.OpponentBoard when hit.InstanceId is not null => ControlOfSlot(_opponentBoard, hit.InstanceId.Value),
            HumanZone.OpponentLeader => _opponentLeaderLabel.Parent as Panel,
            _ => null
        };

        RestoreHighlight();
        if (target is null)
        {
            return;
        }

        _highlighted = target;
        _highlightedColor = target.BackColor;
        _highlightedBorder = target.BorderStyle;
        target.BackColor = valid ? Color.FromArgb(88, 122, 74) : Color.FromArgb(120, 60, 60);
    }

    private static Panel? ControlOfSlot(TableLayoutPanel board, int followerInstanceId)
    {
        foreach (Panel control in board.Controls.OfType<Panel>())
        {
            if (control.Tag is FollowerInstance follower && follower.InstanceId == followerInstanceId)
            {
                return control;
            }
        }

        return null;
    }

    private void RestoreHighlight()
    {
        if (_highlighted is { } control && !control.IsDisposed)
        {
            control.BackColor = _highlightedColor;
            control.BorderStyle = _highlightedBorder;
        }

        _highlighted = null;
    }

    private void FinishDrag()
    {
        RestoreHighlight();
        RestoreDragSource();
        _dragSource = null;
        _dragActive = false;
        RestorePlaybackHints();
    }

    /// <summary>
    /// 拖拽时把被拖的那个格子"提起来"（变亮 + 3D 边框），让用户看得出自己在拖哪张。
    /// 刻意<b>不移动</b>格子，也不 BringToFront：手牌是 FlowLayoutPanel 排的，
    /// 挪位置或改 z 序都会被它立刻重排，卡牌顺序会当场跳掉。
    /// </summary>
    private void LiftDragSource()
    {
        if (_dragSource is not { } source)
        {
            return;
        }

        _dragSourceColor = source.BackColor;
        _dragSourceBorder = source.BorderStyle;
        source.BackColor = Color.FromArgb(96, 138, 178);
        source.BorderStyle = BorderStyle.Fixed3D;
    }

    private void RestoreDragSource()
    {
        if (_dragSource is { } source && !source.IsDisposed)
        {
            source.BackColor = _dragSourceColor;
            source.BorderStyle = _dragSourceBorder;
        }
    }

    /// <summary>
    /// 把动作栏的文字和箭头还原成"当前这一步"该有的样子 —— 拖拽期间它们被改成了拖拽提示。
    /// **刻意不调 <see cref="UpdatePlayback"/>**：那会把正在处理这次鼠标事件的卡牌格子销毁掉。
    /// </summary>
    private void RestorePlaybackHints()
    {
        if (_currentStepIndex >= 0 && _currentStepIndex < _steps.Count)
        {
            var step = _steps[_currentStepIndex];
            _actionLabel.Text = DescribeStep(step);
            _arrowCanvas.Scene = GetArrowScene(step);
            return;
        }

        _actionLabel.Text = _initialState is null
            ? string.Empty
            : $"开局：Player {_initialState.StartingPlayer + 1} 先手。按“下一步”查看双方换牌。";
        _arrowCanvas.Scene = null;
    }

    /// <summary>松手：把手势筛出来的候选动作提交掉；筛出多个就弹菜单让用户挑。</summary>
    private void ResolveDrop(Point screenPoint)
    {
        if (_pendingActions is not { } actions)
        {
            return;
        }

        var hit = HitTestHuman(screenPoint);
        var candidates = DragCandidates(actions, hit);

        if (candidates.Count == 0)
        {
            if (_dragIsHandCard)
            {
                // 分成两种情况说清楚：是"放错地方了"还是"这张牌现在根本打不出"。
                // 只说"不能放在那里"会让费用不够的人一直换地方试。
                var playableSomewhere = actions.Any(action =>
                    HumanActionResolver.PlayedCardOf(action) == _dragInstanceId);
                _humanHint.Text = playableSomewhere
                    ? "这张牌不能放在那里。拖到己方场上，或拖到敌方随从 / 主战者上指定目标。"
                    : "这张牌现在打不出（费用不够，或没有合法目标）。";
            }
            else
            {
                _humanHint.Text = "这个随从现在不能攻击那个目标。";
            }

            return;
        }

        _humanHint.Text = string.Empty;

        // 同一个手势可能对应多个合法变体（选模式 / 从手牌再选一张牌 / 多个目标）。
        // 不猜：只在模式上不同就进专门的选模式界面，其余弹通用菜单。
        BeginAmbiguityResolution(candidates);
    }

    // ───────────────────────────── 落点命中判定 ─────────────────────────────

    /// <summary>
    /// 屏幕坐标 -> 语义落点。从最深的子控件往上找，看它落在哪一块区域里。
    /// 用控件层级而不是硬编码坐标，这样以后改布局不会把拖拽改坏。
    /// </summary>
    private HumanHit HitTestHuman(Point screenPoint)
    {
        var formPoint = PointToClient(screenPoint);
        var deepest = DeepestControlAt(formPoint) ?? this;

        for (Control? control = deepest; control is not null; control = control.Parent)
        {
            if (control == _selfHand)
            {
                return new HumanHit(HumanZone.OwnHand, InstanceIdUnder(deepest));
            }

            if (control == _opponentHand)
            {
                return new HumanHit(HumanZone.None, null);
            }

            if (control == _selfBoard)
            {
                return new HumanHit(HumanZone.OwnBoard, InstanceIdUnder(deepest));
            }

            if (control == _opponentBoard)
            {
                return new HumanHit(HumanZone.OpponentBoard, InstanceIdUnder(deepest));
            }

            if (control == _opponentLeaderLabel || control == _opponentLeaderLabel.Parent)
            {
                return new HumanHit(HumanZone.OpponentLeader, null);
            }

            if (control == _selfLeaderLabel || control == _selfLeaderLabel.Parent)
            {
                return new HumanHit(HumanZone.OwnLeader, null);
            }
        }

        return new HumanHit(HumanZone.None, null);
    }

    /// <summary>从最深的控件往上找第一个带标牌的控件，取出它的实例号。</summary>
    private static int? InstanceIdUnder(Control? control)
    {
        for (var current = control; current is not null; current = current.Parent)
        {
            switch (current.Tag)
            {
                case CardInstance card:
                    return card.InstanceId;
                case FollowerInstance follower:
                    return follower.InstanceId;
                case AmuletInstance amulet:
                    return amulet.InstanceId;
                case null:
                    continue;
                default:
                    return null;
            }
        }

        return null;
    }

    private Control? DeepestControlAt(Point formPoint)
    {
        return DeepestChild(this, formPoint);

        Control? DeepestChild(Control parent, Point parentPoint)
        {
            Control? found = null;
            foreach (Control child in parent.Controls)
            {
                if (!child.Visible || child.IsDisposed || child.Width <= 0 || child.Height <= 0)
                {
                    continue;
                }

                var local = child.PointToClient(parent.PointToScreen(parentPoint));
                if (!child.ClientRectangle.Contains(local))
                {
                    continue;
                }

                found = DeepestChild(child, local) ?? child;
            }

            return found;
        }
    }

    /// <summary>
    /// 拖拽时在动作栏里写清楚"松手会发生什么"。
    /// 直接把筛出来的候选动作原样描述出来 —— 用的和菜单、"全部动作"列表是同一个描述器，
    /// 所以不会出现"提示说一套、实际做另一套"。
    /// </summary>
    private string DescribeDragIntent(HumanHit hit, bool valid)
    {
        if (!valid)
        {
            return _dragIsHandCard ? "这张牌不能放在这里" : "这个随从不能指向那个目标";
        }

        if (_pendingActions is not { } actions || _pendingObservation is not { } observation)
        {
            return string.Empty;
        }

        var candidates = DragCandidates(actions, hit);
        return candidates.Count == 1
            ? "松手：" + HumanActionText.Describe(observation, candidates[0])
            : $"松手：有 {candidates.Count} 个可选动作，会让你选一个";
    }

    // ───────────────────────────── 每步之后重挂交互 ─────────────────────────────

    /// <summary>
    /// 回放每渲染一次，卡牌格子都是新造的，所以要重新把点击/拖拽挂上去。
    /// 这个方法由 <see cref="UpdatePlayback"/> 在实时对局中调用，**绝对不能回头再调 UpdatePlayback**，
    /// 否则递归。
    /// </summary>
    private void RefreshHumanInteraction(bool tilesAreFresh = false)
    {
        // 每次重画造出来的都是全新的格子，旧的"已挂处理器"记录一并丢掉。
        if (tilesAreFresh)
        {
            _interactionAttached.Clear();
        }

        // 等真人决定时不允许翻页：否则看到的是旧的场面，拖拽却按最新一手结算，会出意外。
        // **这一步必须排在 UpdateOpponentThinking 之前** —— 思考面板是按 _currentStepIndex
        // 去找"当前这一步之前最近的一次决策"的，先更新面板再跳步，面板就会短暂地
        // 显示另一手的内容，和画面上的场面不一致。
        if (_liveMatchRunning && _pendingActions is not null && _steps.Count > 0)
        {
            _previousButton.Enabled = false;
            _nextButton.Enabled = false;
            _autoPlayButton.Enabled = false;
            _currentStepIndex = _steps.Count - 1;
        }

        // 思考面板**放在实时对局的判断之前**更新：打完之后一样能翻页回看对手每一步在想什么 ——
        // 那正是"复盘牌手决策"最有价值的时候。
        UpdateOpponentThinking();
        // 行动记录同样：它是纯回放信息，实时对局和事后复盘都要有。
        UpdateActionLog();

        if (!_liveMatchRunning)
        {
            return;
        }

        if (_pendingActions is null || _pendingObservation is null)
        {
            _humanStatus.Text = $"对手：{_humanOpponentSummary}\n对手行动中…";
            return;
        }

        if (IsMulliganPhase)
        {
            ApplyMulliganMarks();
            foreach (Panel tile in _selfHand.Controls.OfType<Panel>())
            {
                if (tile.Tag is CardInstance card && _interactionAttached.Add(tile))
                {
                    AttachMulliganToggle(tile, card.InstanceId);
                }
            }

            return;
        }

        ApplyAvailabilityVisuals();

        foreach (Panel tile in _selfHand.Controls.OfType<Panel>())
        {
            if (tile.Tag is CardInstance card && _interactionAttached.Add(tile))
            {
                AttachHandDrag(tile, card.InstanceId);
                AttachFusionContext(tile, card.InstanceId);
            }
        }

        foreach (Panel slot in _selfBoard.Controls.OfType<Panel>())
        {
            if (slot.Tag is FollowerInstance follower && _interactionAttached.Add(slot))
            {
                AttachFollowerDrag(slot, follower.InstanceId);
            }
        }
    }

    /// <summary>读出对手牌手"刚刚那一次"的决策。只有前瞻牌手有；规则牌手不搜索，没有估值。</summary>
    private LookaheadDecision? CurrentOpponentDecision() => _opponentAgentInstance switch
    {
        LookaheadPlayerAgent lookahead => lookahead.LastDecision,
        LookaheadPlayerAgentV1 lookaheadV1 => lookaheadV1.LastDecision,
        LookaheadPlayerAgentV2 lookaheadV2 => lookaheadV2.LastDecision,
        BaselineLookaheadPlayerAgent baseline => baseline.LastDecision,
        _ => null
    };

    /// <summary>
    /// 把对手最近一次决策的思考过程写进面板。
    /// <para>
    /// 显示原则是<b>只显示公开信息</b>：涉及对手手牌的候选只报类型
    /// （见 <see cref="HumanActionText.DescribeOpponentAction"/>）。
    /// 但这个面板真正的价值在于<b>形状</b> —— 他在几个动作之间选、各自估多少、差距多小、
    /// 是不是让位给了规则牌手。这些全都不依赖对手手牌，所以可以放心显示。
    /// </para>
    /// </summary>
    private void UpdateOpponentThinking()
    {
        // 只有"正在看这一局人机对战"时才显示。用步骤列表的**实例同一性**判断：
        // 重新生成一批机器对局会把 _steps 换成另一个列表，那时的步骤下标和这里的记录毫无关系，
        // 照着显示就会张冠李戴。
        if (_liveSteps is not { } liveSteps || !ReferenceEquals(_steps, liveSteps))
        {
            _thinkingBody.Text = string.Empty;
            return;
        }

        if (!_opponentSupportsThinking)
        {
            _thinkingBody.Text = "对手是规则牌手，没有搜索估值可看。";
            return;
        }

        // 找"当前这一步之前最近的一次对手决策"，这样往回翻页时文字和画面是对上的。
        (int StepIndex, LookaheadDecision Decision)? match = null;
        lock (_liveGate)
        {
            foreach (var entry in _opponentDecisions)
            {
                if (entry.StepIndex > _currentStepIndex)
                {
                    break;
                }

                match = entry;
            }
        }

        if (match is not { } record)
        {
            _thinkingBody.Text = "等对手行动…";
            return;
        }

        var decision = record.Decision;

        // 用"那一步开始前"的局面对**你**做观测来渲染文字 —— 用当前局面会让回看时文字和画面对不上。
        // **步子和局面一起取**：之前是这里做了边界检查、下面取 DescribeStep 时又没做，
        // 两处不一致 —— 边界真越了就是 IndexOutOfRange，而不是干净地什么都不显示。
        var step = record.StepIndex >= 0 && record.StepIndex < _steps.Count
            ? _steps[record.StepIndex]
            : null;
        if (step is null)
        {
            _thinkingBody.Text = string.Empty;
            return;
        }

        var observation = GameEngine.ToObservation(step.BeforeState, HumanPlayerIndex);

        // 文字拼装在引擎层（OpponentThinkingReport）—— 里面有真逻辑
        // （几个候选、第 2 名存不存在、要不要提回退闸），放在界面里自检就够不着。
        // 之前就是在这里对只有一个候选的决策索引了 [1]，直接越界把整局对战卡死。
        _thinkingBody.Text = string.Join(
            Environment.NewLine,
            OpponentThinkingReport.Build(
                observation,
                decision,
                DescribeStep(step),
                ThinkingRows));
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

            // 对手刚走完一步 —— 趁现在把这次决策的思考抓下来。
            // **必须在这里同步抓**：读的是牌手的 LastDecision，那个属性会被下一次决策覆盖，
            // 而界面更新是排队执行的，等界面跑到这一步时它早就变了。
            if (step.ActingPlayer == 1 && CurrentOpponentDecision() is { } decision)
            {
                _opponentDecisions.Add((count - 1, decision));
            }
        }

        RunOnUi(() =>
        {
            _currentStepIndex = count - 1;
            UpdatePlayback();
        });
    }

    /// <summary>
    /// 把一段要在 UI 线程上跑的代码排进去。返回值表示"到底排进去了没有"。
    /// <para>
    /// <b>两件事都必须做：</b>
    /// </para>
    /// <para>
    /// 一是<b>把异常兜住</b>。界面更新里出任何错，在 WinForms 里都会变成未处理异常：
    /// 调试时弹对话框、发布时直接崩，而且界面被留在半死状态 ——
    /// 用户看到的只是"卡住了"，连原因都没有。这个项目已经因此吃过一次亏
    /// （思考面板对只有一个候选的决策越界索引）。
    /// </para>
    /// <para>
    /// 二是<b>告诉调用方有没有排进去</b>。窗体已经关掉时它排不进去，
    /// 而 <see cref="AskHuman"/> 正阻塞在一个 <see cref="TaskCompletionSource{T}"/> 上 ——
    /// 不告诉它，对局线程就永远醒不来，连进程都退不掉。
    /// </para>
    /// </summary>
    private bool RunOnUi(Action action)
    {
        if (IsDisposed || Disposing)
        {
            return false;
        }

        void Guarded()
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                ReportUiFailure(exception);
            }
        }

        if (!InvokeRequired)
        {
            Guarded();
            return true;
        }

        try
        {
            BeginInvoke(Guarded);
            return true;
        }
        catch (Exception exception)
        {
            // 窗体正在被销毁时 BeginInvoke 会抛。调用方要靠这个返回值去解开阻塞。
            ReportUiFailure(exception);
            return false;
        }
    }

    /// <summary>
    /// 界面出错时把原因显示出来并写进调试输出，**绝不 rethrow**。
    /// 这里自己也要能被安全地调用 —— 连显示都失败就只能放弃，但绝不能再抛出去。
    /// </summary>
    private void ReportUiFailure(Exception exception)
    {
        try
        {
            if (!IsDisposed && !Disposing)
            {
                _humanStatus.Text = "界面出错：" + exception.Message;
            }
        }
        catch
        {
            // 刻意吞掉：报错的过程中再出错，就只能放弃显示。
        }

        System.Diagnostics.Debug.WriteLine("[人机对战] 界面出错：" + exception);
    }

    /// <summary>
    /// 纹章区：把**双方**主战者已获得的纹章画出来。
    /// <para>
    /// 纹章是公开信息（对手挂着的纹章你本来就看得见），所以两边都列。
    /// 纹章的完整能力说明放进标签的悬停提示里 —— 名称短、说明长，全塞进版面会顶掉别的内容。
    /// </para>
    /// <para>没有纹章时高度归零，不占版面。</para>
    /// </summary>
    private void RefreshCrestZone(GameObservation observation)
    {
        var mine = observation.Self.Crests ?? [];
        var theirs = observation.Opponent.Crests ?? [];

        var lines = new List<string>();
        if (mine.Count > 0)
        {
            lines.Add("你的纹章：" + string.Join("　", mine.Select(crest => $"◆{crest.Name}")));
        }

        if (theirs.Count > 0)
        {
            lines.Add("对手纹章：" + string.Join("　", theirs.Select(crest => $"◆{crest.Name}")));
        }

        _crestZone.Text = string.Join("\n", lines);
        _crestZone.Height = lines.Count == 0 ? 0 : (22 * lines.Count) + 6;

        var tips = mine.Select(crest => $"【你的】{crest.Name}：{crest.EffectText}")
            .Concat(theirs.Select(crest => $"【对手】{crest.Name}：{crest.EffectText}"))
            .ToArray();
        _crestZoneToolTip.SetToolTip(_crestZone, string.Join("\n\n", tips));
    }

    private static string DescribeOwnState(GameObservation observation)
    {
        var self = observation.Self;
        var opponent = observation.Opponent;

        // 行要短。这段文字会先拼上一行"对手：…"和"第 N 回合"，太长就会换行把下面的内容顶掉 ——
        // 用户实测"看不到墓地数量"就是版面被挤没的。
        return $"你 {self.Health}血 PP{self.CurrentPlayPoints}/{self.MaxPlayPoints}" +
               $" EP{self.EvolutionPoints}+{self.SuperEvolutionPoints}\n" +
               $"牌库 {self.DeckCount}　墓地 {self.GraveyardCount}\n" +
               $"敌方 {opponent.Health}血 手牌{opponent.HandCount}" +
               $"　牌库 {opponent.DeckCount}　墓地 {opponent.GraveyardCount}";
    }

    /// <summary>
    /// 行动记录：把最近若干步倒序列出来（最新在上）。
    /// <para>
    /// 用户实测提的："看不到对面上回合出了什么牌做了什么动作"。
    /// 回放底部那排"上一／当前／下一"只显示相邻三步，而对手一个回合有十几步 ——
    /// 等轮到你的时候，他前面做了什么早翻过去了。
    /// </para>
    /// <para>
    /// 这块和实时对局无关，纯回放模式一样有用，所以**不受 _liveMatchRunning 限制**。
    /// </para>
    /// </summary>
    private void UpdateActionLog()
    {
        if (_steps.Count == 0)
        {
            _logBody.Text = string.Empty;
            return;
        }

        var last = Math.Min(_currentStepIndex, _steps.Count - 1);
        if (last < 0)
        {
            _logBody.Text = "（还没开始）";
            return;
        }

        var lines = new List<string>();
        for (var index = last; index >= 0 && lines.Count < ActionLogLines; index--)
        {
            lines.Add(ShortDescription(_steps[index]));
        }

        _logBody.Text = string.Join(Environment.NewLine, lines);
    }

    /// <summary>
    /// 把一个动作翻译成人话。翻译逻辑放在引擎层（<see cref="HumanActionText"/>），
    /// 这样"同一组候选必须能被文字区分开"这条性质才能被自检覆盖 ——
    /// 界面自己写一份翻译，自检就够不着它，【模式】卡牌会弹出几条一模一样的选项。
    /// </summary>
    private static string DescribeAction(GameObservation observation, GameAction action) =>
        HumanActionText.Describe(observation, action);

    // ───────────────────────────── 【融合】 ─────────────────────────────

    private Button? _fusionConfirmButton;
    private Button? _fusionCancelButton;
    private int? _fusionCardInstanceId;
    private readonly HashSet<int> _fusionSelection = [];
    private readonly Dictionary<Panel, Color> _fusionHighlighted = [];

    /// <summary>
    /// 【融合】：右键点带【融合】的手牌 → **紧贴这张牌**弹出【融合】【取消】两个小按钮。
    /// 弹出期间可以左键点选合法素材（合法目标高亮，**可多选**，术语表："没有指定数量时可融合任意数量"）。
    /// <para>
    /// 「点取消不消耗融合次数」是天然成立的：取消只清界面、**根本不调用引擎**，
    /// 而"1回合仅限1次"只在 <c>GameEngine.ApplyFuse</c> 里扣。再右键会重新弹出这两个按钮。
    /// </para>
    /// </summary>
    private void AttachFusionContext(Panel tile, int cardInstanceId)
    {
        void OnUp(object? sender, MouseEventArgs args)
        {
            if (args.Button == MouseButtons.Right)
            {
                OpenFusionButtons(tile, cardInstanceId);
            }
        }

        tile.MouseUp += OnUp;
        foreach (Control child in tile.Controls)
        {
            child.MouseUp += OnUp;
        }

        void OnDown(object? sender, MouseEventArgs args)
        {
            if (args.Button != MouseButtons.Left || _fusionCardInstanceId is null)
            {
                return;
            }

            if (tile.Tag is CardInstance handCard && _fusionHighlighted.ContainsKey(tile))
            {
                ToggleFusionMaterial(tile, handCard.InstanceId);
            }
        }

        tile.MouseDown += OnDown;
        foreach (Control child in tile.Controls)
        {
            child.MouseDown += OnDown;
        }
    }

    private void OpenFusionButtons(Panel tile, int cardInstanceId)
    {
        CloseFusionUi();

        if (_pendingActions is not { } actions)
        {
            return;
        }

        if (tile.Tag is not CardInstance card || card.Definition.Fusion is null)
        {
            _humanHint.Text = "这张牌没有【融合】能力。";
            return;
        }

        var legal = actions.OfType<FuseAction>()
            .Where(action => action.CardInstanceId == cardInstanceId)
            .ToList();
        if (legal.Count == 0)
        {
            _humanHint.Text = "现在不能融合这张牌：本回合已经融合过，或场上/手上没有合法素材。";
            return;
        }

        _fusionCardInstanceId = cardInstanceId;
        _fusionSelection.Clear();

        // 合法素材 = 所有合法融合动作里出现过的那些手牌。
        var legalMaterialIds = legal
            .SelectMany(action => action.MaterialInstanceIds)
            .ToHashSet();

        foreach (Panel handTile in _selfHand.Controls.OfType<Panel>())
        {
            if (handTile.Tag is CardInstance handCard && legalMaterialIds.Contains(handCard.InstanceId))
            {
                _fusionHighlighted[handTile] = handTile.BackColor;
                handTile.BackColor = Color.FromArgb(70, 116, 62);
            }
        }

        var anchor = PointToClient(tile.PointToScreen(Point.Empty));
        _fusionConfirmButton = new Button
        {
            Text = "融合",
            AutoSize = false,
            Size = new Size(64, 26),
            Location = new Point(anchor.X, Math.Max(0, anchor.Y - 28)),
            BackColor = Color.FromArgb(38, 111, 155),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            UseVisualStyleBackColor = false
        };
        _fusionCancelButton = new Button
        {
            Text = "取消",
            AutoSize = false,
            Size = new Size(64, 26),
            Location = new Point(anchor.X + 68, Math.Max(0, anchor.Y - 28)),
            BackColor = Color.FromArgb(54, 77, 101),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            UseVisualStyleBackColor = false
        };

        _fusionConfirmButton.Click += (_, _) => ConfirmFusion();
        _fusionCancelButton.Click += (_, _) =>
        {
            CloseFusionUi();
            _humanHint.Text = "已取消融合（未消耗本回合的融合次数，可以再次右键这张牌）。";
        };

        Controls.Add(_fusionConfirmButton);
        Controls.Add(_fusionCancelButton);
        _fusionConfirmButton.BringToFront();
        _fusionCancelButton.BringToFront();

        _humanHint.Text = $"【融合】{card.Definition.Name}：左键点击绿色高亮的素材（可多选），再点【融合】确认；点【取消】不消耗次数。";
    }

    private void ToggleFusionMaterial(Panel tile, int materialInstanceId)
    {
        if (_fusionSelection.Contains(materialInstanceId))
        {
            _fusionSelection.Remove(materialInstanceId);
            tile.BackColor = Color.FromArgb(70, 116, 62);
        }
        else
        {
            _fusionSelection.Add(materialInstanceId);
            tile.BackColor = Color.FromArgb(150, 96, 40);
        }

        _humanHint.Text = $"已选 {_fusionSelection.Count} 张素材。再点一次可以取消选择，或点【融合】确认。";
    }

    private void ConfirmFusion()
    {
        if (_pendingActions is not { } actions || _fusionCardInstanceId is not { } cardId)
        {
            CloseFusionUi();
            return;
        }

        // 精确匹配：选中的集合必须正好等于某个合法动作的素材集合。
        // 引擎按"1～3 张的组合"生成动作，所以超出范围的选法会在这里被挡住并说明原因。
        var match = actions.OfType<FuseAction>().FirstOrDefault(action =>
            action.CardInstanceId == cardId &&
            action.MaterialInstanceIds.Count == _fusionSelection.Count &&
            action.MaterialInstanceIds.All(_fusionSelection.Contains));

        if (match is null)
        {
            _humanHint.Text = _fusionSelection.Count == 0
                ? "还没有选素材：先左键点击绿色高亮的牌。"
                : "这个素材组合不合法（可能超过 3 张、费用/类别不符，或本回合已融合过）。";
            return;
        }

        CloseFusionUi();
        CommitHumanAction(match);
    }

    private void CloseFusionUi()
    {
        _fusionConfirmButton?.Dispose();
        _fusionCancelButton?.Dispose();
        _fusionConfirmButton = null;
        _fusionCancelButton = null;
        _fusionCardInstanceId = null;
        _fusionSelection.Clear();

        foreach (var (tile, original) in _fusionHighlighted)
        {
            if (!tile.IsDisposed)
            {
                tile.BackColor = original;
            }
        }

        _fusionHighlighted.Clear();
    }
}
