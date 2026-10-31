using LocalSIEM.Application.Detection;
using LocalSIEM.Domain;
using LocalSIEM.Domain.Enums;
using Xunit;

namespace LocalSIEM.Application.Tests;

public sealed class BruteForceTests
{
    [Fact]
    public async Task BelowThreshold_NoAlert()
    {
        var kit = new DetectionKit(DefaultRules.BruteForce);
        await kit.Failed(4);
        Assert.Empty(kit.Sink.Created);
    }

    [Fact]
    public async Task ThresholdReached_CreatesHighAlertWithAllEvents()
    {
        var kit = new DetectionKit(DefaultRules.BruteForce);
        await kit.Failed(5);
        var created = Assert.Single(kit.Sink.Created);
        Assert.Equal(Severity.High, created.Detection.Rule.Severity);
        Assert.Equal(5, created.Detection.EventIds.Length);
        Assert.Equal("10.0.0.5", created.Detection.Group.Values[GroupFields.SourceIp]);
    }

    [Fact]
    public async Task EventsExactlyOnWindowBoundary_Fires()
    {
        var kit = new DetectionKit(DefaultRules.BruteForce);
        await kit.Failed(5, start: 0, step: 15);
        Assert.Single(kit.Sink.Created);
    }

    [Fact]
    public async Task EventsJustOutsideWindow_NoAlert()
    {
        var kit = new DetectionKit(DefaultRules.BruteForce);
        await kit.Failed(4, start: 0, step: 15);
        await kit.Send(EventTypes.LoginFailed, 60.001);
        Assert.Empty(kit.Sink.Created);
    }

    [Fact]
    public async Task DifferentIps_NoAlert()
    {
        var kit = new DetectionKit(DefaultRules.BruteForce);
        for (var i = 0; i < 10; i++)
        {
            await kit.Send(EventTypes.LoginFailed, i, ip: $"10.0.0.{i + 1}");
        }
        Assert.Empty(kit.Sink.Created);
    }

    [Fact]
    public async Task ContinuedAttack_AppendsInsteadOfDuplicating()
    {
        var kit = new DetectionKit(DefaultRules.BruteForce);
        await kit.Failed(12);
        Assert.Single(kit.Sink.Created);
        Assert.Equal(7, kit.Sink.Appended.Count);
        Assert.All(kit.Sink.Appended, a => Assert.Equal(kit.Sink.Created[0].AlertId, a.AlertId));
    }

    [Fact]
    public async Task AfterCooldown_CreatesNewAlert()
    {
        var kit = new DetectionKit(DefaultRules.BruteForce);
        await kit.Failed(5, start: 0);
        await kit.Failed(5, start: 200);
        Assert.Equal(2, kit.Sink.Created.Count);
    }

    [Fact]
    public async Task ResolvedAlert_NextMatchCreatesNewAlert()
    {
        var kit = new DetectionKit(DefaultRules.BruteForce);
        await kit.Failed(5);
        kit.Pipeline.Engine.Suppression.CloseByAlert(kit.Sink.Created[0].AlertId);
        await kit.Send(EventTypes.LoginFailed, 5);
        Assert.Equal(2, kit.Sink.Created.Count);
    }

    [Fact]
    public async Task MissingGroupField_IsSkipped()
    {
        var kit = new DetectionKit(DefaultRules.BruteForce);
        await kit.Failed(10, ip: null);
        Assert.Empty(kit.Sink.Created);
    }

    [Fact]
    public async Task LateEventOutsideWindow_IsIgnored()
    {
        var kit = new DetectionKit(DefaultRules.BruteForce);
        await kit.Failed(5, start: 100);
        await kit.Send(EventTypes.LoginFailed, 10);
        Assert.Single(kit.Sink.Created);
        Assert.Empty(kit.Sink.Appended);
    }

    [Fact]
    public async Task LateEventInsideWindow_IsCounted()
    {
        var kit = new DetectionKit(DefaultRules.BruteForce);
        await kit.Failed(4, start: 10);
        await kit.Send(EventTypes.LoginFailed, 5);
        var created = Assert.Single(kit.Sink.Created);
        Assert.Equal(DetectionKit.T0.AddSeconds(5), created.Detection.FirstEventAt);
    }

