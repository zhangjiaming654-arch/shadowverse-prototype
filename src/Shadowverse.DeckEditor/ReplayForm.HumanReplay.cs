using System.Text.Json;
using Shadowverse.Engine.Game;
using Shadowverse.Engine.Simulation;

namespace Shadowverse.DeckEditor;

public sealed partial class ReplayForm
{
    private ReplayMatch? _liveReplayTemplate;
    private ReplayMatch? _currentReplay;
    private string? _humanReplaySavedPath;
    private readonly Button _humanReplayExportButton = new SvButton
    { Text = "导出本局 JSON", Dock = DockStyle.Top, Height = 30, Enabled = false };

    private static string AgentSummary(string name, int selectedRollouts) => name switch
    {
        HumanV1AgentName => $"{name}（{HumanV1Rollouts} 次推演）",
        "前瞻牌手 1.0（冻结）" or "前瞻牌手·旧冻结基线" => $"{name}（{FrozenV1Rollouts} 次推演）",
        "前瞻牌手 2.0（冻结）" => $"{name}（{FrozenV2Rollouts} 次推演）",
        "前瞻牌手 3.0（开发中）" => $"{name}（{selectedRollouts} 次推演）",
        _ => name
    };

    private void SetExportAvailability(bool available)
    {
        _exportButton.Enabled = available;
        _humanReplayExportButton.Enabled = available;
    }

    private ReplayMatch? CurrentExportMatch()
    {
        if (_liveReplayTemplate is { } template && ReferenceEquals(template.InitialState, _initialState))
        {
            MatchStep[] steps;
            lock (_liveGate) steps = _liveSteps?.ToArray() ?? [];
            var final = steps.LastOrDefault()?.AfterState ?? template.InitialState;
            return template with { Steps = steps, Winner = final.Winner ?? -1 };
        }
        return _currentReplay ?? _matchSelector.SelectedItem as ReplayMatch;
    }

    // Completion runs on the UI thread. Keep the final board visible and append this match to history.
    private void RegisterHumanReplay(ReplayMatch match)
    {
        _currentReplay = match;
        _liveReplayTemplate = null;
        _steps = match.Steps;
        _matches.Add(match);
        _isUpdatingMatchSelector = true;
        try
        {
            _matchSelector.Items.Add(match);
            _matchSelector.SelectedItem = match;
        }
        finally { _isUpdatingMatchSelector = false; }
        _matchSelector.Enabled = true;
        _newMatchButton.Enabled = true;
        SetExportAvailability(match.Steps.Count > 0);
        _batchExportButton.Enabled = _matches.Count > 0;
    }

    // Called on the match worker, after its final presentation has completed. No UI or dialogs here.
    private static (string? Path, string? Error) SaveHumanReplay(ReplayMatch match)
    {
        string? temporary = null;
        try
        {
            var folder = Path.Combine(Path.GetDirectoryName(Shadowverse.Engine.Decks.DeckCatalog.StoragePath)!,
                "..", "outputs", "human-replays");
            folder = Path.GetFullPath(folder);
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, $"human_{DateTime.Now:yyyyMMdd_HHmmss_fff}_{match.Seed}_{Guid.NewGuid():N}.json");
            temporary = path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(BuildMachineReplay(match), CompactJson));
            File.Move(temporary, path);
            return (path, null);
        }
        catch (Exception exception)
        {
            if (temporary is not null)
                try { File.Delete(temporary); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            return (null, exception.Message);
        }
    }
}
