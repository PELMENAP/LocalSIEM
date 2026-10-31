using LocalSIEM.Domain;
using LocalSIEM.Domain.Enums;

namespace LocalSIEM.Application.Detection;

public sealed class GroupContext
{
    public required string Key { get; init; }
    public required IReadOnlyDictionary<string, string> Values { get; init; }

    public string Describe() => string.Join(", ", Values.Select(v => $"{v.Key}={v.Value}"));

    public static GroupContext? TryBuild(string[] groupBy, DetectionEvent evt)
    {
        var values = new Dictionary<string, string>(groupBy.Length, StringComparer.Ordinal);
        var parts = new string[groupBy.Length];
        for (var i = 0; i < groupBy.Length; i++)
        {
            var field = groupBy[i];
            var value = evt.GetField(field);
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }
            values[field] = value;
            parts[i] = field + "=" + NormalizeValue(field, value);
        }
        return new GroupContext { Key = string.Join('|', parts), Values = values };
    }

    public static string NormalizeValue(string field, string value) =>
        field is GroupFields.SourceIp or GroupFields.TargetIp ? value : value.Trim().ToLowerInvariant();
}

public sealed class DetectionResult
{
    public required RuleDefinition Rule { get; init; }
    public required GroupContext Group { get; init; }
    public required Guid[] EventIds { get; init; }
    public required Guid TriggerEventId { get; init; }
    public required DateTimeOffset FirstEventAt { get; init; }
    public required DateTimeOffset LastEventAt { get; init; }
    public required string Reason { get; init; }
    public bool IsDemo { get; init; }

    public static DetectionResult FromEntries(RuleDefinition rule, GroupContext group, DetectionEvent trigger,
        IReadOnlyList<WindowEntry> entries, Func<WindowEntry, bool> include, string reason)
    {
        var ids = new List<Guid>(entries.Count + 1);
        var first = trigger.Timestamp;
        var last = trigger.Timestamp;
        var isDemo = trigger.IsDemo;
        var hasTrigger = false;
        foreach (var entry in entries)
        {
            if (!include(entry))
            {
                continue;
            }
            ids.Add(entry.EventId);
            hasTrigger |= entry.EventId == trigger.Id;
            isDemo |= entry.IsDemo;
            if (entry.Timestamp < first)
            {
                first = entry.Timestamp;
            }
            if (entry.Timestamp > last)
            {
                last = entry.Timestamp;
            }
        }
        if (!hasTrigger)
        {
            ids.Add(trigger.Id);
        }
        return new DetectionResult
        {
            Rule = rule,
            Group = group,
            EventIds = ids.ToArray(),
            TriggerEventId = trigger.Id,
            FirstEventAt = first,
            LastEventAt = last,
            Reason = reason,
            IsDemo = isDemo
        };
    }
}

public interface IRuleEvaluator
{
    RuleType Type { get; }
    bool IsStateful { get; }
    DetectionResult? Evaluate(RuleDefinition rule, DetectionEvent evt, GroupContext group, WindowState? state);
}

internal static class WindowMath
{
    public static bool IsLate(WindowState state, DateTimeOffset timestamp, TimeSpan window) =>
        state.Entries.Count > 0 && timestamp < state.Newest - window;

    public static string Seconds(TimeSpan window) => ((int)window.TotalSeconds).ToString();
}

public sealed class ThresholdEvaluator : IRuleEvaluator
{
    public RuleType Type => RuleType.Threshold;
    public bool IsStateful => true;

    public DetectionResult? Evaluate(RuleDefinition rule, DetectionEvent evt, GroupContext group, WindowState? state)
    {
        if (state is null || WindowMath.IsLate(state, evt.Timestamp, rule.Window))
        {
            return null;
        }
        state.Insert(new WindowEntry(evt.Timestamp, evt.Id, null, evt.IsDemo));
        state.EvictOlderThan(state.Newest - rule.Window);
        var count = state.Entries.Count;
        if (count < rule.Threshold)
        {
            return null;
        }
        var reason = $"{count} событий {rule.EventType} за {WindowMath.Seconds(rule.Window)} с (порог {rule.Threshold}); группа: {group.Describe()}";
        return DetectionResult.FromEntries(rule, group, evt, state.Entries, static _ => true, reason);
    }
}

