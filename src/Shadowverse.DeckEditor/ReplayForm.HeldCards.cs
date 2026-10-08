using System.Diagnostics;
using System.Drawing.Drawing2D;
using Shadowverse.Engine.Game;

namespace Shadowverse.DeckEditor;

public sealed partial class ReplayForm
{
    private SvHeldCardSurface? _heldCard;
    private (int Id, Rectangle Bounds)? _heldReleaseOrigin;
    private readonly System.Windows.Forms.Timer _heldReturnTimer = new() { Interval = 16 };
    private readonly Stopwatch _heldReturnClock = new();

    private void InitializeHeldCards()
    {
        _heldReturnTimer.Tick += (_, _) => AdvanceHeldReturn(_heldReturnClock.Elapsed.TotalMilliseconds / 160);
        Deactivate += (_, _) => AbortHeldDrag(false);
        if (_battleLayout is { } battle) battle.SizeChanged += (_, _) => AbortHeldDrag(false);
    }

    // Only the bitmap moves. The real card and hand order stay intact until the engine commits.
    private void PickUpHeldCard(Panel source, Point screenPoint)
    {
        RemoveHeldCard();
        if (_battleLayout is not { } battle || source.Width < 1 || source.Height < 1) return;
        Bitmap? sprite = null, scene = null;
        try
        {
            sprite = SnapshotCard(source);
            scene = SnapshotBattle(source);
            var bounds = BattleBounds(source);
            var point = battle.PointToClient(screenPoint);
            var legal = new List<Rectangle>();
            if (_pendingActions is { } actions && _pendingObservation is { } observation)
            {
                foreach (var target in actions.Where(a => HumanActionResolver.PlayedCardOf(a) == _dragInstanceId)
                    .SelectMany(a => HumanTargetSelection.Targets(observation, a, HumanTargetRole.Effect)).Distinct())
                {
                    var control = TargetControl(target);
                    if (control is not null) legal.Add(BattleBounds(control));
                }
                if (HumanActionResolver.DropHand(observation, actions, _dragInstanceId, null).Count > 0)
                    legal.Add(BattleBounds(_selfBoard));
            }
            var surface = new SvHeldCardSurface(scene, sprite, bounds, point,
                BattleBounds(_actionLabel.Parent!), legal);
            scene = null; sprite = null;
            _heldCard = surface;
            surface.Bounds = battle.Bounds;
            Controls.Add(surface);
            surface.BringToFront();
        }
        catch (Exception exception)
        {
            scene?.Dispose(); sprite?.Dispose(); RemoveHeldCard(); ReportUiFailure(exception);
        }
    }

    private Control? TargetControl(HumanTarget target) => target.Zone switch
    {
        HumanTargetZone.EnemyLeader => _opponentLeaderLabel.Parent,
        HumanTargetZone.EnemyBoard when target.InstanceId is int id => ControlOfSlot(_opponentBoard, id),
        HumanTargetZone.OwnBoard when target.InstanceId is int id => ControlOfSlot(_selfBoard, id),
        _ => null
    };

    private void MoveHeldCard(Point screenPoint, HumanHit hit, bool valid)
    {
        if (_heldCard is not { } surface || _battleLayout is not { } battle) return;
        var target = hit.Zone switch
        {
            HumanZone.OpponentLeader => _opponentLeaderLabel.Parent,
            HumanZone.OwnBoard when hit.InstanceId is int id => ControlOfSlot(_selfBoard, id),
            HumanZone.OpponentBoard when hit.InstanceId is int id => ControlOfSlot(_opponentBoard, id),
            HumanZone.OwnBoard => _selfBoard,
            _ => null
        };
        var targeted = hit.Zone == HumanZone.OpponentLeader || hit.InstanceId is not null &&
            hit.Zone is HumanZone.OwnBoard or HumanZone.OpponentBoard;
        surface.MoveCard(battle.PointToClient(screenPoint), target is null ? null : BattleBounds(target),
            valid, targeted, DescribeDragIntent(hit, valid));
    }

    private void ReturnHeldCard()
    {
        _heldReleaseOrigin = null;
        if (_heldCard is not { } surface) return;
        surface.BeginReturn();
        _heldReturnClock.Restart(); _heldReturnTimer.Start();
    }

