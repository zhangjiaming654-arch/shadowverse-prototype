using System.ComponentModel;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace Shadowverse.DeckEditor;

/// <summary>
/// 《影之诗：超凡世界》风格的界面主题。
/// <para>
/// 素材由 <c>image-gen</c> 技能生成，放在 <c>Assets\</c>，随生成目录一起输出。
/// 所有贴图**加载失败时一律回退到纯色**，绝不因为缺图让界面崩掉或变黑。
/// </para>
/// <para>
/// 配色取自生成出来的背景与按钮：深蓝紫底 + 青色主色 + 金色描边。
/// </para>
/// </summary>
internal static class SvTheme
{
    // ───────────────────────── 调色板 ─────────────────────────

    /// <summary>最底层背景（比贴图更暗，贴图半透明叠在它上面）。</summary>
    public static readonly Color Void = Color.FromArgb(10, 13, 24);

    /// <summary>面板底色（深蓝紫）。</summary>
    public static readonly Color Panel = Color.FromArgb(19, 29, 43);

    /// <summary>面板高亮层（悬停/选中）。</summary>
    public static readonly Color PanelLit = Color.FromArgb(29, 48, 65);

    /// <summary>金色描边（主装饰色）。</summary>
    public static readonly Color Gold = Color.FromArgb(170, 151, 112);

    /// <summary>金色亮部（高光/悬停）。</summary>
    public static readonly Color GoldLit = Color.FromArgb(232, 216, 175);

    /// <summary>青色主色（魔法光效）。</summary>
    public static readonly Color Cyan = Color.FromArgb(89, 203, 230);

    /// <summary>青色暗部。</summary>
    public static readonly Color CyanDim = Color.FromArgb(30, 127, 150);

    /// <summary>正文文字。</summary>
    public static readonly Color Text = Color.FromArgb(232, 237, 247);

    /// <summary>次要文字。</summary>
    public static readonly Color TextDim = Color.FromArgb(159, 176, 200);

    /// <summary>警示/危险。</summary>
    public static readonly Color Danger = Color.FromArgb(224, 82, 99);

    /// <summary>卡牌职业色（沿用原有语义，但提亮以适配深色底）。</summary>
    public static readonly Color FollowerColor = Color.FromArgb(64, 132, 196);

    public static readonly Color SpellColor = Color.FromArgb(122, 92, 178);

    public static readonly Color AmuletColor = Color.FromArgb(184, 146, 58);

    // ───────────────────────── 贴图 ─────────────────────────

    private static readonly Dictionary<string, Image?> _cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>素材目录（生成目录下的 Assets）。</summary>
    public static string AssetDirectory { get; } =
        Path.Combine(AppContext.BaseDirectory, "Assets");

    /// <summary>
    /// 取一张贴图；不存在或读失败返回 null（调用方负责回退）。
    /// 带缓存，同一张图只从磁盘读一次。
    /// </summary>
    public static Image? Texture(string fileName)
    {
        if (_cache.TryGetValue(fileName, out var cached))
        {
            return cached;
        }

        Image? image = null;
        try
        {
            var path = Path.Combine(AssetDirectory, fileName);
            if (File.Exists(path))
            {
                // 从字节流加载，避免 Image.FromFile 长期占用文件句柄导致无法覆盖。
                var bytes = File.ReadAllBytes(path);
                using var stream = new MemoryStream(bytes);
                using var decoded = Image.FromStream(stream);
                image = new Bitmap(decoded);
            }
        }
        catch (Exception)
        {
            image = null;
        }

        _cache[fileName] = image;
        return image;
    }

    // ───────────────────────── 绘制助手 ─────────────────────────

