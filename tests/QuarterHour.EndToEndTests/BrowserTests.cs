using System.Net;
using ClosedXML.Excel;
using Microsoft.Playwright;

namespace QuarterHour.EndToEndTests;

[ClassDataSource<QuarterHourApp>(Shared = SharedType.PerTestSession)]
public sealed class BrowserTests(QuarterHourApp app)
{
    [Test]
    [Arguments("Overview", "Overview · QuarterHour")]
    [Arguments("Mapping", "Mapping · QuarterHour")]
    [Arguments("Rules", "Rules · QuarterHour")]
    [Arguments("Categories", "Categories · QuarterHour")]
    [Arguments("Locations", "Locations · QuarterHour")]
    [Arguments("Export", "Export · QuarterHour")]
    [Arguments("Settings", "Settings · QuarterHour")]
    public async Task APageIsOpenedFromTheMenu(string link, string title)
    {
        IPage page = await app.OpenPageAsync();
        List<string> consoleErrors = [];
        page.Console += (_, message) =>
        {
            if (message.Type == "error")
            {
                consoleErrors.Add(message.Text);
            }
        };

        await page.GotoAsync("/settings");
        await page.WaitForInteractiveAsync();
        await page.NavigateAsync(link);
        await Assertions.Expect(page).ToHaveTitleAsync(title);
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        bool crashed = await page.Locator("#blazor-error-ui").IsVisibleAsync();
        int failedTimesheets = await page.GetByText("Couldn't build the timesheet").CountAsync();

        ItShouldNotCrashTheCircuit();
        ItShouldBuildTheTimesheet();
        ItShouldNotLogErrorsInTheBrowser();

        await page.Context.CloseAsync();

        void ItShouldNotCrashTheCircuit() => crashed.ShouldBeFalse();

        void ItShouldBuildTheTimesheet() => failedTimesheets.ShouldBe(0);

        void ItShouldNotLogErrorsInTheBrowser() => consoleErrors.ShouldBeEmpty();
    }

    [Test]
    public async Task TheSampleCalendarIsImportedAndExported()
    {
        IPage page = await app.OpenPageAsync();
        await page.GotoAsync("/settings");
        await page.WaitForInteractiveAsync();
        await page.ShowPeriodAsync(2026, 11);
        ILocator upload = page.Locator("input[type=file][accept^='.ics']");
        // Blazor tags the input once its circuit is wired up; files set before that are silently dropped.
        await upload.EvaluateAsync("input => new Promise(resolve => { const check = () => input._blazorInputFileNextFileId !== undefined ? resolve() : setTimeout(check, 50); check(); })");
        await upload.SetInputFilesAsync(Pages.Sample("november-2026.ics"));
        ILocator imported = page.Locator(".mud-snackbar").Filter(new LocatorFilterOptions { HasText = "Imported" });
        await Assertions.Expect(imported).ToBeVisibleAsync();
        string importMessage = await imported.InnerTextAsync();

        await page.NavigateAsync("Overview");
        await Assertions.Expect(page.Locator("h4.qh-title")).ToHaveTextAsync("November 2026");
        string overview = await page.Locator(".qh-page-head").InnerTextAsync();

        using HttpClient client = app.CreateHttpClient();
        using HttpResponseMessage export = await client.GetAsync("/api/export/2026-11");
        using XLWorkbook workbook = new(await export.Content.ReadAsStreamAsync());
        IXLTable entries = workbook.Worksheet("TimesheetUpload").Table("TimesheetEntry");
        List<DateTime> dates = [.. entries.DataRange.Rows().Select(row => row.Field("Date").GetDateTime())];

        await page.Context.CloseAsync();

        ItShouldImportNovembersMeetings();
        ItShouldShowTheCalendarAsSynced();
        ItShouldDownloadTheUploadFile();
        ItShouldOnlyExportNovember();

        void ItShouldImportNovembersMeetings() => importMessage.ShouldBe("Imported 77 events for November 2026.");

        void ItShouldShowTheCalendarAsSynced() => overview.ShouldContain("calendar synced");

        void ItShouldDownloadTheUploadFile() => export.StatusCode.ShouldBe(HttpStatusCode.OK);

        void ItShouldOnlyExportNovember() => dates.ShouldSatisfyAllConditions(
            () => dates.ShouldNotBeEmpty(),
            () => dates.ShouldAllBe(date => date.Year == 2026 && date.Month == 11));
    }
}
