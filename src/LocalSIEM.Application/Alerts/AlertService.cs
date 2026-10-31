using System.Collections.Concurrent;
using System.Text.Json;
using LocalSIEM.Application.Common;
using LocalSIEM.Application.Detection;
using LocalSIEM.Application.Events;
using LocalSIEM.Domain;
using LocalSIEM.Domain.Entities;
using LocalSIEM.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace LocalSIEM.Application.Alerts;

public sealed class AlertQuery : PageQuery
{
    public AlertStatus[]? Status { get; set; }
    public Severity[]? Severity { get; set; }
    public Guid? RuleId { get; set; }
    private DateTimeOffset? from;
    private DateTimeOffset? to;

    public DateTimeOffset? From { get => from; set => from = value?.ToUniversalTime(); }
    public DateTimeOffset? To { get => to; set => to = value?.ToUniversalTime(); }
    public string? Q { get; set; }
    public string? Sort { get; set; }
}

public sealed record AlertListItemDto(
    Guid Id, string Title, Severity Severity, AlertStatus Status, Guid RuleId, string RuleCode, string RuleName,
    DateTimeOffset DetectedAt, DateTimeOffset FirstEventAt, DateTimeOffset LastEventAt, int EventCount,
    Guid? IncidentId, int? IncidentNumber, uint Version);

public sealed record AlertDetailsDto(
    Guid Id, string Title, string Description, Severity Severity, AlertStatus Status,
    Guid RuleId, string RuleCode, string RuleName, RuleType RuleType, string RuleEventType, int Threshold, int WindowSeconds,
    IReadOnlyDictionary<string, string> GroupValues, DateTimeOffset DetectedAt, DateTimeOffset FirstEventAt,
    DateTimeOffset LastEventAt, int EventCount, int LinkedEventCount, DateTimeOffset? StatusChangedAt, string? StatusChangedBy,
    Guid? IncidentId, int? IncidentNumber, bool IsDemo, uint Version);

public sealed record ChangeAlertStatusDto(AlertStatus Status, uint Version);

public sealed class AlertUpdateThrottle
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(5);
    private readonly ConcurrentDictionary<Guid, DateTimeOffset> last = new();

    public bool ShouldNotify(Guid alertId, DateTimeOffset now)
    {
        if (last.TryGetValue(alertId, out var previous) && now - previous < Interval)
        {
            return false;
        }
        last[alertId] = now;
        if (last.Count > 10_000)
        {
            last.Clear();
        }
        return true;
    }
}

public interface IAlertService : IAlertSink
{
    Task<PagedResult<AlertListItemDto>> ListAsync(AlertQuery query, CancellationToken ct);
    Task<AlertDetailsDto> GetAsync(Guid id, CancellationToken ct);
    Task<PagedResult<EventListItemDto>> GetEventsAsync(Guid id, int page, int pageSize, CancellationToken ct);
    Task<AlertDetailsDto> ChangeStatusAsync(Guid id, ChangeAlertStatusDto dto, CancellationToken ct);
}

