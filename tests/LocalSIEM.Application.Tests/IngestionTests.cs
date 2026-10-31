using System.Text.Json;
using LocalSIEM.Application.Ingestion;
using LocalSIEM.Domain;
using LocalSIEM.Domain.Entities;
using LocalSIEM.Domain.Enums;
using Xunit;

namespace LocalSIEM.Application.Tests;

public sealed class IngestValidatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private static IngestEventDto Valid() => new()
    {
        Timestamp = Now,
        EventType = EventTypes.LoginFailed,
        SourceIp = "10.0.0.5",
        Username = "alice"
    };

    [Fact]
    public void ValidEvent_Passes()
    {
        Assert.False(IngestValidator.Validate([Valid()], Now).HasErrors);
    }

    [Fact]
    public void EmptyBatch_Fails()
    {
        Assert.True(IngestValidator.Validate([], Now).HasErrors);
        Assert.True(IngestValidator.Validate(null, Now).HasErrors);
    }

    [Fact]
    public void OversizedBatch_Fails()
    {
        var batch = Enumerable.Range(0, IngestValidator.MaxBatchSize + 1).Select(_ => Valid()).ToList();
        Assert.True(IngestValidator.Validate(batch, Now).ToDictionary().ContainsKey("events"));
    }

    [Fact]
    public void MissingFieldsAndBadValues_AreReportedPerEvent()
    {
        var bad = new IngestEventDto
        {
            EventType = "1 bad type",
            Severity = "Huge",
            SourceIp = "999.1.1.1",
            TargetIp = "not-an-ip"
        };
        var errors = IngestValidator.Validate([Valid(), bad], Now).ToDictionary();
        Assert.True(errors.ContainsKey("events[1].timestamp"));
        Assert.True(errors.ContainsKey("events[1].eventType"));
        Assert.True(errors.ContainsKey("events[1].severity"));
        Assert.True(errors.ContainsKey("events[1].sourceIp"));
        Assert.True(errors.ContainsKey("events[1].targetIp"));
        Assert.False(errors.Keys.Any(k => k.StartsWith("events[0]", StringComparison.Ordinal)));
    }

    [Fact]
    public void FutureTimestamp_Fails()
    {
        var dto = Valid();
        dto.Timestamp = Now.AddMinutes(10);
        Assert.True(IngestValidator.Validate([dto], Now).ToDictionary().ContainsKey("events[0].timestamp"));
    }

    [Fact]
    public void NestedAttributeAndHugeRaw_Fail()
    {
        var dto = Valid();
        dto.Attributes = new Dictionary<string, JsonElement> { ["nested"] = JsonDocument.Parse("{\"a\":1}").RootElement };
        dto.Raw = JsonDocument.Parse(JsonSerializer.Serialize(new string('x', IngestValidator.MaxRawBytes))).RootElement;
        var errors = IngestValidator.Validate([dto], Now).ToDictionary();
        Assert.True(errors.ContainsKey("events[0].attributes.nested"));
        Assert.True(errors.ContainsKey("events[0].raw"));
    }
}

