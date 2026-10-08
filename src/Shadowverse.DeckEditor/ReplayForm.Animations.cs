using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using Shadowverse.Engine.Game;
using Shadowverse.Engine.Models;
using Shadowverse.Engine.Simulation;

namespace Shadowverse.DeckEditor;

public sealed partial class ReplayForm
{
    private const int PlayAnimationMilliseconds = 680;
    private int _playAnimationDuration = PlayAnimationMilliseconds;
    private readonly System.Windows.Forms.Timer _playAnimationTimer = new() { Interval = 16 };
    private readonly Stopwatch _playAnimationClock = new();
    private SvPlayAnimationSurface? _playAnimation;
    private TaskCompletionSource<bool>? _playPresentation;
    private TaskCompletionSource<bool>? _livePresentationWaiter;
    private bool _applyingPlayImpact;
    private bool _playImpactApplied;

    private void InitializePlayAnimations()
    {
        _playAnimationTimer.Tick += (_, _) => AdvancePlayAnimation(
            _playAnimationClock.Elapsed.TotalMilliseconds / _playAnimationDuration);
        if (_battleLayout is { } battle)
            battle.SizeChanged += (_, _) => FinishPlayAnimation();
    }

    private static int? PlayedHandCard(GameAction action) => action switch
    {
        PlayFollowerAction a => a.CardInstanceId,
        PlayAmuletAction a => a.CardInstanceId,
        PlaySpellAction a => a.CardInstanceId,
        PlayCrystallizeAction a => a.CardInstanceId,
        PlayAccelerateAction a => a.CardInstanceId,
        _ => null
    };

    // The engine has already produced immutable before/after snapshots. Animation never applies rules.
    private void PlayVisualStep(MatchStep step, TaskCompletionSource<bool>? presented = null, bool force = false)
    {
        var release = _heldReleaseOrigin;
        RemoveHeldCard();
        CancelPlayAnimation();
        if ((!Visible && !force) || _battleLayout is not { } battle ||
            PlayedHandCard(step.Action) is not { } id ||
            FindVisualCard(step.ActingPlayer, id, true) is not { } source ||
            source.Width < 1 || source.Height < 1 || battle.Width < 1 || battle.Height < 1)
        {
            try { UpdatePlayback(); }
            finally { presented?.TrySetResult(true); }
            return;
        }

        Bitmap? sprite = null;
        Bitmap? scene = null;
        try
        {
            sprite = SnapshotCard(source);
            var released = step.ActingPlayer == 0 && release is { } origin && origin.Id == id;
            var from = released ? release!.Value.Bounds : BattleBounds(source);
            _actionLabel.Text = DescribeStep(step);
            _progressLabel.Text = $"第 {_currentStepIndex + 1} / {_steps.Count} 步";
            _arrowCanvas.Scene = null;
            scene = SnapshotBattle(source);
            var landing = LandingBounds(step, id, from.Size);
            var effects = BuildPlayImpacts(step);
            var surface = new SvPlayAnimationSurface(scene, sprite, from, landing, effects,
                step.ActingPlayer == 0 ? Color.FromArgb(101, 220, 245) : Color.FromArgb(248, 194, 105),
                step.Action is PlaySpellAction or PlayAccelerateAction, released);
            _playAnimationDuration = released ? 360 : PlayAnimationMilliseconds;
            sprite = null; scene = null; // Surface owns both snapshots from this point.
            _playAnimation = surface;
            _playPresentation = presented;
            _playImpactApplied = false;
            surface.Bounds = battle.Bounds;
            Controls.Add(surface);
            surface.BringToFront();
            _playAnimationClock.Restart();
            _playAnimationTimer.Start();
        }
        catch
        {
            sprite?.Dispose(); scene?.Dispose();
            CancelPlayAnimation();
            // Preserve the exact resolved state even if bitmap allocation fails.
            UpdatePlayback();
            presented?.TrySetResult(true);
            throw;
        }
    }

    private void AdvancePlayAnimation(double fraction)
    {
        if (_playAnimation is not { } surface) return;
        try
        {
            if (fraction >= SvPlayAnimationSurface.ImpactAt && !_playImpactApplied)
            {
                ApplyPlayImpact();
                surface.SetAfter(SnapshotImpactScene(surface));
                surface.RestrictToImpact([BattleBounds(_selfHand), BattleBounds(_opponentHand)]);
            }
            surface.Fraction = Math.Clamp(fraction, 0, 1);
            surface.Invalidate();
            if (fraction >= 1) FinishPlayAnimation();
        }
        catch (Exception exception)
        {
            var presented = _playPresentation;
            CancelPlayAnimation();
            try { UpdatePlayback(); }
            catch (Exception updateException) { ReportUiFailure(updateException); }
            // A painting failure must not strand the match worker waiting for presentation.
            presented?.TrySetResult(true);
            ReportUiFailure(exception);
        }
    }

