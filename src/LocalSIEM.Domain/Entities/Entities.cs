using System.Net;
using LocalSIEM.Domain.Enums;

namespace LocalSIEM.Domain.Entities;

public sealed class Role
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
}

public sealed class User
{
    public Guid Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string PasswordHash { get; set; } = string.Empty;
    public Guid RoleId { get; set; }
    public Role Role { get; set; } = null!;
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? LastLoginAt { get; set; }
}

public sealed class EventSource
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public SourceKind Kind { get; set; }
    public byte[]? ApiKeyHash { get; set; }
    public bool IsEnabled { get; set; } = true;
    public DateTimeOffset? LastSeenAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class SecurityEvent
{
    public Guid Id { get; set; }
    public DateTimeOffset Timestamp { get; set; }
    public DateTimeOffset ReceivedAt { get; set; }
    public Guid SourceId { get; set; }
    public EventSource Source { get; set; } = null!;
    public string EventType { get; set; } = string.Empty;
    public Severity Severity { get; set; }
    public IPAddress? SourceIp { get; set; }
    public IPAddress? TargetIp { get; set; }
    public string? Username { get; set; }
    public string? Hostname { get; set; }
    public string? Message { get; set; }
    public string RawData { get; set; } = "{}";
    public Guid? CorrelationId { get; set; }
    public bool IsDemo { get; set; }
}

public sealed class DetectionRule
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public RuleType RuleType { get; set; }
    public string EventType { get; set; } = string.Empty;
    public int Threshold { get; set; }
    public int WindowSeconds { get; set; }
    public int? CooldownSeconds { get; set; }
    public string[] GroupBy { get; set; } = [];
    public string Parameters { get; set; } = "{}";
    public Severity Severity { get; set; }
    public bool IsEnabled { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public uint Version { get; set; }
}

public sealed class Alert
{
    public Guid Id { get; set; }
    public Guid RuleId { get; set; }
    public DetectionRule Rule { get; set; } = null!;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public Severity Severity { get; set; }
    public AlertStatus Status { get; set; }
    public string DedupKey { get; set; } = string.Empty;
    public string GroupValues { get; set; } = "{}";
    public DateTimeOffset FirstEventAt { get; set; }
    public DateTimeOffset LastEventAt { get; set; }
    public int EventCount { get; set; }
    public DateTimeOffset DetectedAt { get; set; }
    public Guid? StatusChangedBy { get; set; }
    public DateTimeOffset? StatusChangedAt { get; set; }
    public bool IsDemo { get; set; }
    public uint Version { get; set; }
    public IncidentAlert? IncidentLink { get; set; }
}

public sealed class AlertEvent
{
    public Guid AlertId { get; set; }
    public Guid EventId { get; set; }
}

public sealed class Incident
{
    public Guid Id { get; set; }
    public int Number { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public Severity Severity { get; set; }
    public IncidentStatus Status { get; set; }
    public Guid? AssigneeId { get; set; }
    public User? Assignee { get; set; }
    public Guid CreatedBy { get; set; }
    public User Creator { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }
    public bool IsDemo { get; set; }
    public uint Version { get; set; }
    public List<IncidentAlert> Alerts { get; set; } = [];
}

public sealed class IncidentAlert
{
    public Guid IncidentId { get; set; }
    public Incident Incident { get; set; } = null!;
    public Guid AlertId { get; set; }
    public Alert Alert { get; set; } = null!;
    public Guid LinkedBy { get; set; }
    public DateTimeOffset LinkedAt { get; set; }
}

public sealed class IncidentComment
{
    public Guid Id { get; set; }
    public Guid IncidentId { get; set; }
    public Guid AuthorId { get; set; }
    public User Author { get; set; } = null!;
    public string Body { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class IncidentHistoryEntry
{
    public Guid Id { get; set; }
    public Guid IncidentId { get; set; }
    public Guid? ActorId { get; set; }
    public User? Actor { get; set; }
    public IncidentAction Action { get; set; }
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class AuditLog
{
    public Guid Id { get; set; }
    public Guid? UserId { get; set; }
    public User? User { get; set; }
    public string Action { get; set; } = string.Empty;
    public string? EntityType { get; set; }
    public Guid? EntityId { get; set; }
    public string? Details { get; set; }
    public IPAddress? IpAddress { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class SystemSetting
{
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = "null";
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
}
