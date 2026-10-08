using Shadowverse.Engine.Game;
using Shadowverse.Engine.Models;

namespace Shadowverse.DeckEditor;

public partial class ReplayForm
{
    private HumanTargetSelection? _targetSelection;
    private readonly HashSet<Control> _targetClickAttached = [];
    private bool _targetLayoutActive;
    private int _normalHintHeight, _normalStatusHeight;
    private bool _normalLogVisible, _normalThinkingVisible, _normalLegendVisible;
    private readonly Panel _selectionFooter = new SvPanel { Dock = DockStyle.Bottom, Height = 124, Visible = false, Padding = new Padding(4) };
    private readonly Button _selectionConfirm = new SvButton { Name = "TargetAdvance", Dock = DockStyle.Fill, Text = "确认发动", Primary = true };
    private readonly Button _selectionBack = new SvButton { Name = "TargetBack", Dock = DockStyle.Fill, Text = "返回上一步" };
    private readonly Button _selectionCancel = new SvButton { Name = "TargetCancel", Dock = DockStyle.Fill, Text = "取消（Esc）" };
    private readonly Button _selectionAll = new SvButton { Dock = DockStyle.Fill, Text = "显示全部动作" };
    private HumanTargetSelection? _targetUiSelection;
    private int _targetUiStage;
    private Label? _targetSummary;

