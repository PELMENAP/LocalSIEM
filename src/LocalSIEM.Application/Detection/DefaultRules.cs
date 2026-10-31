using LocalSIEM.Domain;
using LocalSIEM.Domain.Entities;
using LocalSIEM.Domain.Enums;

namespace LocalSIEM.Application.Detection;

public static class DefaultRules
{
    public const string BruteForce = "BRUTE_FORCE";
    public const string PasswordSpraying = "PASSWORD_SPRAYING";
    public const string SuccessAfterBruteForce = "SUCCESS_AFTER_BRUTE_FORCE";
    public const string PrivilegeEscalation = "PRIVILEGE_ESCALATION";
    public const string SuspiciousAccess = "SUSPICIOUS_ACCESS";

    public static IReadOnlyList<DetectionRule> Create(DateTimeOffset now) =>
    [
        new()
        {
            Id = Guid.Parse("0199a000-0000-7000-8000-000000000001"),
            Code = BruteForce,
            Name = "Brute Force",
            Description = "Многочисленные неудачные попытки входа с одного IP-адреса.",
            RuleType = RuleType.Threshold,
            EventType = EventTypes.LoginFailed,
            Threshold = 5,
            WindowSeconds = 60,
            GroupBy = [GroupFields.SourceIp],
            Parameters = "{}",
            Severity = Severity.High,
            IsEnabled = true,
            CreatedAt = now,
            UpdatedAt = now
        },
        new()
        {
            Id = Guid.Parse("0199a000-0000-7000-8000-000000000002"),
            Code = PasswordSpraying,
            Name = "Password Spraying",
            Description = "Неудачные попытки входа под множеством разных учётных записей с одного IP-адреса.",
            RuleType = RuleType.DistinctCount,
            EventType = EventTypes.LoginFailed,
            Threshold = 5,
            WindowSeconds = 120,
            GroupBy = [GroupFields.SourceIp],
            Parameters = new RuleParameters { DistinctField = GroupFields.Username }.ToJson(),
            Severity = Severity.Critical,
            IsEnabled = true,
            CreatedAt = now,
            UpdatedAt = now
        },
        new()
        {
            Id = Guid.Parse("0199a000-0000-7000-8000-000000000003"),
            Code = SuccessAfterBruteForce,
            Name = "Successful Login After Brute Force",
            Description = "Успешный вход после серии неудачных попыток с того же IP под той же учётной записью.",
            RuleType = RuleType.Sequence,
            EventType = EventTypes.LoginFailed,
            Threshold = 3,
            WindowSeconds = 300,
            GroupBy = [GroupFields.SourceIp, GroupFields.Username],
            Parameters = new RuleParameters { ThenEventType = EventTypes.LoginSuccess }.ToJson(),
            Severity = Severity.Critical,
            IsEnabled = true,
            CreatedAt = now,
            UpdatedAt = now
        },
        new()
        {
            Id = Guid.Parse("0199a000-0000-7000-8000-000000000004"),
            Code = PrivilegeEscalation,
            Name = "Privilege Escalation",
            Description = "Учётной записи назначены административные полномочия.",
            RuleType = RuleType.SingleEvent,
            EventType = EventTypes.RoleChanged,
            Threshold = 1,
            WindowSeconds = 300,
            GroupBy = [GroupFields.Username],
            Parameters = new RuleParameters
            {
                Field = AttributeNames.NewRole,
                Values = ["Administrator", "Administrators", "Domain Admins", "Enterprise Admins", "root"]
            }.ToJson(),
            Severity = Severity.High,
            IsEnabled = true,
            CreatedAt = now,
            UpdatedAt = now
        },
        new()
        {
            Id = Guid.Parse("0199a000-0000-7000-8000-000000000005"),
            Code = SuspiciousAccess,
            Name = "Suspicious Access",
            Description = "Большое количество обращений пользователя к защищённым ресурсам.",
            RuleType = RuleType.Threshold,
            EventType = EventTypes.ResourceAccess,
            Threshold = 20,
            WindowSeconds = 60,
            GroupBy = [GroupFields.Username],
            Parameters = new RuleParameters { Field = AttributeNames.Protected, Values = ["true"] }.ToJson(),
            Severity = Severity.Medium,
            IsEnabled = true,
            CreatedAt = now,
            UpdatedAt = now
        }
    ];
}
