using Shadowverse.Engine.Game;
using Shadowverse.Engine.Models;

namespace Shadowverse.DeckEditor;

public partial class ReplayForm
{
    private string? _inspectedCardDetail;
    private string? _inspectedCardReason;

    private void InspectCard(Panel tile, string? reason = null)
    {
        if (_targetSelection is not null || _modeSelection is not null || IsMulliganPhase || _fusionCardInstanceId is not null) return;
        CloseTargetSelection();
        _inspectedCardDetail = tile.AccessibleDescription ?? "卡牌详情";
        _inspectedCardReason = reason;
        SetTargetLayout(true);
        _humanHint.Text = reason ?? "卡牌详情 · 点击关闭或按 Esc 返回战场。";
        RefreshActionButtons();
        if (_pendingActions is null) BuildInspectionChoices();
    }

    private void BuildInspectionChoices()
    {
        if (_inspectedCardDetail is not { } detail) return;
        var width = Math.Max(180, _humanActions.ClientSize.Width - 24);
        _humanActions.Controls.Add(new RichTextBox
        {
            Text = detail, ReadOnly = true, Width = width, Height = _humanShowAllActions ? 140 : Math.Max(140, _humanActions.ClientSize.Height - 12),
            BackColor = Color.FromArgb(17, 28, 40), ForeColor = SvTheme.Text, BorderStyle = BorderStyle.None,
            Font = new Font("Microsoft YaHei UI", 10), WordWrap = true, ScrollBars = RichTextBoxScrollBars.Vertical,
            DetectUrls = false, TabStop = false
        });
        _selectionFooter.Visible = true;
        _selectionConfirm.Enabled = false;
        _selectionConfirm.Text = _inspectedCardReason is null ? "查看详情" : "当前不可使用";
        _selectionBack.Enabled = false;
        _selectionAll.Enabled = _pendingActions is not null;
        _selectionAll.Text = _humanShowAllActions ? "收起全部动作" : $"显示全部动作（{_pendingActions?.Count ?? 0}）";
        _selectionCancel.Text = "关闭详情（Esc）";
        if (_humanShowAllActions && _pendingActions is { } actions && _pendingObservation is { } observation)
            foreach (var candidate in actions)
            {
                var button = CreateHumanButton(DescribeAction(observation, candidate), false);
                button.Height = Math.Max(44, TextRenderer.MeasureText(button.Text, button.Font,
                    new Size(width - 16, int.MaxValue), TextFormatFlags.WordBreak).Height + 14);
                button.Click += (_, _) => BeginAmbiguityResolution([candidate]);
                _humanActions.Controls.Add(button);
            }
    }

    private static void ArrangeBoardCard(Panel slot)
    {
        if (slot.Controls.OfType<SvCardFace>().FirstOrDefault() is not { } face) return;
        var scale = slot.DeviceDpi / 96f;
        var top = Math.Min((int)(22 * scale), Math.Max(0, slot.Height / 4));
        var strip = slot.Controls.OfType<SvAbilityStrip>().FirstOrDefault();
        if (strip is { HasAbilities: true } && slot.Height < 82 * scale) top = (int)(2 * scale);
        var stripHeight = strip is { HasAbilities: true } ? Math.Min((int)(23 * scale), Math.Max(0, slot.Height - top - (int)(36 * scale) - 2)) : 0;
        if (strip is not null)
        {
            strip.Visible = stripHeight > 0;
            strip.Bounds = new Rectangle(2, slot.Height - stripHeight, Math.Max(1, slot.Width - 4), stripHeight);
        }
        var height = Math.Max(1, Math.Min((int)(178 * scale), slot.ClientSize.Height - top - stripHeight - 2));
        // Fit a portrait inside the slot instead of stretching a fixed width to a short height.
        var width = Math.Max(1, Math.Min((int)(height * .78f), slot.ClientSize.Width - 4));
        height = Math.Max(1, Math.Min(height, (int)Math.Round(width / .78f)));
        face.BoardPortrait = true;
        var rectangle = new Rectangle((slot.ClientSize.Width - width) / 2, top + Math.Max(0, (slot.ClientSize.Height - top - stripHeight - height) / 2), width, height);
        face.Dock = DockStyle.None;
        if (face.Bounds != rectangle) face.Bounds = rectangle;
        foreach (var badge in slot.Controls.OfType<Label>().Where(l => l.Name.EndsWith("Badge")))
            PositionBoardBadge(slot, badge, badge.Name == "EvolveBadge");
    }

    private static void PositionBoardBadge(Panel slot, Label badge, bool rightAligned)
    {
        var face = slot.Controls.OfType<SvCardFace>().FirstOrDefault();
        if (face is null) return;
        badge.Location = new Point(rightAligned ? Math.Max(face.Left, face.Right - badge.Width) : face.Left,
            Math.Max(0, face.Top - badge.Height - 1));
    }
}
