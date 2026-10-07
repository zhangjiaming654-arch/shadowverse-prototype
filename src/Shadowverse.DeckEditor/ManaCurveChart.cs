using System.Drawing.Drawing2D;

namespace Shadowverse.DeckEditor;

/// <summary>
/// A compact 1-to-7-cost plus 8+ histogram for the deck editor. Values are copy
/// counts, not unique card counts, so the chart always adds up to the deck size.
/// <para>
/// 配色走 <see cref="SvTheme"/>（深底 + 金色描边 + 青色渐变柱），背景透明以便窗体的贴图透上来。
/// </para>
/// </summary>
public sealed class ManaCurveChart : Control
{
    private IReadOnlyDictionary<int, int> _counts = new Dictionary<int, int>();

    public ManaCurveChart()
    {
        SetStyle(ControlStyles.SupportsTransparentBackColor, true);
        DoubleBuffered = true;
        Height = 142;
        MinimumSize = new Size(420, 142);
        Margin = new Padding(0, 4, 0, 0);
        BackColor = Color.Transparent;
        ForeColor = SvTheme.Text;
        Font = new Font("Microsoft YaHei UI", 9f);
    }

    public void SetCounts(IEnumerable<KeyValuePair<int, int>> counts)
    {
        _counts = counts
            .Where(pair => pair.Key >= 1)
            .GroupBy(pair => Math.Min(pair.Key, 8))
            .ToDictionary(group => group.Key, group => group.Sum(pair => pair.Value));
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs eventArgs)
    {
        var graphics = eventArgs.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        var bounds = ClientRectangle;
        if (BackColor != Color.Transparent)
        {
            graphics.Clear(BackColor);
        }

        using var borderPen = new Pen(Color.FromArgb(120, SvTheme.Gold));
        using var axisPen = new Pen(Color.FromArgb(150, SvTheme.CyanDim));
        using var textBrush = new SolidBrush(SvTheme.Text);
        using var mutedBrush = new SolidBrush(SvTheme.TextDim);

        graphics.DrawRectangle(borderPen, 0, 0, Math.Max(0, bounds.Width - 1), Math.Max(0, bounds.Height - 1));
        using (var titleBrush = new SolidBrush(SvTheme.GoldLit))
        {
            graphics.DrawString("费用曲线（张数）", Font, titleBrush, 8, 6);
        }

        var costs = Enumerable.Range(1, 8).ToArray();
        var maximumCount = Math.Max(1, costs.Max(cost => _counts.GetValueOrDefault(cost)));
        var left = 24;
        var right = 16;
        var top = 28;
        var labelHeight = Font.Height + 5;
        var baseline = Math.Max(top + 24, bounds.Height - labelHeight - 9);
        var availableHeight = Math.Max(1, baseline - top);
        var usableWidth = Math.Max(10, bounds.Width - left - right);
        var slotWidth = usableWidth / costs.Length;
        var barWidth = Math.Max(8, Math.Min(34, slotWidth - 12));

        graphics.DrawLine(axisPen, left, baseline, bounds.Width - right, baseline);

        foreach (var (cost, index) in costs.Select((cost, index) => (cost, index)))
        {
            var count = _counts.GetValueOrDefault(cost);
            var centerX = left + (index * slotWidth) + (slotWidth / 2);
            var barHeight = count == 0 ? 0 : Math.Max(3, (int)Math.Round(availableHeight * (count / (double)maximumCount)));
            var barX = centerX - (barWidth / 2);
            var barY = baseline - barHeight;

            if (barHeight > 0)
            {
                // 青色渐变柱：上亮下暗，比纯色更有"魔力"质感。
                var barBounds = new Rectangle(barX, barY, barWidth, barHeight);
                using var barBrush = new LinearGradientBrush(
                    barBounds,
                    SvTheme.Cyan,
                    SvTheme.CyanDim,
                    LinearGradientMode.Vertical);
                graphics.FillRectangle(barBrush, barBounds);

                using var barEdge = new Pen(Color.FromArgb(190, SvTheme.Cyan));
                graphics.DrawRectangle(barEdge, barX, barY, barWidth, barHeight);
            }

            DrawCentered(graphics, count.ToString(), Font, count == 0 ? mutedBrush : textBrush, centerX, barY - Font.Height - 2);
            var label = cost == 8 ? "8+费" : $"{cost}费";
            DrawCentered(graphics, label, Font, mutedBrush, centerX, baseline + 4);
        }
    }

    private static void DrawCentered(Graphics graphics, string text, Font font, Brush brush, int centerX, int y)
    {
        var size = TextRenderer.MeasureText(text, font);
        graphics.DrawString(text, font, brush, centerX - (size.Width / 2), y);
    }
}
