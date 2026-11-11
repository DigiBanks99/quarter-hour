using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using QuarterHour.Core.Configuration;
using QuarterHour.Infrastructure.Storage;
using TUnit.Core.Interfaces;

namespace QuarterHour.EndToEndTests;

/// <summary>
/// Starts the real AppHost once per test session, with the app's data in a temporary directory: a starter workspace in
/// South African time with the agent scanners off, so nothing on the machine running the tests leaks in. Also starts
/// a headless Chromium for the browser tests.
/// </summary>
public sealed class QuarterHourApp : IAsyncInitializer, IAsyncDisposable
{
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromMinutes(3);

    private readonly string _dataDirectory = Path.Combine(Path.GetTempPath(), "quarter-hour-e2e", Guid.NewGuid().ToString("N"));
    private DistributedApplication? _app;
    private IPlaywright? _playwright;
    private IBrowser? _browser;

    public Uri BaseAddress { get; private set; } = default!;

    public string DataDirectory => _dataDirectory;

    public async Task InitializeAsync()
    {
        SeedWorkspace();

        using CancellationTokenSource timeout = new(StartupTimeout);
        IDistributedApplicationTestingBuilder builder = await DistributedApplicationTestingBuilder
            .CreateAsync<Projects.QuarterHour_AppHost>(timeout.Token);

        // The Entra parameters are optional, but Aspire counts an empty value as missing and won't start the app. The
        // app ignores whitespace-only Graph settings, so a space means "not configured".
        builder.Configuration["Parameters:entra-tenant-id"] = " ";
        builder.Configuration["Parameters:entra-client-id"] = " ";
        builder.Configuration["Parameters:entra-client-secret"] = " ";

        IResourceWithEnvironment app = builder.Resources.OfType<IResourceWithEnvironment>().Single(resource => resource.Name == "app");
        builder.CreateResourceBuilder(app).WithEnvironment("QuarterHour__DataDirectory", _dataDirectory);

        _app = await builder.BuildAsync(timeout.Token);
        await _app.StartAsync(timeout.Token);
        await _app.Services.GetRequiredService<ResourceNotificationService>().WaitForResourceHealthyAsync("app", timeout.Token);
        BaseAddress = _app.GetEndpoint("app", "https");

        _playwright = await Playwright.CreateAsync();
        _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
    }

    /// <summary>An HTTP client for the app that accepts its development certificate.</summary>
    public HttpClient CreateHttpClient() => _app!.CreateHttpClient("app", "https");

    /// <summary>A fresh browser tab (and Blazor circuit) on the app.</summary>
    public async Task<IPage> OpenPageAsync()
    {
        IBrowserContext context = await _browser!.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = BaseAddress.ToString(),
            IgnoreHTTPSErrors = true,
            Locale = "en-ZA",
            TimezoneId = "Africa/Johannesburg",
        });
        context.SetDefaultTimeout(30_000);
        return await context.NewPageAsync();
    }

    private void SeedWorkspace()
    {
        WorkspaceConfig config = DefaultWorkspace.Create().Normalize();
        config.General.TimeZoneId = "South Africa Standard Time";
        foreach (AgentScannerSettings scanner in config.Agents.Scanners)
        {
            scanner.Enabled = false;
        }

        config.Agents.AgentsView.Enabled = false;
        new WorkspaceStore(new DataDirectory(_dataDirectory)).SaveConfig(config);
    }

    public async ValueTask DisposeAsync()
    {
        if (_browser is not null)
        {
            await _browser.DisposeAsync();
        }

        _playwright?.Dispose();
        if (_app is not null)
        {
            await _app.DisposeAsync();
        }

        try
        {
            Directory.Delete(_dataDirectory, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
