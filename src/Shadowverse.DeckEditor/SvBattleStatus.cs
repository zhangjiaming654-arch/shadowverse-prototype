using System.Drawing.Drawing2D;
using Shadowverse.Engine.Game;
using Shadowverse.Engine.Models;

namespace Shadowverse.DeckEditor;

internal sealed record SvAbilityMark(string Name, Color Color, string Detail, string Symbol);

internal static class SvFollowerAbilities
{
    internal static IReadOnlyList<SvAbilityMark> For(FollowerInstance follower)
    {
        var marks = new List<SvAbilityMark>();
        void Add(CardKeyword flag, string name, Color color, string detail, string symbol)
        { if (follower.Keywords.HasFlag(flag)) marks.Add(new(name, color, detail, symbol)); }
        var wardInactive = follower.HasStealth || follower.HasIntimidate;
        Add(CardKeyword.Ward, wardInactive ? "守护失效" : "守护", Color.FromArgb(122, 220, 186),
            wardInactive ? "仍拥有守护，但潜伏或威慑使守护阻挡攻击的效果失效。" : "敌方随从须优先攻击拥有守护的随从；无视守护可以绕过。", "shield");
        Add(CardKeyword.Storm, "疾驰", Color.FromArgb(255, 197, 104), "进入战场的回合即可攻击随从或主战者；能否攻击还取决于当前行动状态。", "bolt");
        Add(CardKeyword.Aura, "灵气", Color.FromArgb(197, 163, 247), "对手的能力不能指定本随从；随机效果和全场效果仍可影响它。", "ring");
        Add(CardKeyword.Rush, "突进", Color.FromArgb(239, 163, 105), "进入战场的回合即可攻击敌方随从，不能仅凭突进攻击主战者。", "bolt");
        Add(CardKeyword.Bane, "毁灭", Color.FromArgb(196, 158, 237), "通过战斗伤害使交战随从被破坏。", "diamond");
        Add(CardKeyword.Barrier, "屏障", Color.FromArgb(130, 202, 247), "下一次受到的伤害变为0，随后失去屏障。", "shield");
        Add(CardKeyword.Drain, "虹吸", Color.FromArgb(248, 137, 155), "攻击造成伤害时，为己方主战者恢复相应生命。", "diamond");
        Add(CardKeyword.Stealth, "潜伏", Color.FromArgb(163, 180, 210), "不能被敌方随从攻击或被对手能力指定；攻击或通过能力造成伤害后失去。", "ring");
        Add(CardKeyword.Intimidate, "威慑", Color.FromArgb(224, 141, 175), "不能被敌方随从攻击；拥有此能力时守护失效。", "diamond");
        Add(CardKeyword.IgnoreWard, "无视守护", Color.FromArgb(236, 195, 121), "攻击时可以绕过敌方的守护。", "bolt");
        if (follower.IsSuperEvolved) marks.Add(new("超进化", Color.FromArgb(195, 157, 250),
            "已超进化。控制者自己的回合内，受到的伤害变为0且不会被能力破坏；对手回合可正常破坏。", "diamond"));
        else if (follower.IsEvolved) marks.Add(new("进化", SvTheme.GoldLit, "已进化。", "diamond"));
        return marks;
    }
}

internal sealed class SvAbilityStrip : Control
{
    private IReadOnlyList<SvAbilityMark> _abilities = [];
    internal bool HasAbilities => _abilities.Count > 0;
    internal IReadOnlyList<SvAbilityMark> Abilities => _abilities;
    internal SvAbilityStrip()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
    }
    internal void SetAbilities(IReadOnlyList<SvAbilityMark> abilities)
    {
        if (_abilities.SequenceEqual(abilities)) return;
        _abilities = abilities;
        AccessibleName = string.Join("、", abilities.Select(a => a.Name));
        Invalidate();
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var s = DeviceDpi / 96f;
        var gap = (int)(3 * s);
        using var font = new Font("Microsoft YaHei UI", Height < 18 * s ? 6f : 8f, FontStyle.Bold);
        var widths = _abilities.Select(a => TextRenderer.MeasureText(a.Name, font).Width + (int)(16 * s)).ToArray();
        var total = widths.Sum() + gap * Math.Max(0, widths.Length - 1);
        var x = Math.Max(0, (Width - Math.Min(Width, total)) / 2);
        for (var i = 0; i < _abilities.Count; i++)
        {
            var mark = _abilities[i];
            var reserve = i + 1 < _abilities.Count ? (int)(30 * s) : 0;
            if (x + widths[i] + reserve > Width)
            {
                TextRenderer.DrawText(e.Graphics, "+" + (_abilities.Count - i), font,
                    new Rectangle(x, 0, Math.Max(1, Width - x), Height), SvTheme.GoldLit,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter);
                break;
            }
            var rect = new Rectangle(x, 1, widths[i], Math.Max(1, Height - 2));
            using var fill = new SolidBrush(Color.FromArgb(40, mark.Color));
            using var pen = new Pen(Color.FromArgb(155, mark.Color));
            using var path = SvTheme.RoundedRect(rect, 4);
            e.Graphics.FillPath(fill, path); e.Graphics.DrawPath(pen, path);
            DrawSymbol(e.Graphics, new Rectangle(x + (int)(3 * s), (Height - (int)(10 * s)) / 2,
                (int)(10 * s), (int)(10 * s)), mark.Color, mark.Symbol);
            TextRenderer.DrawText(e.Graphics, mark.Name, font,
                new Rectangle(x + (int)(13 * s), 0, widths[i] - (int)(13 * s), Height), mark.Color,
                TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPadding);
            x += widths[i] + gap;
        }
    }
    internal static void DrawSymbol(Graphics g, Rectangle r, Color color, string symbol)
    {
        using var pen = new Pen(color, 1.5f);
        var cx = r.Left + r.Width / 2f;
        if (symbol == "ring") { g.DrawEllipse(pen, r); return; }
        PointF[] points = symbol switch
        {
            "shield" => [new(r.Left, r.Top), new(r.Right, r.Top), new(r.Right, r.Top + r.Height * .55f), new(cx, r.Bottom), new(r.Left, r.Top + r.Height * .55f)],
            "bolt" => [new(cx + 2, r.Top), new(r.Left, r.Top + r.Height * .6f), new(cx, r.Top + r.Height * .6f), new(cx - 2, r.Bottom), new(r.Right, r.Top + r.Height * .4f), new(cx, r.Top + r.Height * .4f)],
            _ => [new(cx, r.Top), new(r.Right, r.Top + r.Height / 2f), new(cx, r.Bottom), new(r.Left, r.Top + r.Height / 2f)]
        };
        g.DrawPolygon(pen, points);
    }
}

