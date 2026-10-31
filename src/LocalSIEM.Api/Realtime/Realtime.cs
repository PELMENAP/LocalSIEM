using LocalSIEM.Api.Auth;
using LocalSIEM.Application.Common;
using LocalSIEM.Application.Settings;
using LocalSIEM.Application.Sources;
using LocalSIEM.Domain.Entities;
using LocalSIEM.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace LocalSIEM.Api.Realtime;

[Authorize(Policy = Policies.Analyst)]
public sealed class SiemHub : Hub;

public sealed record EventsIngestedMessage(int Count, Dictionary<Severity, int> BySeverity, DateTimeOffset LastTimestamp);

public sealed class SignalRNotificationPublisher(IHubContext<SiemHub> hub, ILogger<SignalRNotificationPublisher> logger)
    : INotificationPublisher
{
    private readonly Lock gate = new();
    private readonly int[] bySeverity = new int[5];
    private int pending;
    private DateTimeOffset lastTimestamp;

    public void EventsIngested(IReadOnlyList<SecurityEvent> events)
    {
        lock (gate)
        {
            foreach (var e in events)
            {
                bySeverity[(int)e.Severity]++;
                if (e.Timestamp > lastTimestamp)
                {
                    lastTimestamp = e.Timestamp;
                }
            }
            pending += events.Count;
        }
    }

    public EventsIngestedMessage? DrainEvents()
    {
        lock (gate)
        {
            if (pending == 0)
            {
                return null;
            }
            var map = new Dictionary<Severity, int>();
            for (var i = 0; i < bySeverity.Length; i++)
            {
                if (bySeverity[i] > 0)
                {
                    map[(Severity)i] = bySeverity[i];
                }
                bySeverity[i] = 0;
            }
            var message = new EventsIngestedMessage(pending, map, lastTimestamp);
            pending = 0;
            return message;
        }
    }

    public Task SendEventsAsync(EventsIngestedMessage message) => SendAsync("EventsIngested", message);

    public Task AlertCreatedAsync(AlertNotification notification) => SendAsync("AlertCreated", notification);
    public Task AlertUpdatedAsync(AlertUpdatedNotification notification) => SendAsync("AlertUpdated", notification);
    public Task IncidentCreatedAsync(IncidentNotification notification) => SendAsync("IncidentCreated", notification);
    public Task IncidentUpdatedAsync(IncidentUpdatedNotification notification) => SendAsync("IncidentUpdated", notification);
    public Task SourceStatusChangedAsync(SourceStatusNotification notification) => SendAsync("SourceStatusChanged", notification);
    public Task SimulatorStateChangedAsync(SimulatorStateNotification notification) => SendAsync("SimulatorStateChanged", notification);

    private async Task SendAsync(string method, object payload)
    {
        try
        {
            await hub.Clients.All.SendAsync(method, payload);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Не удалось отправить SignalR-уведомление {Method}", method);
        }
    }
}

public sealed class RealtimeFlusher(SignalRNotificationPublisher publisher) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            var message = publisher.DrainEvents();
            if (message is not null)
            {
                await publisher.SendEventsAsync(message);
            }
        }
    }
}

public sealed class SourceMonitor(
    IServiceScopeFactory scopes,
    INotificationPublisher notifications,
    SettingsCache settings,
    TimeProvider clock,
    ILogger<SourceMonitor> logger) : BackgroundService
{
    private readonly Dictionary<Guid, SourceStatus> known = [];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<ISiemDbContext>();
                var sources = await db.EventSources.AsNoTracking().ToListAsync(stoppingToken);
                var now = clock.GetUtcNow();
                foreach (var source in sources)
                {
                    var status = SourceStatusPolicy.Compute(source, now, settings.Current.SourceIdleSeconds);
                    if (known.TryGetValue(source.Id, out var previous) && previous == status)
                    {
                        continue;
                    }
                    var isNew = !known.ContainsKey(source.Id);
                    known[source.Id] = status;
                    if (!isNew)
                    {
                        await notifications.SourceStatusChangedAsync(new SourceStatusNotification(source.Id, source.Name, status));
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Ошибка мониторинга источников");
            }
        }
    }
}
