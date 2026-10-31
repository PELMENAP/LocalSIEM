using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace LocalSIEM.Api.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class AuthAndAccessTests(ApiFactory factory)
{
    [Fact]
    public async Task Login_WithValidCredentials_ReturnsTokenAndRole()
    {
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new { username = ApiFactory.AdminUser, password = ApiFactory.AdminPassword });
        var body = await ApiFactory.ReadJsonAsync(response, HttpStatusCode.OK);
        Assert.False(string.IsNullOrEmpty(body.GetProperty("accessToken").GetString()));
        Assert.Equal("Administrator", body.GetProperty("user").GetProperty("role").GetString());
    }

    [Fact]
    public async Task Login_WithWrongPassword_Returns401()
    {
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new { username = ApiFactory.AdminUser, password = "wrong-password" });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ProtectedEndpoints_WithoutToken_Return401()
    {
        var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/events")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/dashboard/summary")).StatusCode);
    }

    [Fact]
    public async Task Analyst_CanReadEvents_ButCannotManageUsersOrRules()
    {
        var admin = await factory.LoginAsync();
        var username = $"analyst{Guid.NewGuid():N}"[..20];
        const string password = "Analyst#Pass1";
        var created = await admin.PostAsJsonAsync("/api/users",
            new { username, displayName = "Аналитик", role = "SecurityAnalyst", password });
        await ApiFactory.ReadJsonAsync(created, HttpStatusCode.OK);

        var analyst = await factory.LoginAsync(username, password);
        Assert.Equal(HttpStatusCode.OK, (await analyst.GetAsync("/api/events")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await analyst.GetAsync("/api/rules")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await analyst.GetAsync("/api/users")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await analyst.GetAsync("/api/settings")).StatusCode);

        var rules = await (await analyst.GetAsync("/api/rules")).Content.ReadFromJsonAsync<JsonElement>(ApiFactory.Json);
        var ruleId = rules[0].GetProperty("id").GetString();
        var toggle = await analyst.PatchAsJsonAsync($"/api/rules/{ruleId}/enabled", new { isEnabled = false });
        Assert.Equal(HttpStatusCode.Forbidden, toggle.StatusCode);
    }
}

[Collection(ApiCollection.Name)]
public sealed class IngestionTests(ApiFactory factory)
{
    [Fact]
    public async Task Ingest_WithoutApiKey_Returns401()
    {
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/ingest/events", new { events = new[] { new { timestamp = DateTimeOffset.UtcNow, eventType = "LoginFailed" } } });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Ingest_InvalidEvent_Returns400WithFieldErrors()
    {
        var admin = await factory.LoginAsync();
        var source = factory.CreateSourceClient(await factory.CreateHttpSourceAsync(admin, $"invalid-{Guid.NewGuid():N}"));
        var response = await source.PostAsJsonAsync("/api/ingest/events", new
        {
            events = new object[] { new { eventType = "1bad", sourceIp = "999.0.0.1" } }
        });
        var body = await ApiFactory.ReadJsonAsync(response, HttpStatusCode.BadRequest);
        var errors = body.GetProperty("errors");
        Assert.True(errors.TryGetProperty("events[0].timestamp", out _));
        Assert.True(errors.TryGetProperty("events[0].eventType", out _));
        Assert.True(errors.TryGetProperty("events[0].sourceIp", out _));
    }

    [Fact]
    public async Task Ingest_ValidEvent_IsNormalizedStoredAndSearchable()
    {
        var admin = await factory.LoginAsync();
        var source = factory.CreateSourceClient(await factory.CreateHttpSourceAsync(admin, $"valid-{Guid.NewGuid():N}"));
        var user = $"u{Guid.NewGuid():N}"[..16];
        var response = await source.PostAsJsonAsync("/api/ingest/events", new
        {
            events = new object[]
            {
                new
                {
                    timestamp = DateTimeOffset.UtcNow,
                    eventType = "ResourceAccess",
                    sourceIp = "10.50.0.1",
                    username = user,
                    message = "integration",
                    attributes = new Dictionary<string, object> { ["protected"] = true },
                    raw = new { origin = "integration-test" }
                }
            }
        });
        var accepted = await ApiFactory.ReadJsonAsync(response, HttpStatusCode.Accepted);
        Assert.Equal(1, accepted.GetProperty("accepted").GetInt32());
        var id = accepted.GetProperty("eventIds")[0].GetString();

        var list = await admin.GetFromJsonAsync<JsonElement>($"/api/events?username={user}", ApiFactory.Json);
        Assert.Equal(1, list.GetProperty("totalCount").GetInt32());
        var item = list.GetProperty("items")[0];
        Assert.Equal("Low", item.GetProperty("severity").GetString());
        Assert.Equal("10.50.0.1", item.GetProperty("sourceIp").GetString());

        var details = await admin.GetFromJsonAsync<JsonElement>($"/api/events/{id}", ApiFactory.Json);
        Assert.Equal("integration-test", details.GetProperty("rawData").GetProperty("raw").GetProperty("origin").GetString());
    }
}

[Collection(ApiCollection.Name)]
public sealed class DetectionAndIncidentTests(ApiFactory factory)
{
    private static object Failed(string ip, string user, DateTimeOffset at) =>
        new { timestamp = at, eventType = "LoginFailed", sourceIp = ip, username = user, message = "bad password" };

