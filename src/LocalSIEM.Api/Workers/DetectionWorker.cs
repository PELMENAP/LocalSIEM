using LocalSIEM.Application.Alerts;
using LocalSIEM.Application.Common;
using LocalSIEM.Application.Detection;
using LocalSIEM.Application.Settings;
using LocalSIEM.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace LocalSIEM.Api.Workers;

public sealed class DetectionWorker(
    IDetectionQueue queue,
    DetectionPipeline pipeline,
    RuleCatalog catalog,
    DetectionControl control,
    SettingsCache settings,
    IServiceScopeFactory scopes,
    TimeProvider clock,
    ILogger<DetectionWorker> logger) : BackgroundService
{
    private const int BatchSize = 256;
    private static readonly TimeSpan SweepInterval = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var startedAt = clock.GetUtcNow();
        await WarmUpAsync(startedAt, stoppingToken);

        var batch = new List<DetectionEvent>(BatchSize);
        var lastSweep = clock.GetUtcNow();
        while (await queue.Reader.WaitToReadAsync(stoppingToken))
        {
            batch.Clear();
            while (batch.Count < BatchSize && queue.Reader.TryRead(out var evt))
            {
                batch.Add(evt);
            }

            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ISiemDbContext>();
            var sink = scope.ServiceProvider.GetRequiredService<IAlertService>();

            if (control.ResetRequested)
            {
                control.Acknowledge();
                pipeline.Engine.ResetAll();
                logger.LogInformation("Состояние детекции сброшено после очистки демо-данных");
            }
            if (catalog.IsDirty)
            {
                await ReloadRulesAsync(db, stoppingToken);
            }

            var rules = catalog.Current;
            var maxLinked = settings.Current.MaxLinkedEventsPerAlert;
            foreach (var evt in batch)
            {
                try
                {
                    await pipeline.ProcessAsync(evt, rules, sink, maxLinked, stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    db.ChangeTracker.Clear();
                    logger.LogError(ex, "Ошибка анализа события {EventId}", evt.Id);
                }
            }

            var now = clock.GetUtcNow();
            if (now - lastSweep >= SweepInterval)
            {
                pipeline.Engine.Sweep(rules, now);
                lastSweep = now;
            }
        }
    }

    private async Task ReloadRulesAsync(ISiemDbContext db, CancellationToken ct)
    {
        var changed = await catalog.ReloadAsync(db, ct);
        foreach (var ruleId in changed)
        {
            pipeline.Engine.ResetRule(ruleId);
        }
        logger.LogInformation("Каталог правил обновлён: активных {Active}, изменено {Changed}", catalog.Current.Count, changed.Count);
    }

    private async Task WarmUpAsync(DateTimeOffset startedAt, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<ISiemDbContext>();
                await ReloadRulesAsync(db, ct);
                var rules = catalog.Current;
                if (rules.Count == 0)
                {
                    return;
                }

                var since = startedAt - catalog.MaxWindow;
                var warmed = 0;
                await foreach (var stored in db.SecurityEvents.AsNoTracking()
                                   .Where(e => e.Timestamp >= since && e.ReceivedAt < startedAt)
                                   .OrderBy(e => e.Timestamp)
                                   .AsAsyncEnumerable()
                                   .WithCancellation(ct))
                {
                    pipeline.Engine.Warm(DetectionEvent.FromStored(stored), rules);
                    warmed++;
                }

                var cooldownSince = startedAt - catalog.MaxCooldown;
                var open = await db.Alerts.AsNoTracking()
                    .Where(a => a.Status != AlertStatus.Resolved && a.LastEventAt >= cooldownSince)
                    .Select(a => new
                    {
                        a.Id,
                        a.RuleId,
                        a.DedupKey,
                        a.LastEventAt,
                        Linked = db.AlertEvents.Count(ae => ae.AlertId == a.Id)
                    })
                    .ToListAsync(ct);
                var byId = rules.ToDictionary(r => r.Id);
                foreach (var alert in open)
                {
                    if (!byId.TryGetValue(alert.RuleId, out var rule) || !alert.DedupKey.StartsWith(rule.Code + ":", StringComparison.Ordinal))
                    {
                        continue;
                    }
                    pipeline.Engine.Suppression.Open(new Episode
                    {
                        AlertId = alert.Id,
                        RuleId = rule.Id,
                        GroupKey = alert.DedupKey[(rule.Code.Length + 1)..],
                        LastEventAt = alert.LastEventAt,
                        LinkedCount = alert.Linked
                    });
                }
                logger.LogInformation("Детекция прогрета: событий {Events}, открытых эпизодов {Episodes}", warmed, open.Count);
                return;
            }
            catch (Exception ex) when (ex is not OperationCanceledException && attempt < 5)
            {
                logger.LogWarning(ex, "Прогрев детекции не удался (попытка {Attempt}), повтор через 5 с", attempt);
                await Task.Delay(TimeSpan.FromSeconds(5), ct);
            }
        }
    }
}
