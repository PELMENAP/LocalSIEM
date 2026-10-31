using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using LocalSIEM.Domain.Entities;
using LocalSIEM.Domain.Enums;

namespace LocalSIEM.Application.Common;

public interface ICurrentUser
{
    Guid? UserId { get; }
    string? Username { get; }
    bool IsAdministrator { get; }
    IPAddress? IpAddress { get; }
}

public sealed record AlertNotification(Guid Id, string Title, Severity Severity, string RuleCode, DateTimeOffset DetectedAt);

public sealed record AlertUpdatedNotification(Guid Id, AlertStatus Status, int EventCount);

public sealed record IncidentNotification(Guid Id, int Number, string Title, Severity Severity);

public sealed record IncidentUpdatedNotification(Guid Id, IncidentStatus Status, Guid? AssigneeId);

public sealed record SourceStatusNotification(Guid Id, string Name, SourceStatus Status);

public sealed record SimulatorStateNotification(Guid? RunId, string State, string? Scenario, int Sent, int Total, string? Error);

public interface INotificationPublisher
{
    void EventsIngested(IReadOnlyList<SecurityEvent> events);
    Task AlertCreatedAsync(AlertNotification notification);
    Task AlertUpdatedAsync(AlertUpdatedNotification notification);
    Task IncidentCreatedAsync(IncidentNotification notification);
    Task IncidentUpdatedAsync(IncidentUpdatedNotification notification);
    Task SourceStatusChangedAsync(SourceStatusNotification notification);
    Task SimulatorStateChangedAsync(SimulatorStateNotification notification);
}

public interface IPasswordHasher
{
    string Hash(string password);
    bool Verify(string hash, string password);
}

public interface ITokenService
{
    (string Token, DateTimeOffset ExpiresAt) CreateToken(User user, string roleName);
}

public static class JsonDefaults
{
    public static readonly JsonSerializerOptions Options = Create();

    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        Configure(options);
        return options;
    }

    public static void Configure(JsonSerializerOptions options)
    {
        options.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        options.Converters.Add(new JsonStringEnumConverter());
        options.Converters.Add(new IpAddressJsonConverter());
    }
}

public sealed class IpAddressJsonConverter : JsonConverter<IPAddress>
{
    public override IPAddress? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var value = reader.GetString();
        return value is null ? null : IPAddress.TryParse(value, out var ip) ? ip : throw new JsonException("Некорректный IP-адрес");
    }

    public override void Write(Utf8JsonWriter writer, IPAddress value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString());
}

public static class Ids
{
    public static Guid New() => Guid.CreateVersion7();
}
