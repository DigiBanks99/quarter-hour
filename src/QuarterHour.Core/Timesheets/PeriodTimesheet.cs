using QuarterHour.Core.Configuration;
using QuarterHour.Core.Locations;
using QuarterHour.Core.Model;
using QuarterHour.Core.Rules;
using QuarterHour.Core.Time;

namespace QuarterHour.Core.Timesheets;

public enum ActivityStatus
{
    /// <summary>Counted under a category.</summary>
    Counted,

    /// <summary>Counted, but no rule says which category yet.</summary>
    Unmapped,

    /// <summary>Mapped to "not relevant".</summary>
    NotRelevant,

    /// <summary>Removed by a relevance filter.</summary>
    Filtered,

    /// <summary>Excluded by hand.</summary>
    Excluded,
}

/// <summary>One day's part of an activity, with everything decided about it.</summary>
public sealed record ActivityResult
{
    public required Activity Activity { get; init; }

    /// <summary>The activity id, or "id@yyyy-MM-dd" for one day of an activity that spans several days.</summary>
    public required string PieceId { get; init; }

    public required DateOnly Date { get; init; }
    public required DateTime Start { get; init; }
    public required DateTime End { get; init; }
    public required ActivityStatus Status { get; init; }
    public required RuleMatch Match { get; init; }

    /// <summary>The rule that decided the category, pin and description (also when filters were overridden).</summary>
    public Rule? MappingRule { get; init; }

    public string? CategoryId { get; init; }
    public required ResolvedLocation Location { get; init; }
    public required string Description { get; init; }

    /// <summary>The measured time (or the fixed time of an all-day event or manual entry).</summary>
    public TimeSpan Raw { get; init; }

    /// <summary>The time that counts after overlaps and overrides, before rounding.</summary>
    public TimeSpan Counted { get; init; }

    public IReadOnlyList<string> OverlappedBy { get; init; } = [];
    public ActivityOverride? Override { get; init; }
    public string? LineId { get; init; }

    /// <summary>Why the builder itself changed the activity's status, such as "On leave that day".</summary>
    public string? Note { get; init; }

    public bool IsCounting => Status is ActivityStatus.Counted or ActivityStatus.Unmapped;
    public bool WasClipped => IsCounting && Counted < Raw && Override?.Minutes is null;
}

/// <summary>A row of the timesheet: rounded time against a category and location on a day.</summary>
public sealed record TimesheetLine
{
    public required string Id { get; init; }
    public required DateOnly Date { get; init; }
    public string? CategoryId { get; init; }
    public required string Location { get; init; }
    public required string Description { get; init; }

    /// <summary>Where the time came from; null when a daily total mixes sources.</summary>
    public ActivitySource? Source { get; init; }

    public TimeSpan Raw { get; init; }
    public int Minutes { get; init; }
    public IReadOnlyList<string> PieceIds { get; init; } = [];
    public DateTime? Start { get; init; }
    public DateTime? End { get; init; }
    public string Sentiment { get; init; } = "Neutral";
    public string? Ticket { get; init; }

    /// <summary>Minutes added to bring the day up to the standard hours (see <see cref="Configuration.TargetSettings.TopUpCategoryId"/>).</summary>
    public int TopUpMinutes { get; init; }

    /// <summary>The user changed the line itself (description, sentiment, ticket or minutes).</summary>
    public LineOverride? Override { get; init; }

    public double Hours => Minutes / 60.0;
}

public sealed record DaySummary
{
    public required DateOnly Date { get; init; }
    public required DayKind Kind { get; init; }
    public string? HolidayName { get; init; }
    public required ResolvedLocation Location { get; init; }
    public int ExpectedMinutes { get; init; }
    public int TotalMinutes { get; init; }
    public int BillableMinutes { get; init; }
    public int LeaveMinutes { get; init; }
    public int UnmappedMinutes { get; init; }
    public int UnmappedCount { get; init; }
    public int OverlapCount { get; init; }
    public int ActivityCount { get; init; }
    public bool IsFuture { get; init; }

    /// <summary>Today is still in progress, so it isn't short of hours yet.</summary>
    public bool IsToday { get; init; }

    public string? Note { get; init; }

    public int MissingMinutes => Math.Max(0, ExpectedMinutes - TotalMinutes);
    public bool HasRecords => TotalMinutes > 0 || ActivityCount > 0;
    public bool NeedsAttention => !IsFuture && ((!IsToday && MissingMinutes > 0) || UnmappedCount > 0);
}

public sealed record CategoryTotal(string? CategoryId, int Minutes);

public sealed record LocationTotal(string Location, int Minutes, int Days);

public sealed record PeriodTotals
{
    public int TotalMinutes { get; init; }
    public int BillableMinutes { get; init; }
    public int NonBillableMinutes { get; init; }
    public int LeaveMinutes { get; init; }
    public int UnmappedMinutes { get; init; }

    /// <summary>The billable target after leave is taken off (when configured).</summary>
    public int TargetMinutes { get; init; }

    public int ExpectedMinutes { get; init; }
    public int WorkDays { get; init; }
    public int DaysMissingTime { get; init; }
    public int MissingMinutes { get; init; }
    public int UnmappedActivities { get; init; }
    public int OverlappingActivities { get; init; }
    public int WeekendOrHolidayDaysWorked { get; init; }
    public IReadOnlyList<CategoryTotal> ByCategory { get; init; } = [];
    public IReadOnlyList<LocationTotal> ByLocation { get; init; } = [];

    public int BillableShortfallMinutes => Math.Max(0, TargetMinutes - BillableMinutes);
    public bool BelowTarget => BillableShortfallMinutes > 0;
}

public sealed record PeriodTimesheet
{
    public required TimesheetPeriod Period { get; init; }
    public required IReadOnlyList<ActivityResult> Activities { get; init; }
    public required IReadOnlyList<TimesheetLine> Lines { get; init; }
    public required IReadOnlyList<DaySummary> Days { get; init; }
    public required PeriodTotals Totals { get; init; }

    /// <summary>The period's commits and pull requests and where each went, when agent work is described by them.</summary>
    public IReadOnlyList<Placement> Candidates { get; init; } = [];

    public IEnumerable<Placement> Unattributed => Candidates.Where(candidate => candidate.IsUnattributed);

    /// <summary>The commits and pull requests describing a block of agent work.</summary>
    public IEnumerable<Placement> CandidatesOf(string blockId) => Candidates.Where(candidate => candidate.BlockId == blockId);

    public DaySummary? Day(DateOnly date) => Days.FirstOrDefault(day => day.Date == date);
}