public sealed class DistinctCountEvaluator : IRuleEvaluator
{
    public RuleType Type => RuleType.DistinctCount;
    public bool IsStateful => true;

    public DetectionResult? Evaluate(RuleDefinition rule, DetectionEvent evt, GroupContext group, WindowState? state)
    {
        if (state is null || rule.DistinctField is null || WindowMath.IsLate(state, evt.Timestamp, rule.Window))
        {
            return null;
        }
        var raw = evt.GetField(rule.DistinctField);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }
        var value = GroupContext.NormalizeValue(rule.DistinctField, raw);
        state.Insert(new WindowEntry(evt.Timestamp, evt.Id, value, evt.IsDemo));
        state.EvictOlderThan(state.Newest - rule.Window);
        var distinct = state.DistinctCount;
        if (distinct < rule.Threshold)
        {
            return null;
        }
        var reason = $"{distinct} различных значений {rule.DistinctField} в событиях {rule.EventType} за {WindowMath.Seconds(rule.Window)} с (порог {rule.Threshold}); группа: {group.Describe()}";
        return DetectionResult.FromEntries(rule, group, evt, state.Entries, static _ => true, reason);
    }
}

public sealed class SequenceEvaluator : IRuleEvaluator
{
    public RuleType Type => RuleType.Sequence;
    public bool IsStateful => true;

    public DetectionResult? Evaluate(RuleDefinition rule, DetectionEvent evt, GroupContext group, WindowState? state)
    {
        if (state is null)
        {
            return null;
        }

        if (string.Equals(evt.EventType, rule.EventType, StringComparison.Ordinal))
        {
            if (WindowMath.IsLate(state, evt.Timestamp, rule.Window))
            {
                return null;
            }
            state.Insert(new WindowEntry(evt.Timestamp, evt.Id, null, evt.IsDemo));
            state.EvictOlderThan(state.Newest - rule.Window);
            return null;
        }

        if (!string.Equals(evt.EventType, rule.ThenEventType, StringComparison.Ordinal) || state.Entries.Count == 0)
        {
            return null;
        }

        var from = evt.Timestamp - rule.Window;
        var to = evt.Timestamp;
        var count = state.CountBetween(from, to);
        if (count < rule.Threshold)
        {
            return null;
        }
        var reason = $"{count} событий {rule.EventType}, затем {rule.ThenEventType} в течение {WindowMath.Seconds(rule.Window)} с (порог {rule.Threshold}); группа: {group.Describe()}";
        return DetectionResult.FromEntries(rule, group, evt, state.Entries, e => e.Timestamp >= from && e.Timestamp <= to, reason);
    }
}

public sealed class SingleEventEvaluator : IRuleEvaluator
{
    private static readonly IReadOnlyList<WindowEntry> NoEntries = [];

    public RuleType Type => RuleType.SingleEvent;
    public bool IsStateful => false;

    public DetectionResult? Evaluate(RuleDefinition rule, DetectionEvent evt, GroupContext group, WindowState? state)
    {
        if (!string.Equals(evt.EventType, rule.EventType, StringComparison.Ordinal) ||
            rule.MatchField is null || rule.MatchValues is null)
        {
            return null;
        }
        var value = evt.GetField(rule.MatchField);
        if (value is null || !rule.MatchValues.Contains(value))
        {
            return null;
        }
        var reason = $"Событие {rule.EventType} с {rule.MatchField} = {value}; группа: {group.Describe()}";
        return DetectionResult.FromEntries(rule, group, evt, NoEntries, static _ => true, reason);
    }
}
