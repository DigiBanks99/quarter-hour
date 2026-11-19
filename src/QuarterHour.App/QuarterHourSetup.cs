using OpenTelemetry.Trace;
using QuarterHour.App.Services;
using QuarterHour.Core.Configuration;
using QuarterHour.Core.Export;
using QuarterHour.Core.Sources;
using QuarterHour.Core.Time;
using QuarterHour.Core.Timesheets;
using QuarterHour.Infrastructure.Agents;
using QuarterHour.Infrastructure.Calendar;
using QuarterHour.Infrastructure.Export;
using QuarterHour.Infrastructure.AzureDevOps;
using QuarterHour.Infrastructure.Git;
using QuarterHour.Infrastructure.Storage;

namespace QuarterHour.App;

public static class QuarterHourSetup
{
    public const string GraphClient = "graph";
    public const string AgentsViewClient = "agentsview";

    public static IServiceCollection AddQuarterHour(this IServiceCollection services, IConfiguration configuration)
    {
        services.ConfigureOpenTelemetryTracerProvider(tracing => tracing.AddSource(Telemetry.BackendSource, Telemetry.UiSource));
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(new DataDirectory(configuration["QuarterHour:DataDirectory"]));
        services.AddSingleton<WorkspaceStore>();
        services.AddSingleton<CalendarCache>();
        services.AddSingleton<GraphAuthService>();
        services.AddHttpClient(GraphClient, client => client.Timeout = TimeSpan.FromSeconds(100));
        services.AddHttpClient(AgentsViewClient, client => client.Timeout = TimeSpan.FromSeconds(30));

        services.AddSingleton<IAgentSessionSource, ClaudeCodeSessionSource>();
        services.AddSingleton<IAgentSessionSource, CopilotCliSessionSource>();
        services.AddSingleton<IAgentSessionSource, CodexSessionSource>();
        services.AddSingleton<IAgentSessionSource, OpenCodeSessionSource>();
        services.AddSingleton<IAgentSessionSource, PiSessionSource>();
        services.AddSingleton<IAgentSessionSource>(provider =>
            new AgentsViewSessionSource(provider.GetRequiredService<IHttpClientFactory>().CreateClient(AgentsViewClient)));
        services.AddSingleton<AgentSessionScanner>();
        services.AddSingleton<ICommitSource>(_ => new GitCommitSource());
        services.AddSingleton<IPullRequestSource>(_ => new AzureDevOpsPullRequestSource());

        services.AddSingleton<WorkspaceService>();
        services.AddScoped<UiState>();
        return services;
    }

    public static void MapQuarterHourEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/export/{period}", async (string period, DateOnly? from, DateOnly? to, WorkspaceService workspace, CancellationToken cancellationToken) =>
        {
            if (!TimesheetPeriod.TryParse(period, out TimesheetPeriod parsed))
            {
                return Results.BadRequest("Use a period like 2026-11.");
            }

            if (!ExportRange.TryCreate(parsed, from, to, out ExportRange? range, out string? problem))
            {
                return Results.BadRequest(problem);
            }

            PeriodTimesheet sheet = await workspace.GetTimesheetAsync(parsed, cancellationToken);
            WorkspaceConfig config = workspace.ConfigSnapshot();
            byte[] file = ExcelExporter.Export(sheet, config, range);
            return Results.File(file, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", ExportRows.FileName(config.Export, parsed, range));
        });

        endpoints.MapGet("/api/config", (WorkspaceService workspace) =>
            Results.File(System.Text.Encoding.UTF8.GetBytes(WorkspaceStore.ExportConfig(workspace.ConfigSnapshot())), "application/json", "quarterhour-config.json"));
    }
}