    private void ApplyPlayImpact()
    {
        if (_playImpactApplied || _playAnimation is null) return;
        _applyingPlayImpact = true;
        try { UpdatePlayback(); _playImpactApplied = true; }
        finally { _applyingPlayImpact = false; }
    }

    private void FinishPlayAnimation()
    {
        if (_playAnimation is null || _applyingPlayImpact) return;
        try { ApplyPlayImpact(); }
        catch (Exception exception) { ReportUiFailure(exception); }
        finally { CancelPlayAnimation(); }
    }

    private void CancelPlayAnimation()
    {
        _playAnimationTimer.Stop();
        _playAnimationClock.Stop();
        var surface = _playAnimation;
        var presented = _playPresentation;
        _playAnimation = null;
        _playPresentation = null;
        if (surface is not null)
        {
            Controls.Remove(surface);
            surface.Dispose();
            _battleLayout?.Invalidate(true);
        }
        presented?.TrySetResult(true);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _liveMatchRunning = false;
            _humanChoice?.TrySetCanceled();
            Interlocked.Exchange(ref _livePresentationWaiter, null)?.TrySetCanceled();
            AbortHeldDrag(false);
            CancelPlayAnimation();
            _heldReturnTimer.Dispose();
            _playAnimationTimer.Dispose();
            _autoPlayTimer.Dispose();
            _liveProgressTimer.Dispose();
            _cardToolTip.Dispose();
        }
        base.Dispose(disposing);
    }

    private Control? FindVisualCard(int player, int id, bool hand = false)
    {
        var host = hand ? (Control)(player == 0 ? _selfHand : _opponentHand)
            : player == 0 ? _selfBoard : _opponentBoard;
        return VisualDescendants(host).FirstOrDefault(c => c is SvCardFace && c.Tag switch
        {
            CardInstance a => a.InstanceId == id,
            FollowerInstance a => a.InstanceId == id,
            AmuletInstance a => a.InstanceId == id,
            _ => false
        });
    }

    private static IEnumerable<Control> VisualDescendants(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            yield return child;
            foreach (var descendant in VisualDescendants(child)) yield return descendant;
        }
    }

    private Rectangle BattleBounds(Control control)
    {
        var location = Point.Empty;
        for (Control? current = control; current is not null && current != _battleLayout; current = current.Parent)
            location.Offset(current.Location);
        return new Rectangle(location, control.Size);
    }

    private Rectangle LandingBounds(MatchStep step, int id, Size spriteSize)
    {
        var player = step.AfterState.Players[step.ActingPlayer];
        var follower = player.Board.ToList().FindIndex(f => f.InstanceId == id);
        var amulet = player.Amulets.ToList().FindIndex(a => a.InstanceId == id);
        var board = step.ActingPlayer == 0 ? _selfBoard : _opponentBoard;
        // Crystallize can create an amulet with a fresh id. Locate newly entered pieces as well.
        if (follower < 0 && amulet < 0 && step.Action is PlayCrystallizeAction)
            amulet = player.Amulets.ToList().FindIndex(a => !step.BeforeState.Players[step.ActingPlayer].Amulets.Any(b => b.InstanceId == a.InstanceId));
        var slotIndex = follower >= 0 ? follower : amulet >= 0 ? player.Board.Count + amulet : -1;
        if (slotIndex < 0 && step.Action is PlayFollowerAction or PlayAmuletAction or PlayCrystallizeAction)
            slotIndex = Math.Min(4, step.Action is PlayFollowerAction
                ? step.BeforeState.Players[step.ActingPlayer].Board.Count
                : step.BeforeState.Players[step.ActingPlayer].OccupiedBoardSlots);
        if (slotIndex >= 0 && board.Controls.Cast<Control>().FirstOrDefault(c => board.GetColumn(c) == slotIndex) is { } slot)
        {
            var bounds = BattleBounds(slot);
            var width = Math.Min(spriteSize.Width, Math.Max(1, bounds.Width - 8));
            var height = Math.Min(spriteSize.Height, Math.Max(1, bounds.Height - 8));
            return new Rectangle(bounds.Left + (bounds.Width - width) / 2, bounds.Top + (bounds.Height - height) / 2, width, height);
        }
        var action = BattleBounds(_actionLabel);
        var y = step.ActingPlayer == 0 ? action.Top - spriteSize.Height / 2 : action.Bottom - spriteSize.Height / 2;
        return new Rectangle(action.Left + (action.Width - spriteSize.Width) / 2, y, spriteSize.Width, spriteSize.Height);
    }

    private static Bitmap SnapshotCard(Control card)
    {
        var result = new Bitmap(card.Width, card.Height);
        try
        {
            card.DrawToBitmap(result, new Rectangle(Point.Empty, card.Size));
            // DrawToBitmap has inconsistent nested label ordering on some WinForms/DPI combinations.
            using var graphics = Graphics.FromImage(result);
            foreach (var label in card.Controls.OfType<Label>().Where(l => l.Width > 0 && l.Height > 0))
            {
                using var text = new Bitmap(label.Width, label.Height);
                label.DrawToBitmap(text, new Rectangle(Point.Empty, label.Size));
                graphics.DrawImageUnscaled(text, label.Location);
            }
            return result;
        }
        catch { result.Dispose(); throw; }
    }

    private Bitmap SnapshotBattle(Control? omit = null)
    {
        var battle = _battleLayout!;
        var tiles = new[] { _selfHand, _opponentHand }.SelectMany(h => h.Controls.Cast<Control>())
            .Where(c => c.Visible).ToArray();
        var result = new Bitmap(battle.Width, battle.Height);
        try
        {
            // Capture once per phase. Redraw the fans in actual back-to-front order, avoiding
            // WinForms DrawToBitmap's reversed overlapping-child order. Never snapshot every tick.
            battle.SuspendLayout();
            foreach (var tile in tiles) tile.Visible = false;
            battle.DrawToBitmap(result, new Rectangle(Point.Empty, battle.Size));
            foreach (var tile in tiles) tile.Visible = true;
            battle.ResumeLayout(false);
            using var graphics = Graphics.FromImage(result);
            foreach (var hand in new[] { _opponentHand, _selfHand })
                foreach (var tile in tiles.Where(t => t.Parent == hand && t != omit).OrderByDescending(hand.Controls.GetChildIndex))
                {
                    using var face = SnapshotCard(tile);
                    graphics.DrawImageUnscaled(face, BattleBounds(tile).Location);
                }
            return result;
        }
        catch { result.Dispose(); throw; }
        finally
        {
            foreach (var tile in tiles) if (!tile.IsDisposed) tile.Visible = true;
            battle.ResumeLayout(false);
        }
    }

    private Bitmap SnapshotImpactScene(SvPlayAnimationSurface surface)
    {
        // After impact only the pulse/cast regions remain covered. Hands and the timeline are
        // displayed by their existing native controls, so do not recapture the whole control tree.
        var result = surface.CopyBefore();
        try
        {
            using var graphics = Graphics.FromImage(result);
            var regions = new[] { _selfBoard.Parent!.Parent!, _opponentBoard.Parent!.Parent!,
                _selfLeaderLabel.Parent!, _opponentLeaderLabel.Parent!, _actionLabel.Parent! };
            foreach (var control in regions.Where(c => c.Width > 0 && c.Height > 0))
            {
                using var patch = new Bitmap(control.Width, control.Height);
                control.DrawToBitmap(patch, new Rectangle(Point.Empty, control.Size));
                graphics.DrawImageUnscaled(patch, BattleBounds(control).Location);
            }
            return result;
        }
        catch { result.Dispose(); throw; }
    }

    private IReadOnlyList<SvPlayImpact> BuildPlayImpacts(MatchStep step)
    {
        var effects = new List<SvPlayImpact>();
        for (var player = 0; player < 2; player++)
        {
            var before = step.BeforeState.Players[player];
            var after = step.AfterState.Players[player];
            var delta = after.Health - before.Health;
            if (delta != 0) effects.Add(new SvPlayImpact(BattleBounds(player == 0 ? _selfLeaderLabel : _opponentLeaderLabel),
                delta > 0 ? $"+{delta}" : delta.ToString(), delta > 0));
            foreach (var follower in before.Board)
            {
                var target = FindVisualCard(player, follower.InstanceId);
                if (target is null) continue;
                var next = after.Board.FirstOrDefault(f => f.InstanceId == follower.InstanceId);
                var difference = next is null ? 0 : next.CurrentDefense - follower.CurrentDefense;
                if (next is null || difference != 0)
                    effects.Add(new SvPlayImpact(BattleBounds(target), next is null ? "离场" : difference > 0 ? $"+{difference}" : difference.ToString(), difference > 0));
                if (next is not null && next.Keywords != follower.Keywords)
                {
                    var gained = KeywordNames(next.Keywords & ~follower.Keywords);
                    var lost = KeywordNames(follower.Keywords & ~next.Keywords);
                    if (gained.Count > 0) effects.Add(new SvPlayImpact(BattleBounds(target), "+" + string.Join("·", gained), true));
                    else if (lost.Count > 0) effects.Add(new SvPlayImpact(BattleBounds(target), "失去" + string.Join("·", lost), false));
                }

            }
        }
        var ids = step.Action switch
        {
            PlayFollowerAction a => a.EnemyFollowerTargetInstanceIds ?? [],
            PlaySpellAction { Target: EnemyFollowerTarget a } => new[] { a.FollowerInstanceId },
            PlaySpellAction { Target: FollowerTarget a } => new[] { a.FollowerInstanceId },
            PlaySpellAction { Target: AmuletTarget a } => new[] { a.AmuletInstanceId },
            _ => (IReadOnlyList<int>)[]
        };
        foreach (var id in ids)
            for (var player = 0; player < 2; player++)
                if (FindVisualCard(player, id) is { } target && !effects.Any(e => e.Bounds == BattleBounds(target)))
                    effects.Add(new SvPlayImpact(BattleBounds(target), string.Empty, false));
        if (step.Action is PlaySpellAction { Target: EnemyLeaderTarget })
        {
            var bounds = BattleBounds(step.ActingPlayer == 0 ? _opponentLeaderLabel : _selfLeaderLabel);
            if (!effects.Any(e => e.Bounds == bounds)) effects.Add(new SvPlayImpact(bounds, string.Empty, false));
        }
        return effects;
    }
}

