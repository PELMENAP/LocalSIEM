using LocalSIEM.Api.Auth;
using LocalSIEM.Application.Common;
using LocalSIEM.Application.Detection;
using LocalSIEM.Application.Events;
using LocalSIEM.Application.Identity;
using LocalSIEM.Application.Incidents;
using LocalSIEM.Application.Settings;
using LocalSIEM.Application.Simulator;
using LocalSIEM.Application.Sources;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LocalSIEM.Api.Controllers;

[ApiController]
[Route("api/incidents")]
[Authorize(Policy = Policies.Analyst)]
public sealed class IncidentsController(IIncidentService incidents) : ControllerBase
{
    [HttpGet]
    public Task<PagedResult<IncidentListItemDto>> List([FromQuery] IncidentQuery query, CancellationToken ct) => incidents.ListAsync(query, ct);

    [HttpPost]
    public async Task<ActionResult<IncidentDetailsDto>> Create([FromBody] CreateIncidentDto dto, CancellationToken ct)
    {
        var created = await incidents.CreateAsync(dto, ct);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    [HttpGet("{id:guid}")]
    public Task<IncidentDetailsDto> Get(Guid id, CancellationToken ct) => incidents.GetAsync(id, ct);

    [HttpPatch("{id:guid}/status")]
    public Task<IncidentDetailsDto> ChangeStatus(Guid id, [FromBody] ChangeIncidentStatusDto dto, CancellationToken ct) =>
        incidents.ChangeStatusAsync(id, dto, ct);

    [HttpPatch("{id:guid}/assignee")]
    public Task<IncidentDetailsDto> Assign(Guid id, [FromBody] AssignIncidentDto dto, CancellationToken ct) =>
        incidents.AssignAsync(id, dto, ct);

    [HttpPost("{id:guid}/alerts")]
    public Task<IncidentDetailsDto> LinkAlerts(Guid id, [FromBody] LinkAlertsDto dto, CancellationToken ct) =>
        incidents.LinkAlertsAsync(id, dto, ct);

    [HttpGet("{id:guid}/timeline")]
    public Task<PagedResult<EventListItemDto>> Timeline(Guid id, [FromQuery] int page = 1, [FromQuery] int pageSize = 100, CancellationToken ct = default) =>
        incidents.GetTimelineAsync(id, page, pageSize, ct);

    [HttpGet("{id:guid}/comments")]
    public Task<IReadOnlyList<CommentDto>> Comments(Guid id, CancellationToken ct) => incidents.GetCommentsAsync(id, ct);

    [HttpPost("{id:guid}/comments")]
    public Task<CommentDto> AddComment(Guid id, [FromBody] AddCommentDto dto, CancellationToken ct) => incidents.AddCommentAsync(id, dto, ct);

    [HttpGet("{id:guid}/history")]
    public Task<IReadOnlyList<HistoryEntryDto>> History(Guid id, CancellationToken ct) => incidents.GetHistoryAsync(id, ct);
}

[ApiController]
[Route("api/rules")]
[Authorize(Policy = Policies.Analyst)]
public sealed class RulesController(IRuleService rules) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<RuleDto>> List(CancellationToken ct) => rules.ListAsync(ct);

    [HttpGet("{id:guid}")]
    public Task<RuleDto> Get(Guid id, CancellationToken ct) => rules.GetAsync(id, ct);

    [HttpPost]
    [Authorize(Policy = Policies.Administrator)]
    public Task<RuleDto> Create([FromBody] RuleInputDto dto, CancellationToken ct) => rules.CreateAsync(dto, ct);

    [HttpPut("{id:guid}")]
    [Authorize(Policy = Policies.Administrator)]
    public Task<RuleDto> Update(Guid id, [FromBody] RuleInputDto dto, CancellationToken ct) => rules.UpdateAsync(id, dto, ct);

    [HttpPatch("{id:guid}/enabled")]
    [Authorize(Policy = Policies.Administrator)]
    public Task<RuleDto> Toggle(Guid id, [FromBody] ToggleRuleDto dto, CancellationToken ct) => rules.ToggleAsync(id, dto.IsEnabled, ct);
}

[ApiController]
[Route("api/sources")]
[Authorize(Policy = Policies.Analyst)]
public sealed class SourcesController(ISourceService sources) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<SourceDto>> List(CancellationToken ct) => sources.ListAsync(ct);

    [HttpPost]
    [Authorize(Policy = Policies.Administrator)]
    public Task<SourceWithKeyDto> Create([FromBody] CreateSourceDto dto, CancellationToken ct) => sources.CreateAsync(dto, ct);

    [HttpPut("{id:guid}")]
    [Authorize(Policy = Policies.Administrator)]
    public Task<SourceDto> Update(Guid id, [FromBody] UpdateSourceDto dto, CancellationToken ct) => sources.UpdateAsync(id, dto, ct);

    [HttpPost("{id:guid}/rotate-key")]
    [Authorize(Policy = Policies.Administrator)]
    public Task<SourceWithKeyDto> RotateKey(Guid id, CancellationToken ct) => sources.RotateKeyAsync(id, ct);
}

