using System.ComponentModel;
using Shadowverse.Engine.Game;
using System.Drawing.Drawing2D;

namespace Shadowverse.DeckEditor;

/// <summary>Restrained metal frame shared by the collection and battle screens.</summary>
internal sealed class SvHeading : Control
{
    private readonly string _subtitle;

    public SvHeading(string title, string subtitle)
    {
        Text = title;
        _subtitle = subtitle;
        Height = 66;
        Dock = DockStyle.Fill;
        SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.ResizeRedraw, true);
        BackColor = Color.Transparent;
        DoubleBuffered = true;
        Font = new Font("Microsoft YaHei UI", 17, FontStyle.Bold);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var scale = DeviceDpi / 96f;
        using var line = new Pen(Color.FromArgb(135, SvTheme.Gold));
        using (var shade = new LinearGradientBrush(ClientRectangle, Color.FromArgb(205, SvTheme.Void), Color.FromArgb(70, SvTheme.Void), LinearGradientMode.Horizontal))
            g.FillRectangle(shade, ClientRectangle);
        var crest = SvTheme.Texture("crest_celestial_v2.png");
        if (crest is not null)
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.DrawImage(crest, new Rectangle((int)(4 * scale), 2, (int)(88 * scale), Height - 4));
        }
        else
        {
            var center = new PointF(43 * scale, Height / 2f);
            g.DrawPolygon(line, new PointF[] { new(center.X, center.Y - 15 * scale), new(center.X + 12 * scale, center.Y),
                new(center.X, center.Y + 15 * scale), new(center.X - 12 * scale, center.Y) });
        }
        var titleHeight = Math.Min((int)(34 * scale), (int)(Height * .67f));
        TextRenderer.DrawText(g, Text, Font, new Rectangle((int)(100 * scale), 0, Width - (int)(100 * scale), titleHeight),
            SvTheme.Text, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        using var small = new Font("Microsoft YaHei UI", 8f);
        TextRenderer.DrawText(g, _subtitle, small, new Rectangle((int)(102 * scale), titleHeight, Width - (int)(102 * scale), Height - titleHeight),
            SvTheme.TextDim, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        g.DrawLine(line, 0, Height - 1, Width, Height - 1);
    }
}

/// <summary>Painted card frame; retains a real label and Panel identity for existing gestures.</summary>
internal sealed class SvCardFace : Panel
{
    private int _cost;
    private HandCardReadout? _handReadout;
    internal void SetHandReadout(HandCardReadout readout)
    {
        if (_handReadout == readout && _cost == readout.Cost) return;
        _handReadout = readout; _cost = readout.Cost; Invalidate();
    }
    private string _body;
    private readonly bool _compact;
    private bool _evolved;
    private string _baseName;
    private Color _accent;
    private readonly Label _name;
    private bool _follower;
    private int _attack, _defense;
    private bool _ward, _aura, _superEvolved;
    internal void SetAbilityFrame(bool ward, bool aura, bool superEvolved = false)
    {
        if (_ward == ward && _aura == aura && _superEvolved == superEvolved) return;
        _ward = ward; _aura = aura; _superEvolved = superEvolved; Invalidate();
    }

    internal void UpdateStats(bool follower, int attack, int defense)
    {
        if (_follower == follower && _attack == attack && _defense == defense) return;
        _follower = follower; _attack = attack; _defense = defense;
        Invalidate();
    }

    public SvCardFace(string name, int cost, string body, Color accent, bool compact, bool evolved)
    {
        _cost = cost;
        _body = body;
        _baseName = name;
        _accent = accent;
        _compact = compact;
        _evolved = evolved;
        BackColor = accent;
        DoubleBuffered = true;
        SetStyle(ControlStyles.ResizeRedraw, true);
        _name = new Label
        {
            UseMnemonic = false,
            Text = name + (evolved ? " ★" : ""), BackColor = Color.Transparent, ForeColor = SvTheme.Text,
            Font = new Font("Microsoft YaHei UI", compact ? 8.5f : 10f, FontStyle.Bold),
            AutoEllipsis = true, TextAlign = compact ? ContentAlignment.MiddleLeft : ContentAlignment.MiddleCenter
        };
        Controls.Add(_name);
    }

    public Label NameLabel => _name;

