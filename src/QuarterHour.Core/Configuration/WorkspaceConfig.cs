using System.Text.Json.Serialization;

namespace QuarterHour.Core.Configuration;

/// <summary>
/// Everything the user configures: categories, rules, locations, rounding, targets, sources and the export layout.
/// It is persisted as one JSON file so it can be copied between machines.
/// </summary>
public sealed class WorkspaceConfig
{
    /// <summary>2: the export follows the Timesheet system's upload template rather than its export.</summary>
    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    public const int CurrentSchemaVersion = 2;
    public GeneralSettings General { get; set; } = new();
    public RoundingSettings Rounding { get; set; } = new();
    public TargetSettings Targets { get; set; } = new();
    public List<Category> Categories { get; set; } = [];
    public LocationSettings Locations { get; set; } = new();
    public List<Rule> Rules { get; set; } = [];
    public CalendarSettings Calendar { get; set; } = new();
    public AgentSettings Agents { get; set; } = new();
    public ExportProfile Export { get; set; } = new();

    public Category? FindCategory(string? id) =>
        id is null ? null : Categories.FirstOrDefault(category => category.Id == id);

    /// <summary>Fills in anything an older or hand-edited file left out, so the rest of the code can rely on it.</summary>
    public WorkspaceConfig Normalize()
    {
        General ??= new GeneralSettings();
        Rounding ??= new RoundingSettings();
        Targets ??= new TargetSettings();
        Categories ??= [];
        Locations ??= new LocationSettings();
        Locations.Names ??= [];
        Locations.Schedule ??= [];
        if (Locations.Names.Count == 0)
        {
            Locations.Names.AddRange(LocationSettings.DefaultNames);
        }

        if (string.IsNullOrWhiteSpace(Locations.Default) || !Locations.Names.Contains(Locations.Default))
        {
            Locations.Default = Locations.Names[0];
        }

        Rules ??= [];
        foreach (Rule rule in Rules)
        {
            rule.Conditions ??= [];
            if (rule.Stage == RuleStage.Filter)
            {
                rule.Action = RuleAction.Ignore;
            }
        }

        Calendar ??= new CalendarSettings();
        Calendar.Graph ??= new GraphSettings();
        Agents ??= new AgentSettings();
        Agents.Scanners ??= [];
        foreach (AgentKind kind in Enum.GetValues<AgentKind>())
        {
            if (Agents.Scanners.All(scanner => scanner.Kind != kind))
            {
                Agents.Scanners.Add(new AgentScannerSettings { Kind = kind });
            }
        }

        foreach (AgentScannerSettings scanner in Agents.Scanners)
        {
            scanner.Paths ??= [];
        }

        Agents.AgentsView ??= new AgentsViewSettings();
        Export ??= new ExportProfile();
        Export.Columns ??= [];
        if (Export.Columns.Count == 0)
        {
            Export.Columns.AddRange(ExportProfile.DefaultColumns());
        }

        Export.Sentiments ??= [];
        if (string.IsNullOrWhiteSpace(Export.TableName))
        {
            Export.TableName = ExportProfile.DefaultTableName;
        }

        if (SchemaVersion < 2)
        {
            Export.UpgradeToUploadTemplate();
            SchemaVersion = CurrentSchemaVersion;
        }

        if (string.IsNullOrWhiteSpace(Export.DefaultSentiment))
        {
            Export.DefaultSentiment = "Neutral";
        }

        if (!Export.Sentiments.Contains(Export.DefaultSentiment))
        {
            Export.Sentiments.Add(Export.DefaultSentiment);
        }

        if (Targets.TopUpCategoryId is { } topUp && Categories.All(category => category.Id != topUp))
        {
            Targets.TopUpCategoryId = null;
        }

        Targets.WorkDays ??= [];
        Targets.CustomHolidays ??= [];
        Rounding.IncrementMinutes = Math.Clamp(Rounding.IncrementMinutes, 1, 240);
        Rounding.RoundUpFromMinutes = Math.Clamp(Rounding.RoundUpFromMinutes, 0, Rounding.IncrementMinutes);
        General.PeriodStartDay = Math.Clamp(General.PeriodStartDay, 1, 28);
        return this;
    }