[ApiController]
[Route("api/simulator")]
[Authorize(Policy = Policies.Analyst)]
public sealed class SimulatorController(SimulatorRunner runner, IAuditService audit, ISiemDbContext db) : ControllerBase
{
    [HttpGet("scenarios")]
    public IReadOnlyList<ScenarioInfo> Scenarios() => runner.Scenarios;

    [HttpGet("runs/current")]
    public SimulationStateDto Current() => runner.State;

    [HttpPost("runs")]
    public async Task<SimulationStateDto> Start([FromBody] StartSimulationDto dto, CancellationToken ct)
    {
        var state = runner.Start(dto);
        audit.Record(AuditActions.SimulatorStarted, "Simulation", state.RunId, new { dto.Scenario, state.Total, state.RatePerSecond });
        await db.SaveChangesAsync(ct);
        return state;
    }

    [HttpDelete("runs/{id:guid}")]
    public async Task<SimulationStateDto> Stop(Guid id, CancellationToken ct)
    {
        var state = runner.Stop(id);
        audit.Record(AuditActions.SimulatorStopped, "Simulation", id);
        await db.SaveChangesAsync(ct);
        return state;
    }

    [HttpPost("purge")]
    [Authorize(Policy = Policies.Administrator)]
    public Task<PurgeResultDto> Purge([FromBody] PurgeRequestDto request, [FromServices] IDemoDataService demo, CancellationToken ct) =>
        demo.PurgeAsync(request, ct);
}

[ApiController]
[Route("api/users")]
[Authorize(Policy = Policies.Administrator)]
public sealed class UsersController(IUserService users) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<UserDto>> List(CancellationToken ct) => users.ListAsync(ct);

    [HttpPost]
    public Task<UserDto> Create([FromBody] CreateUserDto dto, CancellationToken ct) => users.CreateAsync(dto, ct);

    [HttpPut("{id:guid}")]
    public Task<UserDto> Update(Guid id, [FromBody] UpdateUserDto dto, CancellationToken ct) => users.UpdateAsync(id, dto, ct);

    [HttpPatch("{id:guid}/active")]
    public Task<UserDto> SetActive(Guid id, [FromBody] SetActiveDto dto, CancellationToken ct) => users.SetActiveAsync(id, dto.IsActive, ct);

    [HttpPost("{id:guid}/reset-password")]
    public async Task<IActionResult> ResetPassword(Guid id, [FromBody] ResetPasswordDto dto, CancellationToken ct)
    {
        await users.ResetPasswordAsync(id, dto.Password, ct);
        return NoContent();
    }
}

[ApiController]
[Route("api/user-options")]
[Authorize(Policy = Policies.Analyst)]
public sealed class UserOptionsController(IUserService users) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<UserOptionDto>> List(CancellationToken ct) => users.ListActiveAsync(ct);
}

[ApiController]
[Route("api/roles")]
[Authorize(Policy = Policies.Administrator)]
public sealed class RolesController(IUserService users) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<RoleDto>> List(CancellationToken ct) => users.ListRolesAsync(ct);
}

[ApiController]
[Route("api/settings")]
[Authorize(Policy = Policies.Administrator)]
public sealed class SettingsController(ISettingsService settings, SettingsCache cache) : ControllerBase
{
    [HttpGet]
    public SystemSettingsDto Get() => cache.Current;

    [HttpPut]
    public Task<SystemSettingsDto> Update([FromBody] SystemSettingsDto dto, CancellationToken ct) => settings.UpdateAsync(dto, ct);
}

public sealed record AuditLogDto(Guid Id, DateTimeOffset CreatedAt, string? Username, string Action, string? EntityType,
    Guid? EntityId, string? Details, string? IpAddress);

public sealed class AuditQuery : PageQuery
{
    public string? Action { get; set; }
    public Guid? UserId { get; set; }
}

[ApiController]
[Route("api/audit")]
[Authorize(Policy = Policies.Administrator)]
public sealed class AuditController(ISiemDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<PagedResult<AuditLogDto>> List([FromQuery] AuditQuery query, CancellationToken ct)
    {
        var logs = db.AuditLogs.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(query.Action))
        {
            logs = logs.Where(l => l.Action.StartsWith(query.Action));
        }
        if (query.UserId is { } userId)
        {
            logs = logs.Where(l => l.UserId == userId);
        }
        var page = await logs.OrderByDescending(l => l.CreatedAt)
            .Select(l => new { l.Id, l.CreatedAt, Username = l.User != null ? l.User.Username : null, l.Action, l.EntityType, l.EntityId, l.Details, l.IpAddress })
            .ToPagedAsync(query.SafePage, query.SafePageSize, ct);
        return new PagedResult<AuditLogDto>(
            page.Items.Select(l => new AuditLogDto(l.Id, l.CreatedAt, l.Username, l.Action, l.EntityType, l.EntityId, l.Details, l.IpAddress?.ToString())).ToList(),
            page.Page, page.PageSize, page.TotalCount);
    }
}