    private bool _handPortrait;
    private bool _boardPortrait;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal bool BoardPortrait
    {
        get => _boardPortrait;
        set { if (_boardPortrait == value) return; _boardPortrait = value; ArrangeName(); }
    }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal bool HandPortrait
    {
        get => _handPortrait;
        set
        {
            if (_handPortrait == value) return;
            _handPortrait = value;
            _name.AutoEllipsis = !value;
            ArrangeName();
        }
    }

    private int _targetState;
    internal void SetTargetState(int state)
    {
        if (_targetState == state) return;
        _targetState = state;
        Invalidate();
    }

    internal void UpdateCard(string name, int cost, string body, Color accent, bool evolved)
    {
        if (_baseName == name && _cost == cost && _body == body && _accent == accent && _evolved == evolved) return;
        if (_baseName != name || _evolved != evolved)
        {
            _name.Tag = null;
            _name.Text = name + (evolved ? " ★" : "");
        }
        if (_accent != accent) BackColor = accent;
        _baseName = name; _cost = cost; _body = body; _accent = accent; _evolved = evolved;
        Invalidate();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        ArrangeName();
    }

    private void ArrangeName()
    {
        if (_name is null) return;
        _name.Bounds = NameBounds(ClientSize, DeviceDpi, _compact, HandPortrait || BoardPortrait);
    }

