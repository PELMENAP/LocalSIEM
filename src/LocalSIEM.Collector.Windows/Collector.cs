using System.Diagnostics.Eventing.Reader;
using System.Net;
using System.Net.Http.Json;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Threading.Channels;
using System.Xml.Linq;
using Microsoft.Extensions.Options;

namespace LocalSIEM.Collector.Windows;

public sealed class CollectorOptions
{
    public const string Section = "Collector";

    public string ApiUrl { get; set; } = "http://localhost:5080";
    public string ApiKey { get; set; } = string.Empty;
    public string LogName { get; set; } = "Security";
    public int[] EventIds { get; set; } = [4624, 4625, 4672, 4720];
    public string[] IgnoredAccounts { get; set; } = [];
    public bool IgnoreMachineAccounts { get; set; } = true;
    public int BatchSize { get; set; } = 200;
    public int FlushIntervalMilliseconds { get; set; } = 1000;
    public int BufferCapacity { get; set; } = 20_000;
}

public sealed record CollectedEvent(
    DateTimeOffset Timestamp,
    string EventType,
    string? SourceIp,
    string? Username,
    string? Hostname,
    string Message,
    Dictionary<string, object> Attributes,
    Dictionary<string, object> Raw);

public static class WindowsEventMapper
{
    private static readonly XNamespace Ns = "http://schemas.microsoft.com/win/2004/08/events/event";

    public static CollectedEvent? Map(int eventId, DateTimeOffset timestamp, string machineName, long? recordId, string xml, CollectorOptions options)
    {
        var data = ReadEventData(xml);
        string? username;
        string? domain;
        switch (eventId)
        {
            case 4672:
                username = Value(data, "SubjectUserName");
                domain = Value(data, "SubjectDomainName");
                break;
            default:
                username = Value(data, "TargetUserName");
                domain = Value(data, "TargetDomainName");
                break;
        }

        if (username is not null && (options.IgnoredAccounts.Contains(username, StringComparer.OrdinalIgnoreCase) ||
                                     (options.IgnoreMachineAccounts && username.EndsWith('$'))))
        {
            return null;
        }

        var attributes = new Dictionary<string, object> { ["windowsEventId"] = eventId };
        AddIfPresent(attributes, "domain", domain);
        AddIfPresent(attributes, "logonType", Value(data, "LogonType"));
        AddIfPresent(attributes, "status", Value(data, "Status"));
        AddIfPresent(attributes, "subStatus", Value(data, "SubStatus"));
        AddIfPresent(attributes, "failureReason", Value(data, "FailureReason"));
        AddIfPresent(attributes, "authenticationPackage", Value(data, "AuthenticationPackageName"));
        if (eventId == 4720)
        {
            AddIfPresent(attributes, "createdBy", Value(data, "SubjectUserName"));
        }
        if (eventId == 4672)
        {
            AddIfPresent(attributes, "privileges", Value(data, "PrivilegeList")?.Replace("\r", string.Empty).Replace("\n", " ").Trim());
        }

        var message = eventId switch
        {
            4624 => "Успешный вход в систему",
            4625 => "Неудачная попытка входа",
            4672 => "Специальные привилегии назначены новому входу",
            4720 => "Создана учётная запись пользователя",
            _ => $"Событие безопасности {eventId}"
        };
        var raw = new Dictionary<string, object> { ["eventId"] = eventId, ["machineName"] = machineName, ["xml"] = xml };
        if (recordId is { } id)
        {
            raw["recordId"] = id;
        }

        return new CollectedEvent(
            timestamp,
            "WindowsEvent",
            NormalizeIp(Value(data, "IpAddress")),
            username,
            Value(data, "WorkstationName") ?? machineName,
            message,
            attributes,
            raw);
    }

    public static Dictionary<string, string> ReadEventData(string xml)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var document = XDocument.Parse(xml);
        foreach (var element in document.Descendants(Ns + "Data"))
        {
            var name = element.Attribute("Name")?.Value;
            if (!string.IsNullOrEmpty(name))
            {
                result[name] = element.Value;
            }
        }
        return result;
    }

    public static string? NormalizeIp(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value == "-")
        {
            return null;
        }
        if (!IPAddress.TryParse(value, out var ip))
        {
            return null;
        }
        return ip.IsIPv4MappedToIPv6 ? ip.MapToIPv4().ToString() : ip.ToString();
    }

    private static string? Value(Dictionary<string, string> data, string name) =>
        data.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value) && value != "-" ? value.Trim() : null;

    private static void AddIfPresent(Dictionary<string, object> target, string key, string? value)
    {
        if (value is not null)
        {
            target[key] = value;
        }
    }
}