    [Fact]
    public async Task OtherEventTypes_AreIgnored()
    {
        var kit = new DetectionKit(DefaultRules.BruteForce);
        for (var i = 0; i < 10; i++)
        {
            await kit.Send(EventTypes.LoginSuccess, i);
        }
        Assert.Empty(kit.Sink.Created);
    }
}

public sealed class PasswordSprayingTests
{
    [Fact]
    public async Task FiveDistinctUsers_FiresCriticalTogetherWithBruteForce()
    {
        var kit = new DetectionKit();
        for (var i = 0; i < 5; i++)
        {
            await kit.Send(EventTypes.LoginFailed, i, user: $"user{i}");
        }
        Assert.Contains(DefaultRules.PasswordSpraying, kit.Sink.CreatedCodes);
        Assert.Contains(DefaultRules.BruteForce, kit.Sink.CreatedCodes);
        var spraying = kit.Sink.Created.Single(c => c.Detection.Rule.Code == DefaultRules.PasswordSpraying);
        Assert.Equal(Severity.Critical, spraying.Detection.Rule.Severity);
    }

    [Fact]
    public async Task FourDistinctUsers_NoAlert()
    {
        var kit = new DetectionKit(DefaultRules.PasswordSpraying);
        for (var i = 0; i < 8; i++)
        {
            await kit.Send(EventTypes.LoginFailed, i, user: $"user{i % 4}");
        }
        Assert.Empty(kit.Sink.Created);
    }

    [Fact]
    public async Task UsernamesAreCaseInsensitive()
    {
        var kit = new DetectionKit(DefaultRules.PasswordSpraying);
        string[] users = ["Bob", "bob", "BOB", "alice", "ALICE", "Alice"];
        for (var i = 0; i < users.Length; i++)
        {
            await kit.Send(EventTypes.LoginFailed, i, user: users[i]);
        }
        Assert.Empty(kit.Sink.Created);
    }

    [Fact]
    public async Task UsersSpreadBeyondWindow_NoAlert()
    {
        var kit = new DetectionKit(DefaultRules.PasswordSpraying);
        for (var i = 0; i < 5; i++)
        {
            await kit.Send(EventTypes.LoginFailed, i * 50, user: $"user{i}");
        }
        Assert.Empty(kit.Sink.Created);
    }

    [Fact]
    public async Task DifferentIps_NoAlert()
    {
        var kit = new DetectionKit(DefaultRules.PasswordSpraying);
        for (var i = 0; i < 10; i++)
        {
            await kit.Send(EventTypes.LoginFailed, i, ip: $"10.0.1.{i % 2}", user: $"user{i}");
        }
        Assert.Equal(2, kit.Sink.Created.Count);
        Assert.Equal(2, kit.Sink.Created.Select(c => c.Detection.Group.Key).Distinct().Count());
    }
}

public sealed class SuccessAfterBruteForceTests
{
    [Fact]
    public async Task FailuresThenSuccess_FiresCritical()
    {
        var kit = new DetectionKit();
        await kit.Failed(3);
        await kit.Send(EventTypes.LoginSuccess, 10);
        var created = Assert.Single(kit.Sink.Created);
        Assert.Equal(DefaultRules.SuccessAfterBruteForce, created.Detection.Rule.Code);
        Assert.Equal(Severity.Critical, created.Detection.Rule.Severity);
        Assert.Equal(4, created.Detection.EventIds.Length);
    }

    [Fact]
    public async Task TwoFailuresThenSuccess_NoAlert()
    {
        var kit = new DetectionKit(DefaultRules.SuccessAfterBruteForce);
        await kit.Failed(2);
        await kit.Send(EventTypes.LoginSuccess, 10);
        Assert.Empty(kit.Sink.Created);
    }

    [Fact]
    public async Task SuccessForDifferentUser_NoAlert()
    {
        var kit = new DetectionKit(DefaultRules.SuccessAfterBruteForce);
        await kit.Failed(5, user: "alice");
        await kit.Send(EventTypes.LoginSuccess, 10, user: "bob");
        Assert.Empty(kit.Sink.Created);
    }

