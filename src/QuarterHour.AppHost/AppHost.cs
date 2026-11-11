using Aspire.Hosting.Dotnet;

IDistributedApplicationBuilder builder = DistributedApplication
    .CreateBuilder(args);

IResourceBuilder<DotnetProjectResource> app = builder
    .AddDotnetProject("app", "../QuarterHour.App/QuarterHour.App.csproj");

builder.Build().Run();
