using ClosedXML.Excel;
using QuarterHour.Core.Configuration;
using QuarterHour.Core.Export;
using QuarterHour.Core.Time;
using QuarterHour.Core.Timesheets;

namespace QuarterHour.Infrastructure.Export;

/// <summary>Writes the timesheet as an .xlsx: the upload sheet laid out by the export profile, plus an optional summary.</summary>
public static class ExcelExporter
{
    private const double MaxColumnWidth = 80;

    public static byte[] Export(PeriodTimesheet sheet, WorkspaceConfig config)
    {
        ExportTable table = ExportRows.Build(sheet, config);
        using XLWorkbook workbook = new();
        string sheetName = SheetName(config.Export.SheetName, ExportProfile.DefaultSheetName);
        IXLWorksheet worksheet = workbook.AddWorksheet(sheetName);
        for (int column = 0; column < table.Headers.Count; column++)
        {
            IXLCell header = worksheet.Cell(1, column + 1);
            header.Value = table.Headers[column];
            header.Style.Font.Bold = true;
        }

        for (int row = 0; row < table.Rows.Count; row++)
        {
            IReadOnlyList<ExportCell> cells = table.Rows[row];
            for (int column = 0; column < cells.Count; column++)
            {
                Write(worksheet.Cell(row + 2, column + 1), cells[column]);
            }
        }

        if (config.Export.WriteAsTable && table.Headers.Count > 0)
        {
            IXLTable excelTable = worksheet.Range(1, 1, Math.Max(2, table.Rows.Count + 1), table.Headers.Count).CreateTable(TableName(config.Export.TableName, sheetName));
            excelTable.Theme = XLTableTheme.TableStyleMedium6;
            excelTable.ShowAutoFilter = true;
        }
        else
        {
            worksheet.Row(1).Style.Fill.BackgroundColor = XLColor.FromHtml("#E5E7EB");
        }

        worksheet.SheetView.FreezeRows(1);
        Fit(worksheet);

        if (config.Export.IncludeSummarySheet)
        {
            AddSummary(workbook, sheet, config);
        }

        using MemoryStream stream = new();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static void Write(IXLCell cell, ExportCell value)
    {
        switch (value.Kind)
        {
            case ExportCellKind.Date when value.Value is DateTime date:
                cell.Value = date;
                cell.Style.DateFormat.Format = value.Format ?? "yyyy-mm-dd";
                break;
            case ExportCellKind.Number when value.Value is double number:
                cell.Value = number;
                cell.Style.NumberFormat.Format = value.Format ?? "General";
                break;
            default:
                string text = value.Value?.ToString() ?? "";
                cell.Value = text;
                cell.Style.NumberFormat.Format = "@";
                if (text.Contains('\n'))
                {
                    cell.Style.Alignment.WrapText = true;
                }

                break;
        }
    }

    /// <summary>Excel table names: letters, digits and underscores, starting with a letter.</summary>
    private static string TableName(string? tableName, string sheetName)
    {
        foreach (string candidate in new[] { tableName ?? "", sheetName })
        {
            string name = new(candidate.Where(character => char.IsLetterOrDigit(character) || character == '_').ToArray());
            if (name.Length > 0 && char.IsLetter(name[0]))
            {
                return name;
            }
        }

        return ExportProfile.DefaultTableName;
    }

    private static void AddSummary(XLWorkbook workbook, PeriodTimesheet sheet, WorkspaceConfig config)
    {
        IXLWorksheet summary = workbook.AddWorksheet(SheetName("Summary", "Summary") == SheetName(config.Export.SheetName, ExportProfile.DefaultSheetName) ? "Summary (QuarterHour)" : "Summary");
        PeriodTotals totals = sheet.Totals;
        int row = 1;
        summary.Cell(row, 1).Value = $"{sheet.Period.Name} ({sheet.Period.RangeLabel})";
        summary.Cell(row, 1).Style.Font.Bold = true;
        summary.Cell(row, 1).Style.Font.FontSize = 14;
        row += 2;

        (string Label, double Hours)[] figures =
        [
            ("Total hours", totals.TotalMinutes / 60.0),
            ("Billable hours", totals.BillableMinutes / 60.0),
            ("Billable target", totals.TargetMinutes / 60.0),
            ("Short of target", totals.BillableShortfallMinutes / 60.0),
            ("Non-billable hours", totals.NonBillableMinutes / 60.0),
            ("Leave", totals.LeaveMinutes / 60.0),
            ("Unmapped (not exported)", totals.UnmappedMinutes / 60.0),
            ("Expected working hours", totals.ExpectedMinutes / 60.0),
        ];
        foreach ((string label, double hours) in figures)
        {
            summary.Cell(row, 1).Value = label;
            summary.Cell(row, 2).Value = hours;
            summary.Cell(row, 2).Style.NumberFormat.Format = "0.00";
            row++;
        }

        row += 1;
        string[] dayHeaders = ["Date", "Day", "Kind", "Location", "Expected", "Logged", "Billable", "Missing"];
        Header(summary, row, dayHeaders);
        row++;
        foreach (DaySummary day in sheet.Days)
        {
            summary.Cell(row, 1).Value = day.Date.ToDateTime(TimeOnly.MinValue);
            summary.Cell(row, 1).Style.DateFormat.Format = "yyyy-mm-dd";
            summary.Cell(row, 2).Value = day.Date.DayOfWeek.ToString();
            summary.Cell(row, 3).Value = day.Kind switch
            {
                DayKind.PublicHoliday => day.HolidayName ?? "Public holiday",
                DayKind.Weekend => "Weekend",
                _ => "Work day",
            };
            summary.Cell(row, 4).Value = day.Location.Name;
            Hours(summary.Cell(row, 5), day.ExpectedMinutes);
            Hours(summary.Cell(row, 6), day.TotalMinutes);
            Hours(summary.Cell(row, 7), day.BillableMinutes);
            Hours(summary.Cell(row, 8), day.IsFuture ? 0 : day.MissingMinutes);
            row++;
        }

        row += 1;
        Header(summary, row, ["Category", "Billable", "Hours"]);
        row++;
        foreach (CategoryTotal categoryTotal in totals.ByCategory)
        {
            Category? category = config.FindCategory(categoryTotal.CategoryId);
            summary.Cell(row, 1).Value = category?.DisplayName ?? "Unmapped";
            summary.Cell(row, 2).Value = category is { Billable: true, IsLeave: false } ? "Yes" : "No";
            Hours(summary.Cell(row, 3), categoryTotal.Minutes);
            row++;
        }

        Fit(summary);
    }

    private static void Header(IXLWorksheet worksheet, int row, string[] headers)
    {
        for (int column = 0; column < headers.Length; column++)
        {
            IXLCell cell = worksheet.Cell(row, column + 1);
            cell.Value = headers[column];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#E5E7EB");
        }
    }

    private static void Hours(IXLCell cell, int minutes)
    {
        cell.Value = minutes / 60.0;
        cell.Style.NumberFormat.Format = "0.00";
    }

    private static void Fit(IXLWorksheet worksheet)
    {
        worksheet.Columns().AdjustToContents();
        foreach (IXLColumn column in worksheet.ColumnsUsed())
        {
            if (column.Width > MaxColumnWidth)
            {
                column.Width = MaxColumnWidth;
            }
        }
    }

    private static string SheetName(string? name, string fallback)
    {
        string cleaned = new((name ?? "").Where(character => character is not ('[' or ']' or ':' or '*' or '?' or '/' or '\\')).ToArray());
        cleaned = cleaned.Trim().Trim('\'');
        if (cleaned.Length == 0)
        {
            cleaned = fallback;
        }

        return cleaned.Length <= 31 ? cleaned : cleaned[..31];
    }
}
