using System.Globalization;
using System.Text.RegularExpressions;
using QuarterHour.Core.Configuration;
using QuarterHour.Core.Model;
using QuarterHour.Core.Time;
using QuarterHour.Core.Timesheets;

namespace QuarterHour.Core.Export;

public enum ExportCellKind
{
    Text,
    Number,
    Date,
}

/// <summary>A cell value plus the Excel number format to show it with (for numbers and dates).</summary>
public sealed record ExportCell(object? Value, ExportCellKind Kind, string? Format = null)
{
    public string Display => Value switch
    {
        null => "",
        DateTime date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        double number => number.ToString(string.IsNullOrEmpty(Format) || Format == "General" ? "0.##" : Format, CultureInfo.InvariantCulture),
        _ => Value.ToString() ?? "",
    };
}

public sealed record ExportTable(IReadOnlyList<string> Headers, IReadOnlyList<IReadOnlyList<ExportCell>> Rows);

public sealed record ExportToken(string Token, string Meaning);

/// <summary>
/// Lays the timesheet lines out as the export profile describes. Unless every cell is written as text, a column
/// whose template is a single token ("{date}", "{hours}", "{minutes}") becomes a real date or number cell.
/// </summary>
public static partial class ExportRows
{
    public static IReadOnlyList<ExportToken> Tokens { get; } =
    [
        new("{date:yyyy-MM-dd}", "The day, in any .NET date format"),
        new("{day}", "Weekday name"),
        new("{project}", "The category's project (Timesheet \"Project\")"),
        new("{category}", "The category's name (Timesheet \"Category\")"),
        new("{categoryPath}", "Project › Category"),
        new("{code}", "The category's code, if set"),
        new("{hoursPart}", "Whole hours (5 for 5h 45m)"),
        new("{minutesPart}", "Minutes past the hour (45 for 5h 45m)"),
        new("{hours}", "Decimal hours (5.75)"),
        new("{minutes}", "Total minutes (345)"),
        new("{duration}", "h:mm (5:45)"),
        new("{billable}", "True/False, or {billable:Yes/No}"),
        new("{description}", "The line's description"),
        new("{ticket}", "Ticket number, if set"),
        new("{sentiment}", "Sentiment (default from settings)"),
        new("{location}", "Where you worked"),
        new("{source}", "Calendar, Agent, Manual or Mixed"),
        new("{start}", "Start time (HH:mm), for timed lines"),
        new("{end}", "End time (HH:mm), for timed lines"),
        new("{period}", "The period key (2026-11)"),
    ];

    public static IReadOnlyList<TimesheetLine> LinesToExport(PeriodTimesheet sheet, WorkspaceConfig config)
    {
        ExportProfile profile = config.Export;
        return sheet.Lines
            .Where(line => profile.IncludeUnmapped || line.CategoryId is not null)
            .Where(line => !profile.SkipZeroHourRows || line.Minutes > 0)
            .Where(line => profile.IncludeLeave || config.FindCategory(line.CategoryId) is not { IsLeave: true })
            .ToList();
    }

    public static ExportTable Build(PeriodTimesheet sheet, WorkspaceConfig config)
    {
        ExportProfile profile = config.Export;
        List<IReadOnlyList<ExportCell>> rows = [];
        foreach (TimesheetLine line in LinesToExport(sheet, config))
        {
            Category? category = config.FindCategory(line.CategoryId);
            rows.Add(profile.Columns.Select(column => Cell(column.Value, line, category, sheet.Period, profile.CellsAsText)).ToList());
        }

        return new ExportTable(profile.Columns.Select(column => column.Header).ToList(), rows);
    }

    public static ExportCell Cell(string template, TimesheetLine line, Category? category, TimesheetPeriod period, bool asText = false)
    {
        Match single = SingleToken().Match(template.Trim());
        if (!asText && single.Success)
        {
            string token = single.Groups["name"].Value.ToLowerInvariant();
            string? format = single.Groups["format"].Success ? single.Groups["format"].Value : null;
            switch (token)
            {
                case "date" when format is null:
                    return new ExportCell(line.Date.ToDateTime(TimeOnly.MinValue), ExportCellKind.Date, "yyyy-mm-dd");
                case "hours":
                    return new ExportCell(line.Hours, ExportCellKind.Number, format ?? "0.00");
                case "minutes":
                    return new ExportCell((double)line.Minutes, ExportCellKind.Number, format ?? "0");
                case "hourspart":
                    return new ExportCell((double)(line.Minutes / 60), ExportCellKind.Number, "0");
                case "minutespart":
                    return new ExportCell((double)(line.Minutes % 60), ExportCellKind.Number, "0");
            }
        }

        string text = Token().Replace(template, match =>
            Render(match.Groups["name"].Value, match.Groups["format"].Success ? match.Groups["format"].Value : null, line, category, period));
        return new ExportCell(text, ExportCellKind.Text);
    }

    private static string Render(string token, string? format, TimesheetLine line, Category? category, TimesheetPeriod period)
    {
        CultureInfo culture = CultureInfo.InvariantCulture;
        return token.ToLowerInvariant() switch
        {
            "date" => line.Date.ToString(format ?? "yyyy-MM-dd", culture),
            "day" => line.Date.ToString(format ?? "dddd", culture),
            "hours" => line.Hours.ToString(format ?? "0.00", culture),
            "minutes" => line.Minutes.ToString(format ?? "0", culture),
            "hourspart" => (line.Minutes / 60).ToString(culture),
            "minutespart" => (line.Minutes % 60).ToString(culture),
            "duration" => $"{line.Minutes / 60}:{line.Minutes % 60:D2}",
            "project" or "group" => category?.Group ?? "Unmapped",
            "category" or "name" => category?.Name ?? "",
            "categorypath" => category?.DisplayName ?? "Unmapped",
            "code" => category?.Code ?? "",
            "location" => line.Location,
            "description" => line.Description,
            "ticket" => line.Ticket ?? "",
            "sentiment" => line.Sentiment,
            "billable" => Billable(category, format),
            "source" => line.Source switch
            {
                ActivitySource.Calendar => "Calendar",
                ActivitySource.Agent => "Agent",
                ActivitySource.Manual => "Manual",
                _ => "Mixed",
            },
            "start" => line.Start?.ToString(format ?? "HH:mm", culture) ?? "",
            "end" => line.End?.ToString(format ?? "HH:mm", culture) ?? "",
            "period" => period.Key,
            _ => "{" + token + "}",
        };
    }

    private static string Billable(Category? category, string? format)
    {
        string[] words = (format ?? "True/False").Split('/', 2);
        bool billable = category is { Billable: true, IsLeave: false };
        return billable ? words[0] : words.Length > 1 ? words[1] : "";
    }

    public static string FileName(ExportProfile profile, TimesheetPeriod period)
    {
        string name = (string.IsNullOrWhiteSpace(profile.FileNameTemplate) ? "TimesheetExport {period}.xlsx" : profile.FileNameTemplate)
            .Replace("{period}", period.Key, StringComparison.OrdinalIgnoreCase)
            .Replace("{month}", period.Name, StringComparison.OrdinalIgnoreCase);
        foreach (char invalid in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(invalid, '-');
        }

        return name.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase) ? name : name + ".xlsx";
    }

    [GeneratedRegex(@"^\{(?<name>\w+)(:(?<format>[^}]+))?\}$")]
    private static partial Regex SingleToken();

    [GeneratedRegex(@"\{(?<name>\w+)(:(?<format>[^}]+))?\}")]
    private static partial Regex Token();
}
