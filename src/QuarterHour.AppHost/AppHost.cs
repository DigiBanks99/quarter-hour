using Aspire.Hosting.Dotnet;
using QuarterHour.AppHost;

IDistributedApplicationBuilder builder = DistributedApplication
    .CreateBuilder(args);

IResourceBuilder<DotnetProjectResource> app = builder
    .AddDotnetProject("app", "../QuarterHour.App/QuarterHour.App.csproj")
    .WithEntraGraphParameters();

builder.Build().Run();