    [Fact]
    public async Task SuccessFromDifferentIp_NoAlert()
    {
        var kit = new DetectionKit(DefaultRules.SuccessAfterBruteForce);
        await kit.Failed(5, ip: "10.0.0.5");
        await kit.Send(EventTypes.LoginSuccess, 10, ip: "10.0.0.6");
        Assert.Empty(kit.Sink.Created);
    }

    [Fact]
    public async Task SuccessOutsideWindow_NoAlert()
    {
        var kit = new DetectionKit(DefaultRules.SuccessAfterBruteForce);
        await kit.Failed(3, start: 0);
        await kit.Send(EventTypes.LoginSuccess, 302.5);
        Assert.Empty(kit.Sink.Created);
    }

    [Fact]
    public async Task SuccessWithoutFailures_NoAlert()
    {
        var kit = new DetectionKit(DefaultRules.SuccessAfterBruteForce);
        await kit.Send(EventTypes.LoginSuccess, 0);
        Assert.Empty(kit.Sink.Created);
    }

    [Fact]
    public async Task SixFailuresAndSuccess_FiresBruteForceAndSuccessAfter()
    {
        var kit = new DetectionKit();
        await kit.Failed(6);
        await kit.Send(EventTypes.LoginSuccess, 7);
        Assert.Equal(2, kit.Sink.Created.Count);
        Assert.Contains(DefaultRules.BruteForce, kit.Sink.CreatedCodes);
        Assert.Contains(DefaultRules.SuccessAfterBruteForce, kit.Sink.CreatedCodes);
    }
}

public sealed class PrivilegeEscalationTests
{
    [Fact]
    public async Task AdministratorRole_FiresHigh()
    {
        var kit = new DetectionKit(DefaultRules.PrivilegeEscalation);
        await kit.Send(EventTypes.RoleChanged, 0, user: "bob", attributes: (AttributeNames.NewRole, "administrator"));
        var created = Assert.Single(kit.Sink.Created);
        Assert.Equal(Severity.High, created.Detection.Rule.Severity);
        Assert.Single(created.Detection.EventIds);
    }

    [Fact]
    public async Task OrdinaryRole_NoAlert()
    {
        var kit = new DetectionKit(DefaultRules.PrivilegeEscalation);
        await kit.Send(EventTypes.RoleChanged, 0, user: "bob", attributes: (AttributeNames.NewRole, "Users"));
        Assert.Empty(kit.Sink.Created);
    }

    [Fact]
    public async Task RepeatForSameUser_IsSuppressed_OtherUserIsNot()
    {
        var kit = new DetectionKit(DefaultRules.PrivilegeEscalation);
        await kit.Send(EventTypes.RoleChanged, 0, user: "bob", attributes: (AttributeNames.NewRole, "Administrator"));
        await kit.Send(EventTypes.RoleChanged, 10, user: "bob", attributes: (AttributeNames.NewRole, "Administrator"));
        await kit.Send(EventTypes.RoleChanged, 20, user: "carol", attributes: (AttributeNames.NewRole, "Administrator"));
        Assert.Equal(2, kit.Sink.Created.Count);
        Assert.Single(kit.Sink.Appended);
    }
}

public sealed class SuspiciousAccessTests
{
    private static readonly (string, string) Protected = (AttributeNames.Protected, "true");
    private static readonly (string, string) Public = (AttributeNames.Protected, "false");

    [Fact]
    public async Task TwentyProtectedAccesses_FiresMedium()
    {
        var kit = new DetectionKit(DefaultRules.SuspiciousAccess);
        for (var i = 0; i < 20; i++)
        {
            await kit.Send(EventTypes.ResourceAccess, i, attributes: Protected);
        }
        var created = Assert.Single(kit.Sink.Created);
        Assert.Equal(Severity.Medium, created.Detection.Rule.Severity);
    }

    [Fact]
    public async Task NineteenProtectedAccesses_NoAlert()
    {
        var kit = new DetectionKit(DefaultRules.SuspiciousAccess);
        for (var i = 0; i < 19; i++)
        {
            await kit.Send(EventTypes.ResourceAccess, i, attributes: Protected);
        }
        Assert.Empty(kit.Sink.Created);
    }

