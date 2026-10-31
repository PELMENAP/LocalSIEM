using LocalSIEM.Application.Common;
using LocalSIEM.Application.Ingestion;
using LocalSIEM.Application.Settings;
using LocalSIEM.Domain.Entities;
using LocalSIEM.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace LocalSIEM.Application.Sources;

public sealed record SourceDto(
    Guid Id, string Name, SourceKind Kind, bool IsEnabled, DateTimeOffset? LastSeenAt, SourceStatus Status,
    bool HasApiKey, DateTimeOffset CreatedAt);

public sealed record CreateSourceDto(string Name, SourceKind Kind);

public sealed record UpdateSourceDto(string Name, bool IsEnabled);

public sealed record SourceWithKeyDto(SourceDto Source, string ApiKey);

public static class SourceStatusPolicy
{
    public static SourceStatus Compute(EventSource source, DateTimeOffset now, int idleSeconds) =>
        !source.IsEnabled
            ? SourceStatus.Disabled
            : source.LastSeenAt is { } seen && now - seen <= TimeSpan.FromSeconds(idleSeconds)
                ? SourceStatus.Online
                : SourceStatus.Idle;
}

public interface ISourceService
{
    Task<IReadOnlyList<SourceDto>> ListAsync(CancellationToken ct);
    Task<SourceWithKeyDto> CreateAsync(CreateSourceDto dto, CancellationToken ct);
    Task<SourceDto> UpdateAsync(Guid id, UpdateSourceDto dto, CancellationToken ct);
    Task<SourceWithKeyDto> RotateKeyAsync(Guid id, CancellationToken ct);
}

public sealed class SourceService(
    ISiemDbContext db,
    SourceKeyCache keyCache,
    SettingsCache settings,
    IAuditService audit,
    TimeProvider clock) : ISourceService
{
    public async Task<IReadOnlyList<SourceDto>> ListAsync(CancellationToken ct)
    {
        var sources = await db.EventSources.AsNoTracking().OrderBy(s => s.Kind).ThenBy(s => s.Name).ToListAsync(ct);
        var now = clock.GetUtcNow();
        return sources.Select(s => ToDto(s, now)).ToList();
    }

    public async Task<SourceWithKeyDto> CreateAsync(CreateSourceDto dto, CancellationToken ct)
    {
        var errors = new ValidationErrors();
        var name = dto.Name?.Trim() ?? string.Empty;
        if (name.Length is 0 or > 100)
        {
            errors.Add("name", "Имя обязательно, до 100 символов");
        }
        if (dto.Kind == SourceKind.Simulator)
        {
            errors.Add("kind", "Источник симулятора создаётся автоматически");
        }
        errors.ThrowIfAny();
        if (await db.EventSources.AnyAsync(s => s.Name == name, ct))
        {
            throw new ConflictException("Источник с таким именем уже существует");
        }

        var key = ApiKeys.Generate();
        var source = new EventSource
        {
            Id = Ids.New(),
            Name = name,
            Kind = dto.Kind,
            ApiKeyHash = ApiKeys.Hash(key),
            IsEnabled = true,
            CreatedAt = clock.GetUtcNow()
        };
        db.EventSources.Add(source);
        audit.Record(AuditActions.SourceCreated, nameof(EventSource), source.Id, new { source.Name, source.Kind });
        await db.SaveChangesAsync(ct);
        return new SourceWithKeyDto(ToDto(source, clock.GetUtcNow()), key);
    }

    public async Task<SourceDto> UpdateAsync(Guid id, UpdateSourceDto dto, CancellationToken ct)
    {
        var source = await db.EventSources.FirstOrDefaultAsync(s => s.Id == id, ct)
            ?? throw new NotFoundException("Источник не найден");
        var name = dto.Name?.Trim() ?? string.Empty;
        if (name.Length is 0 or > 100)
        {
            throw RequestValidationException.Single("name", "Имя обязательно, до 100 символов");
        }
        if (name != source.Name && await db.EventSources.AnyAsync(s => s.Name == name && s.Id != id, ct))
        {
            throw new ConflictException("Источник с таким именем уже существует");
        }
        source.Name = name;
        source.IsEnabled = dto.IsEnabled;
        audit.Record(AuditActions.SourceUpdated, nameof(EventSource), source.Id, new { source.Name, source.IsEnabled });
        await db.SaveChangesAsync(ct);
        keyCache.Invalidate();
        return ToDto(source, clock.GetUtcNow());
    }

    public async Task<SourceWithKeyDto> RotateKeyAsync(Guid id, CancellationToken ct)
    {
        var source = await db.EventSources.FirstOrDefaultAsync(s => s.Id == id, ct)
            ?? throw new NotFoundException("Источник не найден");
        if (source.Kind == SourceKind.Simulator)
        {
            throw new ConflictException("Симулятор работает без ключа API");
        }
        var key = ApiKeys.Generate();
        source.ApiKeyHash = ApiKeys.Hash(key);
        audit.Record(AuditActions.SourceKeyRotated, nameof(EventSource), source.Id);
        await db.SaveChangesAsync(ct);
        keyCache.Invalidate();
        return new SourceWithKeyDto(ToDto(source, clock.GetUtcNow()), key);
    }

    private SourceDto ToDto(EventSource s, DateTimeOffset now) => new(
        s.Id, s.Name, s.Kind, s.IsEnabled, s.LastSeenAt,
        SourceStatusPolicy.Compute(s, now, settings.Current.SourceIdleSeconds),
        s.ApiKeyHash is not null, s.CreatedAt);
}
