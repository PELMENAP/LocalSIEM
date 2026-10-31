using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using LocalSIEM.Api.Auth;
using LocalSIEM.Api.Hosting;
using LocalSIEM.Api.Realtime;
using LocalSIEM.Api.Workers;
using LocalSIEM.Application;
using LocalSIEM.Application.Common;
using LocalSIEM.Domain;
using LocalSIEM.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, services, logger) => logger
    .ReadFrom.Configuration(context.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File(Path.Combine(AppContext.BaseDirectory, "logs", "localsiem-.log"), rollingInterval: RollingInterval.Day, retainedFileCountLimit: 7));

var connectionString = builder.Configuration.GetConnectionString("Postgres");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException(
        "Не задана строка подключения ConnectionStrings:Postgres (user-secrets или переменная окружения ConnectionStrings__Postgres)");
}

var jwt = builder.Configuration.GetSection("Jwt").Get<JwtOptions>() ?? new JwtOptions();
using (var loggerFactory = LoggerFactory.Create(b => b.AddConsole()))
{
    jwt.Prepare(loggerFactory.CreateLogger("Startup"));
}

builder.Services.AddApplication(builder.Configuration.GetValue("Detection:QueueCapacity", 10_000));
builder.Services.AddInfrastructure(connectionString);
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton(jwt);
builder.Services.AddSingleton<ITokenService, JwtTokenService>();
builder.Services.AddScoped<ICurrentUser, HttpCurrentUser>();
builder.Services.AddSingleton<SignalRNotificationPublisher>();
builder.Services.AddSingleton<INotificationPublisher>(sp => sp.GetRequiredService<SignalRNotificationPublisher>());
builder.Services.AddSingleton<IngestRateLimiter>();
builder.Services.AddHostedService<DetectionWorker>();
builder.Services.AddHostedService<RealtimeFlusher>();
builder.Services.AddHostedService<SourceMonitor>();

builder.Services.AddExceptionHandler<AppExceptionHandler>();
builder.Services.AddProblemDetails();

builder.Services.AddControllers().AddJsonOptions(o =>
{
    o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    o.JsonSerializerOptions.Converters.Add(new IpAddressJsonConverter());
});
builder.Services.AddSignalR().AddJsonProtocol(o =>
{
    o.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    o.PayloadSerializerOptions.Converters.Add(new IpAddressJsonConverter());
});

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
{
    options.MapInboundClaims = false;
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidIssuer = jwt.Issuer,
        ValidAudience = jwt.Audience,
        IssuerSigningKey = jwt.SigningKey,
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ClockSkew = TimeSpan.FromSeconds(30),
        NameClaimType = Claims.Username,
        RoleClaimType = Claims.Role
    };
    options.Events = new JwtBearerEvents
    {
        OnMessageReceived = context =>
        {
            var token = context.Request.Query["access_token"];
            if (!string.IsNullOrEmpty(token) && context.HttpContext.Request.Path.StartsWithSegments("/hubs"))
            {
                context.Token = token;
            }
            return Task.CompletedTask;
        }
    };
});

builder.Services.AddAuthorizationBuilder()
    .AddPolicy(Policies.Analyst, p => p.RequireRole(RoleNames.Administrator, RoleNames.SecurityAnalyst))
    .AddPolicy(Policies.Administrator, p => p.RequireRole(RoleNames.Administrator))
    .SetFallbackPolicy(new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

var loginPerMinute = Math.Max(1, builder.Configuration.GetValue("RateLimits:LoginPerMinute", 5));
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("login", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = loginPerMinute, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});

var app = builder.Build();

await using (var scope = app.Services.CreateAsyncScope())
{
    await scope.ServiceProvider.GetRequiredService<DatabaseInitializer>().InitializeAsync(CancellationToken.None);
}

app.UseExceptionHandler();
app.UseSerilogRequestLogging(o => o.GetLevel = (http, _, ex) =>
    ex is not null || http.Response.StatusCode >= 500 ? Serilog.Events.LogEventLevel.Error :
    http.Request.Path.StartsWithSegments("/api/ingest") ? Serilog.Events.LogEventLevel.Debug : Serilog.Events.LogEventLevel.Information);
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapControllers();
app.MapHub<SiemHub>("/hubs/siem");
app.MapGet("/health", () => Results.Ok(new { status = "ok" })).AllowAnonymous();
app.MapFallbackToFile("index.html").AllowAnonymous();

app.Run();

public partial class Program;