internal sealed record SvPlayImpact(Rectangle Bounds, string Text, bool Healing);

internal sealed class SvPlayAnimationSurface : Control
{
    internal const double ImpactAt = 0.64;
    private readonly Bitmap _before;
    private readonly Bitmap _card;
    private Bitmap? _after;
    private readonly Rectangle _from;
    private readonly Rectangle _to;
    private readonly IReadOnlyList<SvPlayImpact> _effects;
    private readonly Color _accent;
    private readonly bool _casting;
    private readonly bool _released;
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal double Fraction { get; set; }

    internal SvPlayAnimationSurface(Bitmap before, Bitmap card, Rectangle from, Rectangle to,
        IReadOnlyList<SvPlayImpact> effects, Color accent, bool casting, bool released = false)
    {
        _before = before; _card = card; _from = from; _to = to; _effects = effects; _accent = accent; _casting = casting; _released = released;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        SetStyle(ControlStyles.Selectable, false);
        TabStop = false;
        AccessibleName = "出牌动画";
    }

    internal void SetAfter(Bitmap after) { _after?.Dispose(); _after = after; }
    internal Bitmap CopyBefore() => new(_before);
    internal void RestrictToImpact(IReadOnlyList<Rectangle> exclusions)
    {
        var region = new Region(); region.MakeEmpty();
        var margin = (int)(30 * DeviceDpi / 96f);
        foreach (var bounds in _effects.Select(e => e.Bounds).Append(_to))
        {
            var expanded = bounds; expanded.Inflate(margin, margin); region.Union(expanded);
        }
        if (_casting)
        {
            var center = Center(_to);
            var width = (int)(_from.Width * 1.25) + margin * 2;
            var height = (int)(_from.Height * 1.25) + margin * 2;
            region.Union(new Rectangle((int)center.X - width / 2, (int)center.Y - height / 2, width, height));
        }
        foreach (var exclusion in exclusions) region.Exclude(exclusion);
        var previous = Region; Region = region; previous?.Dispose();
    }
    private static float Ease(double t) => (float)(1 - Math.Pow(1 - Math.Clamp(t, 0, 1), 3));
    private static PointF Center(Rectangle r) => new(r.Left + r.Width / 2f, r.Top + r.Height / 2f);

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.DrawImageUnscaled(Fraction >= ImpactAt && _after is not null ? _after : _before, Point.Empty);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        var p = Math.Clamp(Fraction, 0, 1);
        var settle = Math.Clamp((p - ImpactAt) / (1 - ImpactAt), 0, 1);
        // Replay / opponent: visibly lift and hold before releasing. Human drag resumes at its release point.
        var releaseAt = _released ? 0 : 0.30;
        var travel = Ease((p - releaseAt) / (ImpactAt - releaseAt));
        var start = Center(_from);
        var end = Center(_to);
        var lift = _released ? 0 : (float)(Math.Sin(Math.Min(p / 0.18, 1) * Math.PI / 2) * 40 * DeviceDpi / 96f);
        var direction = start.Y > end.Y ? -1 : 1;
        var center = new PointF(start.X + (end.X - start.X) * travel,
            start.Y + (end.Y - start.Y) * travel + direction * lift * (1 - travel));
        if (p < ImpactAt)
        {
            // Short luminous trail conveys the card's direction without covering text or the board.
            using var trail = new Pen(Color.FromArgb((int)(90 * Math.Sin(travel * Math.PI)), _accent), 5 * DeviceDpi / 96f);
            g.DrawLine(trail, new PointF(center.X + (start.X - center.X) * 0.18f, center.Y + (start.Y - center.Y) * 0.18f), center);
            var growth = 1f + (float)(0.09 * Math.Sin(p / ImpactAt * Math.PI));
            // Keep the readable portrait ratio in flight; the board renders its own compact token at impact.
            var width = _from.Width * growth;
            var height = _from.Height * growth;
            var r = new RectangleF(center.X - width / 2, center.Y - height / 2, width, height);
            using var shadow = new SolidBrush(Color.FromArgb(100, 0, 0, 0));
            var transform = g.Save();
            g.TranslateTransform(center.X, center.Y);
            var angle = _released ? -4 * (1 - travel) : direction * 5 * Math.Sin(Math.Min(p / 0.18, 1) * Math.PI / 2) * (1 - travel);
            g.RotateTransform((float)angle);
            var local = new RectangleF(-width / 2, -height / 2, width, height);
            g.FillRectangle(shadow, local.X + 5, local.Y + 8, local.Width, local.Height);
            g.DrawImage(_card, local);
            using var edge = new Pen(Color.FromArgb(180, _accent), 2);
            g.DrawRectangle(edge, local.X, local.Y, local.Width, local.Height);
            g.Restore(transform);
            if (p > 0.18)
                foreach (var impact in _effects)
                {
                    using var tip = new AdjustableArrowCap(4, 5, true);
                    using var link = new Pen(Color.FromArgb((int)(170 * Math.Clamp((p - 0.18) / 0.12, 0, 1)), _accent), 2)
                        { CustomEndCap = tip };
                    var target = Center(impact.Bounds);
                    g.DrawBezier(link, center, new PointF(center.X, center.Y + direction * 60),
                        new PointF(target.X, target.Y - direction * 40), target);
                }
        }
        else
        {
            if (_casting && settle < 0.6)
            {
                var growth = 1f + (float)(settle * 0.25);
                var size = new Size((int)(_from.Width * growth), (int)(_from.Height * growth));
                using var attributes = new ImageAttributes();
                var colors = new ColorMatrix { Matrix33 = (float)(1 - settle / 0.6) };
                attributes.SetColorMatrix(colors);
                g.DrawImage(_card, new Rectangle((int)end.X - size.Width / 2, (int)end.Y - size.Height / 2, size.Width, size.Height),
                    0, 0, _card.Width, _card.Height, GraphicsUnit.Pixel, attributes);
            }
            Pulse(g, _to, _accent, settle);
            foreach (var impact in _effects)
            {
                var color = impact.Healing ? Color.FromArgb(120, 246, 157) : Color.FromArgb(255, 129, 113);
                Pulse(g, impact.Bounds, color, settle);
                if (impact.Text.Length == 0) continue;
                var target = Center(impact.Bounds);
                var size = 20f * DeviceDpi / 96f * (1 + (float)(0.15 * (1 - settle)));
                using var font = new Font("Microsoft YaHei UI", size, FontStyle.Bold, GraphicsUnit.Pixel);
                using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                var point = new PointF(target.X, target.Y - (float)(24 * settle) * DeviceDpi / 96f);
                using var text = new SolidBrush(Color.FromArgb((int)(255 * Math.Min(1, (1 - settle) * 3)), color));
                using var outline = new SolidBrush(Color.FromArgb(text.Color.A, 10, 15, 24));
                g.DrawString(impact.Text, font, outline, new PointF(point.X + 1, point.Y + 2), format);
                g.DrawString(impact.Text, font, text, point, format);
            }
        }
    }

    private void Pulse(Graphics g, Rectangle bounds, Color color, double progress)
    {
        var grow = (float)((4 + 19 * progress) * DeviceDpi / 96f);
        var rectangle = new RectangleF(bounds.X - grow, bounds.Y - grow, bounds.Width + grow * 2, bounds.Height + grow * 2);
        using var pen = new Pen(Color.FromArgb((int)(180 * (1 - progress)), color), (float)(2 + 2 * (1 - progress)));
        g.DrawRectangle(pen, rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _before.Dispose(); _card.Dispose(); _after?.Dispose(); }
        base.Dispose(disposing);
    }
}