    private void AdvanceHeldReturn(double fraction)
    {
        if (_heldCard is not { } surface) return;
        surface.ReturnProgress = Math.Clamp(fraction, 0, 1);
        surface.Invalidate();
        if (fraction >= 1) RemoveHeldCard();
    }

    private void RemoveHeldCard(bool clearRelease = true)
    {
        _heldReturnTimer.Stop(); _heldReturnClock.Stop();
        var surface = _heldCard; _heldCard = null;
        if (surface is not null) { Controls.Remove(surface); surface.Dispose(); _battleLayout?.Invalidate(true); }
        if (clearRelease) _heldReleaseOrigin = null;
    }

    private void AbortHeldDrag(bool animate = true)
    {
        var source = _dragSource;
        if (source is not null)
        {
            FinishDrag();
            if (source.Capture) source.Capture = false;
        }
        if (animate) ReturnHeldCard(); else RemoveHeldCard();
    }

    private bool TryCommitHeldDrop(IReadOnlyList<GameAction> candidates, HumanHit hit)
    {
        if (!_dragIsHandCard || candidates.Count != 1 || _pendingObservation is not { } observation) return false;
        var action = candidates[0];
        var targets = Enum.GetValues<HumanTargetRole>().SelectMany(role =>
            HumanTargetSelection.Targets(observation, action, role)).ToArray();
        HumanTarget? droppedTarget = hit.Zone == HumanZone.OpponentLeader ? new(HumanTargetZone.EnemyLeader) :
            hit.InstanceId is int id && hit.Zone is HumanZone.OwnBoard or HumanZone.OpponentBoard ?
            new(hit.Zone == HumanZone.OwnBoard ? HumanTargetZone.OwnBoard : HumanTargetZone.EnemyBoard, id) : null;
        // A gesture supplies at most one explicit target. Never auto-select extra hand cards or targets.
        if (targets.Length > 0 && (targets.Length != 1 || targets[0] != droppedTarget)) return false;
        if (_heldCard is { } held) _heldReleaseOrigin = (_dragInstanceId, Rectangle.Round(held.CardBounds));
        CommitHumanAction(action);
        return true;
    }
}

internal sealed class SvHeldCardSurface : Control
{
    private readonly Bitmap _scene, _card;
    private readonly Rectangle _from, _hintBounds;
    private readonly IReadOnlyList<Rectangle> _legal;
    private readonly PointF _grab;
    private Point _pointer;
    private Rectangle? _target;
    private bool _valid, _aiming, _returning;
    private string _hint = "拿起卡牌 · 拖到战场或目标后松手 · Esc / 右键取消";
    private RectangleF _returnFrom;
    internal double ReturnProgress;
    internal RectangleF CardBounds => _returning ? Lerp(_returnFrom, _from, (float)(1 - Math.Pow(1 - ReturnProgress, 3))) :
        HeldBounds(ClientSize, _from.Size, _pointer, _grab, DeviceDpi, _aiming);

    internal SvHeldCardSurface(Bitmap scene, Bitmap card, Rectangle from, Point pointer, Rectangle hintBounds,
        IReadOnlyList<Rectangle> legal)
    {
        _scene = scene; _card = card; _from = from; _pointer = pointer; _hintBounds = hintBounds; _legal = legal;
        _grab = new PointF(Math.Clamp((pointer.X - from.Left) / (float)from.Width, 0, 1),
            Math.Clamp((pointer.Y - from.Top) / (float)from.Height, 0, 1));
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        SetStyle(ControlStyles.Selectable, false); TabStop = false; AccessibleName = "拿在手中的卡牌";
    }

    internal void MoveCard(Point pointer, Rectangle? target, bool valid, bool aiming, string hint)
    {
        _pointer = pointer; _target = target; _valid = valid; _aiming = aiming; _hint = hint; Invalidate();
    }
    internal void BeginReturn() { _returnFrom = CardBounds; _returning = true; ReturnProgress = 0; Invalidate(); }

