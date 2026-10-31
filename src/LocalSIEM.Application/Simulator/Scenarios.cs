using LocalSIEM.Domain;

namespace LocalSIEM.Application.Simulator;

public sealed record ScenarioInfo(
    string Key, string Title, string Description, int DefaultCount, double DefaultRate,
    string? DefaultSourceIp, string? DefaultUsername, string ExpectedAlerts);

public sealed record SimEvent(
    string EventType, string? SourceIp, string? TargetIp, string? Username, string? Hostname, string Message,
    IReadOnlyDictionary<string, object>? Attributes = null);

public sealed record ScenarioOptions(int Count, string? SourceIp, string? Username);

public interface IScenario
{
    ScenarioInfo Info { get; }
    IEnumerable<SimEvent> Generate(ScenarioOptions options, Random random);
}

internal static class Population
{
    public static readonly string[] Users =
        ["ivanov", "petrova", "sidorov", "kuznetsova", "smirnov", "popova", "volkov", "sokolova", "lebedev", "novikova"];

    public static readonly string[] Workstations = ["10.10.0.11", "10.10.0.12", "10.10.0.13", "10.10.0.14", "10.10.0.15"];

    public static readonly string[] Resources =
        ["/portal/news", "/crm/clients", "/wiki/onboarding", "/mail/inbox", "/hr/vacations", "/reports/sales"];

    public const string Server = "10.10.0.2";
    public const string ServerName = "SRV-APP-01";

    public static string Host(string ip) => "WS-" + ip[(ip.LastIndexOf('.') + 1)..].PadLeft(3, '0');
}

public sealed class NormalActivityScenario : IScenario
{
    public ScenarioInfo Info { get; } = new("normal", "Нормальная активность",
        "Входы, обращения к открытым ресурсам и выходы 10 сотрудников с 5 рабочих станций.",
        60, 2, null, null, "Нет");

    public IEnumerable<SimEvent> Generate(ScenarioOptions options, Random random)
    {
        for (var i = 0; i < options.Count; i++)
        {
            var user = options.Username ?? Population.Users[random.Next(Population.Users.Length)];
            var ip = options.SourceIp ?? Population.Workstations[random.Next(Population.Workstations.Length)];
            var roll = random.Next(100);
            if (roll < 30)
            {
                yield return new SimEvent(EventTypes.LoginSuccess, ip, Population.Server, user, Population.Host(ip), "Успешный вход");
            }
            else if (roll < 80)
            {
                var resource = Population.Resources[random.Next(Population.Resources.Length)];
                yield return new SimEvent(EventTypes.ResourceAccess, ip, Population.Server, user, Population.Host(ip),
                    $"Обращение к {resource}",
                    new Dictionary<string, object> { [AttributeNames.Resource] = resource, [AttributeNames.Protected] = false });
            }
            else
            {
                yield return new SimEvent(EventTypes.Logout, ip, Population.Server, user, Population.Host(ip), "Выход");
            }
        }
    }
}

public sealed class FailedSeriesScenario : IScenario
{
    public ScenarioInfo Info { get; } = new("failed-series", "Серия неудачных входов",
        "Несколько неудачных входов одной учётной записи — ниже порога Brute Force.",
        4, 1, "10.10.0.77", "petrova", "Нет — порог 5 не достигнут");

    public IEnumerable<SimEvent> Generate(ScenarioOptions options, Random random)
    {
        var ip = options.SourceIp ?? Info.DefaultSourceIp!;
        var user = options.Username ?? Info.DefaultUsername!;
        for (var i = 0; i < options.Count; i++)
        {
            yield return new SimEvent(EventTypes.LoginFailed, ip, Population.Server, user, Population.Host(ip), "Неверный пароль");
        }
    }
}

public sealed class BruteForceScenario : IScenario
{
    public ScenarioInfo Info { get; } = new("brute-force", "Brute Force",
        "Подбор пароля к одной учётной записи с внешнего IP.",
        20, 2, "203.0.113.10", "admin", "Brute Force (High), один алерт, число событий растёт");

