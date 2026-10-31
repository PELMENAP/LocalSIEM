using System.Diagnostics;
using System.Net;
using System.Text.Json;
using LocalSIEM.Application.Common;
using LocalSIEM.Application.Detection;
using LocalSIEM.Application.Ingestion;
using LocalSIEM.Application.Settings;
using LocalSIEM.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LocalSIEM.Application.Simulator;

public sealed class StartSimulationDto
{
    public string Scenario { get; set; } = string.Empty;
    public int? Count { get; set; }
    public double? RatePerSecond { get; set; }
    public string? SourceIp { get; set; }
    public string? Username { get; set; }
}

public sealed record SimulationStateDto(
    Guid? RunId, string State, string? Scenario, int Sent, int Total, double RatePerSecond,
    DateTimeOffset? StartedAt, DateTimeOffset? FinishedAt, string? Error);

public sealed record PurgeRequestDto(string Confirm);

public sealed record PurgeResultDto(int Incidents, int Alerts, int Events);

public sealed class SimulatorRunner(
    IEnumerable<IScenario> scenarios,
    IServiceScopeFactory scopeFactory,
    INotificationPublisher notifications,
    SettingsCache settings,
    TimeProvider clock,
    ILogger<SimulatorRunner> logger)
{
    public const string Idle = "Idle";
    public const string Running = "Running";
    public const string Completed = "Completed";
    public const string Stopped = "Stopped";
    public const string Failed = "Failed";

    private readonly Dictionary<string, IScenario> byKey = scenarios.ToDictionary(s => s.Info.Key, StringComparer.OrdinalIgnoreCase);
    private readonly Lock gate = new();
    private SimulationStateDto state = new(null, Idle, null, 0, 0, 0, null, null, null);
    private CancellationTokenSource? cts;

    public IReadOnlyList<ScenarioInfo> Scenarios => byKey.Values.Select(s => s.Info).ToList();

    public SimulationStateDto State
    {
        get
        {
            lock (gate)
            {
                return state;
            }
        }
    }

    public bool IsRunning => State.State == Running;

    public SimulationStateDto Start(StartSimulationDto dto)
    {
        var errors = new ValidationErrors();
        if (!byKey.TryGetValue(dto.Scenario ?? string.Empty, out var scenario))
        {
            errors.Add("scenario", "Неизвестный сценарий");
            errors.ThrowIfAny();
        }
        var count = dto.Count ?? scenario!.Info.DefaultCount;
        var rate = dto.RatePerSecond ?? scenario!.Info.DefaultRate;
        var maxRate = settings.Current.SimulatorMaxRatePerSecond;
        if (count is < 1 or > 10_000)
        {
            errors.Add("count", "Количество событий — от 1 до 10000");
        }
        if (rate < 0.1 || rate > maxRate)
        {
            errors.Add("ratePerSecond", $"Скорость — от 0.1 до {maxRate} событий/с");
        }
        var sourceIp = string.IsNullOrWhiteSpace(dto.SourceIp) ? null : dto.SourceIp.Trim();
        if (sourceIp is not null && !IPAddress.TryParse(sourceIp, out _))
        {
            errors.Add("sourceIp", "Некорректный IP-адрес");
        }
        var username = string.IsNullOrWhiteSpace(dto.Username) ? null : dto.Username.Trim();
        if (username is { Length: > 64 })
        {
            errors.Add("username", "Не длиннее 64 символов");
        }
        errors.ThrowIfAny();

        var events = scenario!.Generate(new ScenarioOptions(count, sourceIp, username), Random.Shared).ToList();
        CancellationTokenSource tokenSource;
        SimulationStateDto started;
        lock (gate)
        {
            if (state.State == Running)
            {
                throw new ConflictException("Сценарий уже выполняется — остановите его или дождитесь завершения");
            }
            tokenSource = new CancellationTokenSource();
            cts = tokenSource;
            started = new SimulationStateDto(Ids.New(), Running, scenario.Info.Key, 0, events.Count, rate, clock.GetUtcNow(), null, null);
            state = started;
        }
        _ = Task.Run(() => RunAsync(started.RunId!.Value, events, rate, tokenSource.Token));
        _ = notifications.SimulatorStateChangedAsync(ToNotification(started));
        return started;
    }

    public SimulationStateDto Stop(Guid runId)
    {
        lock (gate)
        {
            if (state.RunId != runId)
            {
                throw new NotFoundException("Запуск не найден");
            }
            cts?.Cancel();
            return state;
        }
    }

    private async Task RunAsync(Guid runId, List<SimEvent> events, double rate, CancellationToken ct)
    {
        var sent = 0;
        string finalState = Completed;
        string? error = null;
        try
        {
            var sourceId = await ResolveSourceAsync(ct);
            var stopwatch = Stopwatch.StartNew();
            var lastNotify = TimeSpan.Zero;
            while (sent < events.Count)
            {
                ct.ThrowIfCancellationRequested();
                var due = Math.Min(events.Count, (int)Math.Floor(stopwatch.Elapsed.TotalSeconds * rate) + 1);
                due = Math.Min(due, sent + IngestValidator.MaxBatchSize);
                if (due > sent)
                {
                    var now = clock.GetUtcNow();
                    var batch = new List<IngestEventDto>(due - sent);
                    for (var i = sent; i < due; i++)
                    {
                        batch.Add(ToDto(events[i], now));
                    }
                    using (var scope = scopeFactory.CreateScope())
                    {
                        var ingestion = scope.ServiceProvider.GetRequiredService<IEventIngestionService>();
                        await ingestion.IngestAsync(sourceId, batch, ct);
                    }
                    sent = due;
                    Update(runId, s => s with { Sent = sent });
                    if (stopwatch.Elapsed - lastNotify >= TimeSpan.FromMilliseconds(500))
                    {
                        lastNotify = stopwatch.Elapsed;
                        await notifications.SimulatorStateChangedAsync(ToNotification(State));
                    }
                    continue;
                }
                var nextDue = TimeSpan.FromSeconds(sent / rate);
                var wait = nextDue - stopwatch.Elapsed;
                await Task.Delay(wait > TimeSpan.FromMilliseconds(5) ? wait : TimeSpan.FromMilliseconds(5), ct);
            }
        }
        catch (OperationCanceledException)
        {
            finalState = Stopped;
        }
        catch (Exception ex)
        {
            finalState = Failed;
            error = ex is RequestValidationException validation
                ? string.Join("; ", validation.Errors.SelectMany(e => e.Value))
                : ex.Message;
            logger.LogError(ex, "Сценарий симулятора завершился с ошибкой");
        }

        Update(runId, s => s with { State = finalState, Sent = sent, FinishedAt = clock.GetUtcNow(), Error = error });
        await notifications.SimulatorStateChangedAsync(ToNotification(State));
    }

    private void Update(Guid runId, Func<SimulationStateDto, SimulationStateDto> change)
    {
        lock (gate)
        {
            if (state.RunId == runId)
            {
                state = change(state);
            }
        }
    }

    private async Task<Guid> ResolveSourceAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISiemDbContext>();
        var source = await db.EventSources.AsNoTracking().FirstOrDefaultAsync(s => s.Kind == SourceKind.Simulator, ct)
            ?? throw new InvalidOperationException("Источник Event Simulator не найден — перезапустите API для сидинга");
        if (!source.IsEnabled)
        {
            throw new InvalidOperationException("Источник Event Simulator отключён в настройках");
        }
        return source.Id;
    }

    private static IngestEventDto ToDto(SimEvent e, DateTimeOffset now) => new()
    {
        Timestamp = now,
        EventType = e.EventType,
        SourceIp = e.SourceIp,
        TargetIp = e.TargetIp,
        Username = e.Username,
        Hostname = e.Hostname,
        Message = e.Message,
        Attributes = e.Attributes?.ToDictionary(a => a.Key, a => JsonSerializer.SerializeToElement(a.Value, JsonDefaults.Options)),
        Raw = JsonSerializer.SerializeToElement(new { generator = "LocalSIEM Event Simulator" }, JsonDefaults.Options)
    };

    private static SimulatorStateNotification ToNotification(SimulationStateDto s) =>
        new(s.RunId, s.State, s.Scenario, s.Sent, s.Total, s.Error);
}

