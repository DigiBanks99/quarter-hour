using ClosedXML.Excel;
using QuarterHour.Core.Configuration;
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
}