    // Geometry is independent of native cursor state, including DPI and edge clamping.
    internal static RectangleF HeldBounds(Size client, Size card, Point pointer, PointF grab, int dpi, bool aiming)
    {
        var scale = dpi / 96f;
        var width = card.Width * 1.08f; var height = card.Height * 1.08f;
        var x = aiming ? pointer.X + 28 * scale : pointer.X - width * grab.X;
        var y = pointer.Y - height * grab.Y - 12 * scale;
        var margin = 12 * scale; // Room for the small tilt and shadow, including window edges.
        var insetX = Math.Min(margin, Math.Max(0, (client.Width - width) / 2));
        var insetY = Math.Min(margin, Math.Max(0, (client.Height - height) / 2));
        return new RectangleF(Math.Clamp(x, insetX, Math.Max(insetX, client.Width - width - insetX)),
            Math.Clamp(y, insetY, Math.Max(insetY, client.Height - height - insetY)), width, height);
    }
    private static RectangleF Lerp(RectangleF a, RectangleF b, float t) => new(
        a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t, a.Width + (b.Width - a.Width) * t, a.Height + (b.Height - a.Height) * t);

    protected override void WndProc(ref Message m)
    {
        // The visual must never intercept clicks or target hit testing; the source owns mouse capture.
        if (m.Msg == 0x84) { m.Result = new IntPtr(-1); return; } // WM_NCHITTEST / HTTRANSPARENT
        base.WndProc(ref m);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.DrawImageUnscaled(_scene, Point.Empty);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        var accent = _valid ? Color.FromArgb(108, 233, 249) : Color.FromArgb(239, 147, 130);
        if (!_returning)
        {
            using var legal = new Pen(Color.FromArgb(130, 120, 225, 242), 2 * DeviceDpi / 96f);
            foreach (var bounds in _legal) g.DrawRectangle(legal, Rectangle.Inflate(bounds, 2, 2));
            if (_target is { } target)
            {
                using var glow = new SolidBrush(Color.FromArgb(45, accent)); g.FillRectangle(glow, target);
                using var edge = new Pen(accent, 3 * DeviceDpi / 96f); g.DrawRectangle(edge, target);
            }
        }
        var r = CardBounds;
        var state = g.Save();
        var angle = _returning ? (float)(-4 * (1 - ReturnProgress)) : -4f;
        g.TranslateTransform(r.X + r.Width / 2, r.Y + r.Height / 2); g.RotateTransform(angle);
        var local = new RectangleF(-r.Width / 2, -r.Height / 2, r.Width, r.Height);
        using (var shadow = new SolidBrush(Color.FromArgb(130, 0, 0, 0)))
            g.FillRectangle(shadow, local.X + 7, local.Y + 10, local.Width, local.Height);
        g.DrawImage(_card, local);
        using (var edge = new Pen(Color.FromArgb(215, 124, 228, 247), 2))
            g.DrawRectangle(edge, local.X, local.Y, local.Width, local.Height);
        g.Restore(state);
        if (!_returning && _aiming)
        {
            var start = new PointF(r.X + r.Width / 2, r.Y + r.Height * .75f);
            var end = new PointF(_pointer.X, _pointer.Y);
            using var path = new GraphicsPath();
            path.AddBezier(start, new PointF(start.X, start.Y - 75 * DeviceDpi / 96f),
                new PointF(end.X, end.Y + 55 * DeviceDpi / 96f), end);
            using var halo = new Pen(Color.FromArgb(65, accent), 9 * DeviceDpi / 96f); g.DrawPath(halo, path);
            using var tip = new AdjustableArrowCap(4, 5, true);
            using var arrow = new Pen(accent, 3 * DeviceDpi / 96f) { CustomEndCap = tip }; g.DrawPath(arrow, path);
            using var ring = new Pen(accent, 2); g.DrawEllipse(ring, end.X - 9, end.Y - 9, 18, 18);
        }
        if (!_returning)
        {
            using var backdrop = new SolidBrush(Color.FromArgb(230, 13, 24, 38)); g.FillRectangle(backdrop, _hintBounds);
            using var font = new Font("Microsoft YaHei UI", 14 * DeviceDpi / 96f, FontStyle.Bold, GraphicsUnit.Pixel);
            TextRenderer.DrawText(g, _hint, font, Rectangle.Inflate(_hintBounds, -8, -2), accent,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _scene.Dispose(); _card.Dispose(); }
        base.Dispose(disposing);
    }
}
