using System.Windows.Forms.Layout;

namespace Shadowverse.DeckEditor;

/// <summary>A single hand row, with bounded overlap and raised cards on hover.</summary>
internal sealed class SvHandFan : FlowLayoutPanel
{
    private sealed class HandLayoutEngine : LayoutEngine
    {
        public override bool Layout(object container, LayoutEventArgs args) => false;
    }

    private static readonly LayoutEngine HandLayout = new HandLayoutEngine();
    private Control[] _cards = [];
    private Control? _hovered;
    private bool _arranging;

    internal bool Portrait { get; }

    public SvHandFan(bool portrait = true)
    {
        Portrait = portrait;
        DoubleBuffered = true;
        AutoScroll = false;
        WrapContents = false;
        SetStyle(ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
    }

    public override LayoutEngine LayoutEngine => HandLayout;

    internal void SetCards(IEnumerable<Control> cards)
    {
        var ordered = cards.ToArray();
        if (_cards.SequenceEqual(ordered)) return;
        _cards = ordered;
        if (_hovered is not null && !_cards.Contains(_hovered)) _hovered = null;
        PerformLayout();
    }

    protected override void OnControlAdded(ControlEventArgs e)
    {
        base.OnControlAdded(e);
        if (e.Control is not SvCardFace tile) return;
        void Enter(object? sender, EventArgs args)
        {
            if (_hovered == tile) return;
            _hovered = tile;
            PerformLayout();
        }
        void Leave(object? sender, EventArgs args)
        {
            // Child-label transitions and dragging must not move the captured card.
            if (_hovered != tile || tile.Capture || tile.ClientRectangle.Contains(tile.PointToClient(Cursor.Position))) return;
            _hovered = null;
            PerformLayout();
        }
        tile.MouseEnter += Enter;
        tile.NameLabel.MouseEnter += Enter;
        tile.MouseLeave += Leave;
        tile.NameLabel.MouseLeave += Leave;
        tile.MouseCaptureChanged += Leave;
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        if (_arranging || _cards.Length == 0 || ClientSize.Width < 1 || ClientSize.Height < 1) return;
        _cards = _cards.Where(card => !card.IsDisposed && card.Parent == this).ToArray();
        if (_cards.Length == 0) return;
        if (_hovered?.Parent != this) _hovered = null;
        _arranging = true;
        try
        {
            var rectangles = CardBounds(ClientSize, DeviceDpi, _cards.Length, Array.IndexOf(_cards, _hovered), Portrait);
            for (var i = 0; i < _cards.Length; i++)
            {
                var card = _cards[i];
                if (card.IsDisposed) continue;
                var bounds = rectangles[i];
                if (card.Bounds != bounds && !card.Capture) card.Bounds = bounds;
                // Right-hand cards cover the right edge of the preceding card; every cost stays exposed.
                var z = _cards.Length - 1 - i;
                if (Controls.GetChildIndex(card) != z) Controls.SetChildIndex(card, z);
            }
            if (_hovered is not null && !_hovered.IsDisposed) _hovered.BringToFront();
        }
        finally { _arranging = false; }
    }

    internal static Rectangle[] CardBounds(Size client, int dpi, int count, int hovered, bool portrait)
    {
        if (count <= 0) return [];
        var s = dpi / 96f;
        var inset = (int)(3 * s);
        var lift = portrait ? (int)(10 * s) : (int)(2 * s);
        var bend = portrait ? (int)(6 * s) : 0;
        var height = Math.Max(1, Math.Min((int)((portrait ? 194 : 42) * s), client.Height - inset * 2 - lift - bend));
        var width = portrait ? Math.Min((int)(156 * s), Math.Max((int)(136 * s), (int)(height * .82f))) : (int)(150 * s);
        // Opening hands have room to show every full card, rather than four narrow thumbnails.
        if (portrait && count <= 5)
            width = Math.Min(width, Math.Max(1, (client.Width - inset * 2 - (int)(8 * s) * (count - 1)) / count));
        width = Math.Min(width, Math.Max(1, client.Width - inset * 2));
        var available = Math.Max(0, client.Width - inset * 2 - width);
        var step = count == 1 ? 0 : Math.Min(width + (int)(8 * s), available / (float)(count - 1));
        var start = (client.Width - width - step * (count - 1)) / 2f;
        var middle = (count - 1) / 2f;
        return Enumerable.Range(0, count).Select(i =>
        {
            var arc = middle == 0 ? 0 : (int)(bend * Math.Abs(i - middle) / middle);
            return new Rectangle((int)(start + step * i), i == hovered ? inset : inset + lift + arc, width, height);
        }).ToArray();
    }
}
