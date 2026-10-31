using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using LocalSIEM.Application.Common;
using LocalSIEM.Application.Detection;
using LocalSIEM.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace LocalSIEM.Application.Ingestion;

public interface IEventIngestionService
{
    Task<IngestResult> IngestAsync(Guid sourceId, IReadOnlyList<IngestEventDto>? events, CancellationToken ct);
}

public sealed class EventIngestionService(
    ISiemDbContext db,
    NormalizerRegistry normalizers,
    IDetectionQueue queue,
    INotificationPublisher notifications,
    TimeProvider clock) : IEventIngestionService
{
    public async Task<IngestResult> IngestAsync(Guid sourceId, IReadOnlyList<IngestEventDto>? events, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        IngestValidator.Validate(events, now).ThrowIfAny();

        var source = await db.EventSources.FirstOrDefaultAsync(s => s.Id == sourceId, ct)
            ?? throw new NotFoundException("Источник событий не найден");
        if (!source.IsEnabled)
        {
            throw new ForbiddenException("Источник событий отключён");
        }

        var normalizer = normalizers.Get(source.Kind);
        var entities = new List<SecurityEvent>(events!.Count);
        var detectionEvents = new List<DetectionEvent>(events.Count);
        foreach (var dto in events)
        {
            var normalized = normalizer.Normalize(dto, source, now);
            entities.Add(normalized.Entity);
            detectionEvents.Add(normalized.Detection);
        }

        db.SecurityEvents.AddRange(entities);
        source.LastSeenAt = now;
        await db.SaveChangesAsync(ct);

        foreach (var detectionEvent in detectionEvents)
        {
            await queue.EnqueueAsync(detectionEvent, CancellationToken.None);
        }
        notifications.EventsIngested(entities);

        var ids = new Guid[entities.Count];
        for (var i = 0; i < entities.Count; i++)
        {
            ids[i] = entities[i].Id;
        }
        return new IngestResult(entities.Count, ids);
    }
}

public static class ApiKeys
{
    public const string Prefix = "lsk_";

    public static string Generate() =>
        Prefix + Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static byte[] Hash(string key) => SHA256.HashData(Encoding.UTF8.GetBytes(key));
}

public sealed class SourceKeyCache
{
    private readonly ConcurrentDictionary<string, Guid?> cache = new(StringComparer.Ordinal);

    public async Task<Guid?> ResolveAsync(string apiKey, ISiemDbContext db, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(apiKey) || apiKey.Length > 200)
        {
            return null;
        }
        var hashHex = Convert.ToHexString(ApiKeys.Hash(apiKey));
        if (cache.TryGetValue(hashHex, out var cached))
        {
            return cached;
        }
        var hash = ApiKeys.Hash(apiKey);
        var sourceId = await db.EventSources.AsNoTracking()
            .Where(s => s.ApiKeyHash == hash && s.IsEnabled)
            .Select(s => (Guid?)s.Id)
            .FirstOrDefaultAsync(ct);
        if (sourceId is not null)
        {
            cache[hashHex] = sourceId;
        }
        return sourceId;
    }

    public void Invalidate() => cache.Clear();
}
