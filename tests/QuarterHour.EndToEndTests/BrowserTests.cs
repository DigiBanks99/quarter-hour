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
    [Arguments("Projects", "Projects · QuarterHour")]
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
    public async Task TheCommitsBoardIsOpenedWithoutAgentWork()
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

        await page.GotoAsync("/mapping");
        await page.WaitForInteractiveAsync();
        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { NameRegex = new System.Text.RegularExpressions.Regex("Commits & PRs") }).ClickAsync();
        await Assertions.Expect(page.Locator(".qh-board-candidates")).ToBeVisibleAsync();
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        bool crashed = await page.Locator("#blazor-error-ui").IsVisibleAsync();
        string unattributed = await page.Locator(".qh-orphans .qh-col-head").InnerTextAsync();

        ItShouldNotCrashTheCircuit();
        ItShouldShowAnEmptyUnattributedColumn();
        ItShouldNotLogErrorsInTheBrowser();

        await page.Context.CloseAsync();

        void ItShouldNotCrashTheCircuit() => crashed.ShouldBeFalse();

        void ItShouldShowAnEmptyUnattributedColumn() => unattributed.ShouldContain("Unattributed (0)");

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

    [Test]
    public async Task PartOfTheMonthIsPickedForTheExport()
    {
        IPage page = await app.OpenPageAsync();
        await page.GotoAsync("/settings");
        await page.WaitForInteractiveAsync();
        await page.ShowPeriodAsync(2026, 11);
        await page.NavigateAsync("Export");
        ILocator head = page.Locator(".qh-page-head");
        await Assertions.Expect(head).ToContainTextAsync("TimesheetUpload 2026-11.xlsx");

        ILocator range = page.Locator(".qh-export-range");
        ILocator from = range.GetByRole(AriaRole.Textbox, new LocatorGetByRoleOptions { Name = "From", Exact = true });
        ILocator to = range.GetByRole(AriaRole.Textbox, new LocatorGetByRoleOptions { Name = "To", Exact = true });
        await from.FillAsync("3 Nov 2026");
        await from.PressAsync("Tab");
        await to.FillAsync("14 Nov 2026");
        await to.PressAsync("Tab");
        await Assertions.Expect(head).ToContainTextAsync("TimesheetUpload 2026-11 (03–14).xlsx");
        string note = await page.Locator(".qh-checklist").InnerTextAsync();

        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Whole month" }).ClickAsync();
        await Assertions.Expect(head).ToContainTextAsync("TimesheetUpload 2026-11.xlsx");
        string wholeMonth = await head.InnerTextAsync();

        await page.Context.CloseAsync();

        ItShouldSayHowManyDaysTheUploadHolds();
        ItShouldGoBackToTheWholeMonth();

        void ItShouldSayHowManyDaysTheUploadHolds() => note.ShouldContain("The upload holds 12 days (3–14 Nov) of November 2026");

        void ItShouldGoBackToTheWholeMonth() => wholeMonth.ShouldNotContain("(03–14)");
    }
}