    internal static string NewId() => Guid.NewGuid().ToString("N")[..12];
}

public sealed class GeneralSettings
{
    /// <summary>Windows or IANA time zone id used to turn calendar and agent timestamps into local working time.</summary>
    public string TimeZoneId { get; set; } = TimeZoneInfo.Local.Id;

    /// <summary>Day of the month a timesheet period starts on. 1 means calendar months.</summary>
    public int PeriodStartDay { get; set; } = 1;

    public OverlapPolicy OverlapPolicy { get; set; } = OverlapPolicy.HigherPriorityWins;

    /// <summary>Dark (true) or light (false) theme the user chose. Null follows the browser's colour scheme.</summary>
    public bool? DarkMode { get; set; }

    public TimeZoneInfo ResolveTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(TimeZoneId);
        }
        catch (Exception exception) when (exception is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return TimeZoneInfo.Local;
        }
    }
}

/// <summary>What happens when two activities claim the same minutes.</summary>
public enum OverlapPolicy
{
    /// <summary>Manual entries beat meetings, meetings beat agent sessions; within a tier the earlier one wins.</summary>
    HigherPriorityWins,

    /// <summary>Every activity keeps its full time, even when that means the same minutes are logged twice.</summary>
    CountOverlaps,
}

public sealed class RoundingSettings
{
    public int IncrementMinutes { get; set; } = 15;

    /// <summary>
    /// The leftover (beyond the last whole increment) at which time is rounded up instead of down.
    /// 7 turns 50 minutes into 45 and 3h 22m into 3h 30m.
    /// </summary>
    public int RoundUpFromMinutes { get; set; } = 7;

    public RoundingScope Scope { get; set; } = RoundingScope.EachLine;
}

public enum RoundingScope
{
    /// <summary>Each meeting (and each day's agent work per category) is rounded on its own.</summary>
    EachLine,

    /// <summary>Everything on the same day, category and location is added up first and rounded once.</summary>
    DailyTotalPerCategory,
}

public sealed class TargetSettings
{
    public double BillableTargetHours { get; set; } = 160;
    public bool ReduceTargetByLeave { get; set; } = true;
    public double StandardDayHours { get; set; } = 8;

    /// <summary>Hours an included all-day event (such as leave) counts for on a working day.</summary>
    public double AllDayEventHours { get; set; } = 8;

    public List<DayOfWeek> WorkDays { get; set; } =
        [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday];

    public bool SouthAfricanPublicHolidays { get; set; } = true;
    public List<Holiday> CustomHolidays { get; set; } = [];

    /// <summary>
    /// When set, every past working day with something recorded is topped up to the standard hours with this
    /// category (usually the client project's "Analysis"), merged into that day's agent work for it.
    /// </summary>
    public string? TopUpCategoryId { get; set; }

    /// <summary>On a day with a full day of leave, recurring meetings still in the calendar are left out.</summary>
    public bool IgnoreMeetingsOnLeaveDays { get; set; } = true;
}

public sealed class Holiday
{
    public DateOnly Date { get; set; }
    public string Name { get; set; } = "";
}

/// <summary>
/// A timesheet category: a project (such as "Social Events" or the client project) and a category under it
/// (such as "Team Building Attendance"), exactly as the Timesheet system names them.
/// </summary>
public sealed class Category
{
    public string Id { get; set; } = WorkspaceConfig.NewId();

    /// <summary>The Timesheet system's "Project" column.</summary>
    public string Group { get; set; } = "";

    /// <summary>The Timesheet system's "Category" column.</summary>
    public string Name { get; set; } = "";

    /// <summary>Optional code or id the timesheet system knows the category by.</summary>
    public string? Code { get; set; }

    public bool Billable { get; set; }

    /// <summary>Leave counts towards the day being complete and (optionally) reduces the billable target.</summary>
    public bool IsLeave { get; set; }

    public string? Color { get; set; }
    public bool Archived { get; set; }