    private void InitializeSelectionFooter()
    {
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 3, Margin = new Padding(0), BackColor = Color.Transparent };
        layout.ColumnStyles.Add(new(SizeType.Percent, 50)); layout.ColumnStyles.Add(new(SizeType.Percent, 50));
        layout.RowStyles.Add(new(SizeType.Percent, 38)); layout.RowStyles.Add(new(SizeType.Percent, 32)); layout.RowStyles.Add(new(SizeType.Percent, 30));
        layout.Controls.Add(_selectionConfirm, 0, 0); layout.SetColumnSpan(_selectionConfirm, 2);
        layout.Controls.Add(_selectionBack, 0, 1); layout.Controls.Add(_selectionCancel, 1, 1);
        layout.Controls.Add(_selectionAll, 0, 2); layout.SetColumnSpan(_selectionAll, 2);
        _selectionFooter.Controls.Add(layout);
        _selectionConfirm.Click += (_, _) => AdvanceTargetSelection();
        _selectionCancel.Click += (_, _) => CancelTargetSelection();
        _selectionBack.Click += (_, _) => { if (_targetSelection?.Back() == true) RefreshTargetSelection(); };
        _selectionAll.Click += (_, _) => { _humanShowAllActions = !_humanShowAllActions; RefreshActionButtons(); };
    }

    private void SelectHandToPlay(int instanceId)
    {
        if (_pendingActions is not { } actions) return;
        var candidates = HumanActionResolver.ClickHand(actions, instanceId);
        if (candidates.Count == 0)
        {
            var tile = _selfHand.Controls.OfType<Panel>().FirstOrDefault(t => t.Tag is CardInstance c && c.InstanceId == instanceId);
            if (tile is not null) InspectCard(tile, "这张牌现在无法使用：请检查 PP、战场空位和目标条件。");
            return;
        }
        if (candidates.Count == 1) BeginTargetSelection(candidates);
        else BeginAmbiguityResolution(candidates);
    }

    private void BeginTargetSelection(IReadOnlyList<GameAction> candidates)
    {
        if (_pendingObservation is not { } observation) return;
        CloseTargetSelection();
        CloseFusionUi();
        _modeSelection = null;
        _targetSelection = new HumanTargetSelection(observation, candidates);
        SetTargetLayout(true);
        RefreshTargetSelection();
    }

    private void SetTargetLayout(bool active)
    {
        if (_targetLayoutActive == active) return;
        _targetLayoutActive = active;
        _humanPanel.SuspendLayout();
        if (active)
        {
            _normalHintHeight = _humanHint.Height;
            _normalStatusHeight = _humanStatus.Height;
            _normalLogVisible = _logPanel.Visible;
            _normalThinkingVisible = _thinkingPanel.Visible;
            _normalLegendVisible = _agentLegend.Visible;
            _logPanel.Visible = _thinkingPanel.Visible = _agentLegend.Visible = false;
            _humanStatus.Height = (int)(52 * DeviceDpi / 96f);
            _humanHint.Height = (int)(58 * DeviceDpi / 96f);
        }
        else
        {
            _logPanel.Visible = _normalLogVisible;
            _thinkingPanel.Visible = _normalThinkingVisible;
            _agentLegend.Visible = _normalLegendVisible;
            _humanStatus.Height = _normalStatusHeight;
            _humanHint.Height = _normalHintHeight;
        }
        _humanPanel.ResumeLayout(true);
    }

    private void CloseTargetSelection()
    {
        _targetSelection = null;
        _inspectedCardDetail = null;
        _inspectedCardReason = null;
        _selectionAll.Enabled = true;
        _selectionCancel.Text = "取消（Esc）";
        _targetUiSelection = null;
        _targetSummary = null;
        _selectionFooter.Visible = false;
        SetTargetLayout(false);
        ApplyTargetHighlights();
    }

    private void CancelTargetSelection()
    {
        CloseTargetSelection();
        _modeSelection = null;
        _humanHint.Text = "已取消选择。没有出牌，也没有消耗 PP、进化点。";
        RestorePlaybackHints();
        RefreshActionButtons();
    }

    private void RefreshTargetSelection()
    {
        if (_targetSelection is not { } selection || _pendingObservation is not { } observation) return;
        _humanHint.Text = HumanTargetText.Instruction(observation, selection);
        _actionLabel.Text = selection.StageCount == 0 ? "查看卡牌能力 · 确认使用或 Esc 返回" :
            selection.IsComplete ? "目标已选好 · 请确认发动" : $"选择目标 {selection.StageIndex + 1}/{selection.StageCount} · 金色表示已选 · Esc 取消";
        ApplyTargetHighlights();
        if (ReferenceEquals(_targetUiSelection, selection) && _targetUiStage == selection.StageIndex && _targetSummary is { IsDisposed: false })
            UpdateTargetChoices();
        else RefreshActionButtons();
    }

    private void BuildTargetChoices()
    {
        if (_targetSelection is not { } selection || _pendingObservation is not { } observation) return;
        var width = Math.Max(180, _humanActions.ClientSize.Width - 24);
        var scale = DeviceDpi / 96f;
        Label AddText(string text, Color color, bool bold = false)
        {
            var label = new Label
            {
                Text = text, ForeColor = color, BackColor = Color.Transparent,
                Width = width, Font = new Font("Microsoft YaHei UI", bold ? 10 : 9, bold ? FontStyle.Bold : FontStyle.Regular),
                AutoEllipsis = false, Margin = new Padding(3, 3, 3, 8)
            };
            label.Height = TextRenderer.MeasureText(text, label.Font, new Size(width, int.MaxValue), TextFormatFlags.WordBreak).Height + (int)(8 * scale);
            _humanActions.Controls.Add(label);
            return label;
        }
        var action = selection.Candidates[0];
        AddText(HumanTargetText.Title(observation, action), SvTheme.GoldLit, true);
        var source = HumanTargetText.Source(observation, action);
        AddText(source is null ? "卡牌能力" : $"{source.Cost} 费 · {CardTypeName(source.Type)}" +
            (source.Type == CardType.Follower ? $" · {source.Attack}/{source.Defense}" : ""), SvTheme.TextDim);
        _humanActions.Controls.Add(new RichTextBox
        {
            Text = HumanTargetText.Ability(observation, action), ReadOnly = true,
            Width = width, Height = (int)(120 * scale), BorderStyle = BorderStyle.None,
            BackColor = Color.FromArgb(17, 28, 40), ForeColor = SvTheme.Text,
            Font = new Font("Microsoft YaHei UI", 9), WordWrap = true, ScrollBars = RichTextBoxScrollBars.Vertical,
            DetectUrls = false, TabStop = false
        });
        var targets = selection.Completed.Concat(selection.Selected).ToArray();
        _targetSummary = AddText("", SvTheme.GoldLit);
        _targetUiSelection = selection;
        _targetUiStage = selection.StageIndex;
        _selectionFooter.Visible = true;
        UpdateTargetChoices();
        if (selection.IsComplete && selection.Candidates.Count > 1)
        {
            // Structural duplicates may still be returned by an engine extension; keep every action reachable.
            foreach (var candidate in selection.Candidates)
            {
                var confirm = CreateHumanButton(selection.Candidates.Count == 1 ? "确认发动" : DescribeAction(observation, candidate), true);
                confirm.Name = "TargetConfirm";
                confirm.Height = Math.Max((int)(40 * scale), TextRenderer.MeasureText(confirm.Text, confirm.Font,
                    new Size(width - 16, int.MaxValue), TextFormatFlags.WordBreak).Height + 14);
                confirm.Click += (_, _) => CommitHumanAction(candidate);
                _humanActions.Controls.Add(confirm);
            }
        }
        if (_humanShowAllActions && _pendingActions is { } actions)
            foreach (var candidate in actions)
            {
                var button = CreateHumanButton(DescribeAction(observation, candidate), false);
                button.Height = Math.Max((int)(44 * scale), TextRenderer.MeasureText(button.Text, button.Font,
                    new Size(width - 16, int.MaxValue), TextFormatFlags.WordBreak).Height + 14);
                button.Click += (_, _) => BeginAmbiguityResolution([candidate]);
                _humanActions.Controls.Add(button);
            }
    }

    private void UpdateTargetChoices()
    {
        if (_targetSelection is not { } selection || _pendingObservation is not { } observation) return;
        var targets = selection.Completed.Concat(selection.Selected).ToArray();
        var text = targets.Length == 0 ? selection.StageCount == 0 ? "确认前可取消，不会消耗 PP。" : "尚未选择目标" :
            "已选目标\n" + string.Join("\n", targets.Select(t => "✓ " + HumanTargetText.Name(observation, t)));
        if (_targetSummary is { IsDisposed: false } summary)
        {
            summary.Text = text;
            summary.ForeColor = targets.Length == 0 ? SvTheme.TextDim : SvTheme.GoldLit;
            summary.Height = TextRenderer.MeasureText(text, summary.Font, new Size(summary.Width, int.MaxValue), TextFormatFlags.WordBreak).Height + (int)(8 * DeviceDpi / 96f);
        }
        _selectionConfirm.Text = selection.IsComplete ? "确认使用" : selection.IsLastStage ? "确认发动" : "下一步：确认当前目标";
        _selectionConfirm.Enabled = selection.IsComplete ? selection.Candidates.Count == 1 : selection.CanAdvance;
        _selectionBack.Enabled = selection.CanGoBack;
        _selectionAll.Text = _humanShowAllActions ? "收起全部动作" : $"显示全部动作（{_pendingActions?.Count ?? 0}）";
    }

    private void AdvanceTargetSelection()
    {
        if (_targetSelection is not { } selection) return;
        if (selection.IsComplete)
        {
            if (selection.Candidates.Count == 1) CommitHumanAction(selection.Candidates[0]);
            return;
        }
        if (!selection.Advance()) return;
        if (selection.IsComplete && selection.Candidates.Count == 1)
        {
            CommitHumanAction(selection.Candidates[0]);
            return;
        }
        RefreshTargetSelection();
    }

    private void PickTarget(HumanTarget target)
    {
        if (_targetSelection is not { } selection) return;
        if (target.Zone == HumanTargetZone.OwnHand && target.InstanceId == HumanActionResolver.PlayedCardOf(selection.Candidates[0])) return;
        if (selection.Toggle(target)) RefreshTargetSelection();
        else if (!selection.IsComplete) _humanHint.Text = "这个对象不能用于当前效果。请点击发光目标；已选对象可再点取消。";
    }

    private void AttachTargetClicks()
    {
        void Attach(Control control, HumanTarget target)
        {
            if (_targetClickAttached.Add(control))
                control.MouseUp += (_, args) => { if (args.Button == MouseButtons.Left) PickTarget(target); };
            foreach (Control child in control.Controls) Attach(child, target);
        }
        void Cards(Control host, HumanTargetZone zone)
        {
            foreach (Panel tile in host.Controls.OfType<Panel>())
            {
                var id = tile.Tag switch { CardInstance c => c.InstanceId, FollowerInstance f => f.InstanceId, AmuletInstance a => a.InstanceId, _ => (int?)null };
                if (id is not null) Attach(tile, new(zone, id));
            }
        }
        Cards(_selfHand, HumanTargetZone.OwnHand);
        Cards(_selfBoard, HumanTargetZone.OwnBoard);
        Cards(_opponentBoard, HumanTargetZone.EnemyBoard);
        if (_opponentLeaderLabel.Parent is { } leader) Attach(leader, new(HumanTargetZone.EnemyLeader));
        _targetClickAttached.RemoveWhere(c => c.IsDisposed);
    }

    private void ApplyTargetHighlights()
    {
        var selection = _targetSelection;
        var available = selection?.Available.ToHashSet() ?? [];
        var selected = selection?.Selected.ToHashSet() ?? [];
        var completed = selection?.Completed.ToHashSet() ?? [];
        var sourceId = selection is null ? null : HumanActionResolver.PlayedCardOf(selection.Candidates[0]);
        if (sourceId is null && selection is not null)
            sourceId = selection.Candidates[0] switch { EvolveAction e => e.FollowerInstanceId, SuperEvolveAction s => s.FollowerInstanceId, _ => (int?)null };
        int State(HumanTarget target) => selected.Contains(target) ? 2 : completed.Contains(target) ? 4 :
            available.Contains(target) ? 1 : target.InstanceId == sourceId && sourceId is not null ? 3 : 0;
        void Cards(Control host, HumanTargetZone zone)
        {
            foreach (Panel tile in host.Controls.OfType<Panel>())
            {
                var id = tile.Tag switch { CardInstance c => c.InstanceId, FollowerInstance f => f.InstanceId, AmuletInstance a => a.InstanceId, _ => (int?)null };
                var state = id is null ? 0 : State(new(zone, id));
                if (tile is SvCardFace handFace) handFace.SetTargetState(state);
                foreach (var face in tile.Controls.OfType<SvCardFace>()) face.SetTargetState(state);
                if (selection is not null) tile.Cursor = state is 1 or 2 ? Cursors.Hand : Cursors.Default;
                else tile.Cursor = Cursors.Default;
            }
        }
        Cards(_selfHand, HumanTargetZone.OwnHand);
        Cards(_selfBoard, HumanTargetZone.OwnBoard);
        Cards(_opponentBoard, HumanTargetZone.EnemyBoard);
        if (_opponentLeaderLabel.Parent is SvLeaderFrame leader) leader.SetTargetState(State(new(HumanTargetZone.EnemyLeader)));
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape && _dragSource is not null)
        {
            AbortHeldDrag();
            return true;
        }
        if (keyData == Keys.Escape && (_targetSelection is not null || _modeSelection is not null || _inspectedCardDetail is not null))
        {
            CancelTargetSelection();
            return true;
        }
        if (keyData == Keys.Escape && _settingsVisible)
        {
            SetSettingsVisible(false);
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }
}