    private static async Task<JsonElement?> FindAlertAsync(HttpClient client, string ruleCode, string ip)
    {
        var page = await client.GetFromJsonAsync<JsonElement>("/api/alerts?pageSize=200", ApiFactory.Json);
        foreach (var alert in page.GetProperty("items").EnumerateArray())
        {
            if (alert.GetProperty("ruleCode").GetString() == ruleCode && alert.GetProperty("title").GetString()!.Contains(ip))
            {
                return alert;
            }
        }
        return null;
    }

    [Fact]
    public async Task BruteForceOverHttp_CreatesSingleAlertAndAppendsFurtherEvents()
    {
        var admin = await factory.LoginAsync();
        var source = factory.CreateSourceClient(await factory.CreateHttpSourceAsync(admin, $"bf-{Guid.NewGuid():N}"));
        var ip = $"198.51.100.{Random.Shared.Next(1, 250)}";
        var now = DateTimeOffset.UtcNow;
        var events = Enumerable.Range(0, 8).Select(i => Failed(ip, "victim", now.AddSeconds(i))).ToArray();
        await ApiFactory.ReadJsonAsync(await source.PostAsJsonAsync("/api/ingest/events", new { events }), HttpStatusCode.Accepted);

        var alert = await ApiFactory.WaitForAsync(async () =>
        {
            var found = await FindAlertAsync(admin, "BRUTE_FORCE", ip);
            return found is { } a && a.GetProperty("eventCount").GetInt32() == 8 ? a : null;
        });
        Assert.Equal("High", alert.GetProperty("severity").GetString());

        var page = await admin.GetFromJsonAsync<JsonElement>("/api/alerts?pageSize=200", ApiFactory.Json);
        var count = page.GetProperty("items").EnumerateArray()
            .Count(a => a.GetProperty("ruleCode").GetString() == "BRUTE_FORCE" && a.GetProperty("title").GetString()!.Contains(ip));
        Assert.Equal(1, count);

        var linked = await admin.GetFromJsonAsync<JsonElement>($"/api/alerts/{alert.GetProperty("id").GetString()}/events", ApiFactory.Json);
        Assert.Equal(8, linked.GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task BelowThreshold_CreatesNoAlert()
    {
        var admin = await factory.LoginAsync();
        var source = factory.CreateSourceClient(await factory.CreateHttpSourceAsync(admin, $"below-{Guid.NewGuid():N}"));
        var ip = $"198.51.101.{Random.Shared.Next(1, 250)}";
        var now = DateTimeOffset.UtcNow;
        var events = Enumerable.Range(0, 4).Select(i => Failed(ip, "victim", now.AddSeconds(i))).ToArray();
        await ApiFactory.ReadJsonAsync(await source.PostAsJsonAsync("/api/ingest/events", new { events }), HttpStatusCode.Accepted);
        await Task.Delay(1500);
        Assert.Null(await FindAlertAsync(admin, "BRUTE_FORCE", ip));
    }

    [Fact]
    public async Task IncidentLifecycle_FromAlertsToResolved_WithHistoryAndComments()
    {
        var admin = await factory.LoginAsync();
        var source = factory.CreateSourceClient(await factory.CreateHttpSourceAsync(admin, $"inc-{Guid.NewGuid():N}"));
        var ip = $"198.51.102.{Random.Shared.Next(1, 250)}";
        var now = DateTimeOffset.UtcNow;
        var events = Enumerable.Range(0, 6).Select(i => Failed(ip, $"user{i}", now.AddSeconds(i))).ToArray();
        await ApiFactory.ReadJsonAsync(await source.PostAsJsonAsync("/api/ingest/events", new { events }), HttpStatusCode.Accepted);

        var spraying = await ApiFactory.WaitForAsync(() => FindAlertAsync(admin, "PASSWORD_SPRAYING", ip));
        var bruteForce = await ApiFactory.WaitForAsync(() => FindAlertAsync(admin, "BRUTE_FORCE", ip));

        var createResponse = await admin.PostAsJsonAsync("/api/incidents", new
        {
            title = $"Атака с {ip}",
            description = "Интеграционный тест",
            alertIds = new[] { spraying.GetProperty("id").GetString(), bruteForce.GetProperty("id").GetString() }
        });
        var incident = await ApiFactory.ReadJsonAsync(createResponse, HttpStatusCode.Created);
        var incidentId = incident.GetProperty("id").GetString();
        Assert.Equal("Critical", incident.GetProperty("severity").GetString());
        Assert.Equal("Open", incident.GetProperty("status").GetString());
        Assert.Equal(2, incident.GetProperty("alerts").GetArrayLength());

        var duplicate = await admin.PostAsJsonAsync("/api/incidents", new { title = "dup", alertIds = new[] { spraying.GetProperty("id").GetString() } });
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);

        var invalid = await admin.PatchAsJsonAsync($"/api/incidents/{incidentId}/status",
            new { status = "Closed", version = incident.GetProperty("version").GetUInt32() }, ApiFactory.Json);
        Assert.Equal(HttpStatusCode.Conflict, invalid.StatusCode);

        var investigating = await ApiFactory.ReadJsonAsync(await admin.PatchAsJsonAsync($"/api/incidents/{incidentId}/status",
            new { status = "Investigating", version = incident.GetProperty("version").GetUInt32() }, ApiFactory.Json), HttpStatusCode.OK);

        var stale = await admin.PatchAsJsonAsync($"/api/incidents/{incidentId}/status",
            new { status = "Resolved", version = incident.GetProperty("version").GetUInt32() }, ApiFactory.Json);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);

        Assert.Equal("Investigating", investigating.GetProperty("status").GetString());
        await ApiFactory.ReadJsonAsync(await admin.PostAsJsonAsync($"/api/incidents/{incidentId}/comments", new { body = "Заблокирован IP на периметре" }), HttpStatusCode.OK);
        var current = await admin.GetFromJsonAsync<JsonElement>($"/api/incidents/{incidentId}", ApiFactory.Json);
        var resolved = await ApiFactory.ReadJsonAsync(await admin.PatchAsJsonAsync($"/api/incidents/{incidentId}/status",
            new { status = "Resolved", version = current.GetProperty("version").GetUInt32() }, ApiFactory.Json), HttpStatusCode.OK);
        Assert.Equal("Resolved", resolved.GetProperty("status").GetString());

        var history = await admin.GetFromJsonAsync<JsonElement>($"/api/incidents/{incidentId}/history", ApiFactory.Json);
        var actions = history.EnumerateArray().Select(h => h.GetProperty("action").GetString()).ToList();
        Assert.Contains("Created", actions);
        Assert.Contains("AlertLinked", actions);
        Assert.Contains("CommentAdded", actions);
        Assert.Equal(2, actions.Count(a => a == "StatusChanged"));

        var timeline = await admin.GetFromJsonAsync<JsonElement>($"/api/incidents/{incidentId}/timeline", ApiFactory.Json);
        Assert.Equal(6, timeline.GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task SimulatorRun_GoesThroughPipeline_AndPurgeRemovesDemoData()
    {
        var admin = await factory.LoginAsync();
        var ip = $"203.0.113.{Random.Shared.Next(100, 250)}";
        var start = await admin.PostAsJsonAsync("/api/simulator/runs", new { scenario = "brute-force", count = 6, ratePerSecond = 50, sourceIp = ip });
        await ApiFactory.ReadJsonAsync(start, HttpStatusCode.OK);

        await ApiFactory.WaitForAsync(async () =>
        {
            var state = await admin.GetFromJsonAsync<JsonElement>("/api/simulator/runs/current", ApiFactory.Json);
            return state.GetProperty("state").GetString() == "Completed" ? state : null;
        });
        await ApiFactory.WaitForAsync(() => FindAlertAsync(admin, "BRUTE_FORCE", ip));

        var refused = await admin.PostAsJsonAsync("/api/simulator/purge", new { confirm = "yes" });
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);

        var purged = await ApiFactory.ReadJsonAsync(await admin.PostAsJsonAsync("/api/simulator/purge", new { confirm = "DELETE DEMO DATA" }), HttpStatusCode.OK);
        Assert.True(purged.GetProperty("events").GetInt32() >= 6);
        Assert.Null(await FindAlertAsync(admin, "BRUTE_FORCE", ip));
    }
}
