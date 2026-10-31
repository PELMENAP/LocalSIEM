using LocalSIEM.Api.Auth;
using LocalSIEM.Api.Hosting;
using LocalSIEM.Application.Alerts;
using LocalSIEM.Application.Common;
using LocalSIEM.Application.Dashboard;
using LocalSIEM.Application.Events;
using LocalSIEM.Application.Identity;
using LocalSIEM.Application.Ingestion;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace LocalSIEM.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController(IAuthService auth) : ControllerBase
{
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting("login")]
    public Task<LoginResponse> Login([FromBody] LoginRequest request, CancellationToken ct) => auth.LoginAsync(request, ct);

    [HttpGet("me")]
    [Authorize]
    public Task<UserDto> Me(CancellationToken ct) => auth.GetCurrentAsync(User.RequireUserId(), ct);
}

[ApiController]
[Route("api/ingest")]
[AllowAnonymous]
public sealed class IngestController(
    IEventIngestionService ingestion,
    SourceKeyCache keys,
    ISiemDbContext db,
    IngestRateLimiter limiter) : ControllerBase
{
    public const string ApiKeyHeader = "X-Api-Key";

    [HttpPost("events")]
    [RequestSizeLimit(8 * 1024 * 1024)]
    [ProducesResponseType<IngestResult>(StatusCodes.Status202Accepted)]
    public async Task<IActionResult> Events([FromBody] IngestBatchDto batch, CancellationToken ct)
    {
        var apiKey = Request.Headers[ApiKeyHeader].ToString();
        var sourceId = await keys.ResolveAsync(apiKey, db, ct);
        if (sourceId is null)
        {
            return Problem(statusCode: StatusCodes.Status401Unauthorized, title: $"Нужен действующий ключ источника в заголовке {ApiKeyHeader}");
        }
        if (!limiter.TryAcquire(sourceId.Value, batch.Events?.Count ?? 1))
        {
            return Problem(statusCode: StatusCodes.Status429TooManyRequests, title: "Превышен лимит приёма событий для источника");
        }
        var result = await ingestion.IngestAsync(sourceId.Value, batch.Events, ct);
        return Accepted(result);
    }
}

[ApiController]
[Route("api/events")]
[Authorize(Policy = Policies.Analyst)]
public sealed class EventsController(IEventQueryService events) : ControllerBase
{
    [HttpGet]
    public Task<PagedResult<EventListItemDto>> List([FromQuery] EventQuery query, CancellationToken ct) => events.ListAsync(query, ct);

    [HttpGet("{id:guid}")]
    public Task<EventDetailsDto> Get(Guid id, CancellationToken ct) => events.GetAsync(id, ct);
}

[ApiController]
[Route("api/alerts")]
[Authorize(Policy = Policies.Analyst)]
public sealed class AlertsController(IAlertService alerts) : ControllerBase
{
    [HttpGet]
    public Task<PagedResult<AlertListItemDto>> List([FromQuery] AlertQuery query, CancellationToken ct) => alerts.ListAsync(query, ct);

    [HttpGet("{id:guid}")]
    public Task<AlertDetailsDto> Get(Guid id, CancellationToken ct) => alerts.GetAsync(id, ct);

    [HttpGet("{id:guid}/events")]
    public Task<PagedResult<EventListItemDto>> Events(Guid id, [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken ct = default) =>
        alerts.GetEventsAsync(id, page, pageSize, ct);

    [HttpPatch("{id:guid}/status")]
    public Task<AlertDetailsDto> ChangeStatus(Guid id, [FromBody] ChangeAlertStatusDto dto, CancellationToken ct) =>
        alerts.ChangeStatusAsync(id, dto, ct);
}

[ApiController]
[Route("api/dashboard")]
[Authorize(Policy = Policies.Analyst)]
public sealed class DashboardController(IDashboardService dashboard) : ControllerBase
{
    [HttpGet("summary")]
    public Task<DashboardSummaryDto> Summary(CancellationToken ct) => dashboard.GetSummaryAsync(ct);

    [HttpGet("timeline")]
    public Task<IReadOnlyList<TimelinePointDto>> Timeline([FromQuery] DateTimeOffset? from, [FromQuery] DateTimeOffset? to,
        [FromQuery] string? bucket, CancellationToken ct) => dashboard.GetTimelineAsync(from, to, bucket, ct);

    [HttpGet("top-ips")]
    public Task<IReadOnlyList<TopIpDto>> TopIps([FromQuery] int hours = 24, [FromQuery] int limit = 10, CancellationToken ct = default) =>
        dashboard.GetTopIpsAsync(hours, limit, ct);
}