internal sealed class SvCrestStrip : Control
{
    private IReadOnlyList<VisibleCrest> _crests = [];
    private readonly string _title;
    private readonly ToolTip _tip = new() { InitialDelay = 150, AutoPopDelay = 20000, ShowAlways = true };
    private int _hover = -1;
    internal IReadOnlyList<VisibleCrest> Crests => _crests;
    internal event Action<string>? Inspect;
    internal SvCrestStrip(string title)
    {
        _title = title; DoubleBuffered = true;
        SetStyle(ControlStyles.SupportsTransparentBackColor, true); BackColor = Color.Transparent;
        Cursor = Cursors.Help; SetStyle(ControlStyles.ResizeRedraw, true);
        MouseLeave += (_, _) => { _hover = -1; Invalidate(); };
    }
    internal void SetCrests(IReadOnlyList<VisibleCrest> crests)
    {
        if (_crests.SequenceEqual(crests)) return;
        _crests = crests; _hover = -1;
        AccessibleName = _title + " " + crests.Count;
        AccessibleDescription = AllDetails();
        _tip.SetToolTip(this, AllDetails()); Invalidate();
    }
    private string Detail(VisibleCrest crest) => "纹章 · " + crest.Name +
        (crest.Countdown is int n ? "\n当前吟唱：" + n : "\n持续纹章") + "\n" + crest.EffectText;
    private string AllDetails() => _title + "（" + _crests.Count + "）\n\n" +
        (_crests.Count == 0 ? "暂无纹章" : string.Join("\n\n", _crests.Select(Detail))) + "\n\n点击查看全部纹章。";
    private Rectangle[] Gems()
    {
        var s = DeviceDpi / 96f;
        var count = Math.Min(6, _crests.Count);
        var size = Math.Min((int)(27 * s), Math.Max(1, (Width - (int)(8 * s)) / Math.Max(1, count)));
        return Enumerable.Range(0, count).Select(i => new Rectangle((int)(4 * s) + i * size,
            (int)(24 * s), Math.Max(1, size - (int)(3 * s)), Math.Min(size, Math.Max(1, Height - (int)(26 * s))))).ToArray();
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var index = Array.FindIndex(Gems(), r => r.Contains(e.Location));
        if (index == _hover) return;
        _hover = index;
        _tip.SetToolTip(this, index < 0 || index == 5 && _crests.Count > 6 ? AllDetails() : Detail(_crests[index]));
        Invalidate();
    }
    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button == MouseButtons.Left) Inspect?.Invoke(AllDetails());
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e); var s = DeviceDpi / 96f; var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var font = new Font("Microsoft YaHei UI", 8f, FontStyle.Bold);
        TextRenderer.DrawText(g, _title + "  " + _crests.Count, font,
            new Rectangle((int)(4 * s), 0, Width - (int)(8 * s), (int)(23 * s)), SvTheme.GoldLit,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
        if (_crests.Count == 0)
        {
            TextRenderer.DrawText(g, "暂无纹章", font, new Rectangle((int)(4 * s), (int)(24 * s), Width - (int)(8 * s), Height - (int)(24 * s)), SvTheme.TextDim);
            return;
        }
        var gems = Gems();
        for (var i = 0; i < gems.Length; i++)
        {
            var r = gems[i];
            var color = i == _hover ? SvTheme.Cyan : SvTheme.GoldLit;
            using var fill = new LinearGradientBrush(r, Color.FromArgb(59, 83, 107), SvTheme.Void, 90f);
            using var edge = new Pen(color, 1.5f);
            var middle = r.Left + r.Width / 2f;
            PointF[] hex = [new(middle, r.Top), new(r.Right, r.Top + r.Height * .25f), new(r.Right, r.Bottom - r.Height * .25f),
                new(middle, r.Bottom), new(r.Left, r.Bottom - r.Height * .25f), new(r.Left, r.Top + r.Height * .25f)];
            g.FillPolygon(fill, hex); g.DrawPolygon(edge, hex);
            var text = i == 5 && _crests.Count > 6 ? "+" + (_crests.Count - 5) : _crests[i].Countdown?.ToString() ?? _crests[i].Name[..1];
            TextRenderer.DrawText(g, text, font, r, color, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }
    protected override void Dispose(bool disposing) { if (disposing) _tip.Dispose(); base.Dispose(disposing); }
}
