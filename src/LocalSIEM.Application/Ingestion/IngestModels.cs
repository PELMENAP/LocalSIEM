using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using LocalSIEM.Application.Common;
using LocalSIEM.Domain.Enums;

namespace LocalSIEM.Application.Ingestion;

public sealed class IngestEventDto
{
    public DateTimeOffset? Timestamp { get; set; }
    public string? EventType { get; set; }
    public string? Severity { get; set; }
    public string? SourceIp { get; set; }
    public string? TargetIp { get; set; }
    public string? Username { get; set; }
    public string? Hostname { get; set; }
    public string? Message { get; set; }
    public Guid? CorrelationId { get; set; }
    public Dictionary<string, JsonElement>? Attributes { get; set; }
    public JsonElement? Raw { get; set; }
}

public sealed class IngestBatchDto
{
    public List<IngestEventDto>? Events { get; set; }
}

public sealed record IngestResult(int Accepted, IReadOnlyList<Guid> EventIds);

public static partial class IngestValidator
{
    public const int MaxBatchSize = 500;
    public const int MaxRawBytes = 32 * 1024;
    public const int MaxAttributes = 32;
    public static readonly TimeSpan MaxClockSkew = TimeSpan.FromMinutes(5);

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9_.-]{0,63}$")]
    private static partial Regex EventTypePattern();

    public static ValidationErrors Validate(IReadOnlyList<IngestEventDto>? events, DateTimeOffset now)
    {
        var errors = new ValidationErrors();
        if (events is null || events.Count == 0)
        {
            errors.Add("events", "Пакет должен содержать хотя бы одно событие");
            return errors;
        }
        if (events.Count > MaxBatchSize)
        {
            errors.Add("events", $"Пакет не может содержать больше {MaxBatchSize} событий");
            return errors;
        }

        for (var i = 0; i < events.Count; i++)
        {
            var prefix = $"events[{i}]";
            var e = events[i];
            if (e is null)
            {
                errors.Add(prefix, "Событие не может быть null");
                continue;
            }
            if (e.Timestamp is null)
            {
                errors.Add($"{prefix}.timestamp", "Поле обязательно");
            }
            else if (e.Timestamp.Value > now + MaxClockSkew)
            {
                errors.Add($"{prefix}.timestamp", "Время события в будущем");
            }
            if (string.IsNullOrWhiteSpace(e.EventType) || !EventTypePattern().IsMatch(e.EventType))
            {
                errors.Add($"{prefix}.eventType", "Тип события обязателен: латиница, цифры, _ . -, до 64 символов");
            }
            if (e.Severity is not null && !Enum.TryParse<Severity>(e.Severity, true, out _))
            {
                errors.Add($"{prefix}.severity", "Допустимо: Info, Low, Medium, High, Critical");
            }
            if (e.SourceIp is not null && !IPAddress.TryParse(e.SourceIp, out _))
            {
                errors.Add($"{prefix}.sourceIp", "Некорректный IP-адрес");
            }
            if (e.TargetIp is not null && !IPAddress.TryParse(e.TargetIp, out _))
            {
                errors.Add($"{prefix}.targetIp", "Некорректный IP-адрес");
            }
            if (e.Username is { Length: > 256 })
            {
                errors.Add($"{prefix}.username", "Не длиннее 256 символов");
            }
            if (e.Hostname is { Length: > 256 })
            {
                errors.Add($"{prefix}.hostname", "Не длиннее 256 символов");
            }
            if (e.Message is { Length: > 2000 })
            {
                errors.Add($"{prefix}.message", "Не длиннее 2000 символов");
            }
            if (e.Attributes is not null)
            {
                if (e.Attributes.Count > MaxAttributes)
                {
                    errors.Add($"{prefix}.attributes", $"Не больше {MaxAttributes} атрибутов");
                }
                foreach (var (key, value) in e.Attributes)
                {
                    if (key.Length is 0 or > 64)
                    {
                        errors.Add($"{prefix}.attributes", "Имя атрибута — от 1 до 64 символов");
                    }
                    if (value.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
                    {
                        errors.Add($"{prefix}.attributes.{key}", "Значение атрибута должно быть скалярным");
                    }
                }
            }
            if (e.Raw is { } raw && raw.GetRawText().Length > MaxRawBytes)
            {
                errors.Add($"{prefix}.raw", $"Исходные данные больше {MaxRawBytes / 1024} КБ");
            }
        }
        return errors;
    }
}
