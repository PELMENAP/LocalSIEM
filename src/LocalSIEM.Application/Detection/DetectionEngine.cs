namespace LocalSIEM.Application.Detection;

public sealed class Episode
{
    public required Guid AlertId { get; init; }
    public required Guid RuleId { get; init; }
    public required string GroupKey { get; init; }
    public DateTimeOffset LastEventAt { get; set; }
    public int LinkedCount { get; set; }
}

public sealed class SuppressionStore
{
    private readonly Lock gate = new();
    private readonly Dictionary<(Guid RuleId, string GroupKey), Episode> episodes = [];

    public int Count
    {
        get
        {
            lock (gate)
            {
                return episodes.Count;
            }
        }
    }

    public Episode? FindOpen(Guid ruleId, string groupKey, DateTimeOffset at, TimeSpan cooldown)
    {
        lock (gate)
        {
            if (!episodes.TryGetValue((ruleId, groupKey), out var episode))
            {
                return null;
            }
            if (at - episode.LastEventAt <= cooldown)
            {
                return episode;
            }
            episodes.Remove((ruleId, groupKey));
            return null;
        }
    }

    public void Open(Episode episode)
    {
        lock (gate)
        {
            episodes[(episode.RuleId, episode.GroupKey)] = episode;
        }
    }

    public void CloseByAlert(Guid alertId)
    {
        lock (gate)
        {
            foreach (var (key, episode) in episodes)
            {
                if (episode.AlertId == alertId)
                {
                    episodes.Remove(key);
                    return;
                }
            }
        }
    }

    public void Clear()
    {
        lock (gate)
        {
            episodes.Clear();
        }
    }
}

public sealed class DetectionEngine
{
    private readonly Dictionary<Domain.Enums.RuleType, IRuleEvaluator> evaluators;
    private readonly WindowStore windows = new();
    private readonly List<DetectionResult> scratch = [];

    public DetectionEngine(IEnumerable<IRuleEvaluator> evaluators, SuppressionStore suppression)
    {
        this.evaluators = evaluators.ToDictionary(e => e.Type);
        Suppression = suppression;
    }

    public SuppressionStore Suppression { get; }
    public int ActiveGroups => windows.GroupCount;

    public static DetectionEngine CreateDefault(SuppressionStore? suppression = null) => new(
        [new ThresholdEvaluator(), new DistinctCountEvaluator(), new SequenceEvaluator(), new SingleEventEvaluator()],
        suppression ?? new SuppressionStore());

    public void Evaluate(DetectionEvent evt, IReadOnlyList<RuleDefinition> rules, List<DetectionResult> output)
    {
        foreach (var rule in rules)
        {
            if (!rule.Accepts(evt.EventType) || !evaluators.TryGetValue(rule.Type, out var evaluator) || !rule.PassesFilter(evt))
            {
                continue;
            }
            var group = GroupContext.TryBuild(rule.GroupBy, evt);
            if (group is null)
            {
                continue;
            }
            var state = evaluator.IsStateful ? windows.Get(rule.Id, group.Key) : null;
            var detection = evaluator.Evaluate(rule, evt, group, state);
            if (detection is not null)
            {
                output.Add(detection);
            }
        }
    }

    public void Warm(DetectionEvent evt, IReadOnlyList<RuleDefinition> rules)
    {
        scratch.Clear();
        Evaluate(evt, rules, scratch);
        scratch.Clear();
    }

    public void ResetRule(Guid ruleId) => windows.ResetRule(ruleId);

    public void ResetAll()
    {
        windows.Clear();
        Suppression.Clear();
    }

    public void Sweep(IReadOnlyList<RuleDefinition> rules, DateTimeOffset now) => windows.Sweep(rules, now);
}

public sealed record AlertCreatedResult(Guid AlertId, int LinkedCount);

public interface IAlertSink
{
    Task<AlertCreatedResult> CreateAsync(DetectionResult detection, int maxLinkedEvents, CancellationToken ct);
    Task<int> AppendAsync(Episode episode, DetectionResult detection, int maxLinkedEvents, CancellationToken ct);
}

public sealed class DetectionPipeline(DetectionEngine engine)
{
    private readonly List<DetectionResult> detections = [];

    public DetectionEngine Engine => engine;

    public async Task<int> ProcessAsync(DetectionEvent evt, IReadOnlyList<RuleDefinition> rules, IAlertSink sink,
        int maxLinkedEvents, CancellationToken ct)
    {
        detections.Clear();
        engine.Evaluate(evt, rules, detections);
        var created = 0;
        foreach (var detection in detections)
        {
            var episode = engine.Suppression.FindOpen(detection.Rule.Id, detection.Group.Key, detection.LastEventAt, detection.Rule.Cooldown);
            if (episode is null)
            {
                var result = await sink.CreateAsync(detection, maxLinkedEvents, ct);
                engine.Suppression.Open(new Episode
                {
                    AlertId = result.AlertId,
                    RuleId = detection.Rule.Id,
                    GroupKey = detection.Group.Key,
                    LastEventAt = detection.LastEventAt,
                    LinkedCount = result.LinkedCount
                });
                created++;
            }
            else
            {
                var linked = await sink.AppendAsync(episode, detection, maxLinkedEvents, ct);
                episode.LinkedCount += linked;
                if (detection.LastEventAt > episode.LastEventAt)
                {
                    episode.LastEventAt = detection.LastEventAt;
                }
            }
        }
        detections.Clear();
        return created;
    }
}
