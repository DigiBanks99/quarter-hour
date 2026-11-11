using Aspire.Hosting.Dotnet;
using QuarterHour.AppHost;

IDistributedApplicationBuilder builder = DistributedApplication
    .CreateBuilder(args);

#pragma warning disable ASPIREBROWSERLOGS001 // Browser logs are experimental.
IResourceBuilder<DotnetProjectResource> app = builder
    .AddDotnetProject("app", "../QuarterHour.App/QuarterHour.App.csproj")
    .WithHttpHealthCheck("/health")
    .WithBrowserLogs()
    .WithEntraGraphParameters();
#pragma warning restore ASPIREBROWSERLOGS001

builder.Build().Run();