    internal static Rectangle NameBounds(Size client, int dpi, bool compact, bool portrait)
    {
        var s = dpi / 96f;
        return compact
            ? new Rectangle((int)(37 * s), 3, Math.Max(1, client.Width - (int)(43 * s)), Math.Max(1, client.Height - 6))
            : !portrait && client.Height < 100 * s
                ? new Rectangle((int)(34 * s), 2, Math.Max(1, client.Width - (int)(38 * s)), Math.Max(1, client.Height - (int)(24 * s)))
                : new Rectangle((int)(6 * s), (int)(31 * s), Math.Max(1, client.Width - (int)(12 * s)), Math.Max(1, client.Height - (int)(56 * s)));
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        var g = e.Graphics;
        if (Width < 2 || Height < 2) return;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var r = new Rectangle(0, 0, Width - 1, Height - 1);
        using var path = SvTheme.RoundedRect(r, 5);
        using var fill = new LinearGradientBrush(r, ControlPaint.Dark(BackColor, .45f), SvTheme.Panel, LinearGradientMode.Vertical);
        g.FillPath(fill, path);
        var textured = SvCardDrawing.Plate(g, r, path, _compact ? 70 : 32);
        if (BackColor != _accent)
        {
            var dimmed = BackColor == Color.FromArgb(46, 52, 60);
            using var stateTint = new SolidBrush(Color.FromArgb(dimmed ? 175 : 90, dimmed ? SvTheme.Void : BackColor));
            g.FillPath(stateTint, path);
        }
        using var edge = new Pen(_evolved ? SvTheme.GoldLit : Color.FromArgb(165, BackColor), _evolved ? 2 : 1);
        g.DrawPath(edge, path);
        var s = DeviceDpi / 96f;
        var gemSize = !_compact && Height < 100 * s ? 22 : 26;
        var gem = new Rectangle((int)(5 * s), (int)(5 * s), (int)(gemSize * s), (int)(gemSize * s));
        SvCardDrawing.Gem(g, gem, _cost.ToString(), Color.FromArgb(67, 155, 99));
        if (!_compact)
        {
            using var accent = new Pen(Color.FromArgb(65, SvTheme.Gold));
            var size = Math.Max(12, Math.Min(Height - (int)(54 * s), Width - (int)(46 * s)));
            var ring = new Rectangle((Width - size) / 2, (Height - size) / 2, size, size);
            if (!textured)
            {
                g.DrawEllipse(accent, ring);
                g.DrawLine(accent, ring.Left, Height / 2, ring.Right, Height / 2);
            }
            using var bodyFont = new Font("Microsoft YaHei UI", Height < 100 * s ? 8 : HandPortrait ? 10 : 11, FontStyle.Bold);
            var bodyBounds = Height < 100 * s
                ? new Rectangle(6, Height - (int)(21 * s), Width - 12, (int)(19 * s))
                : new Rectangle(6, Height - (int)(29 * s), Width - 12, (int)(24 * s));
            if (_follower)
            {
                var diameter = Math.Max(10, Math.Min((int)((Height < 100 * s ? 21 : HandPortrait || BoardPortrait ? 21 : 27) * s), (Width - (int)(14 * s)) / 2));
                var y = Height - diameter - (int)(4 * s);
                SvCardDrawing.Gem(g, new Rectangle((int)(6 * s), y, diameter, diameter), _attack.ToString(), Color.FromArgb(47, 104, 166), Height < 100 * s ? 9 : 11);
                SvCardDrawing.Gem(g, new Rectangle(Width - diameter - (int)(6 * s), y, diameter, diameter), _defense.ToString(), Color.FromArgb(159, 50, 69), Height < 100 * s ? 9 : 11);
            }
            else if (_handReadout?.OathGauge is null) TextRenderer.DrawText(g, _body, bodyFont, bodyBounds,
                SvTheme.GoldLit, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            if (_evolved && Height >= 100 * s) TextRenderer.DrawText(g, _superEvolved ? "超进化" : "进化", Font, new Rectangle(Width - (int)(60 * s), 4, (int)(56 * s), (int)(24 * s)), SvTheme.GoldLit);
        }
        if (_handReadout is { } readout && !_compact)
        {
            using var small = new Font("Microsoft YaHei UI", 7.5f, FontStyle.Bold);
            if (readout.Form != HandPlayForm.Normal)
            {
                var mode = new Rectangle((int)(36 * s), (int)(6 * s), Math.Max(1, Width - (int)(41 * s)), (int)(20 * s));
                using var modeFill = new SolidBrush(Color.FromArgb(210, 17, 43, 55));
                g.FillRectangle(modeFill, mode);
                TextRenderer.DrawText(g, readout.FormName, small, mode, SvTheme.Cyan,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }
            if (readout.OathGauge is int gauge)
            {
                var bounds = new Rectangle(_follower ? (int)(30 * s) : (int)(5 * s), Height - (int)(24 * s),
                    Math.Max(1, Width - (int)((_follower ? 60 : 10) * s)), (int)(19 * s));
                var ready = readout.SuperOathReady || readout.OathReady;
                var color = ready ? SvTheme.GoldLit : SvTheme.Cyan;
                using var gaugeFill = new SolidBrush(Color.FromArgb(230, 13, 23, 37));
                g.FillRectangle(gaugeFill, bounds);
                TextRenderer.DrawText(g, readout.GaugeText, small, bounds, color,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                using var track = new SolidBrush(Color.FromArgb(100, color));
                using var progress = new SolidBrush(color);
                var bar = new Rectangle(bounds.X, bounds.Bottom, bounds.Width, Math.Max(2, (int)(2 * s)));
                g.FillRectangle(track, bar);
                bar.Width = (int)(bar.Width * Math.Clamp(gauge / (float)readout.GaugeTarget, 0, 1));
                g.FillRectangle(progress, bar);
            }
        }
        if (_aura)
        {
            using var aura = new Pen(Color.FromArgb(150, 188, 149, 247), 3 * s);
            var ring = r; ring.Inflate(-(int)(5 * s), -(int)(5 * s));
            g.DrawEllipse(aura, ring);
        }
        if (_ward)
        {
            using var ward = new Pen(Color.FromArgb(122, 220, 186), 2 * s);
            var outline = r; outline.Inflate(-(int)(2 * s), -(int)(2 * s));
            g.DrawRectangle(ward, outline);
        }
        SvTargetDrawing.Draw(g, ClientRectangle, _targetState, DeviceDpi);
    }
}

internal static class SvTargetDrawing
{
    // 1 = selectable, 2 = selected, 3 = source, 4 = completed target.
    internal static void Draw(Graphics graphics, Rectangle bounds, int state, int dpi)
    {
        if (state == 0 || bounds.Width < 8 || bounds.Height < 8) return;
        var scale = dpi / 96f;
        bounds.Inflate(-(int)(3 * scale), -(int)(3 * scale));
        using var path = SvTheme.RoundedRect(bounds, 5);
        var color = state == 5 ? SvTheme.Danger : state == 1 ? SvTheme.Cyan : state == 3 ? Color.FromArgb(186, 146, 242) : SvTheme.GoldLit;
        using var glow = new Pen(Color.FromArgb(65, color), 8 * scale);
        using var edge = new Pen(color, 3 * scale);
        graphics.DrawPath(glow, path);
        graphics.DrawPath(edge, path);
        if (state is 2 or 4 && bounds.Height > 100 * scale)
        {
            var badge = new Rectangle(bounds.Right - (int)(50 * scale), bounds.Top + (int)(4 * scale), (int)(48 * scale), (int)(21 * scale));
            using var fill = new SolidBrush(Color.FromArgb(230, 67, 52, 24));
            graphics.FillRectangle(fill, badge);
            TextRenderer.DrawText(graphics, "✓ 已选", SystemFonts.MessageBoxFont, badge, color, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }
}

internal sealed class SvLibraryCard : Control
{
    private readonly string _name, _profession, _rarity, _body, _effect;
    private readonly int _cost;
    private readonly Color _accent;
    private bool _hover;

    public SvLibraryCard(string name, int cost, string profession, string rarity, string body, string effect, Color accent)
    {
        _name = name; _cost = cost; _profession = profession; _rarity = rarity; _body = body; _effect = effect; _accent = accent;
        Size = new Size(152, 190);
        Margin = new Padding(5);
        DoubleBuffered = true;
        Cursor = Cursors.Hand;
        AccessibleName = name;
        AccessibleDescription = "单击加入卡组。" + effect;
        BackColor = SvTheme.Panel;
        SetStyle(ControlStyles.Selectable | ControlStyles.ResizeRedraw, true);
        TabStop = true;
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode is Keys.Enter or Keys.Space) { OnClick(EventArgs.Empty); e.Handled = true; }
        base.OnKeyDown(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var s = DeviceDpi / 96f;
        var r = new Rectangle(0, 0, Width - 1, Height - 1);
        using var path = SvTheme.RoundedRect(r, 6);
        using var fill = new LinearGradientBrush(r, Color.FromArgb(31, 50, 67), Color.FromArgb(14, 23, 35), LinearGradientMode.Vertical);
        g.FillPath(fill, path);
        var textured = SvCardDrawing.Plate(g, r, path, 20);
        using var border = new Pen(_hover || Focused ? SvTheme.Cyan : Color.FromArgb(160, _accent), _hover || Focused ? 2 : 1);
        g.DrawPath(border, path);
        var orb = new Rectangle((int)(6 * s), (int)(6 * s), (int)(30 * s), (int)(30 * s));
        SvCardDrawing.Gem(g, orb, _cost.ToString(), Color.FromArgb(67, 155, 99));
        using var meta = new Font("Microsoft YaHei UI", 8f);
        using var nameFont = new Font("Microsoft YaHei UI", 10f, FontStyle.Bold);
        TextRenderer.DrawText(g, _rarity, meta, new Rectangle((int)(40 * s), (int)(10 * s), Width - (int)(48 * s), (int)(23 * s)), _accent,
            TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
        var emblem = new Rectangle((Width - (int)(52 * s)) / 2, (int)(41 * s), (int)(52 * s), (int)(52 * s));
        using var line = new Pen(Color.FromArgb(120, _accent));
        if (!textured)
        {
            g.DrawEllipse(line, emblem);
            g.DrawPolygon(line, [new(emblem.Left + emblem.Width / 2, emblem.Top - 4), new(emblem.Right + 4, emblem.Top + emblem.Height / 2),
                new(emblem.Left + emblem.Width / 2, emblem.Bottom + 4), new(emblem.Left - 4, emblem.Top + emblem.Height / 2)]);
        }
        else
        {
            using var nameShade = new SolidBrush(Color.FromArgb(180, SvTheme.Void));
            g.FillRectangle(nameShade, 6 * s, 99 * s, Width - 12 * s, 61 * s);
        }
        TextRenderer.DrawText(g, _profession, meta, emblem, SvTheme.TextDim, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        TextRenderer.DrawText(g, _name, nameFont, new Rectangle((int)(7 * s), (int)(99 * s), Width - (int)(14 * s), (int)(38 * s)),
            SvTheme.Text, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis);
        TextRenderer.DrawText(g, _effect, meta, new Rectangle((int)(8 * s), (int)(141 * s), Width - (int)(16 * s), (int)(19 * s)),
            SvTheme.TextDim, TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
        g.DrawLine(line, 8 * s, Height - 26 * s, Width - 8 * s, Height - 26 * s);
        TextRenderer.DrawText(g, _body, meta, new Rectangle(8, Height - (int)(24 * s), Width - 16, (int)(21 * s)),
            SvTheme.GoldLit, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }
}

internal static class SvCardDrawing
{
    private static readonly Dictionary<Size, Bitmap> Plates = [];

    private static Bitmap ScaledPlate(Image source, Size size)
    {
        if (Plates.TryGetValue(size, out var cached)) return cached;
        // Resampling a 1024x1536 image on every paint is expensive. Cache common tile sizes.
        if (Plates.Count >= 32)
        {
            var oldest = Plates.First();
            Plates.Remove(oldest.Key);
            oldest.Value.Dispose();
        }
        var bitmap = new Bitmap(size.Width, size.Height);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        // Uniform cover: crop excess texture instead of squeezing its ornamentation.
        var factor = Math.Max(size.Width / (float)source.Width, size.Height / (float)source.Height);
        var cropWidth = size.Width / factor;
        var cropHeight = size.Height / factor;
        graphics.DrawImage(source, new Rectangle(Point.Empty, size),
            new RectangleF((source.Width - cropWidth) / 2, (source.Height - cropHeight) / 2, cropWidth, cropHeight), GraphicsUnit.Pixel);
        Plates.Add(size, bitmap);
        return bitmap;
    }

    internal static bool Plate(Graphics g, Rectangle bounds, GraphicsPath clip, int dim)
    {
        var plate = SvTheme.Texture("card_plate_v2.png");
        if (plate is null) return false;
        var saved = g.Save();
        g.SetClip(clip);
        g.DrawImageUnscaled(ScaledPlate(plate, bounds.Size), bounds.Location);
        using var veil = new SolidBrush(Color.FromArgb(dim, SvTheme.Void));
        g.FillRectangle(veil, bounds);
        g.Restore(saved);
        return true;
    }

    internal static void Gem(Graphics g, Rectangle bounds, string text, Color accent, float fontSize = 12)
    {
        using var fill = new LinearGradientBrush(bounds, ControlPaint.Light(accent, .35f), ControlPaint.Dark(accent, .5f), LinearGradientMode.Vertical);
        using var edge = new Pen(SvTheme.GoldLit);
        g.FillEllipse(fill, bounds);
        g.DrawEllipse(edge, bounds);
        using var font = new Font("Georgia", fontSize, FontStyle.Bold);
        TextRenderer.DrawText(g, text, font, bounds, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }
}

internal sealed class SvLeaderFrame : SvPanel
{
    private int? _health;
    private int _pp, _maxPp, _ep, _sep;
    private int _targetState;
    internal void SetTargetState(int state) { if (_targetState == state) return; _targetState = state; Invalidate(); }

    public void SetResources(int health, int pp, int maxPp, int ep, int sep)
    {
        if (_health == health && _pp == pp && _maxPp == maxPp && _ep == ep && _sep == sep) return;
        _health = health; _pp = pp; _maxPp = maxPp; _ep = ep; _sep = sep;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var s = DeviceDpi / 96f;
        var resourceHeight = Height - Controls.OfType<SvCrestStrip>().Sum(c => c.Height) - Padding.Vertical;
        var orb = new Rectangle((int)(10 * s), Math.Max((int)(5 * s), (resourceHeight - (int)(48 * s)) / 2), (int)(48 * s), (int)(48 * s));
        SvCardDrawing.Gem(g, orb, _health?.ToString() ?? "—", Color.FromArgb(154, 53, 71));
        // The readable three-line label remains the authoritative resource description.
        // Colored pips are only a second, quickly scanned view of the same information.
        var x = (int)(70 * s);
        var y = Height - (int)(10 * s);
        for (var i = 0; i < Math.Min(10, _maxPp); i++)
        {
            using var brush = new SolidBrush(i < _pp ? Color.FromArgb(100, 188, 133) : Color.FromArgb(51, 67, 62));
            g.FillEllipse(brush, x + i * 10 * s, y, 5 * s, 5 * s);
        }
        for (var i = 0; i < _ep; i++)
        {
            using var brush = new SolidBrush(SvTheme.GoldLit);
            g.FillEllipse(brush, Width - (40 + i * 9) * s, y, 5 * s, 5 * s);
        }
        for (var i = 0; i < _sep; i++)
        {
            using var brush = new SolidBrush(Color.FromArgb(180, 146, 239));
            g.FillEllipse(brush, Width - (14 + i * 9) * s, y, 5 * s, 5 * s);
        }
        SvTargetDrawing.Draw(g, ClientRectangle, _targetState, DeviceDpi);
    }
}

// Buffer containers as well as tiles so layout changes do not expose a blank intermediate frame.
internal sealed class SvTableLayoutPanel : TableLayoutPanel
{
    public SvTableLayoutPanel() => DoubleBuffered = true;
}

internal sealed class SvFlowLayoutPanel : FlowLayoutPanel
{
    public SvFlowLayoutPanel() => DoubleBuffered = true;
}
