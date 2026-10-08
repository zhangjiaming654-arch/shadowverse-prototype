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
    private static readonly Color BoardBackground = SvTheme.Void;
    private static readonly Color SurfaceBackground = SvTheme.Panel;
    private static readonly Color FollowerColor = SvTheme.FollowerColor;
    private static readonly Color SpellColor = SvTheme.SpellColor;
    private static readonly Color AmuletColor = SvTheme.AmuletColor;

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
        // **必须限宽**：AutoSize 的标签在 WinForms 里不会自动裁剪，文本一长就直接
        // 画到右边「人机对战」面板上（实测用户截图：进度文字盖住了右栏）。
        // 定宽 + AutoEllipsis，超长时以"…"收尾，绝不越界。
        AutoSize = false,
        Width = 360,
        Height = 34,
        AutoEllipsis = true,
        TextAlign = ContentAlignment.MiddleLeft,
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
    private readonly FlowLayoutPanel _opponentHand = new SvHandFan() { Dock = DockStyle.Fill, Margin = new Padding(0) };
    private readonly FlowLayoutPanel _selfHand = new SvHandFan { Dock = DockStyle.Fill, Margin = new Padding(0) };
    private TableLayoutPanel? _battleLayout;
    private bool _settingsVisible;
    private (Size Size, int Dpi, bool Live)? _lastBattleLayout;
    private Control? _settingsPanel;
    private Button? _settingsToggle;
    private readonly TableLayoutPanel _opponentBoard = CreateBoardTable();
    private readonly TableLayoutPanel _selfBoard = CreateBoardTable();
    private readonly Label _actionLabel = new()
    {
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleCenter,
        Font = new Font("Microsoft YaHei UI", 10, FontStyle.Bold),
        ForeColor = Color.White,
        BackColor = Color.FromArgb(51, 75, 100),
        BorderStyle = BorderStyle.FixedSingle,
        Padding = new Padding(12, 3, 12, 3),
        Margin = new Padding(6, 0, 6, 0),
        MinimumSize = new Size(0, 32)
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
    private readonly TrackBar _seekBar = new() { Dock = DockStyle.Fill, TickStyle = TickStyle.None,
        Minimum = 0, Maximum = 1, Enabled = false, Height = 25, AutoSize = false, BackColor = SvTheme.Panel };
    private bool _updatingSeek;
    private GamePhase? _renderedPhase;
    private readonly System.Windows.Forms.Timer _autoPlayTimer = new() { Interval = 700 };

    private GameState? _initialState;
    private IReadOnlyList<MatchStep> _steps = [];
    private readonly List<ReplayMatch> _matches = [];
    private int _currentStepIndex = -1;
    private bool _isUpdatingMatchSelector;

    /// <summary>本界面自己正在跑批量生成。此时进度栏归自己管，不显示外部进度。</summary>
    private bool _isGenerating;

    /// <summary>当前进度栏显示的是不是"外部进程（命令行）的进度"。</summary>
    private bool _showingLiveProgress;

    /// <summary>
    /// 每秒轮询一次共享进度文件。**命令行跑批量对局时，这个界面也能显示它的进度** ——
    /// 两个进程之间没有共享内存，所以走文件（见 <see cref="LiveProgress"/>）。
    /// </summary>
    private readonly System.Windows.Forms.Timer _liveProgressTimer = new() { Interval = 1000 };

    public ReplayForm()
    {
        Text = "影之诗原型 - 对局回放 · 20次推演与奥义导出版";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(1000, 640);
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96, 96);
        Size = new Size(1480, 980);
        Font = new Font("Microsoft YaHei UI", 9f);
        DoubleBuffered = true;
        BackColor = BoardBackground;
        KeyPreview = true;

        _liveProgressTimer.Tick += (_, _) => RefreshLiveProgress();
        _liveProgressTimer.Start();

        BuildLayout();
        // 人机对战面板挂在右侧。逻辑全在 ReplayForm.HumanPlay.cs 里，
        // 这里只加一行，把改动面压到最小 —— 界面没法自动化验证，改布局的风险最高。
        BuildHumanPlayPanel();
        InitializePlayAnimations();
        InitializeHeldCards();
        LoadDeckChoices();
        _firstAgent.Items.AddRange(AgentNames);
        _secondAgent.Items.AddRange(AgentNames);
        _firstAgent.SelectedItem = "前瞻牌手 3.0（开发中）";
        _secondAgent.SelectedItem = HumanV1AgentName;
        foreach (var rollouts in new[] { _firstRollouts, _secondRollouts })
        {
            rollouts.Items.AddRange(["10", "20", "60", "120", "240"]);
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
        _seekBar.ValueChanged += (_, _) => {
            if (_updatingSeek || _initialState is null || _liveMatchRunning) return;
            CancelPlayAnimation();
            _currentStepIndex = _seekBar.Value - 1;
            UpdatePlayback();
        };
        _autoPlayTimer.Tick += (_, _) => MoveNext();
        _progressLabel.TextChanged += (_, _) => _cardToolTip.SetToolTip(_progressLabel, _progressLabel.Text);
        _cardToolTip.SetToolTip(_previousButton, "上一步（←）");
        _cardToolTip.SetToolTip(_nextButton, "下一步（→ 或空格）");
        KeyDown += ReplayFormKeyDown;
        FormClosed += (_, _) => _autoPlayTimer.Stop();

        ShowEmptyState();
    }

    /// <summary>
    /// 在 Load 时套《影之诗：超凡世界》主题。
    /// <para>
    /// 放在 OnLoad 而不是构造函数里：此时 <c>BuildLayout</c> / <c>BuildHumanPlayPanel</c>
    /// 已经把控件树建完，递归套用才覆盖得全。背景必须做成窗体自己的
    /// <c>BackgroundImage</c>（见 <see cref="SvTheme.AttachBackdrop"/> 里记的那个坑）。
    /// </para>
    /// </summary>
    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        SvTheme.AttachBackdrop(this, 0.12f, "bg_arena_v2.png");
        SvTheme.ApplyTo(this);
    }

    private void BuildLayout()
    {
        var root = new SvTableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 8,
            Padding = new Padding(14, 10, 10, 10), BackColor = BoardBackground };
        _battleLayout = root;
        // Physical row heights are recalculated after DPI changes as well as resizing.
        var heights = new[] { 46, 0, 194, 0, 34, 0, 56, 194 };
        for (var i = 0; i < heights.Length; i++)
            root.RowStyles.Add(i is 3 or 5 ? new RowStyle(SizeType.Percent, 50) : new RowStyle(SizeType.Absolute, heights[i]));
        root.SizeChanged += (_, _) => ResizeBattleRows();
        DpiChanged += (_, _) => ResizeBattleRows();
        var header = new SvTableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new Padding(0) };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140));
        header.Controls.Add(new SvHeading("对局回放", "底部展开手牌 · 悬停查看 · 拖拽出牌"), 0, 0);
        var toggleSettings = new SvButton { Text = "对局设置 ▾", Dock = DockStyle.Fill, Margin = new Padding(6, 10, 0, 8) };
        var settings = CreateControls();
        _settingsPanel = settings;
        _settingsToggle = toggleSettings;
        settings.Dock = DockStyle.None;
        settings.Visible = false;
        toggleSettings.Click += (_, _) => SetSettingsVisible(!_settingsVisible);
        header.Controls.Add(toggleSettings, 1, 0);
        root.Controls.Add(header, 0, 0);
        root.Controls.Add(CreatePlayerArea(1), 0, 2);
        root.Controls.Add(CreateBoardArea(_opponentBoard, "P2  对手战场"), 0, 3);
        root.Controls.Add(CreateActionArea(), 0, 4);
        root.Controls.Add(CreateBoardArea(_selfBoard, "P1  我方战场"), 0, 5);
        root.Controls.Add(CreateTimelineArea(), 0, 6);
        root.Controls.Add(CreatePlayerArea(0), 0, 7);
        Controls.Add(root);
        Controls.Add(settings);
        PositionSettings();
    }

    private void SetSettingsVisible(bool visible)
    {
        _settingsVisible = visible;
        if (_settingsToggle is not null) _settingsToggle.Text = visible ? "收起设置 ▴" : "对局设置 ▾";
        if (_settingsPanel is not null)
        {
            PositionSettings();
            _settingsPanel.Visible = visible;
            if (visible) _settingsPanel.BringToFront();
        }
    }

    private void PositionSettings()
    {
        if (_settingsPanel is null || _battleLayout is null) return;
        var root = _battleLayout;
        // Temporarily cover the upper board while editing; both hand rows retain their space.
        var boardTop = root.GetControlFromPosition(0, 3)?.Top ?? (int)(240 * DeviceDpi / 96f);
        _settingsPanel.Bounds = new Rectangle(root.Left + root.Padding.Left, root.Top + boardTop,
            Math.Max(1, root.ClientSize.Width - root.Padding.Horizontal), (int)(96 * DeviceDpi / 96f));
    }

    private void ResizeBattleRows()
    {
        if (_battleLayout is null) return;
        var metrics = (_battleLayout.ClientSize, DeviceDpi, _liveMatchRunning);
        if (_lastBattleLayout == metrics) return;
        _lastBattleLayout = metrics;
        var scale = DeviceDpi / 96f;
        var logicalHeight = _battleLayout.ClientSize.Height / scale;
        var compact = logicalHeight < 650;
        var handHeight = Math.Clamp(logicalHeight * .225f, 156, 176);
        var heights = new[] { compact ? 38f : 42, 0, handHeight, 0, compact ? 32 : 34, 0, _liveMatchRunning ? 0 : 56, handHeight };
        if (_battleLayout.GetControlFromPosition(0, 6) is { } timeline) timeline.Visible = !_liveMatchRunning;
        foreach (var button in new[] { _previousButton, _nextButton, _autoPlayButton })
            button.Height = (int)(28 * scale);
        _actionLabel.MinimumSize = Size.Empty;
        _actionLabel.Padding = new Padding((int)(6 * scale), 0, (int)(6 * scale), 0);
        _progressLabel.Height = (int)(28 * scale);
        _progressLabel.Padding = compact ? new Padding((int)(6 * scale), 0, (int)(6 * scale), 0) :
            new Padding((int)(10 * scale), (int)(6 * scale), (int)(10 * scale), (int)(6 * scale));
        _battleLayout.SuspendLayout();
        for (var i = 0; i < heights.Length; i++)
            if (i is not 3 and not 5) _battleLayout.RowStyles[i].Height = heights[i] * scale;
        _battleLayout.ResumeLayout();
        PositionSettings();
    }

    private Control CreateControls()
    {
        var shell = new SvPanel { Dock = DockStyle.Fill, Padding = new Padding(8), CornerRadius = 6 };
        var controls = new SvTableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2, RowCount = 2,
            Margin = new Padding(0), BackColor = Color.Transparent };
        controls.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        controls.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        controls.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        controls.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        FlowLayoutPanel Team(string title, ComboBox deck, ComboBox agent, ComboBox rollouts)
        {
            var line = CreateToolbarLine();
            line.WrapContents = false;
            deck.Width = 164;
            agent.Width = 118;
            rollouts.Width = 64;
            _cardToolTip.SetToolTip(rollouts, "该牌手的推演次数");
            line.Controls.AddRange([new Label { Text = title, AutoSize = true, Padding = new Padding(0, 7, 4, 0), ForeColor = SvTheme.GoldLit },
                deck, agent, rollouts]);
            return line;
        }
        controls.Controls.Add(Team("P1", _firstDeck, _firstAgent, _firstRollouts), 0, 0);
        controls.Controls.Add(Team("P2", _secondDeck, _secondAgent, _secondRollouts), 1, 0);
        var actions = CreateToolbarLine();
        actions.WrapContents = true;
        actions.Controls.AddRange([new Label { Text = "局数", AutoSize = true, Padding = new Padding(0, 7, 4, 0) },
            _matchCount, _newMatchButton, _exportButton, _batchExportButton, _resultSummary]);
        controls.Controls.Add(actions, 0, 1);
        controls.SetColumnSpan(actions, 2);
        shell.Controls.Add(controls);
        return shell;
    }

    private static FlowLayoutPanel CreateToolbarLine() => new()
    {
        Dock = DockStyle.Fill,
        AutoSize = true,
        WrapContents = false,
        BackColor = Color.Transparent,
        Margin = new Padding(0),
        Padding = new Padding(0, 0, 0, 4)
    };

    private static void StyleToolbarButton(Button button, bool isPrimary = false)
    {
        button.AutoSize = false;
        button.FlatStyle = FlatStyle.Flat;
        button.UseVisualStyleBackColor = false;
        button.BackColor = SvTheme.PanelLit;
        if (button is SvButton themed) themed.Primary = isPrimary;
        button.ForeColor = Color.White;
        button.Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Bold);
        var minimumWidth = isPrimary ? 112 : 96;
        button.Size = new Size(Math.Max(minimumWidth, TextRenderer.MeasureText(button.Text, button.Font).Width + 18), 34);
        button.FlatAppearance.BorderColor = SvTheme.Gold;
        button.FlatAppearance.BorderSize = 1;
        button.FlatAppearance.MouseOverBackColor = SvTheme.CyanDim;
        button.FlatAppearance.MouseDownBackColor = SvTheme.Panel;
    }

    private Control CreatePlayerArea(int player)
    {
        var table = new SvTableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = BoardBackground,
            Margin = new Padding(0, 1, 0, 1)
        };
        table.ColumnStyles.Add(new ColumnStyle(player == 1 ? SizeType.Percent : SizeType.Absolute, player == 1 ? 100 : 218));
        table.ColumnStyles.Add(new ColumnStyle(player == 1 ? SizeType.Absolute : SizeType.Percent, player == 1 ? 218 : 100));

        var hand = CreateHandArea(player == 1 ? _opponentHandTitle : _selfHandTitle, player == 1 ? _opponentHand : _selfHand);
        _cardToolTip.SetToolTip(player == 0 ? _selfHandTitle : _opponentHandTitle,
            player == 0 ? "手牌在底部展开；悬停查看完整卡牌，拖向战场或目标出牌。" : "对手手牌在顶部展开；悬停查看完整卡牌。");
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
        var panel = new SvTableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = SurfaceBackground,
            Margin = new Padding(0, 0, 8, 0),
            Padding = new Padding(3, 0, 3, 0)
        };
        title.Margin = new Padding(0);
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        panel.Controls.Add(title, 0, 0);
        panel.Controls.Add(hand, 0, 1);
        return panel;
    }

    private Control CreateLeaderArea(Label leaderLabel)
    {
        var panel = new SvLeaderFrame { Dock = DockStyle.Fill, Padding = new Padding(5, 5, 5, 15), CornerRadius = 6 };
        leaderLabel.Padding = new Padding(60, 0, 0, 0);
        leaderLabel.BackColor = Color.Transparent;
        panel.Controls.Add(leaderLabel);
        var crests = leaderLabel == _selfLeaderLabel ? _selfCrests : _opponentCrests;
        crests.Dock = DockStyle.Bottom;
        crests.Height = 58;
        crests.Inspect += detail => InspectPublicStatus(detail);
        panel.Controls.Add(crests);
        return panel;
    }

    private static Control CreateBoardArea(TableLayoutPanel board, string title)
    {
        var frame = new SvPanel { Dock = DockStyle.Fill, Padding = new Padding(4), Margin = new Padding(2), CornerRadius = 12, GlassOpacity = 48 };
        var layout = new SvTableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 1,
            BackColor = Color.Transparent, Margin = new Padding(0) };
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        frame.AccessibleName = title;
        board.Margin = new Padding(0);
        layout.Controls.Add(board, 0, 0);
        frame.Controls.Add(layout);
        return frame;
    }

    private Control CreateActionArea()
    {
        var panel = new SvTableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(0),
            Padding = new Padding(0),
            BackColor = Color.FromArgb(23, 38, 56)
        };
        // The upper strip holds the action direction; the lower row holds the readable action.
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 10));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        panel.Controls.Add(_arrowCanvas, 0, 0);
        panel.Controls.Add(_actionLabel, 0, 1);
        return panel;
    }

    private Control CreateTimelineArea()
    {
        var frame = new SvPanel { Dock = DockStyle.Fill, Padding = new Padding(4), Margin = new Padding(4, 0, 4, 0), CornerRadius = 6 };
        var layout = new SvTableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, BackColor = Color.Transparent };
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 70));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 30));
        var playback = CreateToolbarLine();
        _matchSelector.Width = 142;
        _progressLabel.Width = 150;
        _progressLabel.Font = new Font("Microsoft YaHei UI", 9f);
        playback.Controls.AddRange([_matchSelector, _previousButton, _nextButton, _autoPlayButton, _progressLabel]);
        layout.Controls.Add(playback, 0, 0);
        layout.Controls.Add(_seekBar, 0, 1);
        frame.Controls.Add(layout);
        return frame;
    }

    private static Label CreateActionPreviewLabel(Color background, FontStyle fontStyle = FontStyle.Regular) => new()
    {
        Dock = DockStyle.Fill,
        AutoEllipsis = true,
        BackColor = background,
        BorderStyle = BorderStyle.FixedSingle,
        ForeColor = Color.WhiteSmoke,
        Font = new Font("Microsoft YaHei UI", 9.25F, fontStyle),
        Padding = new Padding(6, 1, 6, 1),
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

    private async void GenerateMatches()
    {
        if (_firstDeck.SelectedItem is not ReplayDeckChoice firstDeck ||
            _secondDeck.SelectedItem is not ReplayDeckChoice secondDeck ||
            _firstAgent.SelectedItem is not string firstAgent ||
            _secondAgent.SelectedItem is not string secondAgent)
        {
            return;
        }

        CancelPlayAnimation();
        SetSettingsVisible(false);
        var count = Decimal.ToInt32(_matchCount.Value);
        var firstRollouts = int.Parse((string)_firstRollouts.SelectedItem!, CultureInfo.InvariantCulture);
        var secondRollouts = int.Parse((string)_secondRollouts.SelectedItem!, CultureInfo.InvariantCulture);

        // 种子先在**界面线程**上取好：CreateSeed 用的是共享随机源，不能让后台线程并发调用。
        var seeds = new ulong[count];
        for (var index = 0; index < count; index++)
        {
            seeds[index] = CreateSeed();
        }

        var started = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            UseWaitCursor = true;
            _newMatchButton.Enabled = false;
            _matches.Clear();

            _isGenerating = true;
            _progressLabel.ForeColor = Color.FromArgb(255, 214, 130);
            _progressLabel.Text = $"生成中 0/{count}…";

            // **必须在后台线程上跑。**
            // 之前是在界面线程上跑循环、每局之间 await Task.Yield() 让出一次 —— 那只保证
            // "两局之间"能刷新，**一局内部界面照样冻死**（用户实测："好像还卡住了"）。
            // 一局要几十秒，用户就看到画面不动。
            // Progress<T> 在构造时捕获界面线程的同步上下文，所以 Report 会回到界面线程执行，
            // 在后台线程里碰它也是安全的。
            IProgress<string> progress = new Progress<string>(text => _progressLabel.Text = text);

            var generated = await Task.Run(() =>
            {
                var matches = new List<ReplayMatch>(count);
                var firstWins = 0;
                var secondWins = 0;
                var draws = 0;

                for (var index = 1; index <= count; index++)
                {
                    var seed = seeds[index - 1];
                    var firstDeckDefinition = DeckCatalog.Create(firstDeck.Id, $"{firstDeck.Name} P1");
                    var secondDeckDefinition = DeckCatalog.Create(secondDeck.Id, $"{secondDeck.Name} P2");
                    var initial = GameEngine.CreateGame(firstDeckDefinition, secondDeckDefinition, seed);
                    var steps = new List<MatchStep>();
                    var result = MatchRunner.PlayToEnd(
                        initial,
                        CreateAgent(firstAgent, seed + 1, firstRollouts),
                        CreateAgent(secondAgent, seed + 2, secondRollouts),
                        onStep: steps.Add);

                    matches.Add(new ReplayMatch(
                        index,
                        seed,
                        firstDeck.ToString(),
                        secondDeck.ToString(),
                        firstAgent,
                        secondAgent,
                        initial,
                        steps,
                        result.Winner));

                    switch (result.Winner)
                    {
                        case 0: firstWins++; break;
                        case 1: secondWins++; break;
                        default: draws++; break;
                    }

                    var elapsed = started.Elapsed;
                    var perGame = elapsed.TotalSeconds / index;
                    var remaining = TimeSpan.FromSeconds(Math.Max(0, perGame * (count - index)));
                    progress.Report(BuildGenerationStatus(
                        index, count, firstAgent, firstWins, secondAgent, secondWins, draws,
                        elapsed, remaining));
                }

                return (Matches: matches, FirstWins: firstWins, SecondWins: secondWins, Draws: draws);
            });

            _matches.AddRange(generated.Matches);
            _progressLabel.Text = BuildGenerationStatus(
                count, count, firstAgent, generated.FirstWins, secondAgent, generated.SecondWins,
                generated.Draws, started.Elapsed, TimeSpan.Zero).Replace("　预计剩余 0秒", string.Empty);

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
            _isGenerating = false;
        }
    }
    /// <summary>生成进度那一行的文案。</summary>
    private static string BuildGenerationStatus(
        int done,
        int total,
        string firstAgent,
        int firstWins,
        string secondAgent,
        int secondWins,
        int draws,
        TimeSpan elapsed,
        TimeSpan remaining)
    {
        var percent = total == 0 ? 100 : done * 100.0 / total;
        var drawText = draws > 0 ? $" 平{draws}" : string.Empty;
        var remainText = done >= total ? string.Empty : $" 剩{DescribeDuration(remaining)}";
        // 这一栏只有约 280px 可用，用短名并压缩措辞，否则会被省略号截掉关键数字。
        return $"{done}/{total}（{percent:F0}%） {ShortAgentName(firstAgent)} {firstWins}胜 : " +
               $"{secondWins}胜 {ShortAgentName(secondAgent)}{drawText} 用{DescribeDuration(elapsed)}{remainText}";
    }

    /// <summary>
    /// 每秒刷一次**外部进程（命令行）**的进度。本界面自己在生成时让位给它。
    /// <para>
    /// 命令行跑批量对局时会把进度写进共享文件，这里读出来显示 —— 用户要的就是
    /// "我在命令行跑，界面上也能看见"。文件里超过 10 秒没更新会被当作已结束
    /// （命令行被关掉时没有收尾机会）。
    /// </para>
    /// </summary>
    private void RefreshLiveProgress()
    {
        if (_isGenerating)
        {
            return;
        }

        var live = LiveProgress.Read();
        if (live is null)
        {
            // 外部进度结束：把这一栏还给回放（有局就显示步数，没局就显示空闲）。
            if (_showingLiveProgress)
            {
                _showingLiveProgress = false;
                _progressLabel.ForeColor = Color.White;
                _progressLabel.Text = _steps.Count == 0
                    ? "尚未开始"
                    : $"第 {_currentStepIndex + 1} / {_steps.Count} 步";
            }

            return;
        }

        _showingLiveProgress = true;
        _progressLabel.ForeColor = Color.FromArgb(130, 220, 160);
        // 前缀 ▶ 表示"这是另一个进程在跑"，跟本界面的生成区分开。
        // 用 A/B 而不是牌手名：镜像对局两边同名，写了也分不出是谁，
        // 反而把"几比几"挤到被省略号截掉。
        _progressLabel.Text =
            $"▶ 命令行 {live.Done}/{live.Total}　A {live.FirstWins}胜 : {live.SecondWins}胜 B";
    }

    /// <summary>
    /// 把「前瞻牌手 3.0（开发中）」这类长名字压成 <c>3.0</c>。
    /// 进度那一栏只有约 280px，用全名会把关键数字挤出可视区。
    /// 找不到版本号就退回原名（截断到 6 个字）。
    /// </summary>
    private static string ShortAgentName(string agent)
    {
        var match = System.Text.RegularExpressions.Regex.Match(agent, @"\d+\.\d+");
        if (match.Success)
        {
            return match.Value;
        }

        return agent.Length <= 6 ? agent : agent[..6];
    }

    /// <summary>把时长写成"1分23秒"这种一眼能读的形式。</summary>
    private static string DescribeDuration(TimeSpan span)
    {
        if (span.TotalSeconds < 1)
        {
            return "0秒";
        }

        if (span.TotalMinutes < 1)
        {
            return $"{span.TotalSeconds:F0}秒";
        }

        return $"{(int)span.TotalMinutes}分{span.Seconds}秒";
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
        CancelPlayAnimation();
        _currentReplay = match;
        _initialState = match.InitialState;
        _steps = match.Steps;
        _currentStepIndex = -1;
        SetExportAvailability(match.Steps.Count > 0);
        UpdatePlayback();
    }

    /// <summary>
    /// 只有前瞻类牌手会向前模拟；规则牌手和随机牌手都是一步决策，推演次数对它们完全无效
    /// （传进去会被直接丢弃）。把无意义的那个下拉框置灰，免得设了一个看起来生效、其实被忽略的数字。
    /// </summary>
    private void UpdateRolloutAvailability()
    {
        Update(_firstAgent, _firstRollouts);
        Update(_secondAgent, _secondRollouts);
        static void Update(ComboBox agents, ComboBox rollouts)
        {
            var name = agents.SelectedItem as string;
            rollouts.Enabled = AgentSearches(name);
            var fixedBudget = name switch
            {
                HumanV1AgentName => HumanV1Rollouts,
                "前瞻牌手 1.0（冻结）" or "前瞻牌手·旧冻结基线" => FrozenV1Rollouts,
                "前瞻牌手 2.0（冻结）" => FrozenV2Rollouts,
                _ => 0
            };
            if (fixedBudget > 0) rollouts.SelectedItem = fixedBudget.ToString(CultureInfo.InvariantCulture);
        }
    }

    /// <summary>
    /// 冻结版本的**定义值**。这两个数字是它们身份的组成部分，所以选到它们时**忽略推演下拉框** ——
    /// 否则"打 2.0"到底是多少次推演就说不清了（2.0 的定义是 60，1.0 是 10）。
    /// </summary>
    private const int FrozenV1Rollouts = 10;
    private const int HumanV1Rollouts = 20;
    private const string HumanV1AgentName = "前瞻牌手 1.0（20次配置）";

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
        HumanV1AgentName,
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
        HumanV1AgentName => new LookaheadPlayerAgentV1(
            HumanV1Rollouts, futureTurnHorizon: 3, seed, minimumPracticalAdvantage: 0.0),
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
        if (_playAnimation is not null || _liveMatchRunning) return;
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
        PlayVisualStep(_steps[_currentStepIndex]);
    }

    private void MovePrevious()
    {
        if (_liveMatchRunning) return;
        if (_initialState is null || _currentStepIndex < 0)
        {
            return;
        }

        StopAutoPlay();
        CancelPlayAnimation();
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
        if (_playAnimation is not null && !_applyingPlayImpact) return;
        ResizeBattleRows();
        if (_initialState is null)
        {
            ShowEmptyState();
            return;
        }

        var state = _currentStepIndex < 0 ? _initialState : _steps[_currentStepIndex].AfterState;
        if ((_renderedPhase == GamePhase.Mulligan) != (state.Phase == GamePhase.Mulligan))
        {
            // Mulligan and play gestures differ. Rebind only at the phase boundary.
            foreach (Control tile in _selfHand.Controls.Cast<Control>().ToArray()) RemoveCardControl(_selfHand, tile);
        }
        _renderedPhase = state.Phase;
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

        _updatingSeek = true;
        _seekBar.Maximum = Math.Max(1, _steps.Count);
        _seekBar.Value = Math.Clamp(_currentStepIndex + 1, 0, _seekBar.Maximum);
        _seekBar.Enabled = !_liveMatchRunning && _steps.Count > 0;
        _updatingSeek = false;
        _previousButton.Enabled = _currentStepIndex >= 0;
        _nextButton.Enabled = _currentStepIndex + 1 < _steps.Count;
        _autoPlayButton.Enabled = _steps.Count > 0;
        UpdateActionPreviews();
        // Reused tiles keep their handlers; new tiles are attached once by the interaction tracker.
        RefreshHumanInteraction();
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
        _cardToolTip.SetToolTip(_previousButton, "上一步（←）\n" + previous);
        _cardToolTip.SetToolTip(_nextButton, "下一步（→ 或空格）\n" + next);
    }

    private void ShowEmptyState()
    {
        _opponentLeaderLabel.Text = "Player 2\n等待对局";
        _selfLeaderLabel.Text = "Player 1\n等待对局";
        _opponentHandTitle.Text = "Player 2 手牌";
        _selfHandTitle.Text = "Player 1 手牌";
        _actionLabel.Text = "选择双方卡组、牌手和局数后，点击“生成对局”。";
        _selfCrests.SetCrests([]);
        _opponentCrests.SetCrests([]);
        RenderHand(_opponentHand, []);
        RenderHand(_selfHand, []);
        RenderBoard(_opponentBoard, Array.Empty<FollowerInstance>(), Array.Empty<AmuletInstance>());
        RenderBoard(_selfBoard, Array.Empty<FollowerInstance>(), Array.Empty<AmuletInstance>());
        _arrowCanvas.Scene = null;
        _progressLabel.Text = "尚未开始";
        SetExportAvailability(false);
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
        if (CurrentExportMatch() is not { } match) return;

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
            UseStartAbilityAction action => [step.BeforeState.TurnNumber, step.ActingPlayer, "Z", action.AmuletInstanceId,
                action.HandCardTargetInstanceId,
                before.Hand.FirstOrDefault(c => c.InstanceId == action.HandCardTargetInstanceId)?.Definition.Id,
                step.AfterState.Players[step.ActingPlayer].Hand.FirstOrDefault(c => c.InstanceId == action.HandCardTargetInstanceId)?.Definition.Id],
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
        // Keep resources in three short lines so the deck and graveyard counts remain visible.
        if (leaderLabel.Parent is SvLeaderFrame frame)
            frame.SetResources(player.Health, player.CurrentPlayPoints, player.MaxPlayPoints, player.EvolutionPoints, player.SuperEvolutionPoints);
        leaderLabel.Text = $"P{playerIndex + 1}  主战者 · {player.Health}/{player.MaxHealth}\n" +
                           $"PP {player.CurrentPlayPoints}/{player.MaxPlayPoints}　" +
                           $"EP {player.EvolutionPoints}　SEP {player.SuperEvolutionPoints}\n" +
                           $"牌库 {player.Deck.Count}　墓地 {player.Graveyard.Count}";
        handTitle.Text = $"P{playerIndex + 1}  手牌  /  {player.Hand.Count} 张";
        RenderHand(hand, player.Hand, player);
        (playerIndex == 0 ? _selfCrests : _opponentCrests).SetCrests(player.Crests
            .Select(crest => new VisibleCrest(crest.Definition.Id, crest.Definition.Name, crest.Definition.EffectText, crest.Countdown)).ToArray());
    }

    private void RemoveCardControl(Control parent, Control control)
    {
        _interactionAttached.Remove(control);
        parent.Controls.Remove(control);
        control.Dispose();
    }

    private void RenderHand(FlowLayoutPanel panel, IReadOnlyList<CardInstance> cards, PlayerState? player = null)
    {
        var tiles = panel.Controls.OfType<SvCardFace>().Where(tile => tile.Tag is CardInstance)
            .ToDictionary(tile => ((CardInstance)tile.Tag!).InstanceId);
        var wanted = cards.Select(card => card.InstanceId).ToHashSet();
        panel.SuspendLayout();
        try
        {
            foreach (var tile in tiles.Values.Where(tile => !wanted.Contains(((CardInstance)tile.Tag!).InstanceId)))
                RemoveCardControl(panel, tile);
            foreach (var (card, index) in cards.Select((card, index) => (card, index)))
            {
                if (!tiles.TryGetValue(card.InstanceId, out var tile))
                {
                    tile = CreateCardTile(card.Definition, card.Definition.Attack, card.Definition.Defense, compact: panel is not SvHandFan { Portrait: true });
                    if (panel is SvHandFan { Portrait: true })
                    {
                        tile.Dock = DockStyle.None;
                        tile.Margin = new Padding(0);
                        tile.HandPortrait = true;
                        tile.NameLabel.Font = new Font("Microsoft YaHei UI", 9.5f, FontStyle.Bold);
                        UpdateCardTile(tile, card.Definition, card.Definition.Attack, card.Definition.Defense);
                    }
                    panel.Controls.Add(tile);
                }
                else UpdateCardTile(tile, card.Definition, card.Definition.Attack, card.Definition.Defense);
                if (player is not null)
                {
                    var readout = GameEngine.GetHandCardReadout(player, card);
                    tile.SetHandReadout(readout);
                    var detail = readout.Description + "\n\n" + tile.AccessibleDescription;
                    tile.AccessibleDescription = detail;
                    _cardToolTip.SetToolTip(tile, detail);
                    _cardToolTip.SetToolTip(tile.NameLabel, detail);
                }
                tile.Tag = card; // Every engine snapshot contains new model instances.
                if (panel is not SvHandFan && panel.Controls.GetChildIndex(tile) != index) panel.Controls.SetChildIndex(tile, index);
            }
            var empty = panel.Controls.OfType<Label>().FirstOrDefault();
            if (cards.Count == 0 && empty is null)
                panel.Controls.Add(new Label { Text = "（无手牌）", AutoSize = true, ForeColor = Color.LightGray, Padding = new Padding(6, 10, 0, 0) });
            else if (cards.Count > 0 && empty is not null) RemoveCardControl(panel, empty);
            if (panel is SvHandFan fan)
                fan.SetCards(cards.Select(card => panel.Controls.OfType<SvCardFace>().Single(tile => ((CardInstance)tile.Tag!).InstanceId == card.InstanceId)));
        }
        finally { panel.ResumeLayout(); }
    }

    private void RenderBoard(TableLayoutPanel board, IReadOnlyList<FollowerInstance> followers, IReadOnlyList<AmuletInstance> amulets)
    {
        static (char Kind, int Id) Key(Panel slot, TableLayoutPanel parent) => slot.Tag switch
        {
            FollowerInstance follower => ('F', follower.InstanceId),
            AmuletInstance amulet => ('A', amulet.InstanceId),
            _ => ('E', parent.GetColumn(slot))
        };
        var existing = board.Controls.OfType<Panel>().ToDictionary(slot => Key(slot, board));
        var desired = new HashSet<(char Kind, int Id)>();
        for (var i = 0; i < PlayerState.BoardLimit; i++)
            desired.Add(i < followers.Count ? ('F', followers[i].InstanceId) :
                i - followers.Count < amulets.Count ? ('A', amulets[i - followers.Count].InstanceId) : ('E', i));
        board.SuspendLayout();
        try
        {
            foreach (var entry in existing.Where(entry => !desired.Contains(entry.Key))) RemoveCardControl(board, entry.Value);
            for (var i = 0; i < PlayerState.BoardLimit; i++)
            {
                FollowerInstance? follower = i < followers.Count ? followers[i] : null;
                var amuletIndex = i - followers.Count;
                AmuletInstance? amulet = follower is null && amuletIndex >= 0 && amuletIndex < amulets.Count ? amulets[amuletIndex] : null;
                var key = follower is not null ? ('F', follower.InstanceId) : amulet is not null ? ('A', amulet.InstanceId) : ('E', i);
                if (!existing.TryGetValue(key, out var slot))
                {
                    slot = new SvPanel { Dock = DockStyle.Fill, Margin = new Padding(2),
                        CornerRadius = 6, GlassOpacity = follower is null && amulet is null ? 18 : 0, ShowEdge = false };
                    slot.SizeChanged += (_, _) => ArrangeBoardCard(slot);
                    board.Controls.Add(slot, i, 0);
                }
                else if (board.GetColumn(slot) != i) board.SetColumn(slot, i);
                slot.Tag = (object?)follower ?? amulet;
                if (!_liveMatchRunning)
                    foreach (var badge in slot.Controls.OfType<Label>()) badge.Visible = false;
                var definition = follower?.Definition ?? amulet?.Definition;
                if (definition is null) continue;
                var tile = slot.Controls.OfType<SvCardFace>().FirstOrDefault();
                if (tile is null)
                {
                    tile = CreateCardTile(definition, follower?.Attack ?? 0, follower?.CurrentDefense ?? 0, compact: false,
                        isEvolved: follower?.IsEvolved ?? false, currentCountdown: amulet?.Countdown);
                    slot.Controls.Add(tile);
                }
                else UpdateCardTile(tile, definition, follower?.Attack ?? 0, follower?.CurrentDefense ?? 0,
                    follower?.IsEvolved ?? false, amulet?.Countdown);
                tile.Tag = slot.Tag;
                RefreshFollowerStatus(slot, tile, follower);
                ArrangeBoardCard(slot);
            }
        }
        finally { board.ResumeLayout(); }
    }

    private SvCardFace CreateCardTile(CardDefinition definition, int attack, int defense, bool compact,
        bool isEvolved = false, int? currentCountdown = null)
    {
        var body = definition.Type == CardType.Follower ? $"攻击 {attack}    体力 {defense}"
            : currentCountdown is int countdown ? $"吟唱 {countdown}" : CardTypeName(definition.Type);
        var panel = new SvCardFace(definition.Name, definition.Cost, body, TypeColor(definition.Type), compact, isEvolved)
        {
            Dock = compact ? DockStyle.None : DockStyle.Fill,
            Size = compact ? new Size((int)(150 * DeviceDpi / 96f), (int)(42 * DeviceDpi / 96f)) : Size.Empty,
            Margin = compact ? new Padding(3) : new Padding(0), Cursor = Cursors.Help
        };
        UpdateCardTile(panel, definition, attack, defense, isEvolved, currentCountdown);
        ConfigureCardDetailHover(panel, panel.NameLabel);
        return panel;
    }

    private void UpdateCardTile(SvCardFace tile, CardDefinition definition, int attack, int defense,
        bool evolved = false, int? countdown = null)
    {
        var body = definition.Type == CardType.Follower ? tile.HandPortrait ? $"{attack} / {defense}" : $"攻击 {attack}    体力 {defense}"
            : countdown is int value ? $"吟唱 {value}" : CardTypeName(definition.Type);
        tile.UpdateCard(definition.Name, definition.Cost, body, TypeColor(definition.Type), evolved);
        tile.UpdateStats(definition.Type == CardType.Follower, attack, defense);
        if (!_liveMatchRunning)
        {
            tile.BackColor = TypeColor(definition.Type);
            if (tile.NameLabel.Tag is string)
            {
                tile.NameLabel.Tag = null;
                tile.NameLabel.Text = definition.Name + (evolved ? " ★" : "");
            }
        }
        var detail = CardDetail(definition, attack, defense, evolved, countdown);
        if (tile.AccessibleDescription == detail) return;
        tile.AccessibleName = definition.Name;
        tile.AccessibleDescription = detail;
        _cardToolTip.SetToolTip(tile, detail);
        _cardToolTip.SetToolTip(tile.NameLabel, detail);
    }

    private void ConfigureCardDetailHover(Panel tile, Label cardLabel)
    {
        void ShowDetail(object? _, EventArgs __)
        {
            if (!Visible || !IsHandleCreated) return;
            var screen = Screen.FromControl(this).WorkingArea;
            var below = tile.PointToScreen(new Point(tile.Width / 2, tile.Height + 8));
            var showAbove = below.Y + 220 > screen.Bottom;
            _cardToolTip.Show(
                tile.AccessibleDescription ?? "",
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

        void Inspect(object? sender, MouseEventArgs args)
        {
            if (args.Button != MouseButtons.Left || _targetSelection is not null || _fusionCardInstanceId is not null) return;
            var enemy = false;
            for (Control? parent = tile.Parent; parent is not null; parent = parent.Parent)
                if (parent == _opponentHand || parent == _opponentBoard) { enemy = true; break; }
            if (!_liveMatchRunning || enemy) InspectCard(tile);
        }
        tile.MouseUp += Inspect;
        cardLabel.MouseUp += Inspect;
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
            lines.Add("卡面关键词：" + string.Join("、", keywords));
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
            UseStartAbilityAction start => DescribeStartAbilityStep(step, start),
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

    private static string DescribeStartAbilityStep(MatchStep step, UseStartAbilityAction action)
    {
        var before = step.BeforeState.Players[step.ActingPlayer];
        var after = step.AfterState.Players[step.ActingPlayer];
        var source = before.Amulets.FirstOrDefault(a => a.InstanceId == action.AmuletInstanceId)?.Definition.Name ?? "护符";
        var description = $"【启动】Player {step.ActingPlayer + 1}：{source}";
        if (action.HandCardTargetInstanceId is int targetId)
        {
            var oldCard = before.Hand.FirstOrDefault(c => c.InstanceId == targetId)?.Definition.Name ?? $"#{targetId}";
            var newCard = after.Hand.FirstOrDefault(c => c.InstanceId == targetId)?.Definition.Name ?? $"#{targetId}";
            description += $"；手牌 {oldCard} → {newCard}";
        }
        return description + "。";
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
        Font = new Font("Microsoft YaHei UI", 8.5f, FontStyle.Bold),
        TextAlign = ContentAlignment.MiddleLeft
    };

    private static Label CreateSectionLabel() => new()
    {
        AutoSize = true,
        ForeColor = SvTheme.GoldLit,
        Padding = new Padding(2, 0, 0, 2)
    };

    private static TableLayoutPanel CreateBoardTable()
    {
        var board = new SvTableLayoutPanel
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
        public override string ToString() => $"第 {Index} 局｜{(Winner < 0 ? "进行中" : $"P{Winner + 1} 胜")}｜{Steps.Count} 步";
    }
}

/// <summary>Draws a clean directional hint between the two follower rows for attacks and targeted spells.</summary>
internal sealed class ActionArrowCanvas : Control
{
    private ReplayArrow? _scene;

    public ActionArrowCanvas()
    {
        SetStyle(ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        DoubleBuffered = true;
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public ReplayArrow? Scene
    {
        get => _scene;
        set
        {
            if (_scene == value) return;
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
internal sealed class ReadableToolbarButton : SvButton { }
