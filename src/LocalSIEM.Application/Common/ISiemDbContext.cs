using LocalSIEM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace LocalSIEM.Application.Common;

public interface ISiemDbContext
{
    DbSet<Role> Roles { get; }
    DbSet<User> Users { get; }
    DbSet<EventSource> EventSources { get; }
    DbSet<SecurityEvent> SecurityEvents { get; }
    DbSet<DetectionRule> DetectionRules { get; }
    DbSet<Alert> Alerts { get; }
    DbSet<AlertEvent> AlertEvents { get; }
    DbSet<Incident> Incidents { get; }
    DbSet<IncidentAlert> IncidentAlerts { get; }
    DbSet<IncidentComment> IncidentComments { get; }
    DbSet<IncidentHistoryEntry> IncidentHistory { get; }
    DbSet<AuditLog> AuditLogs { get; }
    DbSet<SystemSetting> SystemSettings { get; }
    DatabaseFacade Database { get; }
    ChangeTracker ChangeTracker { get; }
    EntityEntry<TEntity> Entry<TEntity>(TEntity entity) where TEntity : class;
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
