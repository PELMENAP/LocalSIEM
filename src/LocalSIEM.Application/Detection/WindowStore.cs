namespace LocalSIEM.Application.Detection;

public readonly record struct WindowEntry(DateTimeOffset Timestamp, Guid EventId, string? Value, bool IsDemo);

public sealed class WindowState
{
    private readonly List<WindowEntry> entries = [];
    private Dictionary<string, int>? distinct;

    public IReadOnlyList<WindowEntry> Entries => entries;
    public DateTimeOffset Newest { get; private set; } = DateTimeOffset.MinValue;
    public int DistinctCount => distinct?.Count ?? 0;

    public void Insert(in WindowEntry entry)
    {
        var index = entries.Count;
        while (index > 0 && entries[index - 1].Timestamp > entry.Timestamp)
        {
            index--;
        }
        entries.Insert(index, entry);
        if (entry.Timestamp > Newest)
        {
            Newest = entry.Timestamp;
        }
        if (entry.Value is not null)
        {
            distinct ??= new Dictionary<string, int>(StringComparer.Ordinal);
            distinct[entry.Value] = distinct.GetValueOrDefault(entry.Value) + 1;
        }
    }

    public void EvictOlderThan(DateTimeOffset bound)
    {
        var count = 0;
        while (count < entries.Count && entries[count].Timestamp < bound)
        {
            var value = entries[count].Value;
            if (value is not null && distinct is not null)
            {
                var remaining = distinct[value] - 1;
                if (remaining == 0)
                {
                    distinct.Remove(value);
                }
                else
                {
                    distinct[value] = remaining;
                }
            }
            count++;
        }
        if (count > 0)
        {
            entries.RemoveRange(0, count);
        }
    }

    public int CountBetween(DateTimeOffset from, DateTimeOffset to)
    {
        var count = 0;
        foreach (var entry in entries)
        {
            if (entry.Timestamp >= from && entry.Timestamp <= to)
            {
                count++;
            }
        }
        return count;
    }
}

public sealed class WindowStore
{
    private readonly Dictionary<Guid, Dictionary<string, WindowState>> rules = [];

    public WindowState Get(Guid ruleId, string groupKey)
    {
        if (!rules.TryGetValue(ruleId, out var groups))
        {
            groups = new Dictionary<string, WindowState>(StringComparer.Ordinal);
            rules[ruleId] = groups;
        }
        if (!groups.TryGetValue(groupKey, out var state))
        {
            state = new WindowState();
            groups[groupKey] = state;
        }
        return state;
    }

    public int GroupCount => rules.Values.Sum(g => g.Count);

    public void ResetRule(Guid ruleId) => rules.Remove(ruleId);

    public void Clear() => rules.Clear();

    public void Sweep(IReadOnlyList<RuleDefinition> activeRules, DateTimeOffset now)
    {
        var windows = new Dictionary<Guid, TimeSpan>(activeRules.Count);
        foreach (var rule in activeRules)
        {
            windows[rule.Id] = rule.Window;
        }

        var stale = new List<string>();
        var removedRules = new List<Guid>();
        foreach (var (ruleId, groups) in rules)
        {
            if (!windows.TryGetValue(ruleId, out var window))
            {
                removedRules.Add(ruleId);
                continue;
            }
            stale.Clear();
            foreach (var (key, state) in groups)
            {
                if (state.Newest < now - window)
                {
                    stale.Add(key);
                }
            }
            foreach (var key in stale)
            {
                groups.Remove(key);
            }
        }
        foreach (var ruleId in removedRules)
        {
            rules.Remove(ruleId);
        }
    }
}