    /// <summary>圆角矩形路径。</summary>
    public static GraphicsPath RoundedRect(Rectangle bounds, int radius)
    {
        var path = new GraphicsPath();
        if (radius <= 0)
        {
            path.AddRectangle(bounds);
            return path;
        }

        radius = Math.Min(radius, Math.Max(1, Math.Min(bounds.Width, bounds.Height) / 2));
        var d = radius * 2;
        path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
        path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
        path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    /// <summary>
    /// 画一层柔光：从中心向外逐渐透明。用于选中/悬停的发光效果。
    /// </summary>
    public static void DrawGlow(Graphics g, Rectangle bounds, Color color, int radius, float strength)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        using var path = RoundedRect(bounds, radius);
        var center = new PointF(bounds.X + (bounds.Width / 2f), bounds.Y + (bounds.Height / 2f));
        var reach = Math.Max(bounds.Width, bounds.Height) / 1.2f;
        using var brush = new PathGradientBrush(path)
        {
            CenterPoint = center,
            CenterColor = Color.FromArgb((int)(90 * strength), color),
            SurroundColors = [Color.FromArgb(0, color)]
        };
        brush.SetSigmaBellShape(0.5f);
        g.FillPath(brush, path);

        _ = reach;
    }

    /// <summary>画金色描边（内外两层，模拟金属边）。</summary>
    public static void DrawGoldEdge(Graphics g, Rectangle bounds, int radius, bool lit)
    {
        using var path = RoundedRect(bounds, radius);
        using var pen = new Pen(lit ? GoldLit : Gold, lit ? 1.6f : 1.2f);
        g.DrawPath(pen, path);

        var inner = Rectangle.Inflate(bounds, -1, -1);
        using var innerPath = RoundedRect(inner, Math.Max(0, radius - 1));
        using var innerPen = new Pen(Color.FromArgb(lit ? 70 : 45, GoldLit), 1f);
        g.DrawPath(innerPen, innerPath);
    }

    /// <summary>把一张贴图按"铺满并保持比例"的方式画进目标矩形（会裁切多余部分）。</summary>
    public static void DrawCover(Graphics g, Image image, Rectangle bounds)
    {
        var scale = Math.Max(bounds.Width / (float)image.Width, bounds.Height / (float)image.Height);
        var w = (int)Math.Ceiling(image.Width * scale);
        var h = (int)Math.Ceiling(image.Height * scale);
        var x = bounds.X + ((bounds.Width - w) / 2);
        var y = bounds.Y + ((bounds.Height - h) / 2);
        g.DrawImage(image, new Rectangle(x, y, w, h));
    }

    /// <summary>把一张贴图拉伸铺满目标矩形（用于按钮这类需要精确贴合的形状）。</summary>
    public static void DrawStretch(Graphics g, Image image, Rectangle bounds) =>
        g.DrawImage(image, bounds);

    // ───────────────────────── 一键套用 ─────────────────────────

    /// <summary>
    /// 给窗体铺上背景贴图。
    /// <para>
    /// **必须做成窗体的 BackgroundImage，不能用兄弟控件。** 踩过一次：
    /// 最初塞了个 <see cref="SvBackdrop"/> 控件并让容器设成 Transparent，
    /// 结果 WinForms 的 Transparent 只透出**父控件**的画、不透**兄弟控件** ——
    /// 容器透出的是窗体的纯色底，贴图整张被盖住，界面看起来就是一块死黑。
    /// 做成窗体的 BackgroundImage 后，所有 Transparent 子控件都能透出它。
    /// </para>
    /// <para>压暗与暗角在合成时一次画进图里，避免运行时每帧计算。</para>
    /// </summary>
    public static void AttachBackdrop(Form form, float dim = 0.18f, string asset = "bg_archive_v2.png")
    {
        form.BackColor = Void;
        var composed = ComposeBackdrop(1920, 1080, dim, asset);
        if (composed is not null)
        {
            var previous = form.BackgroundImage;
            form.BackgroundImage = composed;
            previous?.Dispose();
            form.BackgroundImageLayout = ImageLayout.Stretch;
        }
    }

