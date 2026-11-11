using System.Net;
using System.Text.Json;

namespace QuarterHour.EndToEndTests;

[ClassDataSource<QuarterHourApp>(Shared = SharedType.PerTestSession)]
public sealed class AppHostTests(QuarterHourApp app)
{
    [Test]
    public async Task TheAppHostIsStarted()
    {
        using HttpClient client = app.CreateHttpClient();
        using HttpResponseMessage health = await client.GetAsync("/health");
        using HttpResponseMessage overview = await client.GetAsync("/");
        string page = await overview.Content.ReadAsStringAsync();

        ItShouldReportHealthy();
        ItShouldServeTheOverview();

        void ItShouldReportHealthy() => health.StatusCode.ShouldBe(HttpStatusCode.OK);

        void ItShouldServeTheOverview() => page.ShouldContain("<title>Overview · QuarterHour</title>");
    }

    [Test]
    public async Task TheConfigurationIsDownloaded()
    {
        using HttpClient client = app.CreateHttpClient();
        using HttpResponseMessage response = await client.GetAsync("/api/config");
        using JsonDocument config = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        ItShouldSucceed();
        ItShouldBeTheSeededWorkspace();

        void ItShouldSucceed() => response.StatusCode.ShouldBe(HttpStatusCode.OK);

        void ItShouldBeTheSeededWorkspace() => config.RootElement.GetProperty("general").GetProperty("timeZoneId").GetString()
            .ShouldBe("South Africa Standard Time");
    }

    [Test]
    [Arguments("next-month")]
    [Arguments("2026-13")]
    public async Task AnExportIsAskedForWithoutAValidPeriod(string period)
    {
        using HttpClient client = app.CreateHttpClient();
        using HttpResponseMessage response = await client.GetAsync($"/api/export/{period}");

        ItShouldBeRefused();

        void ItShouldBeRefused() => response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }
}
