namespace QuarterHour.Core.Configuration;

/// <summary>
/// The user's corrections and additions: per-activity overrides, per-day notes and locations, and manual entries.
/// </summary>
public sealed class Journal
{
    /// <summary>Keyed by activity id (or activity piece id for activities that span several days).</summary>
    public Dictionary<string, ActivityOverride> Overrides { get; set; } = [];

    /// <summary>Keyed by date as yyyy-MM-dd.</summary>
    public Dictionary<string, DayOverride> Days { get; set; } = [];

    public List<ManualEntry> ManualEntries { get; set; } = [];

    /// <summary>Changes made to whole timesheet lines (such as the day's description of agent work), keyed by line id.</summary>
    public Dictionary<string, LineOverride> Lines { get; set; } = [];

    /// <summary>Where commits and pull requests go instead of where attribution put them, keyed like "commit:&lt;sha&gt;" or "pr:&lt;id&gt;".</summary>
    public Dictionary<string, CandidateOverride> Candidates { get; set; } = [];

    public static string DayKey(DateOnly date) => date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    public DayOverride? FindDay(DateOnly date) => Days.GetValueOrDefault(DayKey(date));

    public Journal Normalize()
    {
        Overrides ??= [];
        Days ??= [];
        ManualEntries ??= [];
        Lines ??= [];
        Candidates ??= [];
        foreach (ManualEntry entry in ManualEntries)
        {
            entry.Candidates ??= [];
        }

        return this;
    }
}

public sealed class LineOverride
{
    public string? Description { get; set; }
    public string? Sentiment { get; set; }
    public string? Ticket { get; set; }

    /// <summary>Replaces the line's logged (rounded) minutes.</summary>
    public int? Minutes { get; set; }

    public bool IsEmpty => string.IsNullOrWhiteSpace(Description) && string.IsNullOrWhiteSpace(Sentiment) && string.IsNullOrWhiteSpace(Ticket) && Minutes is null;
}

/// <summary>A commit or pull request moved to another block of agent work, or marked not relevant.</summary>
public sealed class CandidateOverride
{
    /// <summary>The block of agent work (activity id) it describes instead.</summary>
    public string? BlockId { get; set; }

    /// <summary>It describes nothing.</summary>
    public bool NotRelevant { get; set; }

    public bool IsEmpty => BlockId is null && !NotRelevant;
}

public sealed class ActivityOverride
{
    /// <summary>true excludes the activity, false forces it in even when a rule filtered it out.</summary>
    public bool? Excluded { get; set; }

    public string? CategoryId { get; set; }
    public string? Location { get; set; }
    public string? Description { get; set; }

    /// <summary>Replaces the measured minutes (before rounding).</summary>
    public int? Minutes { get; set; }

    public bool IsEmpty => Excluded is null && CategoryId is null && Location is null && Description is null && Minutes is null;
}

public sealed class DayOverride
{
    public string? Location { get; set; }
    public string? Note { get; set; }

    public bool IsEmpty => string.IsNullOrWhiteSpace(Location) && string.IsNullOrWhiteSpace(Note);
}

/// <summary>Time the sources can't see, such as work without an agent, travel or a workshop.</summary>
public sealed class ManualEntry
{
    public string Id { get; set; } = WorkspaceConfig.NewId();
    public DateOnly Date { get; set; }

    /// <summary>When set, the entry occupies that part of the day and wins any overlap.</summary>
    public TimeOnly? Start { get; set; }

    public int Minutes { get; set; }
    public string? CategoryId { get; set; }
    public string? Location { get; set; }
    public string Description { get; set; } = "";

    /// <summary>The unattributed commits and pull requests this entry was made from, which leave the Unattributed column while it exists.</summary>
    public List<string> Candidates { get; set; } = [];
}
