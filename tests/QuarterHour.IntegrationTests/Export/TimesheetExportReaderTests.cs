using ClosedXML.Excel;
using QuarterHour.Core.Learning;
using QuarterHour.Infrastructure.Export;

namespace QuarterHour.IntegrationTests.Export;

public sealed class TimesheetExportReaderTests
{
    [Test]
    public void TheTimesheetSystemsExportIsRead()
    {
        using MemoryStream file = Workbook(
            ["Date", "Project", "Category", "Hours", "Minutes", "Billable", "Description", "Ticket Number", "Sentiment", "Worked From"],
            ["2026-10-12", "R - Acme - Portal", "Analysis", "5", "45", "True", "Product maintenance: search\r\nProposals: prompts", "", "Neutral", "Client"],
            ["2026-10-12", "Social Events", "Team Building Attendance", "0", "30", "False", "Coffee Run", "", "Happy", "Client"],
            ["not a date", "", "", "", "", "", "", "", "", ""]);

        IReadOnlyList<TimesheetRow> rows = TimesheetExportReader.Read(file);

        ItShouldReadEveryRealRow();
        ItShouldAddHoursAndMinutes();
        ItShouldKeepMultiLineDescriptions();

        void ItShouldReadEveryRealRow() => rows.Count.ShouldBe(2);

        void ItShouldAddHoursAndMinutes() => rows[0].Minutes.ShouldBe(345);

        void ItShouldKeepMultiLineDescriptions() => rows[0].Description.ShouldBe("Product maintenance: search\nProposals: prompts");
    }

    [Test]
    public void TheColumnsAreInAnotherOrder()
    {
        using MemoryStream file = Workbook(
            ["Worked From", "Hours", "Category", "Date", "Project"],
            ["Home", "1.5", "Meetings", "2026-10-13", "Client"]);

        TimesheetRow row = TimesheetExportReader.Read(file).Single();

        ItShouldFindColumnsByTheirHeaders();

        void ItShouldFindColumnsByTheirHeaders() => row.ShouldSatisfyAllConditions(
            () => row.Minutes.ShouldBe(90),
            () => row.WorkedFrom.ShouldBe("Home"),
            () => row.Date.ShouldBe(new DateOnly(2026, 10, 13)));
    }

    private static MemoryStream Workbook(params string[][] rows)
    {
        using XLWorkbook workbook = new();
        IXLWorksheet sheet = workbook.AddWorksheet("Timesheet");
        for (int row = 0; row < rows.Length; row++)
        {
            for (int column = 0; column < rows[row].Length; column++)
            {
                sheet.Cell(row + 1, column + 1).Value = rows[row][column];
            }
        }

        MemoryStream stream = new();
        workbook.SaveAs(stream);
        stream.Position = 0;
        return stream;
    }
}
