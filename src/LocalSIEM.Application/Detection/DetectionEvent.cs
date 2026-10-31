using System.Text.Json;
using LocalSIEM.Domain;
using LocalSIEM.Domain.Entities;

namespace LocalSIEM.Application.Detection;

public sealed class DetectionEvent
{
    private static readonly IReadOnlyDictionary<string, string> EmptyAttributes =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public required Guid Id { get; init; }
    public required DateTimeOffset Timestamp { get; init; }
    public required string EventType { get; init; }
    public string? SourceIp { get; init; }
    public string? TargetIp { get; init; }
    public string? Username { get; init; }
    public string? Hostname { get; init; }
    public IReadOnlyDictionary<string, string> Attributes { get; init; } = EmptyAttributes;
    public bool IsDemo { get; init; }

    public string? GetField(string name) => name switch
    {
        GroupFields.SourceIp => SourceIp,
        GroupFields.TargetIp => TargetIp,
        GroupFields.Username => Username,
        GroupFields.Hostname => Hostname,
        _ => Attributes.TryGetValue(name, out var value) ? value : null
    };

    public static DetectionEvent FromEntity(SecurityEvent entity, IReadOnlyDictionary<string, string> attributes) => new()
    {
        Id = entity.Id,
        Timestamp = entity.Timestamp,
        EventType = entity.EventType,
        SourceIp = entity.SourceIp?.ToString(),
        TargetIp = entity.TargetIp?.ToString(),
        Username = entity.Username,
        Hostname = entity.Hostname,
        Attributes = attributes,
        IsDemo = entity.IsDemo
    };

    public static DetectionEvent FromStored(SecurityEvent entity) => FromEntity(entity, ReadAttributes(entity.RawData));

    public static IReadOnlyDictionary<string, string> ReadAttributes(string rawData)
    {
        try
        {
            using var document = JsonDocument.Parse(rawData);
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty("attributes", out var attributes) ||
                attributes.ValueKind != JsonValueKind.Object)
            {
                return EmptyAttributes;
            }

            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in attributes.EnumerateObject())
            {
                var value = AttributeValue(property.Value);
                if (value is not null)
                {
                    result[property.Name] = value;
                }
            }
            return result;
        }
        catch (JsonException)
        {
            return EmptyAttributes;
        }
    }

    public static string? AttributeValue(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => element.GetString(),
        JsonValueKind.Number => element.GetRawText(),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        _ => null
    };
}
