using QuarterHour.Core.Configuration;
using QuarterHour.Core.Export;
using QuarterHour.Core.Model;
using QuarterHour.Core.Time;
using QuarterHour.Core.Timesheets;

namespace QuarterHour.Core.Tests.Export;

public sealed class ExportRowsTests
{
    private static readonly TimesheetPeriod November = new(2026, 11);
    private static readonly DateOnly Monday = new(2026, 11, 9);

    private readonly WorkspaceConfig _config = TestData.Workspace();

    [Test]
    public void AMeetingIsExportedForTheUploadTemplate()
    {
        _config.Rules.Add(TestData.Heuristic("Sync", "title", ConditionOperator.Contains, "sync", DefaultWorkspace.ClientMeetings));
        PeriodTimesheet sheet = Build(TestData.Meeting("m1", "Client sync", Monday.ToDateTime(new TimeOnly(10, 0)), 50));

        ExportTable table = ExportRows.Build(sheet, _config);
        IReadOnlyList<ExportCell> row = table.Rows.Single();

        ItShouldUseTheUploadTemplatesColumns();
        ItShouldWriteARealDateAndWholeNumbers();
        ItShouldFillTheRowAsTheTemplateSpellsIt();

        void ItShouldUseTheUploadTemplatesColumns() => table.Headers.ShouldBe(
            ["Date", "Project", "Category", "Hours", "Minutes", "Billable", "Description", "TicketNumber", "Sentiment", "WorkedFrom"]);

        void ItShouldWriteARealDateAndWholeNumbers() => row.Select(cell => cell.Kind).ShouldBe([
            ExportCellKind.Date, ExportCellKind.Text, ExportCellKind.Text, ExportCellKind.Number, ExportCellKind.Number,
            ExportCellKind.Text, ExportCellKind.Text, ExportCellKind.Text, ExportCellKind.Text, ExportCellKind.Text,
        ]);

        void ItShouldFillTheRowAsTheTemplateSpellsIt() => row.Select(cell => cell.Display).ShouldBe(
            ["2026-11-09", DefaultWorkspace.ClientProject, "Meetings", "0", "45", "Yes", "Client sync", "", "Neutral", "Home"]);
    }

    [Test]
    public void AnOlderConfigurationIsLoaded()
    {
        WorkspaceConfig config = new()
        {
            SchemaVersion = 1,
            Export = new ExportProfile
            {
                SheetName = "Timesheet",
                FileNameTemplate = "TimesheetExport {period}.xlsx",
                CellsAsText = true,
                Sentiments = ["Happy", "Neutral", "Unhappy"],
                Columns =
                [
                    .. new[] { "Date", "Project", "Category", "Hours", "Minutes", "Billable", "Description", "Ticket Number", "Sentiment", "Worked From" }
                        .Select(header => new ExportColumn { Header = header, Value = "{description}" }),
                ],
            },
        };

        config.Normalize();

        ItShouldMoveToTheUploadTemplate();

        void ItShouldMoveToTheUploadTemplate() => config.ShouldSatisfyAllConditions(
            () => config.SchemaVersion.ShouldBe(WorkspaceConfig.CurrentSchemaVersion),
            () => config.Export.Columns.Select(column => column.Header).ShouldBe(ExportProfile.DefaultColumns().Select(column => column.Header)),
            () => config.Export.SheetName.ShouldBe("TimesheetUpload"),
            () => config.Export.TableName.ShouldBe("TimesheetEntry"),
            () => config.Export.FileNameTemplate.ShouldBe("TimesheetUpload {period}.xlsx"),
            () => config.Export.CellsAsText.ShouldBeFalse(),
            () => config.Export.Sentiments.ShouldBe(["Happy", "Neutral", "Sad"]));
    }

