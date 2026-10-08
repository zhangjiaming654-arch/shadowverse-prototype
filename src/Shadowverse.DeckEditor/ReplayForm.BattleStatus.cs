using Shadowverse.Engine.Game;
using Shadowverse.Engine.Models;

namespace Shadowverse.DeckEditor;

public sealed partial class ReplayForm
{
    private readonly SvCrestStrip _selfCrests = new("P1 纹章");
    private readonly SvCrestStrip _opponentCrests = new("P2 纹章");

    private void InspectPublicStatus(string detail)
    {
        if (_targetSelection is not null || _modeSelection is not null || IsMulliganPhase ||
            _fusionCardInstanceId is not null || _heldCard is not null) return;
        CloseTargetSelection();
        _inspectedCardDetail = detail;
        _inspectedCardReason = null;
        SetTargetLayout(true);
        _humanHint.Text = "纹章详情 · 点击关闭或按 Esc 返回战场。";
        RefreshActionButtons();
        if (_pendingActions is null) BuildInspectionChoices();
    }

    private void RefreshFollowerStatus(Panel slot, SvCardFace tile, FollowerInstance? follower)
    {
        var strip = slot.Controls.OfType<SvAbilityStrip>().FirstOrDefault();
        if (follower is null)
        {
            if (strip is not null) strip.SetAbilities([]);
            tile.SetAbilityFrame(false, false);
            return;
        }
        if (strip is null)
        {
            strip = new SvAbilityStrip { Cursor = Cursors.Help };
            strip.MouseUp += (_, e) => { if (e.Button == MouseButtons.Left) InspectCard(tile); };
            slot.Controls.Add(strip);
        }
        var abilities = SvFollowerAbilities.For(follower);
        strip.SetAbilities(abilities);
        tile.SetAbilityFrame(follower.HasWard && !follower.HasStealth && !follower.HasIntimidate, follower.HasAura, follower.IsSuperEvolved);
        var detail = tile.AccessibleDescription + "\n\n当前能力：" +
            (abilities.Count == 0 ? "无" : string.Join("、", abilities.Select(a => a.Name))) +
            "\n" + string.Join("\n", abilities.Select(a => a.Name + "：" + a.Detail));
        tile.AccessibleDescription = detail;
        strip.AccessibleDescription = detail;
        foreach (var control in new Control[] { tile, tile.NameLabel, strip }) _cardToolTip.SetToolTip(control, detail);
    }
}
