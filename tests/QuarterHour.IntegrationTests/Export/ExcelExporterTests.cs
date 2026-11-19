using ClosedXML.Excel;
using QuarterHour.Core.Configuration;
using QuarterHour.Core.Export;
using QuarterHour.Core.Learning;
using QuarterHour.Core.Model;
using QuarterHour.Core.Time;
using QuarterHour.Core.Timesheets;
using QuarterHour.Infrastructure.Export;

namespace QuarterHour.IntegrationTests.Export;

public sealed class ExcelExporterTests
{
    private readonly WorkspaceConfig _config = DefaultWorkspace.Create();
    private readonly PeriodTimesheet _sheet;

    public ExcelExporterTests()
    {
        _config.Rules.Add(new Rule
        {
            Name = "Standups",
            Conditions = [new RuleCondition { Field = "title", Operator = ConditionOperator.Contains, Value = "standup" }],
            CategoryId = DefaultWorkspace.ClientMeetings,
        });
        Activity standup = CalendarActivities.ToActivity(new CalendarEvent
        {
            Id = "s1",
            Subject = "Daily standup",
            Start = new DateTimeOffset(2026, 11, 9, 9, 0, 0, TimeSpan.Zero),
            End = new DateTimeOffset(2026, 11, 9, 9, 20, 0, TimeSpan.Zero),
            Response = "accepted",
        }, TimeZoneInfo.Utc);
        _sheet = new TimesheetBuilder().Build(new TimesheetPeriod(2026, 11), _config, new Journal(), [standup]);
    }

    [Test]
    public void AMonthIsExported()
    {
        byte[] file = ExcelExporter.Export(_sheet, _config);
        using XLWorkbook workbook = new(new MemoryStream(file));
        IXLWorksheet timesheet = workbook.Worksheet("TimesheetUpload");

        ItShouldBeTheOnlySheet();
        ItShouldWriteATableLikeTheUploadTemplate();
        ItShouldWriteTheRowAsTheTemplateExpects();

        void ItShouldBeTheOnlySheet() => workbook.Worksheets.Select(worksheet => worksheet.Name).ShouldBe(["TimesheetUpload"]);

        void ItShouldWriteATableLikeTheUploadTemplate() => timesheet.Tables.Single().ShouldSatisfyAllConditions(
            () => timesheet.Tables.Single().Name.ShouldBe("TimesheetEntry"),
            () => timesheet.Tables.Single().Fields.Select(field => field.Name).ShouldBe(
                ["Date", "Project", "Category", "Hours", "Minutes", "Billable", "Description", "TicketNumber", "Sentiment", "WorkedFrom"]));

        void ItShouldWriteTheRowAsTheTemplateExpects() => timesheet.Row(2).Cells(1, 10).ShouldSatisfyAllConditions(
            () => timesheet.Row(2).Cells(2, 10).Select(cell => cell.GetString()).ShouldBe(
                [DefaultWorkspace.ClientProject, "Meetings", "0", "15", "Yes", "Daily standup", "", "Neutral", "Client"]),
            () => timesheet.Cell(2, 1).GetDateTime().ShouldBe(new DateTime(2026, 11, 9)),
            () => timesheet.Cell(2, 4).DataType.ShouldBe(XLDataType.Number),
            () => timesheet.Cell(2, 5).DataType.ShouldBe(XLDataType.Number));
    }

    [Test]
    public void AnExportIsReadBack()
    {
        byte[] file = ExcelExporter.Export(_sheet, _config);

        TimesheetRow row = TimesheetExportReader.Read(new MemoryStream(file)).Single();

        ItShouldComeBackAsTheSameRow();

        void ItShouldComeBackAsTheSameRow() =>
            row.ShouldBe(new TimesheetRow(new DateOnly(2026, 11, 9), DefaultWorkspace.ClientProject, "Meetings", 15, true, "Daily standup", null, "Neutral", "Client"));
    }

    [Test]
    public void ASummaryIsAskedFor()
    {
        _config.Export.IncludeSummarySheet = true;

        byte[] file = ExcelExporter.Export(_sheet, _config);
        using XLWorkbook workbook = new(new MemoryStream(file));

        ItShouldAddASecondSheet();

        void ItShouldAddASecondSheet() => workbook.Worksheets.Select(worksheet => worksheet.Name).ShouldBe(["TimesheetUpload", "Summary"]);
    }

    [Test]
    public void PartOfTheMonthIsExported()
    {
        _config.Export.IncludeSummarySheet = true;
        _config.Rules.Add(new Rule
        {
            Name = "Team building",
            Conditions = [new RuleCondition { Field = "title", Operator = ConditionOperator.Contains, Value = "team building" }],
            CategoryId = DefaultWorkspace.TeamBuild,
        });
        TimesheetPeriod november = new(2026, 11);
        PeriodTimesheet sheet = new TimesheetBuilder().Build(november, _config, new Journal(),
        [
            Meeting("s1", "Daily standup", 9),
            Meeting("t1", "Team building", 10),
            Meeting("s2", "Daily standup", 20),
        ]);

        byte[] file = ExcelExporter.Export(sheet, _config, new ExportRange(new DateOnly(2026, 11, 9), new DateOnly(2026, 11, 10)));
        using XLWorkbook workbook = new(new MemoryStream(file));
        List<TimesheetRow> rows = [.. TimesheetExportReader.Read(new MemoryStream(file))];
        IXLWorksheet summary = workbook.Worksheet("Summary");
        int summaryDays = summary.Column(1).CellsUsed().Count(cell => cell.DataType == XLDataType.DateTime);

        ItShouldOnlyHoldTheRangesRows();
        ItShouldMarkEachLinesBillable();
        ItShouldSummariseTheWholeMonth();

        void ItShouldOnlyHoldTheRangesRows() => rows.Select(row => row.Date).ShouldBe([new DateOnly(2026, 11, 9), new DateOnly(2026, 11, 10)]);

        void ItShouldMarkEachLinesBillable() => rows.Select(row => row.Billable).ShouldBe([true, false]);

        void ItShouldSummariseTheWholeMonth() => summaryDays.ShouldBe(30);
    }

    private static Activity Meeting(string id, string subject, int day) => CalendarActivities.ToActivity(new CalendarEvent
    {
        Id = id,
        Subject = subject,
        Start = new DateTimeOffset(2026, 11, day, 9, 0, 0, TimeSpan.Zero),
        End = new DateTimeOffset(2026, 11, day, 10, 0, 0, TimeSpan.Zero),
        Response = "accepted",
    }, TimeZoneInfo.Utc);
}
