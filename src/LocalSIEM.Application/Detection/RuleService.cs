using System.Text.RegularExpressions;
using LocalSIEM.Application.Common;
using LocalSIEM.Domain.Entities;
using LocalSIEM.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace LocalSIEM.Application.Detection;

public sealed record RuleDto(
    Guid Id, string Code, string Name, string Description, RuleType RuleType, string EventType, int Threshold,
    int WindowSeconds, int? CooldownSeconds, string[] GroupBy, RuleParameters Parameters, Severity Severity, bool IsEnabled,
    DateTimeOffset UpdatedAt, uint Version, int AlertsTotal, int Alerts24h, DateTimeOffset? LastDetectedAt);

public sealed class RuleInputDto
{
    public string? Code { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public RuleType RuleType { get; set; }
    public string EventType { get; set; } = string.Empty;
    public int Threshold { get; set; }
    public int WindowSeconds { get; set; }
    public int? CooldownSeconds { get; set; }
    public string[] GroupBy { get; set; } = [];
    public RuleParameters Parameters { get; set; } = new();
    public Severity Severity { get; set; }
    public bool IsEnabled { get; set; } = true;
    public uint? Version { get; set; }
}

public sealed record ToggleRuleDto(bool IsEnabled);

public interface IRuleService
{
    Task<IReadOnlyList<RuleDto>> ListAsync(CancellationToken ct);
    Task<RuleDto> GetAsync(Guid id, CancellationToken ct);
    Task<RuleDto> CreateAsync(RuleInputDto dto, CancellationToken ct);
    Task<RuleDto> UpdateAsync(Guid id, RuleInputDto dto, CancellationToken ct);
    Task<RuleDto> ToggleAsync(Guid id, bool isEnabled, CancellationToken ct);
}

public sealed partial class RuleService(
    ISiemDbContext db,
    RuleCatalog catalog,
    IAuditService audit,
    TimeProvider clock) : IRuleService
{
    [GeneratedRegex("^[A-Z][A-Z0-9_]{2,49}$")]
    private static partial Regex CodePattern();

    public async Task<IReadOnlyList<RuleDto>> ListAsync(CancellationToken ct)
    {
        var since = clock.GetUtcNow().AddHours(-24);
        var stats = await db.Alerts.AsNoTracking()
            .GroupBy(a => a.RuleId)
            .Select(g => new
            {
                RuleId = g.Key,
                Total = g.Count(),
                Last24 = g.Count(a => a.DetectedAt >= since),
                Last = g.Max(a => a.DetectedAt)
            })
            .ToDictionaryAsync(s => s.RuleId, ct);
        var rules = await db.DetectionRules.AsNoTracking().OrderBy(r => r.CreatedAt).ThenBy(r => r.Code).ToListAsync(ct);
        return rules.Select(r =>
        {
            stats.TryGetValue(r.Id, out var s);
            return ToDto(r, s?.Total ?? 0, s?.Last24 ?? 0, s?.Last);
        }).ToList();
    }

    public async Task<RuleDto> GetAsync(Guid id, CancellationToken ct)
    {
        var rules = await ListAsync(ct);
        return rules.FirstOrDefault(r => r.Id == id) ?? throw new NotFoundException("Правило не найдено");
    }

    public async Task<RuleDto> CreateAsync(RuleInputDto dto, CancellationToken ct)
    {
        var errors = Validate(dto);
        var code = dto.Code?.Trim().ToUpperInvariant() ?? string.Empty;
        if (!CodePattern().IsMatch(code))
        {
            errors.Add("code", "Код — 3–50 символов: A-Z, 0-9, _ (начинается с буквы)");
        }
        errors.ThrowIfAny();
        if (await db.DetectionRules.AnyAsync(r => r.Code == code, ct))
        {
            throw new ConflictException("Правило с таким кодом уже существует");
        }

        var now = clock.GetUtcNow();
        var rule = new DetectionRule { Id = Ids.New(), Code = code, CreatedAt = now };
        Apply(rule, dto, now);
        db.DetectionRules.Add(rule);
        audit.Record(AuditActions.RuleCreated, nameof(DetectionRule), rule.Id, new { rule.Code });
        await db.SaveChangesAsync(ct);
        catalog.MarkDirty();
        return await GetAsync(rule.Id, ct);
    }

    public async Task<RuleDto> UpdateAsync(Guid id, RuleInputDto dto, CancellationToken ct)
    {
        Validate(dto).ThrowIfAny();
        var rule = await db.DetectionRules.FirstOrDefaultAsync(r => r.Id == id, ct)
            ?? throw new NotFoundException("Правило не найдено");
        if (dto.Version is { } version)
        {
            db.Entry(rule).Property(r => r.Version).OriginalValue = version;
        }
        Apply(rule, dto, clock.GetUtcNow());
        audit.Record(AuditActions.RuleUpdated, nameof(DetectionRule), rule.Id, dto);
        await SaveAsync(ct);
        catalog.MarkDirty();
        return await GetAsync(rule.Id, ct);
    }

    public async Task<RuleDto> ToggleAsync(Guid id, bool isEnabled, CancellationToken ct)
    {
        var rule = await db.DetectionRules.FirstOrDefaultAsync(r => r.Id == id, ct)
            ?? throw new NotFoundException("Правило не найдено");
        rule.IsEnabled = isEnabled;
        rule.UpdatedAt = clock.GetUtcNow();
        audit.Record(AuditActions.RuleToggled, nameof(DetectionRule), rule.Id, new { isEnabled });
        await SaveAsync(ct);
        catalog.MarkDirty();
        return await GetAsync(rule.Id, ct);
    }

    private static ValidationErrors Validate(RuleInputDto dto)
    {
        var parameters = dto.Parameters ?? new RuleParameters();
        var errors = RuleDefinition.Validate(dto.RuleType, dto.EventType, dto.Threshold, dto.WindowSeconds,
            dto.CooldownSeconds, dto.GroupBy, parameters);
        if (string.IsNullOrWhiteSpace(dto.Name) || dto.Name.Length > 200)
        {
            errors.Add("name", "Название обязательно, до 200 символов");
        }
        if (dto.Description is { Length: > 2000 })
        {
            errors.Add("description", "Описание до 2000 символов");
        }
        if (!Enum.IsDefined(dto.Severity))
        {
            errors.Add("severity", "Неизвестная критичность");
        }
        if (!Enum.IsDefined(dto.RuleType))
        {
            errors.Add("ruleType", "Неизвестный тип правила");
        }
        return errors;
    }

    private static void Apply(DetectionRule rule, RuleInputDto dto, DateTimeOffset now)
    {
        rule.Name = dto.Name.Trim();
        rule.Description = dto.Description?.Trim() ?? string.Empty;
        rule.RuleType = dto.RuleType;
        rule.EventType = dto.EventType.Trim();
        rule.Threshold = dto.RuleType == RuleType.SingleEvent ? 1 : dto.Threshold;
        rule.WindowSeconds = dto.WindowSeconds;
        rule.CooldownSeconds = dto.CooldownSeconds;
        rule.GroupBy = dto.GroupBy.Distinct().ToArray();
        rule.Parameters = (dto.Parameters ?? new RuleParameters()).ToJson();
        rule.Severity = dto.Severity;
        rule.IsEnabled = dto.IsEnabled;
        rule.UpdatedAt = now;
    }

    private async Task SaveAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException("Правило изменено другим пользователем, обновите данные");
        }
    }

    private static RuleDto ToDto(DetectionRule r, int total, int last24, DateTimeOffset? last) => new(
        r.Id, r.Code, r.Name, r.Description, r.RuleType, r.EventType, r.Threshold, r.WindowSeconds, r.CooldownSeconds,
        r.GroupBy, RuleParameters.Parse(r.Parameters), r.Severity, r.IsEnabled, r.UpdatedAt, r.Version, total, last24, last);
}
