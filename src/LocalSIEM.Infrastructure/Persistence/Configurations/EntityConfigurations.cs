using LocalSIEM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LocalSIEM.Infrastructure.Persistence.Configurations;

internal sealed class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> b)
    {
        b.ToTable("roles");
        b.HasKey(x => x.Id);
        b.Property(x => x.Name).HasMaxLength(50).IsRequired();
        b.Property(x => x.Description).HasMaxLength(200);
        b.HasIndex(x => x.Name).IsUnique();
    }
}

internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> b)
    {
        b.ToTable("users");
        b.HasKey(x => x.Id);
        b.Property(x => x.Username).HasMaxLength(64).IsRequired();
        b.Property(x => x.DisplayName).HasMaxLength(128).IsRequired();
        b.Property(x => x.Email).HasMaxLength(256);
        b.Property(x => x.PasswordHash).IsRequired();
        b.HasIndex(x => x.Username).IsUnique();
        b.HasIndex(x => x.Email).IsUnique().HasFilter("email IS NOT NULL");
        b.HasOne(x => x.Role).WithMany().HasForeignKey(x => x.RoleId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class EventSourceConfiguration : IEntityTypeConfiguration<EventSource>
{
    public void Configure(EntityTypeBuilder<EventSource> b)
    {
        b.ToTable("event_sources", t => t.HasCheckConstraint("ck_event_sources_kind", "kind BETWEEN 0 AND 2"));
        b.HasKey(x => x.Id);
        b.Property(x => x.Name).HasMaxLength(100).IsRequired();
        b.HasIndex(x => x.Name).IsUnique();
        b.HasIndex(x => x.ApiKeyHash).IsUnique().HasFilter("api_key_hash IS NOT NULL");
    }
}

internal sealed class SecurityEventConfiguration : IEntityTypeConfiguration<SecurityEvent>
{
    public void Configure(EntityTypeBuilder<SecurityEvent> b)
    {
        b.ToTable("security_events", t => t.HasCheckConstraint("ck_security_events_severity", "severity BETWEEN 0 AND 4"));
        b.HasKey(x => x.Id);
        b.Property(x => x.EventType).HasMaxLength(64).IsRequired();
        b.Property(x => x.Username).HasMaxLength(256);
        b.Property(x => x.Hostname).HasMaxLength(256);
        b.Property(x => x.Message).HasMaxLength(2000);
        b.Property(x => x.RawData).HasColumnType("jsonb").IsRequired();
        b.HasOne(x => x.Source).WithMany().HasForeignKey(x => x.SourceId).OnDelete(DeleteBehavior.Restrict);

        b.HasIndex(x => x.Timestamp).IsDescending().HasDatabaseName("ix_events_ts");
        b.HasIndex(x => new { x.EventType, x.Timestamp }).IsDescending(false, true).HasDatabaseName("ix_events_type_ts");
        b.HasIndex(x => new { x.SourceId, x.Timestamp }).IsDescending(false, true).HasDatabaseName("ix_events_source_ts");
        b.HasIndex(x => new { x.Severity, x.Timestamp }).IsDescending(false, true).HasDatabaseName("ix_events_sev_ts");
        b.HasIndex(x => new { x.SourceIp, x.Timestamp }).IsDescending(false, true).HasFilter("source_ip IS NOT NULL").HasDatabaseName("ix_events_srcip_ts");
        b.HasIndex(x => new { x.Username, x.Timestamp }).IsDescending(false, true).HasFilter("username IS NOT NULL").HasDatabaseName("ix_events_user_ts");
        b.HasIndex(x => x.CorrelationId).HasFilter("correlation_id IS NOT NULL").HasDatabaseName("ix_events_corr");
        b.HasIndex(x => x.IsDemo).HasFilter("is_demo").HasDatabaseName("ix_events_demo");
        b.HasIndex(x => x.Message).HasMethod("gin").HasOperators("gin_trgm_ops").HasDatabaseName("ix_events_msg_trgm");
    }
}

internal sealed class DetectionRuleConfiguration : IEntityTypeConfiguration<DetectionRule>
{
    public void Configure(EntityTypeBuilder<DetectionRule> b)
    {
        b.ToTable("detection_rules", t =>
        {
            t.HasCheckConstraint("ck_rules_type", "rule_type BETWEEN 0 AND 3");
            t.HasCheckConstraint("ck_rules_threshold", "threshold >= 1");
            t.HasCheckConstraint("ck_rules_window", "window_seconds BETWEEN 1 AND 86400");
            t.HasCheckConstraint("ck_rules_cooldown", "cooldown_seconds IS NULL OR cooldown_seconds >= 0");
            t.HasCheckConstraint("ck_rules_severity", "severity BETWEEN 0 AND 4");
        });
        b.HasKey(x => x.Id);
        b.Property(x => x.Code).HasMaxLength(50).IsRequired();
        b.Property(x => x.Name).HasMaxLength(200).IsRequired();
        b.Property(x => x.Description).IsRequired();
        b.Property(x => x.EventType).HasMaxLength(64).IsRequired();
        b.Property(x => x.Parameters).HasColumnType("jsonb").IsRequired();
        b.Property(x => x.Version).IsRowVersion();
        b.HasIndex(x => x.Code).IsUnique();
    }
}

internal sealed class AlertConfiguration : IEntityTypeConfiguration<Alert>
{
    public void Configure(EntityTypeBuilder<Alert> b)
    {
        b.ToTable("alerts", t =>
        {
            t.HasCheckConstraint("ck_alerts_severity", "severity BETWEEN 0 AND 4");
            t.HasCheckConstraint("ck_alerts_status", "status BETWEEN 0 AND 2");
            t.HasCheckConstraint("ck_alerts_event_count", "event_count >= 1");
        });
        b.HasKey(x => x.Id);
        b.Property(x => x.Title).HasMaxLength(200).IsRequired();
        b.Property(x => x.Description).IsRequired();
        b.Property(x => x.DedupKey).HasMaxLength(300).IsRequired();
        b.Property(x => x.GroupValues).HasColumnType("jsonb").IsRequired();
        b.Property(x => x.Version).IsRowVersion();
        b.HasOne(x => x.Rule).WithMany().HasForeignKey(x => x.RuleId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.StatusChangedBy).OnDelete(DeleteBehavior.SetNull);
        b.HasIndex(x => x.DetectedAt).IsDescending().HasDatabaseName("ix_alerts_detected");
        b.HasIndex(x => new { x.Status, x.DetectedAt }).IsDescending(false, true).HasDatabaseName("ix_alerts_status_detected");
        b.HasIndex(x => new { x.RuleId, x.DetectedAt }).IsDescending(false, true).HasDatabaseName("ix_alerts_rule_detected");
        b.HasIndex(x => new { x.DedupKey, x.LastEventAt }).IsDescending(false, true).HasFilter("status < 2").HasDatabaseName("ix_alerts_dedup_open");
        b.HasIndex(x => x.IsDemo).HasFilter("is_demo").HasDatabaseName("ix_alerts_demo");
    }
}

internal sealed class AlertEventConfiguration : IEntityTypeConfiguration<AlertEvent>
{
    public void Configure(EntityTypeBuilder<AlertEvent> b)
    {
        b.ToTable("alert_events");
        b.HasKey(x => new { x.AlertId, x.EventId });
        b.HasOne<Alert>().WithMany().HasForeignKey(x => x.AlertId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<SecurityEvent>().WithMany().HasForeignKey(x => x.EventId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(x => x.EventId).HasDatabaseName("ix_alert_events_event");
    }
}

internal sealed class IncidentConfiguration : IEntityTypeConfiguration<Incident>
{
    public void Configure(EntityTypeBuilder<Incident> b)
    {
        b.ToTable("incidents", t =>
        {
            t.HasCheckConstraint("ck_incidents_severity", "severity BETWEEN 0 AND 4");
            t.HasCheckConstraint("ck_incidents_status", "status BETWEEN 0 AND 3");
        });
        b.HasKey(x => x.Id);
        b.Property(x => x.Number).UseIdentityAlwaysColumn();
        b.Property(x => x.Title).HasMaxLength(200).IsRequired();
        b.Property(x => x.Description).IsRequired();
        b.Property(x => x.Version).IsRowVersion();
        b.HasOne(x => x.Assignee).WithMany().HasForeignKey(x => x.AssigneeId).OnDelete(DeleteBehavior.SetNull);
        b.HasOne(x => x.Creator).WithMany().HasForeignKey(x => x.CreatedBy).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => x.Number).IsUnique();
        b.HasIndex(x => new { x.Status, x.CreatedAt }).IsDescending(false, true).HasDatabaseName("ix_incidents_status_created");
        b.HasIndex(x => x.AssigneeId).HasFilter("assignee_id IS NOT NULL").HasDatabaseName("ix_incidents_assignee");
    }
}

internal sealed class IncidentAlertConfiguration : IEntityTypeConfiguration<IncidentAlert>
{
    public void Configure(EntityTypeBuilder<IncidentAlert> b)
    {
        b.ToTable("incident_alerts");
        b.HasKey(x => new { x.IncidentId, x.AlertId });
        b.HasOne(x => x.Incident).WithMany(x => x.Alerts).HasForeignKey(x => x.IncidentId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Alert).WithOne(x => x.IncidentLink).HasForeignKey<IncidentAlert>(x => x.AlertId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.LinkedBy).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => x.AlertId).IsUnique().HasDatabaseName("ux_incident_alerts_alert_id");
    }
}

internal sealed class IncidentCommentConfiguration : IEntityTypeConfiguration<IncidentComment>
{
    public void Configure(EntityTypeBuilder<IncidentComment> b)
    {
        b.ToTable("incident_comments", t => t.HasCheckConstraint("ck_comments_body", "char_length(body) BETWEEN 1 AND 5000"));
        b.HasKey(x => x.Id);
        b.Property(x => x.Body).IsRequired();
        b.HasOne<Incident>().WithMany().HasForeignKey(x => x.IncidentId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Author).WithMany().HasForeignKey(x => x.AuthorId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.IncidentId, x.CreatedAt }).HasDatabaseName("ix_comments_incident");
    }
}

internal sealed class IncidentHistoryConfiguration : IEntityTypeConfiguration<IncidentHistoryEntry>
{
    public void Configure(EntityTypeBuilder<IncidentHistoryEntry> b)
    {
        b.ToTable("incident_history");
        b.HasKey(x => x.Id);
        b.Property(x => x.OldValue).HasMaxLength(200);
        b.Property(x => x.NewValue).HasMaxLength(200);
        b.HasOne<Incident>().WithMany().HasForeignKey(x => x.IncidentId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Actor).WithMany().HasForeignKey(x => x.ActorId).OnDelete(DeleteBehavior.SetNull);
        b.HasIndex(x => new { x.IncidentId, x.CreatedAt }).HasDatabaseName("ix_history_incident");
    }
}

internal sealed class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> b)
    {
        b.ToTable("audit_logs");
        b.HasKey(x => x.Id);
        b.Property(x => x.Action).HasMaxLength(100).IsRequired();
        b.Property(x => x.EntityType).HasMaxLength(100);
        b.Property(x => x.Details).HasColumnType("jsonb");
        b.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.SetNull);
        b.HasIndex(x => x.CreatedAt).IsDescending().HasDatabaseName("ix_audit_created");
        b.HasIndex(x => new { x.UserId, x.CreatedAt }).IsDescending(false, true).HasDatabaseName("ix_audit_user_created");
    }
}

internal sealed class SystemSettingConfiguration : IEntityTypeConfiguration<SystemSetting>
{
    public void Configure(EntityTypeBuilder<SystemSetting> b)
    {
        b.ToTable("system_settings");
        b.HasKey(x => x.Key);
        b.Property(x => x.Key).HasMaxLength(100);
        b.Property(x => x.Value).HasColumnType("jsonb").IsRequired();
        b.HasOne<User>().WithMany().HasForeignKey(x => x.UpdatedBy).OnDelete(DeleteBehavior.SetNull);
    }
}
