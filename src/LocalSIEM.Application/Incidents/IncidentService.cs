using LocalSIEM.Application.Alerts;
using LocalSIEM.Application.Common;
using LocalSIEM.Application.Events;
using LocalSIEM.Domain;
using LocalSIEM.Domain.Entities;
using LocalSIEM.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace LocalSIEM.Application.Incidents;

public sealed class IncidentQuery : PageQuery
{
    private DateTimeOffset? from;
    private DateTimeOffset? to;

    public IncidentStatus[]? Status { get; set; }
    public Severity[]? Severity { get; set; }
    public Guid? AssigneeId { get; set; }
    public DateTimeOffset? From { get => from; set => from = value?.ToUniversalTime(); }
    public DateTimeOffset? To { get => to; set => to = value?.ToUniversalTime(); }
    public string? Sort { get; set; }
}

public sealed record IncidentListItemDto(
    Guid Id, int Number, string Title, Severity Severity, IncidentStatus Status, Guid? AssigneeId, string? AssigneeName,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, DateTimeOffset? DetectedAt, int AlertCount, uint Version);

public sealed record IncidentDetailsDto(
    Guid Id, int Number, string Title, string Description, Severity Severity, IncidentStatus Status,
    Guid? AssigneeId, string? AssigneeName, string CreatedByName, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt,
    DateTimeOffset? ResolvedAt, DateTimeOffset? ClosedAt, bool IsDemo, uint Version, IReadOnlyList<AlertListItemDto> Alerts);

public sealed record CommentDto(Guid Id, Guid AuthorId, string AuthorName, string Body, DateTimeOffset CreatedAt);

public sealed record HistoryEntryDto(Guid Id, IncidentAction Action, string? OldValue, string? NewValue, string? ActorName, DateTimeOffset CreatedAt);

public sealed class CreateIncidentDto
{
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public Severity? Severity { get; set; }
    public Guid? AssigneeId { get; set; }
    public Guid[] AlertIds { get; set; } = [];
}

public sealed record ChangeIncidentStatusDto(IncidentStatus Status, uint Version);

public sealed record AssignIncidentDto(Guid? AssigneeId, uint Version);

public sealed record LinkAlertsDto(Guid[] AlertIds);

public sealed record AddCommentDto(string Body);

public interface IIncidentService
{
    Task<PagedResult<IncidentListItemDto>> ListAsync(IncidentQuery query, CancellationToken ct);
    Task<IncidentDetailsDto> GetAsync(Guid id, CancellationToken ct);
    Task<IncidentDetailsDto> CreateAsync(CreateIncidentDto dto, CancellationToken ct);
    Task<IncidentDetailsDto> ChangeStatusAsync(Guid id, ChangeIncidentStatusDto dto, CancellationToken ct);
    Task<IncidentDetailsDto> AssignAsync(Guid id, AssignIncidentDto dto, CancellationToken ct);
    Task<IncidentDetailsDto> LinkAlertsAsync(Guid id, LinkAlertsDto dto, CancellationToken ct);
    Task<IReadOnlyList<CommentDto>> GetCommentsAsync(Guid id, CancellationToken ct);
    Task<CommentDto> AddCommentAsync(Guid id, AddCommentDto dto, CancellationToken ct);
    Task<IReadOnlyList<HistoryEntryDto>> GetHistoryAsync(Guid id, CancellationToken ct);
    Task<PagedResult<EventListItemDto>> GetTimelineAsync(Guid id, int page, int pageSize, CancellationToken ct);
}