[SupportedOSPlatform("windows")]
public sealed class SecurityLogWatcher(
    Channel<CollectedEvent> channel,
    IOptions<CollectorOptions> options,
    ILogger<SecurityLogWatcher> logger) : BackgroundService
{
    private long dropped;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        var filter = string.Join(" or ", settings.EventIds.Select(id => $"EventID={id}"));
        var xpath = $"*[System[({filter})]]";
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var query = new EventLogQuery(settings.LogName, PathType.LogName, xpath);
                using var watcher = new EventLogWatcher(query);
                watcher.EventRecordWritten += OnEventRecordWritten;
                watcher.Enabled = true;
                logger.LogInformation("Подписка на журнал {Log} активна, события: {Ids}", settings.LogName, string.Join(", ", settings.EventIds));
                await Task.Delay(Timeout.Infinite, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (UnauthorizedAccessException)
            {
                logger.LogError("Нет прав на чтение журнала {Log}. Добавьте пользователя в группу «Event Log Readers» (Читатели журнала событий) или запустите сборщик от имени администратора. Повтор через 30 с",
                    settings.LogName);
                await DelayAsync(stoppingToken);
            }
            catch (EventLogException ex)
            {
                logger.LogError(ex, "Журнал {Log} недоступен. Повтор через 30 с", settings.LogName);
                await DelayAsync(stoppingToken);
            }
        }
    }

    private void OnEventRecordWritten(object? sender, EventRecordWrittenEventArgs args)
    {
        if (args.EventException is not null)
        {
            logger.LogWarning(args.EventException, "Ошибка чтения записи журнала");
            return;
        }
        using var record = args.EventRecord;
        if (record is null)
        {
            return;
        }
        try
        {
            var timestamp = record.TimeCreated is { } created ? new DateTimeOffset(created.ToUniversalTime(), TimeSpan.Zero) : DateTimeOffset.UtcNow;
            var collected = WindowsEventMapper.Map(record.Id, timestamp, record.MachineName, record.RecordId, record.ToXml(), options.Value);
            if (collected is not null && !channel.Writer.TryWrite(collected))
            {
                var total = Interlocked.Increment(ref dropped);
                if (total % 1000 == 1)
                {
                    logger.LogWarning("Буфер сборщика переполнен, отброшено событий: {Dropped}", total);
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Не удалось разобрать событие {EventId}", record.Id);
        }
    }

    private static async Task DelayAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(30), ct);
        }
        catch (OperationCanceledException)
        {
        }
    }
}

public sealed class EventForwarder(HttpClient http, IOptions<CollectorOptions> options)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<HttpStatusCode> SendAsync(IReadOnlyList<CollectedEvent> events, CancellationToken ct)
    {
        var settings = options.Value;
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(new Uri(settings.ApiUrl), "/api/ingest/events"))
        {
            Content = JsonContent.Create(new { events }, options: Json)
        };
        request.Headers.Add("X-Api-Key", settings.ApiKey);
        using var response = await http.SendAsync(request, ct);
        return response.StatusCode;
    }
}

public sealed class ForwarderService(
    Channel<CollectedEvent> channel,
    EventForwarder forwarder,
    IOptions<CollectorOptions> options,
    ILogger<ForwarderService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        if (string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            logger.LogCritical("Не задан Collector:ApiKey — создайте источник WindowsCollector в LocalSIEM (Settings → Источники) и укажите ключ");
            return;
        }

        var batch = new List<CollectedEvent>(settings.BatchSize);
        var flushInterval = TimeSpan.FromMilliseconds(Math.Max(100, settings.FlushIntervalMilliseconds));
        var backoff = TimeSpan.FromSeconds(1);
        while (!stoppingToken.IsCancellationRequested)
        {
            if (batch.Count == 0)
            {
                if (!await channel.Reader.WaitToReadAsync(stoppingToken))
                {
                    return;
                }
                using var window = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                window.CancelAfter(flushInterval);
                try
                {
                    while (batch.Count < settings.BatchSize)
                    {
                        if (channel.Reader.TryRead(out var item))
                        {
                            batch.Add(item);
                            continue;
                        }
                        await channel.Reader.WaitToReadAsync(window.Token);
                    }
                }
                catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
                {
                }
            }

            try
            {
                var status = await forwarder.SendAsync(batch, stoppingToken);
                if (status == HttpStatusCode.Accepted)
                {
                    logger.LogInformation("Отправлено событий: {Count}", batch.Count);
                    batch.Clear();
                    backoff = TimeSpan.FromSeconds(1);
                    continue;
                }
                if (status == HttpStatusCode.BadRequest)
                {
                    logger.LogError("LocalSIEM отклонил пакет из {Count} событий как некорректный — пакет отброшен", batch.Count);
                    batch.Clear();
                    continue;
                }
                if (status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                {
                    logger.LogError("Ключ API отклонён ({Status}) — проверьте Collector:ApiKey и что источник включён", status);
                }
                else
                {
                    logger.LogWarning("LocalSIEM ответил {Status}, повтор через {Delay}", status, backoff);
                }
            }
            catch (HttpRequestException ex)
            {
                logger.LogWarning("LocalSIEM недоступен ({Message}), повтор через {Delay}", ex.Message, backoff);
            }
            catch (TaskCanceledException) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning("Тайм-аут отправки, повтор через {Delay}", backoff);
            }

            await Task.Delay(backoff, stoppingToken);
            backoff = TimeSpan.FromSeconds(Math.Min(backoff.TotalSeconds * 2, 60));
        }
    }
}