public interface IDemoDataService
{
    Task<PurgeResultDto> PurgeAsync(PurgeRequestDto request, CancellationToken ct);
}

public sealed class DemoDataService(
    ISiemDbContext db,
    SimulatorRunner runner,
    DetectionControl detectionControl,
    IAuditService audit) : IDemoDataService
{
    public const string ConfirmationPhrase = "DELETE DEMO DATA";

    public async Task<PurgeResultDto> PurgeAsync(PurgeRequestDto request, CancellationToken ct)
    {
        if (!string.Equals(request.Confirm, ConfirmationPhrase, StringComparison.Ordinal))
        {
            throw RequestValidationException.Single("confirm", $"Для подтверждения введите «{ConfirmationPhrase}»");
        }
        if (runner.IsRunning)
        {
            throw new ConflictException("Остановите сценарий симулятора перед очисткой");
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var incidents = await db.Incidents.Where(i => i.IsDemo).ExecuteDeleteAsync(ct);
        var alerts = await db.Alerts.Where(a => a.IsDemo).ExecuteDeleteAsync(ct);
        var events = await db.SecurityEvents.Where(e => e.IsDemo).ExecuteDeleteAsync(ct);
        audit.Record(AuditActions.DemoDataPurged, null, null, new { incidents, alerts, events });
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        detectionControl.RequestReset();
        return new PurgeResultDto(incidents, alerts, events);
    }
}
