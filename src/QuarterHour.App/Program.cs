using System.Diagnostics;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using MudBlazor.Services;
using QuarterHour.App;
using QuarterHour.App.Components;
using QuarterHour.App.Services;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddMudServices();
builder.Services.AddQuarterHour(builder.Configuration);

WebApplication app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();
app.MapQuarterHourEndpoints();

if (app.Configuration.GetValue("QuarterHour:OpenBrowser", true) && !app.Environment.IsDevelopment())
{
    app.Lifetime.ApplicationStarted.Register(() =>
    {
        string? address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()?.Addresses.FirstOrDefault();
        if (address is null)
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(address.Replace("://[::]", "://localhost").Replace("://0.0.0.0", "://localhost")) { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            app.Logger.LogInformation(exception, "Couldn't open a browser; open {Address} yourself.", address);
        }
    });
}

app.Run();
