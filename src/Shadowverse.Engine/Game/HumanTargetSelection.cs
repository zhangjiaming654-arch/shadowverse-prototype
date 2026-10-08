namespace Shadowverse.Engine.Game;

public enum HumanTargetZone { OwnHand, OwnBoard, EnemyBoard, EnemyLeader }
public sealed record HumanTarget(HumanTargetZone Zone, int? InstanceId = null);
public enum HumanTargetRole { Effect, ReturnHand, DiscardHand, EvolveAlly }

/// <summary>Guided selection over engine-provided actions; it never creates or judges an action.</summary>
public sealed class HumanTargetSelection
{
    private IReadOnlyList<GameAction> _candidates;
    private readonly HumanTargetRole[] _roles;
    private readonly GameObservation _observation;
    private readonly HashSet<HumanTarget> _selected = [];
    private readonly List<HumanTarget> _completed = [];
    private int _stage;
    private readonly Stack<(IReadOnlyList<GameAction> Candidates, HumanTarget[] Selected, HumanTarget[] Completed, int Stage)> _history = [];

    public HumanTargetSelection(GameObservation observation, IReadOnlyList<GameAction> candidates)
    {
        if (candidates.Count == 0) throw new ArgumentException("Selection needs legal candidates.", nameof(candidates));
        _observation = observation;
        _candidates = candidates.ToArray();
        _roles = Enum.GetValues<HumanTargetRole>().Where(role => candidates.Any(a => Targets(observation, a, role).Count > 0)).ToArray();
    }

    public IReadOnlyList<GameAction> Candidates => _candidates;
    public bool IsComplete => _stage >= _roles.Length;
    public bool IsLastStage => _stage == _roles.Length - 1;
    public int StageIndex => _stage;
    public int StageCount => _roles.Length;
    public bool CanGoBack => _history.Count > 0;
    public HumanTargetRole? CurrentRole => IsComplete ? null : _roles[_stage];
    public IReadOnlyCollection<HumanTarget> Selected => _selected;
    public IReadOnlyList<HumanTarget> Completed => _completed;
    public IReadOnlyList<HumanTarget> Available => IsComplete ? [] :
        (RequiredCounts.Count == 1 && RequiredCounts[0] == 1 ? _candidates : Compatible())
        .SelectMany(a => Targets(_observation, a, _roles[_stage])).Distinct().ToArray();
    public IReadOnlyList<int> RequiredCounts => IsComplete ? [] : _candidates
        .Select(a => Targets(_observation, a, _roles[_stage]).Count).Distinct().Order().ToArray();
    public bool CanAdvance => !IsComplete && _candidates.Any(a => Exact(a));

    public bool Toggle(HumanTarget target)
    {
        if (IsComplete) return false;
        if (_selected.Remove(target)) return true;
        if (RequiredCounts.Count == 1 && RequiredCounts[0] == 1 &&
            _candidates.Any(a => Targets(_observation, a, _roles[_stage]).Contains(target)))
        {
            _selected.Clear();
            _selected.Add(target);
            return true;
        }
        if (!Available.Contains(target)) return false;
        _selected.Add(target);
        return true;
    }

    public bool Advance()
    {
        if (!CanAdvance) return false;
        _history.Push((_candidates, _selected.ToArray(), _completed.ToArray(), _stage));
        _candidates = _candidates.Where(Exact).ToArray();
        _completed.AddRange(_selected);
        _selected.Clear();
        _stage++;
        return true;
    }

    public bool Back()
    {
        if (!_history.TryPop(out var previous)) return false;
        _candidates = previous.Candidates;
        _selected.Clear();
        _selected.UnionWith(previous.Selected);
        _completed.Clear();
        _completed.AddRange(previous.Completed);
        _stage = previous.Stage;
        return true;
    }

    private IEnumerable<GameAction> Compatible() => _candidates.Where(a =>
        _selected.All(target => Targets(_observation, a, _roles[_stage]).Contains(target)));
    private bool Exact(GameAction action) => _selected.SetEquals(Targets(_observation, action, _roles[_stage]));

    public static bool HasTargets(GameObservation observation, GameAction action) =>
        Enum.GetValues<HumanTargetRole>().Any(role => Targets(observation, action, role).Count > 0);

    public static string FamilyKey(GameAction action) => $"{action.GetType().Name}:{HumanActionResolver.ModeIndexOf(action)}";

    public static IReadOnlyList<HumanTarget> Targets(GameObservation observation, GameAction action, HumanTargetRole role)
    {
        HumanTarget Board(int id) => new(observation.Self.Board.Any(f => f.InstanceId == id) ||
            (observation.Self.Amulets?.Any(a => a.InstanceId == id) ?? false) ? HumanTargetZone.OwnBoard : HumanTargetZone.EnemyBoard, id);
        if (role == HumanTargetRole.Effect)
        {
            return action switch
            {
                PlaySpellAction { Target: EnemyLeaderTarget } => [new(HumanTargetZone.EnemyLeader)],
                PlaySpellAction { Target: EnemyFollowerTarget t } => [new(HumanTargetZone.EnemyBoard, t.FollowerInstanceId)],
                PlaySpellAction { Target: FollowerTarget t } => [Board(t.FollowerInstanceId)],
                PlaySpellAction { Target: AmuletTarget t } => [Board(t.AmuletInstanceId)],
                PlayFollowerAction { EnemyFollowerTargetInstanceIds: { } ids } => ids.Select(id => new HumanTarget(HumanTargetZone.EnemyBoard, id)).ToArray(),
                EvolveAction { EnemyFollowerTargetInstanceId: int id } => [Board(id)],
                SuperEvolveAction { EnemyFollowerTargetInstanceId: int id } => [Board(id)],
                _ => []
            };
        }
        if (role == HumanTargetRole.ReturnHand && action is PlayFollowerAction { HandCardTargetInstanceId: int handId })
            return [new(HumanTargetZone.OwnHand, handId)];
        if (role == HumanTargetRole.EvolveAlly && action is SuperEvolveAction { OtherFollowerTargetInstanceId: int allyId })
            return [new(HumanTargetZone.OwnBoard, allyId)];
        if (role == HumanTargetRole.DiscardHand)
        {
            var ids = action switch
            {
                PlayFollowerAction a => a.OwnHandCardTargetInstanceIds,
                PlaySpellAction a => a.OwnHandCardTargetInstanceIds,
                EvolveAction a => a.OwnHandCardTargetInstanceIds,
                SuperEvolveAction a => a.OwnHandCardTargetInstanceIds,
                _ => null
            };
            return ids?.Select(id => new HumanTarget(HumanTargetZone.OwnHand, id)).ToArray() ?? [];
        }
        return [];
    }
}
