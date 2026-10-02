using System.ComponentModel;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Shadowverse.Engine.Agents;
using Shadowverse.Engine.Decks;
using Shadowverse.Engine.Game;
using Shadowverse.Engine.Models;
using Shadowverse.Engine.Simulation;

namespace Shadowverse.DeckEditor;

/// <summary>
/// A compact, full-information replay viewer. It deliberately favours readable board changes
/// over card art: hands, the two leaders, five follower slots per side, and one action at a time.
/// </summary>
public sealed partial class ReplayForm : Form
{
    private static readonly Color BoardBackground = Color.FromArgb(26, 42, 61);
    private static readonly Color SurfaceBackground = Color.FromArgb(36, 55, 76);
    private static readonly Color FollowerColor = Color.FromArgb(50, 104, 156);
    private static readonly Color SpellColor = Color.FromArgb(102, 75, 151);
    private static readonly Color AmuletColor = Color.FromArgb(152, 119, 45);

    private readonly ComboBox _firstDeck = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 180 };
    private readonly ComboBox _secondDeck = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 180 };
    private readonly ComboBox _firstAgent = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 110 };
    private readonly ComboBox _secondAgent = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 110 };
    private readonly NumericUpDown _matchCount = new() { Minimum = 1, Maximum = 100, Value = 1, Width = 54 };

    /// <summary>
    /// 批量生成后显示"谁赢了几局"。只看每局谁胜没法判断牌手强弱，
    /// 30 局里出现 18:12 完全可能只是运气。
    /// </summary>
    private readonly Label _resultSummary = new()
    {
        AutoSize = true,
        ForeColor = Color.Gainsboro,
        Padding = new Padding(12, 7, 0, 0)
    };
    private readonly ComboBox _matchSelector = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 190, Enabled = false };
    private readonly Button _newMatchButton = new ReadableToolbarButton { Text = "生成对局", AutoSize = true };

    /// <summary>
    /// 前瞻牌手对每个候选动作向前模拟的次数。次数越多判断噪声越小，耗时按比例增加。
    /// 两边分开设置，才能做"同一个牌手、两种搜索量对打"这个真正有用的对照——
    /// 两边设成一样的话，分不清是谁在受益。
    /// </summary>
    private readonly ComboBox _firstRollouts = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 62 };
    private readonly ComboBox _secondRollouts = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 62 };
    private readonly Button _previousButton = new ReadableToolbarButton { Text = "← 上一步", AutoSize = true, Enabled = false };
    private readonly Button _nextButton = new ReadableToolbarButton { Text = "下一步 →", AutoSize = true, Enabled = false };
    private readonly Button _autoPlayButton = new ReadableToolbarButton { Text = "▶ 自动播放", AutoSize = true, Enabled = false };
    private readonly Button _exportButton = new ReadableToolbarButton { Text = "导出本局 JSON", AutoSize = true, Enabled = false };
    private readonly Button _batchExportButton = new ReadableToolbarButton { Text = "批量导出 JSON", AutoSize = true, Enabled = false };
    private readonly Label _progressLabel = new()
    {
        AutoSize = true,
        BackColor = Color.FromArgb(20, 35, 52),
        ForeColor = Color.White,
        Font = new Font("Microsoft YaHei UI", 10, FontStyle.Bold),
        Padding = new Padding(10, 6, 10, 6),
        Margin = new Padding(8, 0, 0, 0)
    };
    private readonly Label _opponentLeaderLabel = CreateLeaderLabel();
    private readonly Label _selfLeaderLabel = CreateLeaderLabel();
    private readonly Label _opponentHandTitle = CreateSectionLabel();
    private readonly Label _selfHandTitle = CreateSectionLabel();
    private readonly FlowLayoutPanel _opponentHand = CreateHandFlow();
    private readonly FlowLayoutPanel _selfHand = CreateHandFlow();
    private readonly TableLayoutPanel _opponentBoard = CreateBoardTable();
    private readonly TableLayoutPanel _selfBoard = CreateBoardTable();
    private readonly Label _actionLabel = new()
    {
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleCenter,
        Font = new Font("Microsoft YaHei UI", 12, FontStyle.Bold),
        ForeColor = Color.White,
        BackColor = Color.FromArgb(51, 75, 100),
        BorderStyle = BorderStyle.FixedSingle,
        Padding = new Padding(16, 7, 16, 7),
        Margin = new Padding(6, 0, 6, 0),
        MinimumSize = new Size(0, 56)
    };
    private readonly ActionArrowCanvas _arrowCanvas = new() { Dock = DockStyle.Fill };
    private readonly Label _previousActionPreview = CreateActionPreviewLabel(Color.FromArgb(39, 61, 82));
    private readonly Label _currentActionPreview = CreateActionPreviewLabel(Color.FromArgb(51, 75, 100), FontStyle.Bold);
    private readonly Label _nextActionPreview = CreateActionPreviewLabel(Color.FromArgb(39, 61, 82));
    private readonly ToolTip _cardToolTip = new()
    {
        AutoPopDelay = 15000,
        InitialDelay = 1,
        ReshowDelay = 100,
        ShowAlways = true,
        UseAnimation = false,
        UseFading = false
    };
    private readonly System.Windows.Forms.Timer _autoPlayTimer = new() { Interval = 700 };

    private GameState? _initialState;
    private IReadOnlyList<MatchStep> _steps = [];
    private readonly List<ReplayMatch> _matches = [];
    private int _currentStepIndex = -1;
    private bool _isUpdatingMatchSelector;

    public ReplayForm()
    {
        Text = "影之诗原型 - 对局回放";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(1080, 820);
        Size = new Size(1280, 900);
        BackColor = BoardBackground;
        KeyPreview = true;

        BuildLayout();
        // 人机对战面板挂在右侧。逻辑全在 ReplayForm.HumanPlay.cs 里，
        // 这里只加一行，把改动面压到最小 —— 界面没法自动化验证，改布局的风险最高。
        BuildHumanPlayPanel();
        LoadDeckChoices();
        _firstAgent.Items.AddRange(AgentNames);
        _secondAgent.Items.AddRange(AgentNames);
        _firstAgent.SelectedItem = "前瞻牌手 3.0（开发中）";
        _secondAgent.SelectedItem = "前瞻牌手 1.0（冻结）";
        foreach (var rollouts in new[] { _firstRollouts, _secondRollouts })
        {
            rollouts.Items.AddRange(["20", "60", "120", "240"]);
            rollouts.SelectedItem = "60";
        }

        StyleToolbarButton(_newMatchButton, isPrimary: true);
        StyleToolbarButton(_previousButton);
        StyleToolbarButton(_nextButton);
        StyleToolbarButton(_autoPlayButton);
        StyleToolbarButton(_exportButton, isPrimary: true);
        StyleToolbarButton(_batchExportButton);

        _newMatchButton.Click += (_, _) => GenerateMatches();
        _previousButton.Click += (_, _) => MovePrevious();
        _nextButton.Click += (_, _) => MoveNext();
        _autoPlayButton.Click += (_, _) => ToggleAutoPlay();
        _exportButton.Click += (_, _) => ExportCurrentMachineReplay();
        _batchExportButton.Click += (_, _) => ExportAllMachineReplays();
        _matchSelector.SelectedIndexChanged += MatchSelectionChanged;
        _firstAgent.SelectedIndexChanged += (_, _) => UpdateRolloutAvailability();
        _secondAgent.SelectedIndexChanged += (_, _) => UpdateRolloutAvailability();
        UpdateRolloutAvailability();
        _autoPlayTimer.Tick += (_, _) => MoveNext();
        KeyDown += ReplayFormKeyDown;
        FormClosed += (_, _) => _autoPlayTimer.Stop();

        ShowEmptyState();
    }

    private void BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 7,
            Padding = new Padding(12),
            BackColor = BoardBackground
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 102));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        // Keep the arrow and the action explanation in independent, roomy rows.
        // At a scaled display this prevents the explanation's glyphs from being clipped.
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 104));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 102));
        // The three action previews need a full text line each at 125%/150% display scaling.
        // Keep this fixed rather than letting the two board rows squeeze the text at the bottom.
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 164));

        root.Controls.Add(CreateControls(), 0, 0);
        root.Controls.Add(CreatePlayerArea(player: 1), 0, 1);
        root.Controls.Add(CreateBoardArea(_opponentBoard, "Player 2 战场"), 0, 2);
        root.Controls.Add(CreateActionArea(), 0, 3);
        root.Controls.Add(CreateBoardArea(_selfBoard, "Player 1 战场"), 0, 4);
        root.Controls.Add(CreatePlayerArea(player: 0), 0, 5);
        root.Controls.Add(CreateTimelineArea(), 0, 6);
        Controls.Add(root);
    }

    private Control CreateControls()
    {
        var controls = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = SurfaceBackground,
            Padding = new Padding(8),
            ColumnCount = 1,
            RowCount = 2
        };
        controls.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        controls.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var setupLine = CreateToolbarLine();
        setupLine.Controls.AddRange(
        [
            new Label { Text = "P1 卡组", AutoSize = true, ForeColor = Color.White, Padding = new Padding(0, 7, 0, 0) },
            _firstDeck,
            _firstAgent,
            new Label { Text = "对阵", AutoSize = true, ForeColor = Color.Gainsboro, Padding = new Padding(8, 7, 8, 0) },
            _secondAgent,
            _secondDeck,
            new Label { Text = "局数", AutoSize = true, ForeColor = Color.Gainsboro, Padding = new Padding(8, 7, 0, 0) },
            _matchCount,
            new Label { Text = "推演", AutoSize = true, ForeColor = Color.Gainsboro, Padding = new Padding(8, 7, 0, 0) },
            _firstRollouts,
            new Label { Text = "/", AutoSize = true, ForeColor = Color.Gainsboro, Padding = new Padding(2, 7, 2, 0) },
            _secondRollouts,
            _newMatchButton,
            _resultSummary
        ]);

        var playbackLine = CreateToolbarLine();
        playbackLine.Controls.AddRange(
        [
            _matchSelector,
            _previousButton,
            _nextButton,
            _autoPlayButton,
            _exportButton,
            _batchExportButton,
            _progressLabel,
            new Label
            {
                Text = "快捷键：← → / 空格",
                AutoSize = true,
                ForeColor = Color.WhiteSmoke,
                Font = new Font("Microsoft YaHei UI", 10, FontStyle.Bold),
                BackColor = Color.FromArgb(20, 35, 52),
                Padding = new Padding(12, 6, 12, 6),
                Margin = new Padding(8, 0, 0, 0)
            }
        ]);
        controls.Controls.Add(setupLine, 0, 0);
        controls.Controls.Add(playbackLine, 0, 1);
        return controls;
    }

    private static FlowLayoutPanel CreateToolbarLine() => new()
    {
        Dock = DockStyle.Fill,
        AutoSize = true,
        WrapContents = false,
        BackColor = SurfaceBackground,
        Margin = new Padding(0),
        Padding = new Padding(0, 0, 0, 4)
    };

    private static void StyleToolbarButton(Button button, bool isPrimary = false)
    {
        button.AutoSize = false;
        button.FlatStyle = FlatStyle.Flat;
        button.UseVisualStyleBackColor = false;
        button.BackColor = isPrimary ? Color.FromArgb(38, 111, 155) : Color.FromArgb(54, 77, 101);
        button.ForeColor = Color.White;
        button.Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Bold);
        var minimumWidth = isPrimary ? 112 : 96;
        button.Size = new Size(Math.Max(minimumWidth, TextRenderer.MeasureText(button.Text, button.Font).Width + 18), 34);
        button.FlatAppearance.BorderColor = Color.FromArgb(182, 214, 238);
        button.FlatAppearance.BorderSize = 1;
        button.FlatAppearance.MouseOverBackColor = Color.FromArgb(61, 133, 176);
        button.FlatAppearance.MouseDownBackColor = Color.FromArgb(28, 88, 126);
    }

    private Control CreatePlayerArea(int player)
    {
        var table = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = BoardBackground,
            Margin = new Padding(0, 4, 0, 4)
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, player == 1 ? 72 : 28));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, player == 1 ? 28 : 72));

        var hand = CreateHandArea(player == 1 ? _opponentHandTitle : _selfHandTitle, player == 1 ? _opponentHand : _selfHand);
        var leader = CreateLeaderArea(player == 1 ? _opponentLeaderLabel : _selfLeaderLabel);
        if (player == 1)
        {
            table.Controls.Add(hand, 0, 0);
            table.Controls.Add(leader, 1, 0);
        }
        else
        {
            table.Controls.Add(leader, 0, 0);
            table.Controls.Add(hand, 1, 0);
        }

        return table;
    }

    private static Control CreateHandArea(Label title, FlowLayoutPanel hand)
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = SurfaceBackground,
            Margin = new Padding(0, 0, 8, 0),
            Padding = new Padding(6)
        };
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        panel.Controls.Add(title, 0, 0);
        panel.Controls.Add(hand, 0, 1);
        return panel;
    }

    private static Control CreateLeaderArea(Label leaderLabel) => new Panel
    {
        Dock = DockStyle.Fill,
        BackColor = Color.FromArgb(52, 72, 92),
        Padding = new Padding(8),
        Controls = { leaderLabel }
    };

    private static Control CreateBoardArea(TableLayoutPanel board, string title)
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(0, 4, 0, 4),
            BackColor = Color.FromArgb(30, 50, 69),
            Padding = new Padding(8)
        };
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        panel.Controls.Add(new Label
        {
            Text = title + "　（最多 5 个随从／护符）",
            AutoSize = true,
            ForeColor = Color.LightSteelBlue,
            Padding = new Padding(0, 0, 0, 4)
        }, 0, 0);
        panel.Controls.Add(board, 0, 1);
        return panel;
    }

    private Control CreateActionArea()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(0, 4, 0, 4),
            Padding = new Padding(0, 4, 0, 4),
            BackColor = Color.FromArgb(23, 38, 56)
        };
        // Keep 56+ pixels for the label after margins/padding at 100% and 125% DPI.
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        panel.Controls.Add(_arrowCanvas, 0, 0);
        panel.Controls.Add(_actionLabel, 0, 1);
        return panel;
    }

    private Control CreateTimelineArea()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            BackColor = SurfaceBackground,
            Padding = new Padding(8),
            Margin = new Padding(0, 4, 0, 0)
        };
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100F / 3));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100F / 3));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100F / 3));
        panel.Controls.Add(new Label
        {
            Text = "操作预览（上一／当前／下一；方向键可逐步确认）",
            AutoSize = true,
            ForeColor = Color.LightSteelBlue,
            Padding = new Padding(0, 0, 0, 3)
        }, 0, 0);
        panel.Controls.Add(_previousActionPreview, 0, 1);
        panel.Controls.Add(_currentActionPreview, 0, 2);
        panel.Controls.Add(_nextActionPreview, 0, 3);
        return panel;
    }

    private static Label CreateActionPreviewLabel(Color background, FontStyle fontStyle = FontStyle.Regular) => new()
    {
        Dock = DockStyle.Fill,
        AutoEllipsis = true,
        BackColor = background,
        BorderStyle = BorderStyle.FixedSingle,
        ForeColor = Color.WhiteSmoke,
        Font = new Font("Microsoft YaHei UI", 9.25F, fontStyle),
        Padding = new Padding(8, 4, 8, 4),
        TextAlign = ContentAlignment.MiddleLeft,
        Margin = new Padding(0, 1, 0, 1)
    };

    private void LoadDeckChoices()
    {
        var decks = DeckCatalog.All.Select(deck => new ReplayDeckChoice(deck.Id, deck.Name)).ToArray();
        _firstDeck.Items.AddRange(decks);
        _secondDeck.Items.AddRange(decks);
        if (decks.Length == 0)
        {
            _newMatchButton.Enabled = false;
            _actionLabel.Text = "还没有可用卡组，请先在卡组编辑器保存一副 40 张卡组。";
            return;
        }

        var defaultDeck = decks.FirstOrDefault(deck => deck.Id == "DECK-002") ?? decks[0];
        _firstDeck.SelectedItem = defaultDeck;
        _secondDeck.SelectedItem = defaultDeck;
    }

    private void GenerateMatches()
    {
        if (_firstDeck.SelectedItem is not ReplayDeckChoice firstDeck ||
            _secondDeck.SelectedItem is not ReplayDeckChoice secondDeck ||
            _firstAgent.SelectedItem is not string firstAgent ||
            _secondAgent.SelectedItem is not string secondAgent)
        {
            return;
        }

        try
        {
            UseWaitCursor = true;
            _newMatchButton.Enabled = false;
            _matches.Clear();
            var count = Decimal.ToInt32(_matchCount.Value);
            var firstRollouts = int.Parse((string)_firstRollouts.SelectedItem!, CultureInfo.InvariantCulture);
            var secondRollouts = int.Parse((string)_secondRollouts.SelectedItem!, CultureInfo.InvariantCulture);
            for (var index = 1; index <= count; index++)
            {
                var seed = CreateSeed();
                var firstDeckDefinition = DeckCatalog.Create(firstDeck.Id, $"{firstDeck.Name} P1");
                var secondDeckDefinition = DeckCatalog.Create(secondDeck.Id, $"{secondDeck.Name} P2");
                var initial = GameEngine.CreateGame(firstDeckDefinition, secondDeckDefinition, seed);
                var steps = new List<MatchStep>();
                var result = MatchRunner.PlayToEnd(
                    initial,
                    CreateAgent(firstAgent, seed + 1, firstRollouts),
                    CreateAgent(secondAgent, seed + 2, secondRollouts),
                    onStep: steps.Add);

                _matches.Add(new ReplayMatch(
                    index,
                    seed,
                    firstDeck.ToString(),
                    secondDeck.ToString(),
                    firstAgent,
                    secondAgent,
                    initial,
                    steps,
                    result.Winner));
            }

            PopulateMatchSelector();
            ShowResultSummary();
        }
        catch (Exception exception)
        {
            MessageBox.Show(exception.Message, "无法生成回放", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            UseWaitCursor = false;
            _newMatchButton.Enabled = true;
        }
    }

    /// <summary>
    /// 汇总本次批量生成里双方的胜局。原来只把每局结果列在下拉框里，看不出"谁更强"：
    /// 在这种样本量下 18:12 和 15:15 没有区别，所以这里把误差范围一起报出来。
    /// </summary>
    private void ShowResultSummary()
    {
        var total = _matches.Count;
        if (total == 0)
        {
            _resultSummary.Text = string.Empty;
            return;
        }

        var firstWins = _matches.Count(match => match.Winner == 0);
        var secondWins = total - firstWins;
        var firstShare = 100.0 * firstWins / total;
        // 按最坏情况 p=0.5 估算 95% 误差，用来提醒"这么点局数说明不了问题"。
        var margin = 1.96 * Math.Sqrt(0.25 / total) * 100.0;
        var firstRollouts = AgentSearches(_firstAgent.SelectedItem as string)
            ? _firstRollouts.SelectedItem?.ToString() ?? "?"
            : "—";
        var secondRollouts = AgentSearches(_secondAgent.SelectedItem as string)
            ? _secondRollouts.SelectedItem?.ToString() ?? "?"
            : "—";
        var rollouts = $"{firstRollouts} / {secondRollouts}";
        // 把阵容一起写进汇总：下拉框选错了（例如两边牌手没改、或推演次数没改）光看胜率是发现不了的。
        var lineup = $"{_firstAgent.SelectedItem ?? "?"} vs {_secondAgent.SelectedItem ?? "?"}";
        _resultSummary.Text =
            $"{lineup} ｜ 推演 {rollouts} ｜ " +
            $"P1 {firstWins} 胜 / {secondWins} 负（{firstShare:F1}%）｜ " +
            $"{total} 局的 95% 误差约 ±{margin:F0} 个百分点";
    }

    private void PopulateMatchSelector()
    {
        _isUpdatingMatchSelector = true;
        _matchSelector.Items.Clear();
        _matchSelector.Items.AddRange(_matches.Cast<object>().ToArray());
        _matchSelector.Enabled = _matches.Count > 0;
        _matchSelector.SelectedIndex = _matches.Count > 0 ? 0 : -1;
        _isUpdatingMatchSelector = false;

        _batchExportButton.Enabled = _matches.Count > 0;
        if (_matches.Count > 0)
        {
            LoadMatch(_matches[0]);
        }
        else
        {
            ShowEmptyState();
        }
    }

    private void MatchSelectionChanged(object? sender, EventArgs eventArgs)
    {
        if (_isUpdatingMatchSelector || _matchSelector.SelectedItem is not ReplayMatch match)
        {
            return;
        }

        StopAutoPlay();
        LoadMatch(match);
    }

    private void LoadMatch(ReplayMatch match)
    {
        _initialState = match.InitialState;
        _steps = match.Steps;
        _currentStepIndex = -1;
        _exportButton.Enabled = match.Steps.Count > 0;
        UpdatePlayback();
    }

    /// <summary>
    /// 只有前瞻类牌手会向前模拟；规则牌手和随机牌手都是一步决策，推演次数对它们完全无效
    /// （传进去会被直接丢弃）。把无意义的那个下拉框置灰，免得设了一个看起来生效、其实被忽略的数字。
    /// </summary>
    private void UpdateRolloutAvailability()
    {
        _firstRollouts.Enabled = AgentSearches(_firstAgent.SelectedItem as string);
        _secondRollouts.Enabled = AgentSearches(_secondAgent.SelectedItem as string);
    }

    /// <summary>
    /// 冻结版本的**定义值**。这两个数字是它们身份的组成部分，所以选到它们时**忽略推演下拉框** ——
    /// 否则"打 2.0"到底是多少次推演就说不清了（2.0 的定义是 60，1.0 是 10）。
    /// </summary>
    private const int FrozenV1Rollouts = 10;

    private const int FrozenV2Rollouts = 60;

    /// <summary>
    /// 下拉框里的牌手名单。命名和命令行基准（<c>AgentBenchmark.DisplayName</c>）保持一致，
    /// 免得同一份代码在两处叫不同的名字 —— 之前就是这样把"老基线"叫成了"1.0"。
    /// </summary>
    private static readonly string[] AgentNames =
    [
        "前瞻牌手 3.0（开发中）",
        "前瞻牌手 2.0（冻结）",
        "前瞻牌手 1.0（冻结）",
        "前瞻牌手·旧冻结基线",
        "规则牌手",
        "规则牌手·冻结基线",
        "随机牌手"
    ];

    /// <summary>
    /// 只有 3.0 是活的，推演次数才由下拉框决定。四个冻结版本都用各自的定义值。
    /// </summary>
    private static bool AgentSearches(string? agentName) =>
        agentName is "前瞻牌手 3.0（开发中）";

    private static IPlayerAgent CreateAgent(string agentName, ulong seed, int rolloutsPerAction) => agentName switch
    {
        "前瞻牌手 3.0（开发中）" => new LookaheadPlayerAgent(rolloutsPerAction, futureTurnHorizon: 3, seed),
        "前瞻牌手 2.0（冻结）" => new LookaheadPlayerAgentV2(seed, FrozenV2Rollouts),
        "前瞻牌手 1.0（冻结）" => new LookaheadPlayerAgentV1(
            FrozenV1Rollouts, futureTurnHorizon: 3, seed, minimumPracticalAdvantage: 0.0),
        "前瞻牌手·旧冻结基线" => new BaselineLookaheadPlayerAgent(
            FrozenV1Rollouts, futureTurnHorizon: 3, seed),
        "规则牌手" => new GreedyPlayerAgent(),
        "规则牌手·冻结基线" => new BaselineGreedyPlayerAgent(),
        "随机牌手" => new RandomPlayerAgent(unchecked((int)seed)),
        _ => throw new ArgumentException($"未知牌手：{agentName}")
    };

    private static ulong CreateSeed()
    {
        var value = BitConverter.ToUInt64(RandomNumberGenerator.GetBytes(sizeof(ulong)));
        return value == 0 ? 1UL : value;
    }

    private void MoveNext()
    {
        if (_initialState is null)
        {
            return;
        }

        if (_currentStepIndex + 1 >= _steps.Count)
        {
            StopAutoPlay();
            return;
        }

        _currentStepIndex++;
        UpdatePlayback();
    }

    private void MovePrevious()
    {
        if (_initialState is null || _currentStepIndex < 0)
        {
            return;
        }

        StopAutoPlay();
        _currentStepIndex--;
        UpdatePlayback();
    }

    private void ToggleAutoPlay()
    {
        if (_initialState is null)
        {
            return;
        }

        if (_autoPlayTimer.Enabled)
        {
            StopAutoPlay();
        }
        else
        {
            _autoPlayTimer.Start();
            _autoPlayButton.Text = "❚❚ 暂停";
        }
    }

    private void StopAutoPlay()
    {
        _autoPlayTimer.Stop();
        _autoPlayButton.Text = "▶ 自动播放";
    }

    private void ReplayFormKeyDown(object? sender, KeyEventArgs eventArgs)
    {
        if (eventArgs.KeyCode is Keys.Right or Keys.Space)
        {
            MoveNext();
            eventArgs.Handled = true;
        }
        else if (eventArgs.KeyCode == Keys.Left)
        {
            MovePrevious();
            eventArgs.Handled = true;
        }
    }

    private void UpdatePlayback()
    {
        if (_initialState is null)
        {
            ShowEmptyState();
            return;
        }

        var state = _currentStepIndex < 0 ? _initialState : _steps[_currentStepIndex].AfterState;
        RenderPlayer(state.Players[1], playerIndex: 1);
        RenderPlayer(state.Players[0], playerIndex: 0);
        RenderBoard(_opponentBoard, state.Players[1].Board, state.Players[1].Amulets);
        RenderBoard(_selfBoard, state.Players[0].Board, state.Players[0].Amulets);

        if (_currentStepIndex < 0)
        {
            _actionLabel.Text = $"开局：Player {_initialState.StartingPlayer + 1} 先手。按“下一步”查看双方换牌。";
            _arrowCanvas.Scene = null;
            _progressLabel.Text = $"开局 / {_steps.Count} 步";
        }
        else
        {
            var step = _steps[_currentStepIndex];
            _actionLabel.Text = DescribeStep(step);
            _arrowCanvas.Scene = GetArrowScene(step);
            _progressLabel.Text = $"第 {_currentStepIndex + 1} / {_steps.Count} 步";
        }

        _previousButton.Enabled = _currentStepIndex >= 0;
        _nextButton.Enabled = _currentStepIndex + 1 < _steps.Count;
        _autoPlayButton.Enabled = _steps.Count > 0;
        UpdateActionPreviews();
        // 人机对战：卡牌格子每次都是新造的，点击/拖拽要重新挂上去。
        // 非实时对局时这个方法直接返回，回放模式不受影响。
        // 传 true 表示"格子是全新的"，让它可以丢掉旧的防重复挂记录。
        RefreshHumanInteraction(tilesAreFresh: true);
    }

    private void UpdateActionPreviews()
    {
        var opening = _initialState is null
            ? "开局"
            : $"开局：Player {_initialState.StartingPlayer + 1} 先手。";
        var previous = _currentStepIndex <= 0 ? opening : ShortDescription(_steps[_currentStepIndex - 1]);
        var current = _currentStepIndex < 0 ? opening : ShortDescription(_steps[_currentStepIndex]);
        var next = _currentStepIndex + 1 < _steps.Count
            ? ShortDescription(_steps[_currentStepIndex + 1])
            : "本局没有下一步。";

        _previousActionPreview.Text = "上一：" + previous;
        _currentActionPreview.Text = "当前：" + current;
        _nextActionPreview.Text = "下一：" + next;
        _cardToolTip.SetToolTip(_previousActionPreview, previous);
        _cardToolTip.SetToolTip(_currentActionPreview, current);
        _cardToolTip.SetToolTip(_nextActionPreview, next);
    }

    private void ShowEmptyState()
    {
        _opponentLeaderLabel.Text = "Player 2\n等待对局";
        _selfLeaderLabel.Text = "Player 1\n等待对局";
        _opponentHandTitle.Text = "Player 2 手牌";
        _selfHandTitle.Text = "Player 1 手牌";
        _actionLabel.Text = "选择双方卡组、牌手和局数后，点击“生成对局”。";
        RenderHand(_opponentHand, []);
        RenderHand(_selfHand, []);
        RenderBoard(_opponentBoard, Array.Empty<FollowerInstance>(), Array.Empty<AmuletInstance>());
        RenderBoard(_selfBoard, Array.Empty<FollowerInstance>(), Array.Empty<AmuletInstance>());
        _arrowCanvas.Scene = null;
        _progressLabel.Text = "尚未开始";
        _exportButton.Enabled = false;
        _batchExportButton.Enabled = false;
        _previousActionPreview.Text = "上一：—";
        _currentActionPreview.Text = "当前：等待生成对局。";
        _nextActionPreview.Text = "下一：—";
        // 行动记录是另一半界面上的东西，换局时要一起清掉，否则会留着上一局的记录。
        _logBody.Text = string.Empty;
        _thinkingBody.Text = string.Empty;
    }

    private void ExportCurrentMachineReplay()
    {
        if (_matchSelector.SelectedItem is not ReplayMatch match)
        {
            return;
        }

        using var dialog = new SaveFileDialog
        {
            Title = "导出机器回放 JSON",
            Filter = "JSON 文件 (*.json)|*.json",
            FileName = $"shadowverse_replay_{match.Index:D3}_{match.Seed}.json",
            AddExtension = true,
            DefaultExt = "json"
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            WriteMachineReplay(dialog.FileName, match);
        }
    }

    private void ExportAllMachineReplays()
    {
        if (_matches.Count == 0)
        {
            return;
        }

        using var dialog = new FolderBrowserDialog
        {
            Description = "选择机器回放 JSON 的导出位置",
            UseDescriptionForTitle = true
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        try
        {
            var folder = Path.Combine(dialog.SelectedPath, $"shadowverse_replays_{DateTime.Now:yyyyMMdd_HHmmss}");
            Directory.CreateDirectory(folder);
            var exported = 0;
            foreach (var match in _matches)
            {
                if (WriteMachineReplay(Path.Combine(folder, $"replay_{match.Index:D3}_{match.Seed}.json"), match, showSuccessMessage: false))
                {
                    exported++;
                }
            }

            var index = _matches.Select(match => new { i = match.Index, seed = match.Seed.ToString(), win = match.Winner, n = match.Steps.Count });
            // 把阵容写进索引：只看胜负数字分不清"这次的 P2 到底换没换"，
            // 而选错下拉框会静默产出一个看起来正常的实验。
            var config = new
            {
                p1Agent = _firstAgent.SelectedItem?.ToString(),
                p2Agent = _secondAgent.SelectedItem?.ToString(),
                p1Deck = _firstDeck.SelectedItem?.ToString(),
                p2Deck = _secondDeck.SelectedItem?.ToString(),
                p1Rollouts = _firstRollouts.SelectedItem?.ToString(),
                p2Rollouts = _secondRollouts.SelectedItem?.ToString()
            };
            File.WriteAllText(
                Path.Combine(folder, "index.json"),
                JsonSerializer.Serialize(new { v = 1, f = "SVP/R1", cfg = config, matches = index }, CompactJson));
            MessageBox.Show($"已导出 {exported}/{_matches.Count} 局机器回放：\n{folder}", "批量导出完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception exception)
        {
            MessageBox.Show(exception.Message, "无法批量导出", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static readonly JsonSerializerOptions CompactJson = new() { WriteIndented = false };

    private bool WriteMachineReplay(string path, ReplayMatch match, bool showSuccessMessage = true)
    {
        try
        {
            File.WriteAllText(path, JsonSerializer.Serialize(BuildMachineReplay(match), CompactJson));
            if (showSuccessMessage)
            {
                MessageBox.Show("已导出机器回放 JSON。把该文件直接拖进聊天窗口即可分析。", "导出成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }

            return true;
        }
        catch (Exception exception)
        {
            if (showSuccessMessage)
            {
                MessageBox.Show(exception.Message, "无法导出回放", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }

            return false;
        }
    }

    /// <summary>
    /// 机器可读回放：**序列化逻辑在引擎里**（<see cref="MachineReplay"/>），
    /// 这样命令行也能导出同一份格式，自检才验得到。
    /// </summary>
    private static object BuildMachineReplay(ReplayMatch match) => MachineReplay.Build(
        match.Seed,
        match.FirstDeckInfo,
        match.FirstAgentInfo,
        match.SecondDeckInfo,
        match.SecondAgentInfo,
        match.InitialState,
        match.Winner,
        match.Steps);
    // Compact event schema: [turn, actor, action-code, ...action-specific values].
    private static object?[] ToMachineEvent(MatchStep step)
    {
        var before = step.BeforeState.Players[step.ActingPlayer];
        return step.Action switch
        {
            MulliganAction action => [step.BeforeState.TurnNumber, step.ActingPlayer, "M", action.ReplaceInstanceIds],
            PlayFollowerAction action => [step.BeforeState.TurnNumber, step.ActingPlayer, "F", CardId(before, action.CardInstanceId), action.CardInstanceId, action.HandCardTargetInstanceId, action.EnemyFollowerTargetInstanceIds, action.ModeChoiceIndex, action.OwnHandCardTargetInstanceIds],
            PlayAmuletAction action => [step.BeforeState.TurnNumber, step.ActingPlayer, "A", CardId(before, action.CardInstanceId), action.CardInstanceId],
            PlayCrystallizeAction action => [step.BeforeState.TurnNumber, step.ActingPlayer, "Y", CardId(before, action.CardInstanceId), action.CardInstanceId],
            PlayAccelerateAction action => [step.BeforeState.TurnNumber, step.ActingPlayer, "X", CardId(before, action.CardInstanceId), action.CardInstanceId],
            PlaySpellAction action => [step.BeforeState.TurnNumber, step.ActingPlayer, "S", CardId(before, action.CardInstanceId), action.CardInstanceId, SpellTargetCode(action.Target), action.OwnHandCardTargetInstanceIds],
            EvolveAction action => [step.BeforeState.TurnNumber, step.ActingPlayer, "E", action.FollowerInstanceId, action.ModeChoiceIndex, action.OwnHandCardTargetInstanceIds, action.EnemyFollowerTargetInstanceId],
            SuperEvolveAction action => [step.BeforeState.TurnNumber, step.ActingPlayer, "U", action.FollowerInstanceId, action.OtherFollowerTargetInstanceId, action.ModeChoiceIndex, action.OwnHandCardTargetInstanceIds, action.EnemyFollowerTargetInstanceId],
            UseExtraPlayPointAction => [step.BeforeState.TurnNumber, step.ActingPlayer, "P"],
            AttackLeaderAction action => [step.BeforeState.TurnNumber, step.ActingPlayer, "L", action.AttackerInstanceId],
            AttackFollowerAction action => [step.BeforeState.TurnNumber, step.ActingPlayer, "B", action.AttackerInstanceId, action.DefenderInstanceId],
            EndTurnAction => [step.BeforeState.TurnNumber, step.ActingPlayer, "T"],
            _ => [step.BeforeState.TurnNumber, step.ActingPlayer, "?", step.Action.GetType().Name]
        };
    }

    private static string CardId(PlayerState player, int instanceId) =>
        player.Hand.Single(card => card.InstanceId == instanceId).Definition.Id;

    private static object? SpellTargetCode(SpellTarget? target) => target switch
    {
        null => null,
        EnemyLeaderTarget => "L",
        EnemyFollowerTarget follower => follower.FollowerInstanceId,
        _ => "?"
    };

    private void RenderPlayer(PlayerState player, int playerIndex)
    {
        var leaderLabel = playerIndex == 1 ? _opponentLeaderLabel : _selfLeaderLabel;
        var handTitle = playerIndex == 1 ? _opponentHandTitle : _selfHandTitle;
        var hand = playerIndex == 1 ? _opponentHand : _selfHand;
        // **刻意压成 3 行**：主战者那一行是 RowStyle(Absolute, 102) 的固定高度，
        // 排 4 行在 125% DPI 下会把最后一行裁掉 —— 用户实测"看不到墓地数量"就是这么来的。
        // 加行之前先看这里还剩多少高度。
        leaderLabel.Text = $"Player {playerIndex + 1}：生命 {player.Health}/{player.MaxHealth}\n" +
                           $"PP {player.CurrentPlayPoints}/{player.MaxPlayPoints}　" +
                           $"EP {player.EvolutionPoints}　SEP {player.SuperEvolutionPoints}\n" +
                           $"牌库 {player.Deck.Count}　墓地 {player.Graveyard.Count}";
        handTitle.Text = $"Player {playerIndex + 1} 手牌（{player.Hand.Count}）";
        RenderHand(hand, player.Hand);
    }

    private void RenderHand(FlowLayoutPanel panel, IReadOnlyList<CardInstance> cards)
    {
        panel.SuspendLayout();
        panel.Controls.Clear();
        foreach (var card in cards)
        {
            var tile = CreateCardTile(card.Definition, card.Definition.Attack, card.Definition.Defense, compact: true);
            // 标出这是哪张牌：人机对战的点击换牌、拖拽出牌都要靠这个 Tag 反查实例号。
            tile.Tag = card;
            panel.Controls.Add(tile);
        }

        if (cards.Count == 0)
        {
            panel.Controls.Add(new Label { Text = "（无手牌）", AutoSize = true, ForeColor = Color.LightGray, Padding = new Padding(6, 10, 0, 0) });
        }

        panel.ResumeLayout();
    }

    private void RenderBoard(
        TableLayoutPanel board,
        IReadOnlyList<FollowerInstance> followers,
        IReadOnlyList<AmuletInstance> amulets)
    {
        board.SuspendLayout();
        board.Controls.Clear();
        for (var slot = 0; slot < PlayerState.BoardLimit; slot++)
        {
            var slotPanel = new Panel
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(4),
                BackColor = Color.FromArgb(41, 63, 83),
                BorderStyle = BorderStyle.FixedSingle
            };
            if (slot < followers.Count)
            {
                var follower = followers[slot];
                var tile = CreateCardTile(
                    follower.Definition,
                    follower.Attack,
                    follower.CurrentDefense,
                    compact: false,
                    isEvolved: follower.IsEvolved);
                // 场上的格子也要能反查到实例：拖拽攻击、右键进化都靠它。
                tile.Tag = follower;
                slotPanel.Tag = follower;
                slotPanel.Controls.Add(tile);
            }
            else
            {
                var amuletIndex = slot - followers.Count;
                if (amuletIndex < amulets.Count)
                {
                    var amulet = amulets[amuletIndex];
                    var tile = CreateCardTile(
                        amulet.Definition,
                        attack: 0,
                        defense: 0,
                        compact: false,
                        currentCountdown: amulet.Countdown);
                    tile.Tag = amulet;
                    slotPanel.Tag = amulet;
                    slotPanel.Controls.Add(tile);
                }
            }

            board.Controls.Add(slotPanel, slot, 0);
        }

        board.ResumeLayout();
    }

    private Control CreateCardTile(
        CardDefinition definition,
        int attack,
        int defense,
        bool compact,
        bool isEvolved = false,
        int? currentCountdown = null)
    {
        var panel = new Panel
        {
            Dock = compact ? DockStyle.None : DockStyle.Fill,
            Size = compact ? new Size(128, 42) : Size.Empty,
            Margin = compact ? new Padding(3) : new Padding(0),
            BackColor = TypeColor(definition.Type),
            BorderStyle = isEvolved ? BorderStyle.Fixed3D : BorderStyle.FixedSingle,
            Padding = new Padding(4),
            Cursor = Cursors.Help
        };
        var body = definition.Type == CardType.Follower
            ? $"  {attack}/{defense}"
            : currentCountdown is int countdown
                ? $"  吟唱 {countdown}"
                : string.Empty;
        var evolved = isEvolved ? " ★" : string.Empty;
        var label = new Label
        {
            Dock = DockStyle.Fill,
            Text = definition.Name + body + evolved,
            ForeColor = Color.White,
            Font = new Font("Microsoft YaHei UI", compact ? 8.5F : 9.5F, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleCenter,
            AutoEllipsis = true
        };
        panel.Controls.Add(label);
        var detail = CardDetail(definition, attack, defense, isEvolved, currentCountdown);
        _cardToolTip.SetToolTip(panel, detail);
        _cardToolTip.SetToolTip(label, detail);
        ConfigureCardDetailHover(panel, label, detail);
        return panel;
    }

    private void ConfigureCardDetailHover(Panel tile, Label cardLabel, string detail)
    {
        void ShowDetail(object? _, EventArgs __)
        {
            var screen = Screen.FromControl(this).WorkingArea;
            var below = tile.PointToScreen(new Point(tile.Width / 2, tile.Height + 8));
            var showAbove = below.Y + 220 > screen.Bottom;
            _cardToolTip.Show(
                detail,
                tile,
                new Point(tile.Width / 2, showAbove ? -8 : tile.Height + 8),
                15000);
        }

        void HideDetail(object? _, EventArgs __)
        {
            if (!tile.ClientRectangle.Contains(tile.PointToClient(Cursor.Position)))
            {
                _cardToolTip.Hide(tile);
            }
        }

        tile.MouseEnter += ShowDetail;
        cardLabel.MouseEnter += ShowDetail;
        tile.MouseLeave += HideDetail;
        cardLabel.MouseLeave += HideDetail;
    }

    private static string CardDetail(
        CardDefinition definition,
        int attack,
        int defense,
        bool isEvolved,
        int? currentCountdown = null)
    {
        var lines = new List<string>
        {
            definition.Name,
            $"{CardTypeName(definition.Type)} · {CardProfessionName(definition.Profession)} · {CardRarityName(definition.Rarity)} · {definition.Cost} 费"
        };

        if (definition.Type == CardType.Follower)
        {
            var printedStats = $"原始身材 {definition.Attack}/{definition.Defense}";
            var currentStats = $"当前身材 {attack}/{defense}";
            lines.Add(isEvolved ? $"{currentStats}（已进化；{printedStats}）" : $"{currentStats}（{printedStats}）");
        }
        else if (definition.Countdown is int countdown)
        {
            lines.Add(currentCountdown is int current
                ? $"吟唱：{current}/{countdown}"
                : $"吟唱：{countdown}");
        }

        var keywords = KeywordNames(definition.Keywords);
        if (keywords.Count > 0)
        {
            lines.Add("关键词：" + string.Join("、", keywords));
        }

        if (definition.Traits is { Count: > 0 })
        {
            lines.Add("类型：" + string.Join("、", definition.Traits));
        }

        if (!string.IsNullOrWhiteSpace(definition.EffectText))
        {
            lines.Add("效果：" + definition.EffectText);
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static IReadOnlyList<string> KeywordNames(CardKeyword keywords)
    {
        var names = new List<string>();
        if (keywords.HasFlag(CardKeyword.Ward)) names.Add("守护");
        if (keywords.HasFlag(CardKeyword.Storm)) names.Add("疾驰");
        if (keywords.HasFlag(CardKeyword.Bane)) names.Add("毁灭");
        if (keywords.HasFlag(CardKeyword.Rush)) names.Add("突进");
        if (keywords.HasFlag(CardKeyword.IgnoreWard)) names.Add("无视守护");
        if (keywords.HasFlag(CardKeyword.Barrier)) names.Add("屏障");
        if (keywords.HasFlag(CardKeyword.Intimidate)) names.Add("威慑");
        if (keywords.HasFlag(CardKeyword.Aura)) names.Add("灵气");
        if (keywords.HasFlag(CardKeyword.Drain)) names.Add("虹吸");
        return names;
    }

    private static string CardTypeName(CardType type) => type switch
    {
        CardType.Follower => "随从",
        CardType.Spell => "法术",
        CardType.Amulet => "护符",
        _ => "未知类型"
    };

    private static string CardRarityName(CardRarity rarity) => rarity switch
    {
        CardRarity.Bronze => "铜卡",
        CardRarity.Silver => "银卡",
        CardRarity.Gold => "金卡",
        CardRarity.Rainbow => "虹卡",
        _ => "未知稀有度"
    };

    private static string CardProfessionName(CardProfession profession) => profession switch
    {
        CardProfession.Elf => "精灵",
        CardProfession.Royal => "皇家护卫",
        CardProfession.Witch => "巫师",
        CardProfession.Dragon => "龙族",
        CardProfession.Nightmare => "梦魇",
        CardProfession.Bishop => "主教",
        CardProfession.Nemesis => "超越者",
        CardProfession.Neutral => "中立",
        _ => "未知职业"
    };

    private static Color TypeColor(CardType type) => type switch
    {
        CardType.Follower => FollowerColor,
        CardType.Spell => SpellColor,
        CardType.Amulet => AmuletColor,
        _ => Color.DimGray
    };

    private static string DescribeStep(MatchStep step)
    {
        var player = step.ActingPlayer;
        var before = step.BeforeState.Players[player];
        var after = step.AfterState.Players[player];
        return step.Action switch
        {
            MulliganAction mulligan => DescribeMulligan(step, mulligan),
            PlayFollowerAction play => $"【出随从】Player {player + 1}：{CardText(HandCard(before, play.CardInstanceId))} 进入战场。",
            PlayAmuletAction play => $"【出护符】Player {player + 1}：{CardText(HandCard(before, play.CardInstanceId))}。",
            PlayCrystallizeAction play => $"【结晶】Player {player + 1}：{CardText(HandCard(before, play.CardInstanceId))} 以护符形态进入战场。",
            PlayAccelerateAction play => $"【激奏】Player {player + 1}：{CardText(HandCard(before, play.CardInstanceId))}。",
            PlaySpellAction play => $"【法术】Player {player + 1}：{CardText(HandCard(before, play.CardInstanceId))}。",
            EvolveAction evolve =>
                $"【进化】Player {player + 1}：{FollowerText(before.Board.Single(follower => follower.InstanceId == evolve.FollowerInstanceId))}" +
                EvolutionTargetText(step, evolve.FollowerInstanceId, evolve.EnemyFollowerTargetInstanceId) + "。",
            SuperEvolveAction evolve =>
                $"【超进化】Player {player + 1}：{FollowerText(before.Board.Single(follower => follower.InstanceId == evolve.FollowerInstanceId))}" +
                EvolutionTargetText(step, evolve.FollowerInstanceId, evolve.EnemyFollowerTargetInstanceId) + "。",
            AttackLeaderAction attack => DescribeLeaderAttack(step, attack),
            AttackFollowerAction attack => DescribeFollowerAttack(step, attack),
            UseExtraPlayPointAction => $"【额外 PP】Player {player + 1}：PP {before.CurrentPlayPoints} → {after.CurrentPlayPoints}。",
            EndTurnAction => $"【结束回合】Player {player + 1}。",
            _ => "未知操作。"
        };
    }

    private static string ShortDescription(MatchStep step) =>
        $"回合 {step.BeforeState.TurnNumber} · P{step.ActingPlayer + 1}　{DescribeStep(step)}";

    private static string DescribeMulligan(MatchStep step, MulliganAction action)
    {
        var hand = step.BeforeState.Players[step.ActingPlayer].Hand;
        var changed = hand.Where(card => action.ReplaceInstanceIds.Contains(card.InstanceId)).Select(CardText).ToArray();
        var result = step.AfterState.Players[step.ActingPlayer].Hand.Select(CardText);
        return changed.Length == 0
            ? $"【起手】Player {step.ActingPlayer + 1}：不换牌。"
            : $"【换牌】Player {step.ActingPlayer + 1} 换走：{string.Join("、", changed)}；新手牌：{string.Join("、", result)}。";
    }

    private static string DescribeLeaderAttack(MatchStep step, AttackLeaderAction action)
    {
        var source = step.BeforeState.Players[step.ActingPlayer].Board.Single(follower => follower.InstanceId == action.AttackerInstanceId);
        var targetPlayer = OtherPlayer(step.ActingPlayer);
        var beforeHealth = step.BeforeState.Players[targetPlayer].Health;
        var afterHealth = step.AfterState.Players[targetPlayer].Health;
        return $"【打脸】{FollowerText(source)}  →  Player {targetPlayer + 1} 主战者，生命 {beforeHealth} → {afterHealth}。";
    }

    private static string DescribeFollowerAttack(MatchStep step, AttackFollowerAction action)
    {
        var attacker = step.BeforeState.Players[step.ActingPlayer].Board.Single(follower => follower.InstanceId == action.AttackerInstanceId);
        var targetPlayer = OtherPlayer(step.ActingPlayer);
        var defender = step.BeforeState.Players[targetPlayer].Board.Single(follower => follower.InstanceId == action.DefenderInstanceId);
        return $"【随从交战】{FollowerText(attacker)}  →  {FollowerText(defender)}。";
    }

    private static ReplayArrow? GetArrowScene(MatchStep step)
    {
        var player = step.ActingPlayer;
        return step.Action switch
        {
            AttackLeaderAction attack => new ReplayArrow(
                player,
                BoardIndex(step.BeforeState.Players[player].Board, attack.AttackerInstanceId),
                TargetSlot: 2,
                TargetsLeader: true),
            AttackFollowerAction attack => new ReplayArrow(
                player,
                BoardIndex(step.BeforeState.Players[player].Board, attack.AttackerInstanceId),
                BoardIndex(step.BeforeState.Players[OtherPlayer(player)].Board, attack.DefenderInstanceId),
                TargetsLeader: false),
            PlaySpellAction play when play.Target is EnemyFollowerTarget target => new ReplayArrow(
                player,
                SourceSlot: 2,
                BoardIndex(step.BeforeState.Players[OtherPlayer(player)].Board, target.FollowerInstanceId),
                TargetsLeader: false),
            PlaySpellAction play when play.Target is EnemyLeaderTarget => new ReplayArrow(player, 2, 2, TargetsLeader: true),
            _ => null
        };
    }

    private static int BoardIndex(IReadOnlyList<FollowerInstance> board, int instanceId) =>
        board.ToList().FindIndex(follower => follower.InstanceId == instanceId);

    private static CardInstance HandCard(PlayerState player, int instanceId) =>
        player.Hand.Single(card => card.InstanceId == instanceId);

    private static string CardText(CardInstance card) =>
        card.Definition.Type == CardType.Follower
            ? $"{card.Definition.Name}（{card.Definition.Attack}/{card.Definition.Defense}）"
            : card.Definition.Name;

    private static string FollowerText(FollowerInstance follower) =>
        $"{follower.Definition.Name}（{follower.Attack}/{follower.CurrentDefense}）";

    private static int OtherPlayer(int player) => player == 0 ? 1 : 0;

    /// <summary>Describes the enemy follower an 【进化时】 effect damaged, when one was chosen.</summary>
    private static string EvolutionTargetText(MatchStep step, int evolvingFollowerInstanceId, int? targetInstanceId)
    {
        if (targetInstanceId is null)
        {
            return string.Empty;
        }

        var player = step.ActingPlayer;
        var opponentIndex = OtherPlayer(player);
        var target = step.BeforeState.Players[opponentIndex].Board
            .FirstOrDefault(follower => follower.InstanceId == targetInstanceId.Value);
        var evolving = step.BeforeState.Players[player].Board
            .SingleOrDefault(follower => follower.InstanceId == evolvingFollowerInstanceId);
        var damage = evolving?.Definition.EvolutionEffects?
            .FirstOrDefault(effect => effect.Kind == CardEffectKind.DealDamageToEnemyFollower)?.Amount;
        if (target is null || damage is null)
        {
            return string.Empty;
        }

        var targetAfter = step.AfterState.Players[opponentIndex].Board
            .FirstOrDefault(follower => follower.InstanceId == targetInstanceId.Value);
        return targetAfter is null
            ? $"；【进化时】对 {FollowerText(target)} 造成{damage}点伤害，将其破坏"
            : $"；【进化时】对 {FollowerText(target)} 造成{damage}点伤害，剩余体力 {targetAfter.CurrentDefense}";
    }

    /// <summary>
    /// 主战者信息标签。字号刻意用 9 而不是 10 —— 这块地方是固定高的，
    /// 字号一大就会把最后一行（牌库／墓地）挤掉，而在 125% DPI 下尤其明显。
    /// </summary>
    private static Label CreateLeaderLabel() => new()
    {
        Dock = DockStyle.Fill,
        ForeColor = Color.White,
        Font = new Font("Microsoft YaHei UI", 9, FontStyle.Bold),
        TextAlign = ContentAlignment.MiddleCenter
    };

    private static Label CreateSectionLabel() => new()
    {
        AutoSize = true,
        ForeColor = Color.LightSteelBlue,
        Padding = new Padding(2, 0, 0, 2)
    };

    private static FlowLayoutPanel CreateHandFlow() => new()
    {
        Dock = DockStyle.Fill,
        AutoScroll = true,
        WrapContents = true,
        BackColor = SurfaceBackground,
        Padding = new Padding(2)
    };

    private static TableLayoutPanel CreateBoardTable()
    {
        var board = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = PlayerState.BoardLimit,
            RowCount = 1,
            BackColor = Color.FromArgb(30, 50, 69)
        };
        for (var slot = 0; slot < PlayerState.BoardLimit; slot++)
        {
            board.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F / PlayerState.BoardLimit));
        }

        board.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        return board;
    }

    private sealed record ReplayDeckChoice(string Id, string Name)
    {
        public override string ToString() => $"{Id} - {Name}";
    }

    private sealed record ReplayMatch(
        int Index,
        ulong Seed,
        string FirstDeckInfo,
        string SecondDeckInfo,
        string FirstAgentInfo,
        string SecondAgentInfo,
        GameState InitialState,
        IReadOnlyList<MatchStep> Steps,
        int Winner)
    {
        public override string ToString() => $"第 {Index} 局｜P{Winner + 1} 胜｜{Steps.Count} 步";
    }
}

/// <summary>Draws a clean directional hint between the two follower rows for attacks and targeted spells.</summary>
internal sealed class ActionArrowCanvas : Control
{
    private ReplayArrow? _scene;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public ReplayArrow? Scene
    {
        get => _scene;
        set
        {
            _scene = value;
            Invalidate();
        }
    }

    protected override void OnPaint(PaintEventArgs eventArgs)
    {
        base.OnPaint(eventArgs);
        if (_scene is not ReplayArrow scene)
        {
            return;
        }

        var slotWidth = ClientSize.Width / (float)PlayerState.BoardLimit;
        var sourceX = (scene.SourceSlot + 0.5F) * slotWidth;
        var targetX = (scene.TargetSlot + 0.5F) * slotWidth;
        var startsAtBottom = scene.FromPlayer == 0;
        var source = new PointF(sourceX, startsAtBottom ? ClientSize.Height - 4 : 4);
        var target = new PointF(targetX, startsAtBottom ? 4 : ClientSize.Height - 4);
        using var pen = new Pen(scene.TargetsLeader ? Color.Gold : Color.OrangeRed, 3)
        {
            CustomEndCap = new System.Drawing.Drawing2D.AdjustableArrowCap(5, 6, true)
        };
        eventArgs.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        eventArgs.Graphics.DrawLine(pen, source, target);
    }
}

internal sealed record ReplayArrow(int FromPlayer, int SourceSlot, int TargetSlot, bool TargetsLeader);

/// <summary>
/// WinForms' system renderer uses near-black text for disabled buttons. That is illegible on the
/// replay viewer's dark toolbar, so disabled playback controls use an explicit high-contrast draw.
/// </summary>
internal sealed class ReadableToolbarButton : Button
{
    protected override void OnPaint(PaintEventArgs eventArgs)
    {
        if (Enabled)
        {
            base.OnPaint(eventArgs);
            return;
        }

        eventArgs.Graphics.Clear(Color.FromArgb(45, 63, 81));
        ControlPaint.DrawBorder(
            eventArgs.Graphics,
            ClientRectangle,
            Color.FromArgb(126, 151, 173),
            ButtonBorderStyle.Solid);
        TextRenderer.DrawText(
            eventArgs.Graphics,
            Text,
            Font,
            ClientRectangle,
            Color.FromArgb(205, 218, 231),
            TextFormatFlags.HorizontalCenter |
            TextFormatFlags.VerticalCenter |
            TextFormatFlags.EndEllipsis |
            TextFormatFlags.SingleLine);
    }
}