    [JsonIgnore]
    public string DisplayName => string.IsNullOrWhiteSpace(Group) ? Name : $"{Group} › {Name}";
}

public sealed class LocationSettings
{
    public static readonly IReadOnlyList<string> DefaultNames = ["Client", "Entelect", "Home", "Other"];

    public List<string> Names { get; set; } = [.. DefaultNames];
    public string Default { get; set; } = "Client";

    /// <summary>Recurring "where do I usually work" rules. The first one that applies to a day wins.</summary>
    public List<LocationScheduleRule> Schedule { get; set; } = [];
}

public sealed class LocationScheduleRule
{
    public string Id { get; set; } = WorkspaceConfig.NewId();
    public bool Enabled { get; set; } = true;
    public string Location { get; set; } = "";
    public List<DayOfWeek> Days { get; set; } = [];
    public ScheduleRecurrence Recurrence { get; set; } = ScheduleRecurrence.Weekly;

    /// <summary>For <see cref="ScheduleRecurrence.EveryNWeeks"/>: how many weeks apart.</summary>
    public int IntervalWeeks { get; set; } = 2;

    /// <summary>For <see cref="ScheduleRecurrence.EveryNWeeks"/>: any date in a week the rule applies to.</summary>
    public DateOnly? AnchorDate { get; set; }

    /// <summary>For <see cref="ScheduleRecurrence.MonthlyNthWeekday"/>: 1 to 4, or -1 for the last one.</summary>
    public int WeekOfMonth { get; set; } = 1;

    public DateOnly? From { get; set; }
    public DateOnly? To { get; set; }
    public string? Note { get; set; }
}

public enum ScheduleRecurrence
{
    Weekly,
    EveryNWeeks,
    MonthlyNthWeekday,
}

public sealed class CalendarSettings
{
    public CalendarProviderKind Provider { get; set; } = CalendarProviderKind.MicrosoftGraph;
    public GraphSettings Graph { get; set; } = new();
}

public enum CalendarProviderKind
{
    /// <summary>Only imported .ics files.</summary>
    None,
    MicrosoftGraph,

    /// <summary>Classic Outlook on this machine, read through COM. No app registration needed.</summary>
    OutlookDesktop,
}

public sealed class GraphSettings
{
    /// <summary>"organizations", "common" or the tenant's id or domain.</summary>
    public string TenantId { get; set; } = "organizations";

    /// <summary>Application (client) id of an Entra ID app registration that allows public client sign-in.</summary>
    public string ClientId { get; set; } = "";

    public GraphSignInMode SignInMode { get; set; } = GraphSignInMode.InteractiveBrowser;
}

public enum GraphSignInMode
{
    InteractiveBrowser,
    DeviceCode,
}

public sealed class AgentSettings
{
    /// <summary>A pause longer than this splits a session into separate blocks of work.</summary>
    public int IdleGapMinutes { get; set; } = 15;

    /// <summary>Time added after the last message of a block, for reading and reviewing the result.</summary>
    public int PaddingMinutes { get; set; } = 5;

    public List<AgentScannerSettings> Scanners { get; set; } = [];
    public AgentsViewSettings AgentsView { get; set; } = new();
}

public enum AgentKind
{
    ClaudeCode,
    CopilotCli,
    Codex,
    OpenCode,
    Pi,
}

public sealed class AgentScannerSettings
{
    public AgentKind Kind { get; set; }
    public bool Enabled { get; set; } = true;

    /// <summary>Folders (or files) to read. Empty means the agent's usual locations.</summary>
    public List<string> Paths { get; set; } = [];
}

public sealed class AgentsViewSettings
{
    public bool Enabled { get; set; }
    public string BaseUrl { get; set; } = "http://127.0.0.1:8080";

    /// <summary>Skip AgentsView sessions for agents a built-in scanner already reads, so nothing is counted twice.</summary>
    public bool SkipAgentsCoveredByScanners { get; set; } = true;
}