    public IEnumerable<SimEvent> Generate(ScenarioOptions options, Random random)
    {
        var ip = options.SourceIp ?? Info.DefaultSourceIp!;
        var user = options.Username ?? Info.DefaultUsername!;
        for (var i = 0; i < options.Count; i++)
        {
            yield return new SimEvent(EventTypes.LoginFailed, ip, Population.Server, user, "unknown", "Неверный пароль");
        }
    }
}

public sealed class PasswordSprayingScenario : IScenario
{
    public ScenarioInfo Info { get; } = new("password-spraying", "Password Spraying",
        "Один пароль перебирается по множеству учётных записей с одного IP.",
        10, 2, "203.0.113.20", null, "Password Spraying (Critical) + Brute Force (High)");

    public IEnumerable<SimEvent> Generate(ScenarioOptions options, Random random)
    {
        var ip = options.SourceIp ?? Info.DefaultSourceIp!;
        for (var i = 0; i < options.Count; i++)
        {
            var user = options.Username is null
                ? (i < Population.Users.Length ? Population.Users[i] : $"user{i + 1:D3}")
                : $"{options.Username}{i + 1}";
            yield return new SimEvent(EventTypes.LoginFailed, ip, Population.Server, user, "unknown", "Неверный пароль");
        }
    }
}

public sealed class SuccessAfterFailuresScenario : IScenario
{
    public ScenarioInfo Info { get; } = new("success-after-failures", "Успешный вход после серии",
        "Серия неудачных входов и затем успешный вход с того же IP под той же учётной записью.",
        7, 2, "203.0.113.30", "petrova", "Brute Force (High) + Successful Login After Brute Force (Critical)");

    public IEnumerable<SimEvent> Generate(ScenarioOptions options, Random random)
    {
        var ip = options.SourceIp ?? Info.DefaultSourceIp!;
        var user = options.Username ?? Info.DefaultUsername!;
        var failures = Math.Max(1, options.Count - 1);
        for (var i = 0; i < failures; i++)
        {
            yield return new SimEvent(EventTypes.LoginFailed, ip, Population.Server, user, "unknown", "Неверный пароль");
        }
        yield return new SimEvent(EventTypes.LoginSuccess, ip, Population.Server, user, "unknown", "Успешный вход");
    }
}

public sealed class PrivilegeEscalationScenario : IScenario
{
    public ScenarioInfo Info { get; } = new("privilege-escalation", "Изменение привилегий",
        "Учётной записи назначается роль Administrator.",
        1, 1, "10.10.0.12", "sidorov", "Privilege Escalation (High)");

    public IEnumerable<SimEvent> Generate(ScenarioOptions options, Random random)
    {
        var ip = options.SourceIp ?? Info.DefaultSourceIp!;
        var user = options.Username ?? Info.DefaultUsername!;
        for (var i = 0; i < options.Count; i++)
        {
            yield return new SimEvent(EventTypes.RoleChanged, ip, "10.10.0.3", user, "DC-01",
                $"Пользователю {user} назначена роль Administrator",
                new Dictionary<string, object> { [AttributeNames.NewRole] = "Administrator", ["oldRole"] = "User", ["changedBy"] = "helpdesk" });
        }
    }
}

public sealed class SuspiciousAccessScenario : IScenario
{
    public ScenarioInfo Info { get; } = new("suspicious-access", "Подозрительный доступ",
        "Массовое чтение файлов из защищённого каталога одним пользователем.",
        30, 3, "10.10.0.14", "volkov", "Suspicious Access (Medium)");

    public IEnumerable<SimEvent> Generate(ScenarioOptions options, Random random)
    {
        var ip = options.SourceIp ?? Info.DefaultSourceIp!;
        var user = options.Username ?? Info.DefaultUsername!;
        for (var i = 0; i < options.Count; i++)
        {
            var resource = $"/finance/payroll/{2026 - i / 12}-{i % 12 + 1:D2}.xlsx";
            yield return new SimEvent(EventTypes.ResourceAccess, ip, Population.Server, user, Population.Host(ip),
                $"Чтение {resource}",
                new Dictionary<string, object> { [AttributeNames.Resource] = resource, [AttributeNames.Protected] = true });
        }
    }
}