    [Test]
    public void TypedCellsAreWanted()
    {
        _config.Export.CellsAsText = false;
        _config.Export.Columns = [new ExportColumn { Header = "Date", Value = "{date}" }, new ExportColumn { Header = "Hours", Value = "{hours}" }];
        _config.Rules.Add(TestData.Heuristic("Sync", "title", ConditionOperator.Contains, "sync", DefaultWorkspace.ClientMeetings));
        PeriodTimesheet sheet = Build(TestData.Meeting("m1", "Client sync", Monday.ToDateTime(new TimeOnly(10, 0)), 50));

        IReadOnlyList<ExportCell> row = ExportRows.Build(sheet, _config).Rows.Single();

        ItShouldWriteARealDateAndNumber();

        void ItShouldWriteARealDateAndNumber() => row.ShouldBe([
            new ExportCell(new DateTime(2026, 11, 9), ExportCellKind.Date, "yyyy-mm-dd"),
            new ExportCell(0.75, ExportCellKind.Number, "0.00"),
        ]);
    }

    [Test]
    public void AColumnCombinesSeveralTokens()
    {
        TimesheetLine line = new()
        {
            Id = "l1",
            Date = Monday,
            CategoryId = DefaultWorkspace.TeamBuild,
            Location = "Client",
            Description = "Coffee run",
            Source = ActivitySource.Calendar,
            Minutes = 30,
        };
        Category category = _config.FindCategory(DefaultWorkspace.TeamBuild)!;
        category.Code = "SOC-01";

        ExportCell combined = ExportRows.Cell("{project} - {category} ({code}) on {date:dd/MM/yyyy}", line, category, November);
        ExportCell billable = ExportRows.Cell("{billable:Yes/No}", line, category, November);

        ItShouldBeText();
        ItShouldUseTheChosenWordsForBillable();

        void ItShouldBeText() => combined.ShouldBe(new ExportCell("Social Events - Team Building Attendance (SOC-01) on 09/11/2026", ExportCellKind.Text));

        void ItShouldUseTheChosenWordsForBillable() => billable.Display.ShouldBe("No");
    }

    [Test]
    public void SomeTimeIsStillUnmapped()
    {
        PeriodTimesheet sheet = Build(TestData.Meeting("m1", "Mystery", Monday.ToDateTime(new TimeOnly(10, 0)), 60));

        ExportTable leftOut = ExportRows.Build(sheet, _config);
        _config.Export.IncludeUnmapped = true;
        ExportTable included = ExportRows.Build(sheet, _config);

        ItShouldBeLeftOutUnlessAskedFor();

        void ItShouldBeLeftOutUnlessAskedFor()
        {
            leftOut.Rows.ShouldBeEmpty();
            included.Rows.Count.ShouldBe(1);
        }
    }

    [Test]
    public void LeaveWasTaken()
    {
        _config.Rules.Add(TestData.Heuristic("Out of office", "showAs", ConditionOperator.Equals, "oof", DefaultWorkspace.AnnualLeave));
        PeriodTimesheet sheet = Build(TestData.AllDay("leave", "Leave", Monday, days: 1));

        ExportTable byDefault = ExportRows.Build(sheet, _config);
        _config.Export.IncludeLeave = true;
        ExportTable withLeave = ExportRows.Build(sheet, _config);

        ItShouldStayOutOfTheUploadUnlessAskedFor();

        void ItShouldStayOutOfTheUploadUnlessAskedFor()
        {
            byDefault.Rows.ShouldBeEmpty();
            withLeave.Rows.Single()[3].Display.ShouldBe("8");
        }
    }

    [Test]
    public void TheFileIsNamedForThePeriod()
    {
        string name = ExportRows.FileName(new ExportProfile { FileNameTemplate = "Wilco {month}" }, November);

        ItShouldIncludeTheMonthAndExtension();

        void ItShouldIncludeTheMonthAndExtension() => name.ShouldBe("Wilco November 2026.xlsx");
    }

    private PeriodTimesheet Build(params Activity[] activities) =>
        new TimesheetBuilder().Build(November, _config.Normalize(), new Journal(), activities);
}