    /// <summary>合成"贴图 + 压暗 + 上下暗角"的一张底图；缺贴图时返回 null。</summary>
    private static Bitmap? ComposeBackdrop(int width, int height, float dim, string asset)
    {
        var texture = Texture(asset) ?? Texture("bg_main.png");
        if (texture is null)
        {
            return null;
        }

        var bitmap = new Bitmap(width, height);
        using var g = Graphics.FromImage(bitmap);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.Clear(Void);
        DrawCover(g, texture, new Rectangle(0, 0, width, height));

        if (dim > 0f)
        {
            using var veil = new SolidBrush(Color.FromArgb((int)(255 * Math.Clamp(dim, 0f, 1f)), Void));
            g.FillRectangle(veil, 0, 0, width, height);
        }

        var full = new Rectangle(0, 0, width, height);
        using var vignette = new LinearGradientBrush(full, Color.Black, Color.Black, LinearGradientMode.Vertical)
        {
            InterpolationColors = new ColorBlend(3)
            {
                Colors = [Color.FromArgb(70, 0, 0, 0), Color.FromArgb(0, 0, 0, 0), Color.FromArgb(70, 0, 0, 0)],
                Positions = [0f, 0.5f, 1f]
            }
        };
        g.FillRectangle(vignette, full);
        return bitmap;
    }

    /// <summary>深色表格：去网格线、深底、金色表头、青色选中。</summary>
    public static void StyleGrid(DataGridView grid)
    {
        grid.BackgroundColor = Panel;
        grid.RowTemplate.Height = 32;
        grid.ColumnHeadersHeight = 34;
        grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
        grid.DefaultCellStyle.Padding = new Padding(5, 0, 5, 0);
        grid.DefaultCellStyle.Font = new Font("Microsoft YaHei UI", 9f);
        grid.CellPainting -= PaintGridButton;
        grid.CellPainting += PaintGridButton;
        grid.BorderStyle = BorderStyle.None;
        grid.GridColor = Color.FromArgb(48, 60, 92);
        grid.EnableHeadersVisualStyles = false;
        grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
        grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
        grid.RowHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
        grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(30, 38, 62);
        grid.ColumnHeadersDefaultCellStyle.ForeColor = GoldLit;
        grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = Color.FromArgb(30, 38, 62);
        grid.ColumnHeadersDefaultCellStyle.SelectionForeColor = GoldLit;
        grid.ColumnHeadersDefaultCellStyle.Font = new Font("Microsoft YaHei UI", 9f, FontStyle.Bold);
        grid.DefaultCellStyle.BackColor = Color.FromArgb(24, 31, 52);
        grid.DefaultCellStyle.ForeColor = Text;
        grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(38, 92, 116);
        grid.DefaultCellStyle.SelectionForeColor = Color.White;
        grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(28, 36, 60);
        grid.RowHeadersDefaultCellStyle.BackColor = Color.FromArgb(30, 38, 62);
        grid.RowHeadersDefaultCellStyle.ForeColor = TextDim;
    }

