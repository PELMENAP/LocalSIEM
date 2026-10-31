using System.Threading.Channels;
using LocalSIEM.Application.Common;
using Microsoft.EntityFrameworkCore;

namespace LocalSIEM.Application.Detection;

public sealed class RuleCatalog
{
    private volatile bool dirty = true;
    private IReadOnlyList<RuleDefinition> rules = [];

    public IReadOnlyList<RuleDefinition> Current => rules;
    public bool IsDirty => dirty;

    public void MarkDirty() => dirty = true;

    public TimeSpan MaxWindow => rules.Count == 0 ? TimeSpan.Zero : rules.Max(r => r.Window);
    public TimeSpan MaxCooldown => rules.Count == 0 ? TimeSpan.Zero : rules.Max(r => r.Cooldown);

    public async Task<IReadOnlyList<Guid>> ReloadAsync(ISiemDbContext db, CancellationToken ct)
    {
        dirty = false;
        var entities = await db.DetectionRules.AsNoTracking().Where(r => r.IsEnabled).ToListAsync(ct);
        var fresh = entities.Select(RuleDefinition.FromEntity).ToList();
        var previous = rules.ToDictionary(r => r.Id);
        var changed = new List<Guid>();
        foreach (var rule in fresh)
        {
            if (!previous.TryGetValue(rule.Id, out var old) || old.UpdatedAt != rule.UpdatedAt)
            {
                changed.Add(rule.Id);
            }
            previous.Remove(rule.Id);
        }
        changed.AddRange(previous.Keys);
        rules = fresh;
        return changed;
    }
}

public interface IDetectionQueue
{
    ValueTask EnqueueAsync(DetectionEvent evt, CancellationToken ct);
    ChannelReader<DetectionEvent> Reader { get; }
    int Pending { get; }
}

public sealed class DetectionQueue : IDetectionQueue
{
    private readonly Channel<DetectionEvent> channel;

    public DetectionQueue(int capacity)
    {
        channel = Channel.CreateBounded<DetectionEvent>(new BoundedChannelOptions(capacity)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait
        });
    }

    public ChannelReader<DetectionEvent> Reader => channel.Reader;
    public int Pending => channel.Reader.Count;

    public ValueTask EnqueueAsync(DetectionEvent evt, CancellationToken ct) => channel.Writer.WriteAsync(evt, ct);
}

public sealed class DetectionControl
{
    private volatile bool resetRequested;

    public bool ResetRequested => resetRequested;

    public void RequestReset() => resetRequested = true;

    public void Acknowledge() => resetRequested = false;
}
