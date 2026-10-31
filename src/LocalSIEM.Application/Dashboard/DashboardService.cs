using LocalSIEM.Application.Alerts;
using LocalSIEM.Application.Common;
using LocalSIEM.Application.Detection;
using LocalSIEM.Application.Sources;
using LocalSIEM.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace LocalSIEM.Application.Dashboard;

public sealed record SeverityCountDto(Severity Severity, int Count);

public sealed record DashboardSummaryDto(
    long TotalEvents, int Events24h, int AlertsTotal, int AlertsNew, int AlertsAcknowledged, int ActiveIncidents,
    IReadOnlyList<SeverityCountDto> SeverityDistribution24h, IReadOnlyList<AlertListItemDto> RecentAlerts,
    IReadOnlyList<SourceDto> Sources, int DetectionQueue, DateTimeOffset GeneratedAt);

public sealed record TimelinePointDto(DateTimeOffset Bucket, Severity Severity, int Count);

public sealed record TopIpDto(string Ip, int Count, DateTimeOffset LastSeen);

public sealed class TimelineRow
{
    public DateTimeOffset Bucket { get; set; }
    public short Severity { get; set; }
    public int Count { get; set; }
}

public sealed class TopIpRow
{
    public string Ip { get; set; } = string.Empty;
    public int Count { get; set; }
    public DateTimeOffset LastSeen { get; set; }
}

public interface IDashboardService
{
    Task<DashboardSummaryDto> GetSummaryAsync(CancellationToken ct);
    Task<IReadOnlyList<TimelinePointDto>> GetTimelineAsync(DateTimeOffset? from, DateTimeOffset? to, string? bucket, CancellationToken ct);
    Task<IReadOnlyList<TopIpDto>> GetTopIpsAsync(int hours, int limit, CancellationToken ct);
}

public sealed class DashboardService(
    ISiemDbContext db,
    IAlertService alerts,
    ISourceService sources,
    IDetectionQueue queue,
    TimeProvider clock) : IDashboardService
{
    public async Task<DashboardSummaryDto> GetSummaryAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var since = now.AddHours(-24);
        var totalEvents = await db.SecurityEvents.LongCountAsync(ct);
        var distribution = await db.SecurityEvents.AsNoTracking().Where(e => e.Timestamp >= since)
            .GroupBy(e => e.Severity).Select(g => new SeverityCountDto(g.Key, g.Count())).ToListAsync(ct);
        var alertStats = await db.Alerts.AsNoTracking().GroupBy(a => a.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() }).ToListAsync(ct);
        var activeIncidents = await db.Incidents.CountAsync(i => i.Status == IncidentStatus.Open || i.Status == IncidentStatus.Investigating, ct);
        var recent = await alerts.ListAsync(new AlertQuery { Page = 1, PageSize = 8 }, ct);
        var sourceList = await sources.ListAsync(ct);

        var fullDistribution = Enum.GetValues<Severity>()
            .Select(s => new SeverityCountDto(s, distribution.FirstOrDefault(d => d.Severity == s)?.Count ?? 0))
            .ToList();

        return new DashboardSummaryDto(
            totalEvents,
            fullDistribution.Sum(d => d.Count),
            alertStats.Sum(s => s.Count),
            alertStats.FirstOrDefault(s => s.Status == AlertStatus.New)?.Count ?? 0,
            alertStats.FirstOrDefault(s => s.Status == AlertStatus.Acknowledged)?.Count ?? 0,
            activeIncidents,
            fullDistribution,
            recent.Items,
            sourceList,
            queue.Pending,
            now);
    }

    public async Task<IReadOnlyList<TimelinePointDto>> GetTimelineAsync(DateTimeOffset? from, DateTimeOffset? to, string? bucket, CancellationToken ct)
    {
        var end = (to ?? clock.GetUtcNow()).ToUniversalTime();
        var start = (from ?? end.AddHours(-24)).ToUniversalTime();
        if (start >= end)
        {
            throw RequestValidationException.Single("from", "Начало периода должно быть раньше конца");
        }
        var step = bucket switch
        {
            "1m" => TimeSpan.FromMinutes(1),
            "5m" => TimeSpan.FromMinutes(5),
            "15m" => TimeSpan.FromMinutes(15),
            "1h" => TimeSpan.FromHours(1),
            null or "" => end - start <= TimeSpan.FromHours(2) ? TimeSpan.FromMinutes(1) : TimeSpan.FromHours(1),
            _ => throw RequestValidationException.Single("bucket", "Допустимо: 1m, 5m, 15m, 1h")
        };
        if ((end - start) / step > 2000)
        {
            throw RequestValidationException.Single("bucket", "Слишком много интервалов, увеличьте шаг");
        }

        var origin = DateTimeOffset.UnixEpoch;
        var rows = await db.Database.SqlQuery<TimelineRow>($"""
            SELECT date_bin({step}, timestamp, {origin}) AS "Bucket", severity AS "Severity", count(*)::int AS "Count"
            FROM security_events
            WHERE timestamp >= {start} AND timestamp < {end}
            GROUP BY 1, 2
            ORDER BY 1, 2
            """).ToListAsync(ct);
        return rows.Select(r => new TimelinePointDto(r.Bucket, (Severity)r.Severity, r.Count)).ToList();
    }

    public async Task<IReadOnlyList<TopIpDto>> GetTopIpsAsync(int hours, int limit, CancellationToken ct)
    {
        var since = clock.GetUtcNow().AddHours(-Math.Clamp(hours, 1, 24 * 30));
        var take = Math.Clamp(limit, 1, 50);
        var rows = await db.Database.SqlQuery<TopIpRow>($"""
            SELECT host(source_ip) AS "Ip", count(*)::int AS "Count", max(timestamp) AS "LastSeen"
            FROM security_events
            WHERE timestamp >= {since} AND source_ip IS NOT NULL
            GROUP BY source_ip
            ORDER BY 2 DESC
            LIMIT {take}
            """).ToListAsync(ct);
        return rows.Select(r => new TopIpDto(r.Ip, r.Count, r.LastSeen)).ToList();
    }
}
