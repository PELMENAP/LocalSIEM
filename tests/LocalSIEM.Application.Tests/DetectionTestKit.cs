using LocalSIEM.Application.Detection;
using LocalSIEM.Domain;

namespace LocalSIEM.Application.Tests;

internal sealed class FakeAlertSink : IAlertSink
{
    public List<(Guid AlertId, DetectionResult Detection)> Created { get; } = [];
    public List<(Guid AlertId, DetectionResult Detection)> Appended { get; } = [];

    public IReadOnlyList<string> CreatedCodes => Created.Select(c => c.Detection.Rule.Code).ToList();

    public Task<AlertCreatedResult> CreateAsync(DetectionResult detection, int maxLinkedEvents, CancellationToken ct)
    {
        var id = Guid.NewGuid();
        Created.Add((id, detection));
        return Task.FromResult(new AlertCreatedResult(id, Math.Min(detection.EventIds.Length, maxLinkedEvents)));
    }

    public Task<int> AppendAsync(Episode episode, DetectionResult detection, int maxLinkedEvents, CancellationToken ct)
    {
        Appended.Add((episode.AlertId, detection));
        return Task.FromResult(1);
    }
}

internal sealed class DetectionKit
{
    public static readonly DateTimeOffset T0 = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    public DetectionKit(params string[] onlyCodes)
    {
        var all = DefaultRules.Create(T0).Select(RuleDefinition.FromEntity);
        Rules = onlyCodes.Length == 0 ? all.ToList() : all.Where(r => onlyCodes.Contains(r.Code)).ToList();
        Pipeline = new DetectionPipeline(DetectionEngine.CreateDefault());
    }

    public List<RuleDefinition> Rules { get; }
    public DetectionPipeline Pipeline { get; }
    public FakeAlertSink Sink { get; } = new();
    public int MaxLinkedEvents { get; set; } = 1000;

    public static DetectionEvent Event(string type, double seconds, string? ip = "10.0.0.5", string? user = "alice",
        params (string Key, string Value)[] attributes) => new()
    {
        Id = Guid.NewGuid(),
        Timestamp = T0.AddSeconds(seconds),
        EventType = type,
        SourceIp = ip,
        Username = user,
        Attributes = attributes.ToDictionary(a => a.Key, a => a.Value, StringComparer.OrdinalIgnoreCase)
    };

    public Task<int> Send(DetectionEvent evt) => Pipeline.ProcessAsync(evt, Rules, Sink, MaxLinkedEvents, CancellationToken.None);

    public Task<int> Send(string type, double seconds, string? ip = "10.0.0.5", string? user = "alice",
        params (string Key, string Value)[] attributes) => Send(Event(type, seconds, ip, user, attributes));

    public async Task Failed(int count, double start = 0, double step = 1, string? ip = "10.0.0.5", string? user = "alice")
    {
        for (var i = 0; i < count; i++)
        {
            await Send(EventTypes.LoginFailed, start + i * step, ip, user);
        }
    }
}
