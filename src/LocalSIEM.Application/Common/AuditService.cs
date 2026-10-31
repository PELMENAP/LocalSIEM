using System.Text.Json;
using LocalSIEM.Domain.Entities;

namespace LocalSIEM.Application.Common;

public interface IAuditService
{
    void Record(string action, string? entityType = null, Guid? entityId = null, object? details = null);
    void RecordFor(Guid? userId, string action, string? entityType = null, Guid? entityId = null, object? details = null);
}

public sealed class AuditService(ISiemDbContext db, ICurrentUser currentUser, TimeProvider clock) : IAuditService
{
    public void Record(string action, string? entityType = null, Guid? entityId = null, object? details = null) =>
        RecordFor(currentUser.UserId, action, entityType, entityId, details);

    public void RecordFor(Guid? userId, string action, string? entityType = null, Guid? entityId = null, object? details = null)
    {
        db.AuditLogs.Add(new AuditLog
        {
            Id = Ids.New(),
            UserId = userId,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            Details = details is null ? null : JsonSerializer.Serialize(details, JsonDefaults.Options),
            IpAddress = currentUser.IpAddress,
            CreatedAt = clock.GetUtcNow()
        });
    }
}

public static class AuditActions
{
    public const string LoginSucceeded = "auth.login";
    public const string LoginFailed = "auth.login_failed";
    public const string UserCreated = "user.created";
    public const string UserUpdated = "user.updated";
    public const string UserActivation = "user.activation";
    public const string UserPasswordReset = "user.password_reset";
    public const string SourceCreated = "source.created";
    public const string SourceUpdated = "source.updated";
    public const string SourceKeyRotated = "source.key_rotated";
    public const string RuleCreated = "rule.created";
    public const string RuleUpdated = "rule.updated";
    public const string RuleToggled = "rule.toggled";
    public const string AlertStatusChanged = "alert.status";
    public const string IncidentCreated = "incident.created";
    public const string IncidentStatusChanged = "incident.status";
    public const string IncidentAssigned = "incident.assigned";
    public const string IncidentAlertsLinked = "incident.alerts_linked";
    public const string IncidentCommented = "incident.comment";
    public const string SettingsUpdated = "settings.updated";
    public const string SimulatorStarted = "simulator.started";
    public const string SimulatorStopped = "simulator.stopped";
    public const string DemoDataPurged = "demo.purged";
}