    [Fact]
    public async Task UnprotectedAccesses_AreNotCounted()
    {
        var kit = new DetectionKit(DefaultRules.SuspiciousAccess);
        for (var i = 0; i < 40; i++)
        {
            await kit.Send(EventTypes.ResourceAccess, i * 0.5, attributes: Public);
        }
        Assert.Empty(kit.Sink.Created);
    }

    [Fact]
    public async Task SplitBetweenUsers_NoAlert()
    {
        var kit = new DetectionKit(DefaultRules.SuspiciousAccess);
        for (var i = 0; i < 30; i++)
        {
            await kit.Send(EventTypes.ResourceAccess, i, user: i % 2 == 0 ? "alice" : "bob", attributes: Protected);
        }
        Assert.Empty(kit.Sink.Created);
    }
}

public sealed class EngineBehaviourTests
{
    [Fact]
    public async Task WarmedState_ContinuesCountingWithoutAlertsForHistory()
    {
        var kit = new DetectionKit(DefaultRules.BruteForce);
        for (var i = 0; i < 4; i++)
        {
            kit.Pipeline.Engine.Warm(DetectionKit.Event(EventTypes.LoginFailed, i), kit.Rules);
        }
        Assert.Empty(kit.Sink.Created);
        await kit.Send(EventTypes.LoginFailed, 4);
        var created = Assert.Single(kit.Sink.Created);
        Assert.Equal(5, created.Detection.EventIds.Length);
    }

    [Fact]
    public async Task ResetRule_ClearsItsWindows()
    {
        var kit = new DetectionKit(DefaultRules.BruteForce);
        await kit.Failed(4);
        kit.Pipeline.Engine.ResetRule(kit.Rules[0].Id);
        await kit.Send(EventTypes.LoginFailed, 4);
        Assert.Empty(kit.Sink.Created);
    }

    [Fact]
    public async Task Sweep_RemovesStaleGroups()
    {
        var kit = new DetectionKit(DefaultRules.BruteForce);
        await kit.Failed(1, ip: "10.0.0.1");
        await kit.Failed(1, start: 100, ip: "10.0.0.2");
        Assert.Equal(2, kit.Pipeline.Engine.ActiveGroups);
        kit.Pipeline.Engine.Sweep(kit.Rules, DetectionKit.T0.AddSeconds(120));
        Assert.Equal(1, kit.Pipeline.Engine.ActiveGroups);
    }

    [Fact]
    public async Task DisabledRule_DoesNotFire()
    {
        var kit = new DetectionKit(DefaultRules.PasswordSpraying);
        await kit.Failed(10);
        Assert.Empty(kit.Sink.Created);
    }

    [Fact]
    public async Task DemoFlag_PropagatesToDetection()
    {
        var kit = new DetectionKit(DefaultRules.BruteForce);
        for (var i = 0; i < 5; i++)
        {
            var evt = DetectionKit.Event(EventTypes.LoginFailed, i);
            await kit.Send(new DetectionEvent
            {
                Id = evt.Id,
                Timestamp = evt.Timestamp,
                EventType = evt.EventType,
                SourceIp = evt.SourceIp,
                Username = evt.Username,
                IsDemo = true
            });
        }
        Assert.True(Assert.Single(kit.Sink.Created).Detection.IsDemo);
    }

    [Fact]
    public void RuleValidation_RejectsUnknownGroupFieldAndMissingParameters()
    {
        var errors = RuleDefinition.Validate(RuleType.DistinctCount, EventTypes.LoginFailed, 5, 60, null, ["Nope"], new RuleParameters());
        var map = errors.ToDictionary();
        Assert.True(map.ContainsKey("groupBy"));
        Assert.True(map.ContainsKey("parameters.distinctField"));
    }

    [Fact]
    public void DefaultRules_AreValid()
    {
        foreach (var rule in DefaultRules.Create(DetectionKit.T0))
        {
            var errors = RuleDefinition.Validate(rule.RuleType, rule.EventType, rule.Threshold, rule.WindowSeconds,
                rule.CooldownSeconds, rule.GroupBy, RuleParameters.Parse(rule.Parameters));
            Assert.False(errors.HasErrors);
        }
    }
}
