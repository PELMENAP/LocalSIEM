using System.Security.Cryptography;
using LocalSIEM.Application.Common;
using LocalSIEM.Application.Detection;
using LocalSIEM.Application.Settings;
using LocalSIEM.Domain;
using LocalSIEM.Domain.Entities;
using LocalSIEM.Domain.Enums;
using LocalSIEM.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LocalSIEM.Infrastructure;

public sealed class IdentityPasswordHasher : Application.Common.IPasswordHasher
{
    private static readonly User Subject = new();
    private readonly PasswordHasher<User> inner = new();

    public string Hash(string password) => inner.HashPassword(Subject, password);

    public bool Verify(string hash, string password)
    {
        if (string.IsNullOrEmpty(hash))
        {
            return false;
        }
        try
        {
            return inner.VerifyHashedPassword(Subject, hash, password) != PasswordVerificationResult.Failed;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}

public static class InfrastructureRegistration
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<SiemDbContext>(options => options.UseNpgsql(connectionString, npgsql => npgsql.CommandTimeout(60)));
        services.AddScoped<ISiemDbContext>(sp => sp.GetRequiredService<SiemDbContext>());
        services.AddSingleton<Application.Common.IPasswordHasher, IdentityPasswordHasher>();
        services.AddScoped<DatabaseInitializer>();
        return services;
    }
}

public sealed class DatabaseInitializer(
    SiemDbContext db,
    Application.Common.IPasswordHasher hasher,
    ISettingsService settings,
    IConfiguration configuration,
    TimeProvider clock,
    ILogger<DatabaseInitializer> logger)
{
    public async Task InitializeAsync(CancellationToken ct)
    {
        if (db.Database.GetMigrations().Any())
        {
            await db.Database.MigrateAsync(ct);
        }
        else
        {
            logger.LogWarning("Миграции не найдены — схема создаётся через EnsureCreated. Создайте миграцию: scripts/create-migration");
            await db.Database.EnsureCreatedAsync(ct);
        }

        var now = clock.GetUtcNow();
        await SeedRolesAsync(ct);
        await SeedSimulatorSourceAsync(now, ct);
        await SeedRulesAsync(now, ct);
        await SeedAdministratorAsync(now, ct);
        await settings.LoadAsync(ct);
    }

    private async Task SeedRolesAsync(CancellationToken ct)
    {
        (string Name, string Description)[] roles =
        [
            (RoleNames.Administrator, "Полный доступ: пользователи, правила, источники, настройки"),
            (RoleNames.SecurityAnalyst, "События, предупреждения, расследование инцидентов")
        ];
        foreach (var (name, description) in roles)
        {
            if (!await db.Roles.AnyAsync(r => r.Name == name, ct))
            {
                db.Roles.Add(new Role { Id = Ids.New(), Name = name, Description = description });
            }
        }
        await db.SaveChangesAsync(ct);
    }

    private async Task SeedSimulatorSourceAsync(DateTimeOffset now, CancellationToken ct)
    {
        if (!await db.EventSources.AnyAsync(s => s.Kind == SourceKind.Simulator, ct))
        {
            db.EventSources.Add(new EventSource
            {
                Id = Ids.New(),
                Name = "Event Simulator",
                Kind = SourceKind.Simulator,
                IsEnabled = true,
                CreatedAt = now
            });
            await db.SaveChangesAsync(ct);
        }
    }

    private async Task SeedRulesAsync(DateTimeOffset now, CancellationToken ct)
    {
        var existing = await db.DetectionRules.Select(r => r.Code).ToListAsync(ct);
        foreach (var rule in DefaultRules.Create(now))
        {
            if (!existing.Contains(rule.Code))
            {
                db.DetectionRules.Add(rule);
            }
        }
        await db.SaveChangesAsync(ct);
    }

    private async Task SeedAdministratorAsync(DateTimeOffset now, CancellationToken ct)
    {
        if (await db.Users.AnyAsync(ct))
        {
            return;
        }
        var username = configuration["Seed:AdminUser"];
        if (string.IsNullOrWhiteSpace(username))
        {
            username = "admin";
        }
        var password = configuration["Seed:AdminPassword"];
        var generated = string.IsNullOrWhiteSpace(password);
        if (generated)
        {
            password = Convert.ToBase64String(RandomNumberGenerator.GetBytes(12)).Replace('+', 'x').Replace('/', 'y');
        }
        var role = await db.Roles.FirstAsync(r => r.Name == RoleNames.Administrator, ct);
        db.Users.Add(new User
        {
            Id = Ids.New(),
            Username = username,
            DisplayName = "Администратор",
            PasswordHash = hasher.Hash(password!),
            RoleId = role.Id,
            IsActive = true,
            CreatedAt = now
        });
        await db.SaveChangesAsync(ct);
        if (generated)
        {
            logger.LogWarning("Создан администратор {Username} со сгенерированным паролем: {Password} — смените его в Settings", username, password);
        }
        else
        {
            logger.LogInformation("Создан администратор {Username}", username);
        }
    }
}
