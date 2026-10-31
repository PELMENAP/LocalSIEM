using System.Text.Json;
using LocalSIEM.Application.Common;
using LocalSIEM.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace LocalSIEM.Application.Settings;

public sealed record SystemSettingsDto(int MaxLinkedEventsPerAlert, int SourceIdleSeconds, int SimulatorMaxRatePerSecond)
{
    public static readonly SystemSettingsDto Default = new(1000, 300, 200);
}

public sealed class SettingsCache
{
    private volatile SystemSettingsDto current = SystemSettingsDto.Default;

    public SystemSettingsDto Current => current;

    public void Set(SystemSettingsDto value) => current = value;
}

public interface ISettingsService
{
    Task<SystemSettingsDto> LoadAsync(CancellationToken ct);
    Task<SystemSettingsDto> UpdateAsync(SystemSettingsDto dto, CancellationToken ct);
}

public sealed class SettingsService(ISiemDbContext db, SettingsCache cache, IAuditService audit, ICurrentUser currentUser, TimeProvider clock)
    : ISettingsService
{
    private const string Key = "system";

    public async Task<SystemSettingsDto> LoadAsync(CancellationToken ct)
    {
        var row = await db.SystemSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Key == Key, ct);
        var value = row is null
            ? SystemSettingsDto.Default
            : JsonSerializer.Deserialize<SystemSettingsDto>(row.Value, JsonDefaults.Options) ?? SystemSettingsDto.Default;
        cache.Set(value);
        return value;
    }

    public async Task<SystemSettingsDto> UpdateAsync(SystemSettingsDto dto, CancellationToken ct)
    {
        var errors = new ValidationErrors();
        if (dto.MaxLinkedEventsPerAlert is < 10 or > 100_000)
        {
            errors.Add("maxLinkedEventsPerAlert", "Допустимо от 10 до 100000");
        }
        if (dto.SourceIdleSeconds is < 10 or > 86_400)
        {
            errors.Add("sourceIdleSeconds", "Допустимо от 10 до 86400");
        }
        if (dto.SimulatorMaxRatePerSecond is < 1 or > 1000)
        {
            errors.Add("simulatorMaxRatePerSecond", "Допустимо от 1 до 1000");
        }
        errors.ThrowIfAny();

        var row = await db.SystemSettings.FirstOrDefaultAsync(s => s.Key == Key, ct);
        if (row is null)
        {
            row = new SystemSetting { Key = Key };
            db.SystemSettings.Add(row);
        }
        row.Value = JsonSerializer.Serialize(dto, JsonDefaults.Options);
        row.UpdatedAt = clock.GetUtcNow();
        row.UpdatedBy = currentUser.UserId;
        audit.Record(AuditActions.SettingsUpdated, nameof(SystemSetting), null, dto);
        await db.SaveChangesAsync(ct);
        cache.Set(dto);
        return dto;
    }
}
