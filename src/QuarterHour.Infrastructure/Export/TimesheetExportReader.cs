using System.Globalization;
using ClosedXML.Excel;
using QuarterHour.Core.Learning;

namespace QuarterHour.Infrastructure.Export;

/// <summary>
/// Reads a timesheet the Timesheet system exported (TimesheetExport.xlsx). Columns are found by their headers, so
/// their order doesn't matter; a single decimal "Hours" column works when there's no "Minutes" column.
/// </summary>
public static class TimesheetExportReader
{
    public static IReadOnlyList<TimesheetRow> Read(Stream xlsx)
    {
        using XLWorkbook workbook = new(xlsx);
        IXLWorksheet worksheet = workbook.Worksheets.FirstOrDefault(sheet => string.Equals(sheet.Name, "Timesheet", StringComparison.OrdinalIgnoreCase))
                                 ?? workbook.Worksheets.First();
        IXLRange? used = worksheet.RangeUsed();
        if (used is null)
        {
            return [];
        }

        IXLRangeRow header = used.FirstRow();
        Dictionary<string, int> columns = new(StringComparer.OrdinalIgnoreCase);
        foreach (IXLCell cell in header.Cells())
        {
            string name = Normalise(cell.GetString());
            if (name.Length > 0)
            {
                columns.TryAdd(name, cell.Address.ColumnNumber);
            }
        }

        int? date = Column(columns, "date", "day");
        int? project = Column(columns, "project", "client");
        int? category = Column(columns, "category", "task", "subcategory");
        int? hours = Column(columns, "hours", "hour", "duration");
        int? minutes = Column(columns, "minutes", "mins");
        if (date is null || hours is null || (project is null && category is null))
        {
            throw new FormatException("That workbook doesn't look like a timesheet export: it needs Date, Project/Category and Hours columns.");
        }

        int? billable = Column(columns, "billable");
        int? description = Column(columns, "description", "comment", "comments", "notes");
        int? ticket = Column(columns, "ticketnumber", "ticket");
        int? sentiment = Column(columns, "sentiment", "mood");
        int? workedFrom = Column(columns, "workedfrom", "location", "where");

        List<TimesheetRow> rows = [];
        foreach (IXLRangeRow row in used.RowsUsed().Skip(1))
        {
            if (!TryDate(row.Cell(date.Value), out DateOnly day))
            {
                continue;
            }

            double hourValue = Number(row.Cell(hours.Value));
            int total = minutes is { } minuteColumn
                ? (int)Math.Round((Math.Truncate(hourValue) * 60) + Number(row.Cell(minuteColumn)))
                : (int)Math.Round(hourValue * 60);
            rows.Add(new TimesheetRow(
                day,
                Text(row, project),
                Text(row, category),
                Math.Max(0, total),
                billable is { } billableColumn && IsTrue(row.Cell(billableColumn).GetString()),
                Text(row, description).Replace("\r\n", "\n"),
                NullIfEmpty(Text(row, ticket)),
                NullIfEmpty(Text(row, sentiment)),
                NullIfEmpty(Text(row, workedFrom))));
        }

        return rows;
    }

    private static int? Column(Dictionary<string, int> columns, params string[] names)
    {
        foreach (string name in names)
        {
            if (columns.TryGetValue(name, out int column))
            {
                return column;
            }
        }

        return null;
    }

    private static string Normalise(string header) => new(header.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    private static string Text(IXLRangeRow row, int? column) => column is { } index ? row.Cell(index).GetString().Trim() : "";

    private static string? NullIfEmpty(string text) => text.Length == 0 ? null : text;

    private static bool TryDate(IXLCell cell, out DateOnly date)
    {
        date = default;
        if (cell.DataType == XLDataType.DateTime)
        {
            date = DateOnly.FromDateTime(cell.GetDateTime());
            return true;
        }

        string text = cell.GetString().Trim();
        if (DateOnly.TryParseExact(text, ["yyyy-MM-dd", "yyyy/MM/dd", "dd/MM/yyyy", "d/M/yyyy", "dd-MM-yyyy"], CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
        {
            return true;
        }

        if (cell.DataType == XLDataType.Number && cell.GetDouble() is > 20000 and < 80000 and double serial)
        {
            date = DateOnly.FromDateTime(DateTime.FromOADate(serial));
            return true;
        }

        return false;
    }

    private static double Number(IXLCell cell)
    {
        if (cell.DataType == XLDataType.Number)
        {
            return cell.GetDouble();
        }

        string text = cell.GetString().Trim().Replace(',', '.');
        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) ? value : 0;
    }

    private static bool IsTrue(string text) =>
        text.Trim().ToLowerInvariant() is "true" or "yes" or "y" or "1" or "billable";
}
