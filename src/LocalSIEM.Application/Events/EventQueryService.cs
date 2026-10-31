using System.Net;
using System.Text.Json;
using LocalSIEM.Application.Alerts;
using LocalSIEM.Application.Common;
using LocalSIEM.Domain.Entities;
using LocalSIEM.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace LocalSIEM.Application.Events;

public sealed class EventQuery : PageQuery
{
    private DateTimeOffset? from;
    private DateTimeOffset? to;

    public DateTimeOffset? From { get => from; set => from = value?.ToUniversalTime(); }
    public DateTimeOffset? To { get => to; set => to = value?.ToUniversalTime(); }
    public Severity[]? Severity { get; set; }
    public Guid[]? SourceId { get; set; }
    public string? EventType { get; set; }
    public string? SourceIp { get; set; }
    public string? Username { get; set; }
    public string? Q { get; set; }
    public string? Sort { get; set; }
}

public sealed record EventListItemDto(
    Guid Id, DateTimeOffset Timestamp, Guid SourceId, string SourceName, string EventType, Severity Severity,
    IPAddress? SourceIp, IPAddress? TargetIp, string? Username, string? Hostname, string? Message, bool IsDemo);

public sealed record EventDetailsDto(
    Guid Id, DateTimeOffset Timestamp, DateTimeOffset ReceivedAt, Guid SourceId, string SourceName, SourceKind SourceKind,
    string EventType, Severity Severity, IPAddress? SourceIp, IPAddress? TargetIp, string? Username, string? Hostname,
    string? Message, Guid? CorrelationId, bool IsDemo, JsonElement RawData, IReadOnlyList<Guid> AlertIds);

public static class EventProjection
{
    public static IQueryable<EventListItemDto> ToListItems(IQueryable<SecurityEvent> events) =>
        events.Select(e => new EventListItemDto(e.Id, e.Timestamp, e.SourceId, e.Source.Name, e.EventType, e.Severity,
            e.SourceIp, e.TargetIp, e.Username, e.Hostname, e.Message, e.IsDemo));
}

public interface IEventQueryService
{
    Task<PagedResult<EventListItemDto>> ListAsync(EventQuery query, CancellationToken ct);
    Task<EventDetailsDto> GetAsync(Guid id, CancellationToken ct);
}

public sealed class EventQueryService(ISiemDbContext db) : IEventQueryService
{
    public async Task<PagedResult<EventListItemDto>> ListAsync(EventQuery query, CancellationToken ct)
    {
        var events = db.SecurityEvents.AsNoTracking();
        if (query.From is { } from)
        {
            events = events.Where(e => e.Timestamp >= from);
        }
        if (query.To is { } to)
        {
            events = events.Where(e => e.Timestamp <= to);
        }
        if (query.Severity is { Length: > 0 })
        {
            events = events.Where(e => query.Severity.Contains(e.Severity));
        }
        if (query.SourceId is { Length: > 0 })
        {
            events = events.Where(e => query.SourceId.Contains(e.SourceId));
        }
        if (!string.IsNullOrWhiteSpace(query.EventType))
        {
            var eventType = query.EventType.Trim();
            events = events.Where(e => e.EventType == eventType);
        }
        if (!string.IsNullOrWhiteSpace(query.SourceIp))
        {
            if (!IPAddress.TryParse(query.SourceIp.Trim(), out var ip))
            {
                throw RequestValidationException.Single("sourceIp", "Некорректный IP-адрес");
            }
            events = events.Where(e => e.SourceIp == ip || e.TargetIp == ip);
        }
        if (!string.IsNullOrWhiteSpace(query.Username))
        {
            var username = query.Username.Trim().ToLower();
            events = events.Where(e => e.Username != null && e.Username.ToLower() == username);
        }
        if (!string.IsNullOrWhiteSpace(query.Q))
        {
            var pattern = $"%{AlertService.EscapeLike(query.Q.Trim())}%";
            events = events.Where(e =>
                (e.Message != null && EF.Functions.ILike(e.Message, pattern, "\\")) ||
                (e.Username != null && EF.Functions.ILike(e.Username, pattern, "\\")) ||
                (e.Hostname != null && EF.Functions.ILike(e.Hostname, pattern, "\\")) ||
                EF.Functions.ILike(e.EventType, pattern, "\\"));
        }

        events = query.Sort switch
        {
            "timestamp" => events.OrderBy(e => e.Timestamp).ThenBy(e => e.Id),
            "severity" => events.OrderBy(e => e.Severity).ThenByDescending(e => e.Timestamp),
            "-severity" => events.OrderByDescending(e => e.Severity).ThenByDescending(e => e.Timestamp),
            "eventType" => events.OrderBy(e => e.EventType).ThenByDescending(e => e.Timestamp),
            _ => events.OrderByDescending(e => e.Timestamp).ThenByDescending(e => e.Id)
        };

        return await EventProjection.ToListItems(events).ToPagedAsync(query.SafePage, query.SafePageSize, ct);
    }

    public async Task<EventDetailsDto> GetAsync(Guid id, CancellationToken ct)
    {
        var row = await db.SecurityEvents.AsNoTracking().Where(e => e.Id == id).Select(e => new
        {
            Event = e,
            SourceName = e.Source.Name,
            SourceKind = e.Source.Kind,
            AlertIds = db.AlertEvents.Where(ae => ae.EventId == e.Id).Select(ae => ae.AlertId).ToList()
        }).FirstOrDefaultAsync(ct) ?? throw new NotFoundException("Событие не найдено");

        var e = row.Event;
        using var raw = JsonDocument.Parse(e.RawData);
        return new EventDetailsDto(e.Id, e.Timestamp, e.ReceivedAt, e.SourceId, row.SourceName, row.SourceKind, e.EventType,
            e.Severity, e.SourceIp, e.TargetIp, e.Username, e.Hostname, e.Message, e.CorrelationId, e.IsDemo,
            raw.RootElement.Clone(), row.AlertIds);
    }
}
