using System.Net;
using System.Text.Json;
using LocalSIEM.Application.Common;
using LocalSIEM.Application.Detection;
using LocalSIEM.Domain;
using LocalSIEM.Domain.Entities;
using LocalSIEM.Domain.Enums;

namespace LocalSIEM.Application.Ingestion;

public sealed record NormalizedEvent(SecurityEvent Entity, DetectionEvent Detection);

public interface IEventNormalizer
{
    SourceKind Kind { get; }
    NormalizedEvent Normalize(IngestEventDto dto, EventSource source, DateTimeOffset receivedAt);
}

public static class SeverityPolicy
{
    public static Severity Default(string eventType, IReadOnlyDictionary<string, string> attributes) => eventType switch
    {
        EventTypes.LoginFailed => Severity.Low,
        EventTypes.RoleChanged => Severity.Medium,
        EventTypes.AccountCreated => Severity.Medium,
        EventTypes.PrivilegedLogon => Severity.Low,
        EventTypes.ResourceAccess when attributes.TryGetValue(AttributeNames.Protected, out var p) && p == "true" => Severity.Low,
        _ => Severity.Info
    };
}

public abstract class EventNormalizerBase : IEventNormalizer
{
    public abstract SourceKind Kind { get; }

    protected virtual bool IsDemo => false;

    protected virtual (string EventType, Severity? Severity) Classify(IngestEventDto dto, IReadOnlyDictionary<string, string> attributes) =>
        (dto.EventType!, null);

    public NormalizedEvent Normalize(IngestEventDto dto, EventSource source, DateTimeOffset receivedAt)
    {
        var attributes = FlattenAttributes(dto.Attributes);
        var (eventType, forcedSeverity) = Classify(dto, attributes);
        var severity = forcedSeverity
            ?? (dto.Severity is not null && Enum.TryParse<Severity>(dto.Severity, true, out var parsed)
                ? parsed
                : SeverityPolicy.Default(eventType, attributes));

        var entity = new SecurityEvent
        {
            Id = Ids.New(),
            Timestamp = dto.Timestamp!.Value.ToUniversalTime(),
            ReceivedAt = receivedAt,
            SourceId = source.Id,
            EventType = eventType,
            Severity = severity,
            SourceIp = ParseIp(dto.SourceIp),
            TargetIp = ParseIp(dto.TargetIp),
            Username = Clean(dto.Username),
            Hostname = Clean(dto.Hostname),
            Message = Clean(dto.Message),
            RawData = JsonSerializer.Serialize(dto, JsonDefaults.Options),
            CorrelationId = dto.CorrelationId ?? Ids.New(),
            IsDemo = IsDemo
        };
        return new NormalizedEvent(entity, DetectionEvent.FromEntity(entity, attributes));
    }

    private static IPAddress? ParseIp(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : IPAddress.TryParse(value, out var ip) ? ip : null;

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public static IReadOnlyDictionary<string, string> FlattenAttributes(Dictionary<string, JsonElement>? attributes)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (attributes is null)
        {
            return result;
        }
        foreach (var (key, element) in attributes)
        {
            var value = DetectionEvent.AttributeValue(element);
            if (value is not null)
            {
                result[key] = value;
            }
        }
        return result;
    }
}

public sealed class HttpApiNormalizer : EventNormalizerBase
{
    public override SourceKind Kind => SourceKind.HttpApi;
}

public sealed class SimulatorNormalizer : EventNormalizerBase
{
    public override SourceKind Kind => SourceKind.Simulator;
    protected override bool IsDemo => true;
}

public sealed class WindowsEventNormalizer : EventNormalizerBase
{
    public override SourceKind Kind => SourceKind.WindowsCollector;

    protected override (string EventType, Severity? Severity) Classify(IngestEventDto dto, IReadOnlyDictionary<string, string> attributes)
    {
        if (!string.Equals(dto.EventType, EventTypes.WindowsEvent, StringComparison.Ordinal) ||
            !attributes.TryGetValue(AttributeNames.WindowsEventId, out var id))
        {
            return (dto.EventType!, null);
        }
        return id switch
        {
            "4624" => (EventTypes.LoginSuccess, Severity.Info),
            "4625" => (EventTypes.LoginFailed, Severity.Low),
            "4672" => (EventTypes.PrivilegedLogon, Severity.Low),
            "4720" => (EventTypes.AccountCreated, Severity.Medium),
            _ => (EventTypes.Generic, Severity.Info)
        };
    }
}

public sealed class NormalizerRegistry(IEnumerable<IEventNormalizer> normalizers)
{
    private readonly Dictionary<SourceKind, IEventNormalizer> byKind = normalizers.ToDictionary(n => n.Kind);

    public IEventNormalizer Get(SourceKind kind) =>
        byKind.TryGetValue(kind, out var normalizer)
            ? normalizer
            : throw new InvalidOperationException($"Нет нормализатора для источника {kind}");
}