    private static void PaintGridButton(object? sender, DataGridViewCellPaintingEventArgs e)
    {
        if (sender is not DataGridView grid || e.RowIndex < 0 || e.ColumnIndex < 0 ||
            grid.Columns[e.ColumnIndex] is not DataGridViewButtonColumn) return;
        e.PaintBackground(e.ClipBounds, true);
        var bounds = Rectangle.Inflate(e.CellBounds, -5, -5);
        if (bounds.Width > 0 && bounds.Height > 0)
        {
            using var fill = new SolidBrush(PanelLit);
            using var edge = new Pen(Color.FromArgb(100, Cyan));
            e.Graphics!.FillRectangle(fill, bounds);
            e.Graphics.DrawRectangle(edge, bounds);
            TextRenderer.DrawText(e.Graphics, e.FormattedValue?.ToString() ?? "", grid.Font, bounds, Text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
        e.Handled = true;
    }

    internal static Control Frame(Control content, string title)
    {
        var frame = new SvPanel { Dock = DockStyle.Fill, Padding = new Padding(12), Margin = new Padding(5), CornerRadius = 6, GlassOpacity = 158 };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, BackColor = Color.Transparent };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(new Label { Text = title, Dock = DockStyle.Fill, ForeColor = GoldLit,
            Font = new Font("Microsoft YaHei UI", 10, FontStyle.Bold), TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
        content.Dock = DockStyle.Fill;
        layout.Controls.Add(content, 0, 1);
        frame.Controls.Add(layout);
        return frame;
    }

    /// <summary>深色输入类控件（文本框 / 下拉框）。</summary>
    public static void StyleInput(Control control)
    {
        control.BackColor = Color.FromArgb(26, 33, 55);
        control.ForeColor = Text;
        control.Font = new Font("Microsoft YaHei UI", 9f);

        if (control is ComboBox combo)
        {
            combo.FlatStyle = FlatStyle.Flat;
            combo.DrawMode = DrawMode.OwnerDrawFixed;
            combo.ItemHeight = Math.Max(20, combo.Font.Height + 5);
            combo.DrawItem -= DrawComboItem;
            combo.DrawItem += DrawComboItem;
        }
    }

    private static void DrawComboItem(object? sender, DrawItemEventArgs e)
    {
        if (sender is not ComboBox combo) return;
        var selected = (e.State & DrawItemState.Selected) != 0;
        using var background = new SolidBrush(selected ? PanelLit : Panel);
        e.Graphics.FillRectangle(background, e.Bounds);
        var text = e.Index >= 0 && e.Index < combo.Items.Count ? combo.GetItemText(combo.Items[e.Index]) : combo.Text;
        TextRenderer.DrawText(e.Graphics, text, combo.Font, Rectangle.Inflate(e.Bounds, -3, 0),
            combo.Enabled ? Text : TextDim, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        if ((e.State & DrawItemState.Focus) != 0) e.DrawFocusRectangle();
    }

    /// <summary>统一的正文标签样式。</summary>
    public static void StyleLabel(Label label, bool primary = false)
    {
        if (primary || label.ForeColor == SystemColors.ControlText || label.ForeColor == Color.Black)
            label.ForeColor = primary ? GoldLit : Text;
        label.BackColor = Color.Transparent;
    }

    /// <summary>
    /// 递归把主题套到整棵控件树上（标签 / 输入框 / 表格）。
    /// 按钮不在这里换 —— 已有的 Button 实例无法原地变成 SvButton，得在创建处换。
    /// </summary>
    public static void ApplyTo(Control root)
    {
        switch (root)
        {
            // 自绘控件自己会画，别动它们的背景
            case SvPanel or SvButton or SvOrnament or SvBackdrop or SvCardFace or SvLibraryCard or SvHeading:
                break;
            case DataGridView grid:
                StyleGrid(grid);
                break;
            case Label label:
                StyleLabel(label);
                break;
            case TextBox or ComboBox or NumericUpDown:
                StyleInput(root);
                break;
            case Form form:
                form.BackColor = Void;
                break;
            // 容器一律透明 —— 否则它们不透明的底色会把窗体的背景贴图整块盖住，
            // 界面就变成"白底黑字贴了几块深色控件"。（第一版就是这么翻车的）
            // 注意必须写全名：本类的调色板里有个 `Panel` 颜色字段，直接写 `Panel` 会解析成它。
            case System.Windows.Forms.Panel:
                root.BackColor = Color.Transparent;
                break;
        }

        foreach (Control child in root.Controls)
        {
            ApplyTo(child);
        }
    }
}

/// <summary>
/// 全窗口背景：贴图 + 暗角 + 可选的水平渐变压暗。
/// 放在窗体最底层（Dock = Fill，先于其他控件加入 Controls）。
/// </summary>
internal sealed class SvBackdrop : Control
{
    public SvBackdrop()
    {
        SetStyle(
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.UserPaint |
            ControlStyles.ResizeRedraw,
            true);
        Dock = DockStyle.Fill;
        Enabled = false;   // 纯背景，不参与交互
    }

    /// <summary>背景压暗强度 0~1（越大越暗，方便上层文字读得清）。</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public float Dim { get; set; } = 0.35f;

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(SvTheme.Void);

        var texture = SvTheme.Texture("bg_main.png");
        if (texture is not null)
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            SvTheme.DrawCover(g, texture, ClientRectangle);
        }

        if (Dim > 0f)
        {
            using var veil = new SolidBrush(Color.FromArgb((int)(255 * Math.Clamp(Dim, 0f, 1f)), SvTheme.Void));
            g.FillRectangle(veil, ClientRectangle);
        }

        // 自上而下的暗角，让顶部/底部更沉，中间透气。
        using var vignette = new LinearGradientBrush(
            ClientRectangle,
            Color.FromArgb(170, 0, 0, 0),
            Color.FromArgb(170, 0, 0, 0),
            LinearGradientMode.Vertical);
        var blend = new ColorBlend(3)
        {
            Colors = [Color.FromArgb(70, 0, 0, 0), Color.FromArgb(0, 0, 0, 0), Color.FromArgb(70, 0, 0, 0)],
            Positions = [0f, 0.5f, 1f]
        };
        vignette.InterpolationColors = blend;
        g.FillRectangle(vignette, ClientRectangle);
    }
}

/// <summary>
/// 玻璃面板：贴图底 + 金色描边 + 可选标题。
/// </summary>
internal class SvPanel : Panel
{
    public SvPanel()
    {
        SetStyle(
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.UserPaint |
            ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor,
            true);
        BackColor = Color.Transparent;
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int GlassOpacity { get; set; } = 205;

    /// <summary>圆角半径。</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int CornerRadius { get; set; } = 10;

    /// <summary>是否高亮（悬停/选中）。</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Lit { get; set; }

    /// <summary>是否画金色描边。</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool ShowEdge { get; set; } = true;

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        if (Width < 3 || Height < 3) return;
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var bounds = new Rectangle(0, 0, Width - 1, Height - 1);
        using var path = SvTheme.RoundedRect(bounds, CornerRadius);

        // Paint the parent's scene first, then tint it like smoked glass.
        base.OnPaintBackground(e);
        using (var fill = new SolidBrush(Color.FromArgb(Math.Clamp(GlassOpacity, 0, 255), Lit ? SvTheme.PanelLit : SvTheme.Panel)))
            g.FillPath(fill, path);
        using (var sheen = new LinearGradientBrush(bounds, Color.FromArgb(28, SvTheme.GoldLit), Color.Transparent, LinearGradientMode.Vertical))
            g.FillPath(sheen, path);

        if (ShowEdge)
        {
            SvTheme.DrawGoldEdge(g, bounds, CornerRadius, Lit);
        }
    }
}

/// <summary>
/// 按钮：贴图底 + 悬停发光 + 按下位移。
/// 用法与普通 Button 一致（Text / Click 都能用），只是改成自绘。
/// </summary>
internal class SvButton : Button
{
    private bool _hover;
    private bool _pressed;

    public SvButton()
    {
        SetStyle(
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.UserPaint |
            ControlStyles.ResizeRedraw,
            true);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        BackColor = SvTheme.Panel;
        ForeColor = SvTheme.Text;
        Font = new Font("Microsoft YaHei UI", 9.5f, FontStyle.Bold);
        Cursor = Cursors.Hand;
        UseVisualStyleBackColor = false;
    }

    /// <summary>主按钮（青色高亮）还是次要按钮。</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Primary { get; set; }

    /// <summary>危险操作（红调）。</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Danger { get; set; }

    protected override void OnMouseEnter(EventArgs e)
    {
        _hover = true;
        Invalidate();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hover = false;
        _pressed = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs mevent)
    {
        _pressed = true;
        Invalidate();
        base.OnMouseDown(mevent);
    }

    protected override void OnMouseUp(MouseEventArgs mevent)
    {
        _pressed = false;
        Invalidate();
        base.OnMouseUp(mevent);
    }

    protected override void OnPaint(PaintEventArgs pevent)
    {
        var g = pevent.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        var accent = Danger ? SvTheme.Danger : Primary ? SvTheme.Cyan : SvTheme.CyanDim;
        var bounds = new Rectangle(0, 0, Width - 1, Height - 1);
        const int radius = 5;

        if (_hover && Enabled)
        {
            SvTheme.DrawGlow(g, Rectangle.Inflate(bounds, -4, -4), accent, radius, Primary ? 1.0f : 0.6f);
        }

        using var path = SvTheme.RoundedRect(bounds, radius);

        using (var fill = new SolidBrush(_pressed ? SvTheme.Panel : SvTheme.PanelLit))
        {
            g.FillPath(fill, path);
        }

        var texture = SvTheme.Texture("btn_plate.png");
        if (texture is not null && Enabled)
        {
            var saved = g.Save();
            g.SetClip(path);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            SvTheme.DrawStretch(g, texture, bounds);
            // 用色调把按钮染成主/次/危险三色，同时压暗以便文字可读。
            var tintAlpha = _pressed ? 210 : _hover ? 165 : 195;
            using var tint = new SolidBrush(Color.FromArgb(tintAlpha, accent));
            g.FillRectangle(tint, bounds);
            using var dark = new SolidBrush(Color.FromArgb(60, SvTheme.Void));
            g.FillRectangle(dark, bounds);
            g.Restore(saved);
        }
        else
        {
            using var fallback = new LinearGradientBrush(
                bounds,
                ControlPaint.Light(SvTheme.PanelLit, 0.25f),
                SvTheme.Panel,
                LinearGradientMode.Vertical);
            g.FillPath(fallback, path);
        }

        SvTheme.DrawGoldEdge(g, bounds, radius, _hover || Primary);

        var textColor = Enabled ? SvTheme.Text : SvTheme.TextDim;
        TextRenderer.DrawText(
            g,
            Text,
            Font,
            bounds,
            textColor,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }
}

/// <summary>
/// 标题装饰：一条金色藤蔓 + 青色发光横线，中间可以压一行标题文字。
/// </summary>
internal sealed class SvOrnament : Control
{
    public SvOrnament()
    {
        SetStyle(
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.UserPaint |
            ControlStyles.ResizeRedraw,
            true);
        Height = 34;
        ForeColor = SvTheme.GoldLit;
        Font = new Font("Microsoft YaHei UI", 10.5f, FontStyle.Bold);
    }

    /// <summary>是否在中央显示标题文字。</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool ShowText { get; set; } = true;

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        var texture = SvTheme.Texture("ornament_line.png");
        if (texture is not null)
        {
            // 素材是"黑底金藤蔓+青线"，用亮化叠加把黑底吃掉，只留纹样。
            var bounds = new Rectangle(0, 0, Width, Height);
            var saved = g.Save();
            var matrix = new ColorMatrix([[1, 0, 0, 0, 0], [0, 1, 0, 0, 0], [0, 0, 1, 0, 0], [0, 0, 0, 1, 0], [0.55f, 0.55f, 0.55f, 0, 1]]);
            using var attributes = new ImageAttributes();
            attributes.SetColorMatrix(matrix);
            g.DrawImage(
                texture,
                bounds,
                0,
                0,
                texture.Width,
                texture.Height,
                GraphicsUnit.Pixel,
                attributes);
            g.Restore(saved);
        }

        if (ShowText && !string.IsNullOrEmpty(Text))
        {
            var textBounds = new Rectangle(0, 0, Width, Height);
            TextRenderer.DrawText(
                g,
                Text,
                Font,
                textBounds,
                ForeColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
    }
}