public sealed class IncidentService(
    ISiemDbContext db,
    INotificationPublisher notifications,
    IAuditService audit,
    ICurrentUser currentUser,
    TimeProvider clock) : IIncidentService
{
    public async Task<PagedResult<IncidentListItemDto>> ListAsync(IncidentQuery query, CancellationToken ct)
    {
        var incidents = db.Incidents.AsNoTracking();
        if (query.Status is { Length: > 0 })
        {
            incidents = incidents.Where(i => query.Status.Contains(i.Status));
        }
        if (query.Severity is { Length: > 0 })
        {
            incidents = incidents.Where(i => query.Severity.Contains(i.Severity));
        }
        if (query.AssigneeId is { } assigneeId)
        {
            incidents = incidents.Where(i => i.AssigneeId == assigneeId);
        }
        if (query.From is { } from)
        {
            incidents = incidents.Where(i => i.CreatedAt >= from);
        }
        if (query.To is { } to)
        {
            incidents = incidents.Where(i => i.CreatedAt <= to);
        }
        incidents = query.Sort switch
        {
            "createdAt" => incidents.OrderBy(i => i.CreatedAt),
            "-severity" => incidents.OrderByDescending(i => i.Severity).ThenByDescending(i => i.CreatedAt),
            "severity" => incidents.OrderBy(i => i.Severity).ThenByDescending(i => i.CreatedAt),
            "status" => incidents.OrderBy(i => i.Status).ThenByDescending(i => i.CreatedAt),
            "-updatedAt" => incidents.OrderByDescending(i => i.UpdatedAt),
            _ => incidents.OrderByDescending(i => i.CreatedAt)
        };
        return await incidents.Select(i => new IncidentListItemDto(
                i.Id, i.Number, i.Title, i.Severity, i.Status, i.AssigneeId,
                i.Assignee != null ? i.Assignee.DisplayName : null,
                i.CreatedAt, i.UpdatedAt,
                i.Alerts.Min(a => (DateTimeOffset?)a.Alert.DetectedAt),
                i.Alerts.Count, i.Version))
            .ToPagedAsync(query.SafePage, query.SafePageSize, ct);
    }

    public async Task<IncidentDetailsDto> GetAsync(Guid id, CancellationToken ct)
    {
        var row = await db.Incidents.AsNoTracking().Where(i => i.Id == id).Select(i => new
        {
            Incident = i,
            AssigneeName = i.Assignee != null ? i.Assignee.DisplayName : null,
            CreatorName = i.Creator.DisplayName
        }).FirstOrDefaultAsync(ct) ?? throw new NotFoundException("Инцидент не найден");

        var linked = await db.IncidentAlerts.AsNoTracking().Where(l => l.IncidentId == id)
            .Select(l => l.AlertId).ToListAsync(ct);
        var alertQuery = new AlertQuery { Page = 1, PageSize = PageQuery.MaxPageSize };
        var alertItems = linked.Count == 0
            ? []
            : await db.Alerts.AsNoTracking().Where(a => linked.Contains(a.Id)).OrderBy(a => a.DetectedAt)
                .Select(a => new AlertListItemDto(a.Id, a.Title, a.Severity, a.Status, a.RuleId, a.Rule.Code, a.Rule.Name,
                    a.DetectedAt, a.FirstEventAt, a.LastEventAt, a.EventCount, (Guid?)id, (int?)row.Incident.Number, a.Version))
                .Take(alertQuery.SafePageSize)
                .ToListAsync(ct);

        var i = row.Incident;
        return new IncidentDetailsDto(i.Id, i.Number, i.Title, i.Description, i.Severity, i.Status, i.AssigneeId,
            row.AssigneeName, row.CreatorName, i.CreatedAt, i.UpdatedAt, i.ResolvedAt, i.ClosedAt, i.IsDemo, i.Version, alertItems);
    }

    public async Task<IncidentDetailsDto> CreateAsync(CreateIncidentDto dto, CancellationToken ct)
    {
        var errors = new ValidationErrors();
        var title = dto.Title?.Trim() ?? string.Empty;
        if (title.Length is 0 or > 200)
        {
            errors.Add("title", "Название обязательно, до 200 символов");
        }
        if (dto.Description is { Length: > 10_000 })
        {
            errors.Add("description", "Описание до 10000 символов");
        }
        var alertIds = (dto.AlertIds ?? []).Distinct().ToArray();
        if (alertIds.Length == 0)
        {
            errors.Add("alertIds", "Инцидент создаётся хотя бы из одного предупреждения");
        }
        if (alertIds.Length > 100)
        {
            errors.Add("alertIds", "Не больше 100 предупреждений за раз");
        }
        errors.ThrowIfAny();

        var userId = RequireUser();
        await EnsureAssigneeAsync(dto.AssigneeId, ct);
        var linkedAlerts = await LoadLinkableAlertsAsync(alertIds, ct);

        var now = clock.GetUtcNow();
        var incident = new Incident
        {
            Id = Ids.New(),
            Title = title,
            Description = dto.Description?.Trim() ?? string.Empty,
            Severity = dto.Severity ?? linkedAlerts.Max(a => a.Severity),
            Status = IncidentStatus.Open,
            AssigneeId = dto.AssigneeId,
            CreatedBy = userId,
            CreatedAt = now,
            UpdatedAt = now,
            IsDemo = linkedAlerts.All(a => a.IsDemo)
        };
        db.Incidents.Add(incident);
        AddHistory(incident.Id, IncidentAction.Created, null, title, now);
        if (dto.AssigneeId is not null)
        {
            AddHistory(incident.Id, IncidentAction.Assigned, null, await UserNameAsync(dto.AssigneeId, ct), now);
        }
        LinkAlerts(incident.Id, linkedAlerts, userId, now);
        audit.Record(AuditActions.IncidentCreated, nameof(Incident), incident.Id, new { title, alertIds });
        await SaveAsync(ct);

        await notifications.IncidentCreatedAsync(new IncidentNotification(incident.Id, incident.Number, incident.Title, incident.Severity));
        await NotifyAlertsAsync(linkedAlerts);
        return await GetAsync(incident.Id, ct);
    }

    public async Task<IncidentDetailsDto> ChangeStatusAsync(Guid id, ChangeIncidentStatusDto dto, CancellationToken ct)
    {
        var incident = await LoadAsync(id, ct);
        if (incident.Status == dto.Status)
        {
            return await GetAsync(id, ct);
        }
        if (!StatusTransitions.CanChange(incident.Status, dto.Status))
        {
            throw new ConflictException($"Переход {incident.Status} → {dto.Status} недопустим");
        }
        db.Entry(incident).Property(i => i.Version).OriginalValue = dto.Version;
        var now = clock.GetUtcNow();
        var previous = incident.Status;
        incident.Status = dto.Status;
        incident.UpdatedAt = now;
        if (dto.Status == IncidentStatus.Resolved)
        {
            incident.ResolvedAt = now;
        }
        if (dto.Status == IncidentStatus.Closed)
        {
            incident.ClosedAt = now;
        }
        if (dto.Status == IncidentStatus.Investigating)
        {
            incident.ResolvedAt = null;
        }
        AddHistory(id, IncidentAction.StatusChanged, previous.ToString(), dto.Status.ToString(), now);
        audit.Record(AuditActions.IncidentStatusChanged, nameof(Incident), id, new { from = previous, to = dto.Status });
        await SaveAsync(ct);
        await notifications.IncidentUpdatedAsync(new IncidentUpdatedNotification(id, incident.Status, incident.AssigneeId));
        return await GetAsync(id, ct);
    }

    public async Task<IncidentDetailsDto> AssignAsync(Guid id, AssignIncidentDto dto, CancellationToken ct)
    {
        var incident = await LoadAsync(id, ct);
        if (incident.AssigneeId == dto.AssigneeId)
        {
            return await GetAsync(id, ct);
        }
        await EnsureAssigneeAsync(dto.AssigneeId, ct);
        db.Entry(incident).Property(i => i.Version).OriginalValue = dto.Version;
        var now = clock.GetUtcNow();
        var oldName = await UserNameAsync(incident.AssigneeId, ct);
        var newName = await UserNameAsync(dto.AssigneeId, ct);
        incident.AssigneeId = dto.AssigneeId;
        incident.UpdatedAt = now;
        AddHistory(id, IncidentAction.Assigned, oldName, newName, now);
        audit.Record(AuditActions.IncidentAssigned, nameof(Incident), id, new { dto.AssigneeId });
        await SaveAsync(ct);
        await notifications.IncidentUpdatedAsync(new IncidentUpdatedNotification(id, incident.Status, incident.AssigneeId));
        return await GetAsync(id, ct);
    }

    public async Task<IncidentDetailsDto> LinkAlertsAsync(Guid id, LinkAlertsDto dto, CancellationToken ct)
    {
        var alertIds = (dto.AlertIds ?? []).Distinct().ToArray();
        if (alertIds.Length is 0 or > 100)
        {
            throw RequestValidationException.Single("alertIds", "Передайте от 1 до 100 предупреждений");
        }
        var incident = await LoadAsync(id, ct);
        if (incident.Status == IncidentStatus.Closed)
        {
            throw new ConflictException("Закрытый инцидент изменять нельзя");
        }
        var userId = RequireUser();
        var linkedAlerts = await LoadLinkableAlertsAsync(alertIds, ct);
        var now = clock.GetUtcNow();
        LinkAlerts(id, linkedAlerts, userId, now);
        incident.UpdatedAt = now;
        var maxSeverity = linkedAlerts.Max(a => a.Severity);
        if (maxSeverity > incident.Severity)
        {
            incident.Severity = maxSeverity;
        }
        if (!linkedAlerts.All(a => a.IsDemo))
        {
            incident.IsDemo = false;
        }
        audit.Record(AuditActions.IncidentAlertsLinked, nameof(Incident), id, new { alertIds });
        await SaveAsync(ct);
        await NotifyAlertsAsync(linkedAlerts);
        await notifications.IncidentUpdatedAsync(new IncidentUpdatedNotification(id, incident.Status, incident.AssigneeId));
        return await GetAsync(id, ct);
    }

    public async Task<IReadOnlyList<CommentDto>> GetCommentsAsync(Guid id, CancellationToken ct)
    {
        await EnsureExistsAsync(id, ct);
        return await db.IncidentComments.AsNoTracking().Where(c => c.IncidentId == id).OrderBy(c => c.CreatedAt)
            .Select(c => new CommentDto(c.Id, c.AuthorId, c.Author.DisplayName, c.Body, c.CreatedAt)).ToListAsync(ct);
    }

    public async Task<CommentDto> AddCommentAsync(Guid id, AddCommentDto dto, CancellationToken ct)
    {
        var body = dto.Body?.Trim() ?? string.Empty;
        if (body.Length is 0 or > 5000)
        {
            throw RequestValidationException.Single("body", "Комментарий — от 1 до 5000 символов");
        }
        var incident = await LoadAsync(id, ct);
        var userId = RequireUser();
        var now = clock.GetUtcNow();
        var comment = new IncidentComment { Id = Ids.New(), IncidentId = id, AuthorId = userId, Body = body, CreatedAt = now };
        db.IncidentComments.Add(comment);
        incident.UpdatedAt = now;
        AddHistory(id, IncidentAction.CommentAdded, null, body.Length > 200 ? body[..197] + "..." : body, now);
        audit.Record(AuditActions.IncidentCommented, nameof(Incident), id);
        await SaveAsync(ct);
        var authorName = await UserNameAsync(userId, ct) ?? string.Empty;
        return new CommentDto(comment.Id, userId, authorName, comment.Body, comment.CreatedAt);
    }

    public async Task<IReadOnlyList<HistoryEntryDto>> GetHistoryAsync(Guid id, CancellationToken ct)
    {
        await EnsureExistsAsync(id, ct);
        return await db.IncidentHistory.AsNoTracking().Where(h => h.IncidentId == id).OrderBy(h => h.CreatedAt).ThenBy(h => h.Id)
            .Select(h => new HistoryEntryDto(h.Id, h.Action, h.OldValue, h.NewValue, h.Actor != null ? h.Actor.DisplayName : null, h.CreatedAt))
            .ToListAsync(ct);
    }

    public async Task<PagedResult<EventListItemDto>> GetTimelineAsync(Guid id, int page, int pageSize, CancellationToken ct)
    {
        await EnsureExistsAsync(id, ct);
        var eventIds = db.AlertEvents
            .Where(ae => db.IncidentAlerts.Any(l => l.IncidentId == id && l.AlertId == ae.AlertId))
            .Select(ae => ae.EventId);
        var events = db.SecurityEvents.AsNoTracking().Where(e => eventIds.Contains(e.Id)).OrderBy(e => e.Timestamp).ThenBy(e => e.Id);
        return await EventProjection.ToListItems(events)
            .ToPagedAsync(Math.Max(1, page), Math.Clamp(pageSize, 1, PageQuery.MaxPageSize), ct);
    }

    private async Task<List<Alert>> LoadLinkableAlertsAsync(Guid[] alertIds, CancellationToken ct)
    {
        var found = await db.Alerts.Include(a => a.IncidentLink).Where(a => alertIds.Contains(a.Id)).ToListAsync(ct);
        if (found.Count != alertIds.Length)
        {
            throw new NotFoundException("Часть предупреждений не найдена");
        }
        var alreadyLinked = found.Where(a => a.IncidentLink is not null).Select(a => a.Id).ToList();
        if (alreadyLinked.Count > 0)
        {
            throw new ConflictException($"Предупреждения уже входят в другой инцидент: {string.Join(", ", alreadyLinked)}");
        }
        return found;
    }

    private void LinkAlerts(Guid incidentId, List<Alert> linkedAlerts, Guid userId, DateTimeOffset now)
    {
        foreach (var alert in linkedAlerts)
        {
            db.IncidentAlerts.Add(new IncidentAlert { IncidentId = incidentId, AlertId = alert.Id, LinkedBy = userId, LinkedAt = now });
            if (alert.Status == AlertStatus.New)
            {
                alert.Status = AlertStatus.Acknowledged;
                alert.StatusChangedAt = now;
                alert.StatusChangedBy = userId;
            }
            AddHistory(incidentId, IncidentAction.AlertLinked, null, alert.Title, now);
        }
    }

    private async Task NotifyAlertsAsync(IEnumerable<Alert> linkedAlerts)
    {
        foreach (var alert in linkedAlerts)
        {
            await notifications.AlertUpdatedAsync(new AlertUpdatedNotification(alert.Id, alert.Status, alert.EventCount));
        }
    }

    private void AddHistory(Guid incidentId, IncidentAction action, string? oldValue, string? newValue, DateTimeOffset now) =>
        db.IncidentHistory.Add(new IncidentHistoryEntry
        {
            Id = Ids.New(),
            IncidentId = incidentId,
            ActorId = currentUser.UserId,
            Action = action,
            OldValue = Truncate(oldValue),
            NewValue = Truncate(newValue),
            CreatedAt = now
        });

    private static string? Truncate(string? value) => value is { Length: > 200 } ? value[..200] : value;

    private Guid RequireUser() => currentUser.UserId ?? throw new ForbiddenException("Требуется аутентификация");

    private async Task<Incident> LoadAsync(Guid id, CancellationToken ct) =>
        await db.Incidents.FirstOrDefaultAsync(i => i.Id == id, ct) ?? throw new NotFoundException("Инцидент не найден");

    private async Task EnsureExistsAsync(Guid id, CancellationToken ct)
    {
        if (!await db.Incidents.AnyAsync(i => i.Id == id, ct))
        {
            throw new NotFoundException("Инцидент не найден");
        }
    }

    private async Task EnsureAssigneeAsync(Guid? assigneeId, CancellationToken ct)
    {
        if (assigneeId is { } id && !await db.Users.AnyAsync(u => u.Id == id && u.IsActive, ct))
        {
            throw RequestValidationException.Single("assigneeId", "Ответственный не найден или отключён");
        }
    }

    private async Task<string?> UserNameAsync(Guid? userId, CancellationToken ct) =>
        userId is null ? null : await db.Users.Where(u => u.Id == userId).Select(u => u.DisplayName).FirstOrDefaultAsync(ct);

    private async Task SaveAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException("Инцидент изменён другим пользователем, обновите данные");
        }
        catch (DbUpdateException ex) when (ex.InnerException?.Message.Contains("incident_alerts", StringComparison.OrdinalIgnoreCase) == true)
        {
            throw new ConflictException("Предупреждение уже привязано к другому инциденту");
        }
    }
}
