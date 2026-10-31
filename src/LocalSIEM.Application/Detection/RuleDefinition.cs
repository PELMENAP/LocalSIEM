using System.Text.Json;
using LocalSIEM.Application.Common;
using LocalSIEM.Domain;
using LocalSIEM.Domain.Entities;
using LocalSIEM.Domain.Enums;

namespace LocalSIEM.Application.Detection;

public sealed class RuleParameters
{
    public string? DistinctField { get; set; }
    public string? ThenEventType { get; set; }
    public string? Field { get; set; }
    public string[]? Values { get; set; }

    public static RuleParameters Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new RuleParameters();
        }
        return JsonSerializer.Deserialize<RuleParameters>(json, JsonDefaults.Options) ?? new RuleParameters();
    }

    public string ToJson() => JsonSerializer.Serialize(this, JsonDefaults.Options);
}

public sealed class RuleDefinition
{
    public required Guid Id { get; init; }
    public required string Code { get; init; }
    public required string Name { get; init; }
    public string Description { get; init; } = string.Empty;
    public required RuleType Type { get; init; }
    public required string EventType { get; init; }
    public int Threshold { get; init; } = 1;
    public required TimeSpan Window { get; init; }
    public required TimeSpan Cooldown { get; init; }
    public required string[] GroupBy { get; init; }
    public required Severity Severity { get; init; }
    public string? DistinctField { get; init; }
    public string? ThenEventType { get; init; }
    public string? MatchField { get; init; }
    public IReadOnlySet<string>? MatchValues { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }

    public bool Accepts(string eventType) =>
        string.Equals(eventType, EventType, StringComparison.Ordinal) ||
        (Type == RuleType.Sequence && string.Equals(eventType, ThenEventType, StringComparison.Ordinal));

    public bool PassesFilter(DetectionEvent evt)
    {
        if (MatchField is null || MatchValues is null || !string.Equals(evt.EventType, EventType, StringComparison.Ordinal))
        {
            return true;
        }
        var value = evt.GetField(MatchField);
        return value is not null && MatchValues.Contains(value);
    }

    public static RuleDefinition FromEntity(DetectionRule rule)
    {
        var parameters = RuleParameters.Parse(rule.Parameters);
        var window = TimeSpan.FromSeconds(rule.WindowSeconds);
        return new RuleDefinition
        {
            Id = rule.Id,
            Code = rule.Code,
            Name = rule.Name,
            Description = rule.Description,
            Type = rule.RuleType,
            EventType = rule.EventType,
            Threshold = rule.Threshold,
            Window = window,
            Cooldown = rule.CooldownSeconds is { } cooldown ? TimeSpan.FromSeconds(cooldown) : window,
            GroupBy = rule.GroupBy,
            Severity = rule.Severity,
            DistinctField = parameters.DistinctField,
            ThenEventType = parameters.ThenEventType,
            MatchField = parameters.Field,
            MatchValues = parameters.Values is null
                ? null
                : new HashSet<string>(parameters.Values, StringComparer.OrdinalIgnoreCase),
            UpdatedAt = rule.UpdatedAt
        };
    }

    public static ValidationErrors Validate(RuleType type, string? eventType, int threshold, int windowSeconds,
        int? cooldownSeconds, string[]? groupBy, RuleParameters parameters)
    {
        var errors = new ValidationErrors();
        if (string.IsNullOrWhiteSpace(eventType) || eventType.Length > 64)
        {
            errors.Add("eventType", "Тип события обязателен (до 64 символов)");
        }
        if (threshold < 1 || threshold > 100_000)
        {
            errors.Add("threshold", "Порог должен быть от 1 до 100000");
        }
        if (windowSeconds < 1 || windowSeconds > 86_400)
        {
            errors.Add("windowSeconds", "Окно должно быть от 1 до 86400 секунд");
        }
        if (cooldownSeconds is < 0 or > 86_400)
        {
            errors.Add("cooldownSeconds", "Cooldown должен быть от 0 до 86400 секунд");
        }
        if (groupBy is null || groupBy.Length == 0)
        {
            errors.Add("groupBy", "Нужен хотя бы один признак группировки");
        }
        else
        {
            foreach (var field in groupBy)
            {
                if (!GroupFields.All.Contains(field))
                {
                    errors.Add("groupBy", $"Неизвестный признак группировки: {field}");
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(parameters.Field) && parameters.Values is not { Length: > 0 })
        {
            errors.Add("parameters.values", "Для фильтра по полю нужен непустой список values");
        }

        switch (type)
        {
            case RuleType.DistinctCount:
                if (string.IsNullOrWhiteSpace(parameters.DistinctField))
                {
                    errors.Add("parameters.distinctField", "Для DistinctCount нужно поле distinctField");
                }
                break;
            case RuleType.Sequence:
                if (string.IsNullOrWhiteSpace(parameters.ThenEventType))
                {
                    errors.Add("parameters.thenEventType", "Для Sequence нужен thenEventType");
                }
                else if (parameters.ThenEventType == eventType)
                {
                    errors.Add("parameters.thenEventType", "thenEventType должен отличаться от eventType");
                }
                break;
            case RuleType.SingleEvent:
                if (string.IsNullOrWhiteSpace(parameters.Field) || parameters.Values is not { Length: > 0 })
                {
                    errors.Add("parameters", "Для SingleEvent нужны field и непустой values");
                }
                break;
        }
        return errors;
    }
}