public sealed class AlertService(
    ISiemDbContext db,
    INotificationPublisher notifications,
    IAuditService audit,
    ICurrentUser currentUser,
    SuppressionStore suppression,
    AlertUpdateThrottle throttle,
    TimeProvider clock) : IAlertService
{
    public async Task<AlertCreatedResult> CreateAsync(DetectionResult detection, int maxLinkedEvents, CancellationToken ct)
    {
        var rule = detection.Rule;
        var dedupKey = $"{rule.Code}:{detection.Group.Key}";
        if (dedupKey.Length > 300)
        {
            dedupKey = dedupKey[..300];
        }
        var alert = new Alert
        {
            Id = Ids.New(),
            RuleId = rule.Id,
            Title = Truncate($"{rule.Name}: {detection.Group.Describe()}", 200),
            Description = string.IsNullOrWhiteSpace(rule.Description) ? detection.Reason : $"{rule.Description}\n{detection.Reason}",
            Severity = rule.Severity,
            Status = AlertStatus.New,
            DedupKey = dedupKey,
            GroupValues = JsonSerializer.Serialize(detection.Group.Values, JsonDefaults.Options),
            FirstEventAt = detection.FirstEventAt,
            LastEventAt = detection.LastEventAt,
            EventCount = detection.EventIds.Length,
            DetectedAt = clock.GetUtcNow(),
            IsDemo = detection.IsDemo
        };
        var linked = Math.Min(detection.EventIds.Length, maxLinkedEvents);
        db.Alerts.Add(alert);
        for (var i = 0; i < linked; i++)
        {
            db.AlertEvents.Add(new AlertEvent { AlertId = alert.Id, EventId = detection.EventIds[i] });
        }
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();

        await notifications.AlertCreatedAsync(new AlertNotification(alert.Id, alert.Title, alert.Severity, rule.Code, alert.DetectedAt));
        return new AlertCreatedResult(alert.Id, linked);
    }

    public async Task<int> AppendAsync(Episode episode, DetectionResult detection, int maxLinkedEvents, CancellationToken ct)
    {
        var room = Math.Max(0, maxLinkedEvents - episode.LinkedCount);
        var inserted = 0;
        if (room > 0)
        {
            var ids = detection.EventIds;
            var alertId = episode.AlertId;
            inserted = await db.Database.ExecuteSqlAsync(
                $"""
                INSERT INTO alert_events (alert_id, event_id)
                SELECT {alertId}, x.id FROM unnest({ids}) AS x(id)
                WHERE NOT EXISTS (SELECT 1 FROM alert_events ae WHERE ae.alert_id = {alertId} AND ae.event_id = x.id)
                LIMIT {room}
                ON CONFLICT DO NOTHING
                """, ct);
        }
        var added = room > 0 ? inserted : 1;
        var isDemo = detection.IsDemo;
        var lastEventAt = detection.LastEventAt;
        await db.Alerts.Where(a => a.Id == episode.AlertId).ExecuteUpdateAsync(s => s
            .SetProperty(a => a.EventCount, a => a.EventCount + added)
            .SetProperty(a => a.LastEventAt, a => a.LastEventAt > lastEventAt ? a.LastEventAt : lastEventAt)
            .SetProperty(a => a.IsDemo, a => a.IsDemo || isDemo), ct);

        if (added > 0 && throttle.ShouldNotify(episode.AlertId, clock.GetUtcNow()))
        {
            var state = await db.Alerts.AsNoTracking().Where(a => a.Id == episode.AlertId)
                .Select(a => new { a.Status, a.EventCount }).FirstOrDefaultAsync(ct);
            if (state is not null)
            {
                await notifications.AlertUpdatedAsync(new AlertUpdatedNotification(episode.AlertId, state.Status, state.EventCount));
            }
        }
        return inserted;
    }

    public async Task<PagedResult<AlertListItemDto>> ListAsync(AlertQuery query, CancellationToken ct)
    {
        var alerts = db.Alerts.AsNoTracking();
        if (query.Status is { Length: > 0 })
        {
            alerts = alerts.Where(a => query.Status.Contains(a.Status));
        }
        if (query.Severity is { Length: > 0 })
        {
            alerts = alerts.Where(a => query.Severity.Contains(a.Severity));
        }
        if (query.RuleId is { } ruleId)
        {
            alerts = alerts.Where(a => a.RuleId == ruleId);
        }
        if (query.From is { } from)
        {
            alerts = alerts.Where(a => a.DetectedAt >= from);
        }
        if (query.To is { } to)
        {
            alerts = alerts.Where(a => a.DetectedAt <= to);
        }
        if (!string.IsNullOrWhiteSpace(query.Q))
        {
            var pattern = $"%{EscapeLike(query.Q.Trim())}%";
            alerts = alerts.Where(a => EF.Functions.ILike(a.Title, pattern, "\\") || EF.Functions.ILike(a.Description, pattern, "\\"));
        }

        alerts = query.Sort switch
        {
            "detectedAt" => alerts.OrderBy(a => a.DetectedAt),
            "severity" => alerts.OrderBy(a => a.Severity).ThenByDescending(a => a.DetectedAt),
            "-severity" => alerts.OrderByDescending(a => a.Severity).ThenByDescending(a => a.DetectedAt),
            "eventCount" => alerts.OrderBy(a => a.EventCount),
            "-eventCount" => alerts.OrderByDescending(a => a.EventCount),
            "status" => alerts.OrderBy(a => a.Status).ThenByDescending(a => a.DetectedAt),
            _ => alerts.OrderByDescending(a => a.DetectedAt)
        };

        return await alerts.Select(a => new AlertListItemDto(
                a.Id, a.Title, a.Severity, a.Status, a.RuleId, a.Rule.Code, a.Rule.Name, a.DetectedAt, a.FirstEventAt,
                a.LastEventAt, a.EventCount,
                a.IncidentLink != null ? (Guid?)a.IncidentLink.IncidentId : null,
                a.IncidentLink != null ? (int?)a.IncidentLink.Incident.Number : null,
                a.Version))
            .ToPagedAsync(query.SafePage, query.SafePageSize, ct);
    }

    public async Task<AlertDetailsDto> GetAsync(Guid id, CancellationToken ct)
    {
        var row = await db.Alerts.AsNoTracking().Where(a => a.Id == id).Select(a => new
        {
            Alert = a,
            a.Rule.Code,
            a.Rule.Name,
            a.Rule.RuleType,
            a.Rule.EventType,
            a.Rule.Threshold,
            a.Rule.WindowSeconds,
            IncidentId = a.IncidentLink != null ? (Guid?)a.IncidentLink.IncidentId : null,
            IncidentNumber = a.IncidentLink != null ? (int?)a.IncidentLink.Incident.Number : null,
            Linked = db.AlertEvents.Count(e => e.AlertId == a.Id),
            ChangedBy = db.Users.Where(u => u.Id == a.StatusChangedBy).Select(u => u.DisplayName).FirstOrDefault()
        }).FirstOrDefaultAsync(ct) ?? throw new NotFoundException("Предупреждение не найдено");

        var a = row.Alert;
        var groupValues = JsonSerializer.Deserialize<Dictionary<string, string>>(a.GroupValues, JsonDefaults.Options)
            ?? new Dictionary<string, string>();
        return new AlertDetailsDto(a.Id, a.Title, a.Description, a.Severity, a.Status, a.RuleId, row.Code, row.Name,
            row.RuleType, row.EventType, row.Threshold, row.WindowSeconds, groupValues, a.DetectedAt, a.FirstEventAt,
            a.LastEventAt, a.EventCount, row.Linked, a.StatusChangedAt, row.ChangedBy, row.IncidentId, row.IncidentNumber,
            a.IsDemo, a.Version);
    }

    public async Task<PagedResult<EventListItemDto>> GetEventsAsync(Guid id, int page, int pageSize, CancellationToken ct)
    {
        if (!await db.Alerts.AnyAsync(a => a.Id == id, ct))
        {
            throw new NotFoundException("Предупреждение не найдено");
        }
        var events = db.SecurityEvents.AsNoTracking()
            .Where(e => db.AlertEvents.Any(ae => ae.AlertId == id && ae.EventId == e.Id))
            .OrderBy(e => e.Timestamp);
        return await EventProjection.ToListItems(events)
            .ToPagedAsync(Math.Max(1, page), Math.Clamp(pageSize, 1, PageQuery.MaxPageSize), ct);
    }

    public async Task<AlertDetailsDto> ChangeStatusAsync(Guid id, ChangeAlertStatusDto dto, CancellationToken ct)
    {
        var alert = await db.Alerts.FirstOrDefaultAsync(a => a.Id == id, ct)
            ?? throw new NotFoundException("Предупреждение не найдено");
        if (alert.Status == dto.Status)
        {
            return await GetAsync(id, ct);
        }
        if (!StatusTransitions.CanChange(alert.Status, dto.Status))
        {
            throw new ConflictException($"Переход {alert.Status} → {dto.Status} недопустим");
        }
        db.Entry(alert).Property(a => a.Version).OriginalValue = dto.Version;
        var previous = alert.Status;
        alert.Status = dto.Status;
        alert.StatusChangedAt = clock.GetUtcNow();
        alert.StatusChangedBy = currentUser.UserId;
        audit.Record(AuditActions.AlertStatusChanged, nameof(Alert), alert.Id, new { from = previous, to = dto.Status });
        await SaveWithConcurrencyAsync(ct);

        if (dto.Status == AlertStatus.Resolved)
        {
            suppression.CloseByAlert(alert.Id);
        }
        await notifications.AlertUpdatedAsync(new AlertUpdatedNotification(alert.Id, alert.Status, alert.EventCount));
        return await GetAsync(id, ct);
    }

    private async Task SaveWithConcurrencyAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException("Запись изменена другим пользователем, обновите данные");
        }
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];

    internal static string EscapeLike(string value) =>
        value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
}