public sealed class NormalizerTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private static EventSource Source(SourceKind kind) => new() { Id = Guid.NewGuid(), Name = kind.ToString(), Kind = kind };

    [Fact]
    public void HttpEvent_IsMappedWithDefaultSeverityAndRawData()
    {
        var dto = new IngestEventDto
        {
            Timestamp = Now.ToOffset(TimeSpan.FromHours(3)),
            EventType = EventTypes.LoginFailed,
            SourceIp = "10.0.0.5",
            Username = "  alice ",
            Message = "Invalid password",
            Raw = JsonDocument.Parse("{\"origin\":\"test\"}").RootElement
        };
        var result = new HttpApiNormalizer().Normalize(dto, Source(SourceKind.HttpApi), Now);
        Assert.Equal(Severity.Low, result.Entity.Severity);
        Assert.Equal(TimeSpan.Zero, result.Entity.Timestamp.Offset);
        Assert.Equal(Now, result.Entity.Timestamp);
        Assert.Equal("alice", result.Entity.Username);
        Assert.Equal("10.0.0.5", result.Entity.SourceIp!.ToString());
        Assert.False(result.Entity.IsDemo);
        Assert.NotNull(result.Entity.CorrelationId);
        Assert.Contains("\"origin\":\"test\"", result.Entity.RawData);
        Assert.Equal(result.Entity.Id, result.Detection.Id);
    }

    [Fact]
    public void ExplicitSeverity_Wins()
    {
        var dto = new IngestEventDto { Timestamp = Now, EventType = EventTypes.Generic, Severity = "critical" };
        Assert.Equal(Severity.Critical, new HttpApiNormalizer().Normalize(dto, Source(SourceKind.HttpApi), Now).Entity.Severity);
    }

    [Fact]
    public void SimulatorEvents_AreMarkedAsDemo()
    {
        var dto = new IngestEventDto { Timestamp = Now, EventType = EventTypes.LoginSuccess };
        var result = new SimulatorNormalizer().Normalize(dto, Source(SourceKind.Simulator), Now);
        Assert.True(result.Entity.IsDemo);
        Assert.True(result.Detection.IsDemo);
    }

    [Theory]
    [InlineData("4624", EventTypes.LoginSuccess, Severity.Info)]
    [InlineData("4625", EventTypes.LoginFailed, Severity.Low)]
    [InlineData("4672", EventTypes.PrivilegedLogon, Severity.Low)]
    [InlineData("4720", EventTypes.AccountCreated, Severity.Medium)]
    [InlineData("1102", EventTypes.Generic, Severity.Info)]
    public void WindowsEvents_AreClassifiedById(string id, string expectedType, Severity expectedSeverity)
    {
        var dto = new IngestEventDto
        {
            Timestamp = Now,
            EventType = EventTypes.WindowsEvent,
            Attributes = new Dictionary<string, JsonElement> { [AttributeNames.WindowsEventId] = JsonDocument.Parse(id).RootElement }
        };
        var result = new WindowsEventNormalizer().Normalize(dto, Source(SourceKind.WindowsCollector), Now);
        Assert.Equal(expectedType, result.Entity.EventType);
        Assert.Equal(expectedSeverity, result.Entity.Severity);
        Assert.Equal(expectedType, result.Detection.EventType);
    }

    [Fact]
    public void Attributes_AreAvailableToDetectionAndRestoredFromRawData()
    {
        var dto = new IngestEventDto
        {
            Timestamp = Now,
            EventType = EventTypes.ResourceAccess,
            Username = "bob",
            Attributes = new Dictionary<string, JsonElement>
            {
                [AttributeNames.Protected] = JsonDocument.Parse("true").RootElement,
                [AttributeNames.Resource] = JsonDocument.Parse("\"/finance/report.xlsx\"").RootElement
            }
        };
        var result = new HttpApiNormalizer().Normalize(dto, Source(SourceKind.HttpApi), Now);
        Assert.Equal("true", result.Detection.GetField(AttributeNames.Protected));
        Assert.Equal(Severity.Low, result.Entity.Severity);
        var restored = Detection.DetectionEvent.FromStored(result.Entity);
        Assert.Equal("true", restored.GetField(AttributeNames.Protected));
        Assert.Equal("/finance/report.xlsx", restored.GetField(AttributeNames.Resource));
    }
}

public sealed class StatusTransitionTests
{
    [Theory]
    [InlineData(AlertStatus.New, AlertStatus.Acknowledged, true)]
    [InlineData(AlertStatus.New, AlertStatus.Resolved, true)]
    [InlineData(AlertStatus.Acknowledged, AlertStatus.Resolved, true)]
    [InlineData(AlertStatus.Resolved, AlertStatus.New, false)]
    [InlineData(AlertStatus.Acknowledged, AlertStatus.New, false)]
    public void Alert(AlertStatus from, AlertStatus to, bool allowed) =>
        Assert.Equal(allowed, StatusTransitions.CanChange(from, to));

    [Theory]
    [InlineData(IncidentStatus.Open, IncidentStatus.Investigating, true)]
    [InlineData(IncidentStatus.Investigating, IncidentStatus.Resolved, true)]
    [InlineData(IncidentStatus.Resolved, IncidentStatus.Closed, true)]
    [InlineData(IncidentStatus.Resolved, IncidentStatus.Investigating, true)]
    [InlineData(IncidentStatus.Open, IncidentStatus.Closed, false)]
    [InlineData(IncidentStatus.Closed, IncidentStatus.Open, false)]
    [InlineData(IncidentStatus.Investigating, IncidentStatus.Open, false)]
    public void Incident(IncidentStatus from, IncidentStatus to, bool allowed) =>
        Assert.Equal(allowed, StatusTransitions.CanChange(from, to));
}