/// <summary>
/// How the .xlsx is laid out. The defaults match the Timesheet system's upload template (TimesheetUpload.xlsx): a
/// "TimesheetUpload" sheet holding a "TimesheetEntry" table with Date, Project, Category, Hours, Minutes, Billable,
/// Description, TicketNumber, Sentiment and WorkedFrom, spelled exactly as the importer expects. Dates are real dates
/// and Hours/Minutes whole numbers, as the template's validation asks for.
/// </summary>
public sealed class ExportProfile
{
    public const string DefaultSheetName = "TimesheetUpload";
    public const string DefaultTableName = "TimesheetEntry";
    public const string DefaultFileNameTemplate = "TimesheetUpload {period}.xlsx";

    public string SheetName { get; set; } = DefaultSheetName;

    /// <summary>The Excel table's name, when the rows are written as a table.</summary>
    public string TableName { get; set; } = DefaultTableName;

    public string FileNameTemplate { get; set; } = DefaultFileNameTemplate;
    public List<ExportColumn> Columns { get; set; } = [];

    /// <summary>Write every cell as text instead of real dates and numbers.</summary>
    public bool CellsAsText { get; set; }

    /// <summary>Wrap the rows in an Excel table, like the upload template does.</summary>
    public bool WriteAsTable { get; set; } = true;

    /// <summary>Adds a second sheet with totals. Off by default, so the upload file has only the timesheet.</summary>
    public bool IncludeSummarySheet { get; set; }

    public bool IncludeUnmapped { get; set; }

    /// <summary>Leave usually goes through the leave system, so it counts towards the day but isn't exported.</summary>
    public bool IncludeLeave { get; set; }

    public bool SkipZeroHourRows { get; set; } = true;
    public string DefaultSentiment { get; set; } = "Neutral";

    /// <summary>The upload template's Sentiment values.</summary>
    public List<string> Sentiments { get; set; } = ["Neutral", "Happy", "Sad"];

    public static IEnumerable<ExportColumn> DefaultColumns() =>
    [
        new() { Header = "Date", Value = "{date}" },
        new() { Header = "Project", Value = "{project}" },
        new() { Header = "Category", Value = "{category}" },
        new() { Header = "Hours", Value = "{hoursPart}" },
        new() { Header = "Minutes", Value = "{minutesPart}" },
        new() { Header = "Billable", Value = "{billable:Yes/No}" },
        new() { Header = "Description", Value = "{description}" },
        new() { Header = "TicketNumber", Value = "{ticket}" },
        new() { Header = "Sentiment", Value = "{sentiment}" },
        new() { Header = "WorkedFrom", Value = "{location}" },
    ];

    /// <summary>The headers of the layout before version 2, which copied the Timesheet system's export instead of its upload template.</summary>
    private static readonly string[] ExportLayoutHeaders =
        ["Date", "Project", "Category", "Hours", "Minutes", "Billable", "Description", "Ticket Number", "Sentiment", "Worked From"];

    /// <summary>
    /// Moves a profile still on the old export-shaped defaults to the upload template. Anything the user changed
    /// themselves (a custom column list, sheet or file name) is kept.
    /// </summary>
    public void UpgradeToUploadTemplate()
    {
        if (Columns.Select(column => column.Header).SequenceEqual(ExportLayoutHeaders))
        {
            Columns = [.. DefaultColumns()];
            CellsAsText = false;
        }

        if (SheetName == "Timesheet")
        {
            SheetName = DefaultSheetName;
        }

        if (FileNameTemplate == "TimesheetExport {period}.xlsx")
        {
            FileNameTemplate = DefaultFileNameTemplate;
        }

        Sentiments = Sentiments.Select(sentiment => sentiment == "Unhappy" ? "Sad" : sentiment).Distinct().ToList();
        if (DefaultSentiment == "Unhappy")
        {
            DefaultSentiment = "Sad";
        }
    }
}

public sealed class ExportColumn
{
    public string Header { get; set; } = "";

    /// <summary>A template such as "{date:yyyy-MM-dd}", "{hours}" or "{group} - {name}".</summary>
    public string Value { get; set; } = "";
}
