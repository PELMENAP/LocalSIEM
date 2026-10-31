using LocalSIEM.Application.Alerts;
using LocalSIEM.Application.Common;
using LocalSIEM.Application.Dashboard;
using LocalSIEM.Application.Detection;
using LocalSIEM.Application.Events;
using LocalSIEM.Application.Identity;
using LocalSIEM.Application.Incidents;
using LocalSIEM.Application.Ingestion;
using LocalSIEM.Application.Settings;
using LocalSIEM.Application.Simulator;
using LocalSIEM.Application.Sources;
using Microsoft.Extensions.DependencyInjection;

namespace LocalSIEM.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services, int detectionQueueCapacity)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IDetectionQueue>(_ => new DetectionQueue(detectionQueueCapacity));
        services.AddSingleton<RuleCatalog>();
        services.AddSingleton<DetectionControl>();
        services.AddSingleton<SuppressionStore>();
        services.AddSingleton<IRuleEvaluator, ThresholdEvaluator>();
        services.AddSingleton<IRuleEvaluator, DistinctCountEvaluator>();
        services.AddSingleton<IRuleEvaluator, SequenceEvaluator>();
        services.AddSingleton<IRuleEvaluator, SingleEventEvaluator>();
        services.AddSingleton<DetectionEngine>();
        services.AddSingleton<DetectionPipeline>();
        services.AddSingleton<AlertUpdateThrottle>();
        services.AddSingleton<SourceKeyCache>();
        services.AddSingleton<SettingsCache>();

        services.AddSingleton<IEventNormalizer, HttpApiNormalizer>();
        services.AddSingleton<IEventNormalizer, SimulatorNormalizer>();
        services.AddSingleton<IEventNormalizer, WindowsEventNormalizer>();
        services.AddSingleton<NormalizerRegistry>();

        services.AddSingleton<IScenario, NormalActivityScenario>();
        services.AddSingleton<IScenario, FailedSeriesScenario>();
        services.AddSingleton<IScenario, BruteForceScenario>();
        services.AddSingleton<IScenario, PasswordSprayingScenario>();
        services.AddSingleton<IScenario, SuccessAfterFailuresScenario>();
        services.AddSingleton<IScenario, PrivilegeEscalationScenario>();
        services.AddSingleton<IScenario, SuspiciousAccessScenario>();
        services.AddSingleton<SimulatorRunner>();

        services.AddScoped<IAuditService, AuditService>();
        services.AddScoped<IEventIngestionService, EventIngestionService>();
        services.AddScoped<IEventQueryService, EventQueryService>();
        services.AddScoped<IAlertService, AlertService>();
        services.AddScoped<IIncidentService, IncidentService>();
        services.AddScoped<IRuleService, RuleService>();
        services.AddScoped<IDashboardService, DashboardService>();
        services.AddScoped<ISourceService, SourceService>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IUserService, UserService>();
        services.AddScoped<ISettingsService, SettingsService>();
        services.AddScoped<IDemoDataService, DemoDataService>();
        return services;
    }
}
