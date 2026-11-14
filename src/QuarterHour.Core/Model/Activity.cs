namespace QuarterHour.Core.Model;

public enum ActivitySource
{
    Calendar,
    Agent,
    Manual,
}

/// <summary>
/// Something that took time: a calendar event, a block of agent coding work or a manual entry.
/// Times are local wall-clock times in the workspace's time zone.
/// </summary>
public sealed record Activity
{
    public required string Id { get; init; }
    public required ActivitySource Source { get; init; }
    public required DateTime Start { get; init; }
    public required DateTime End { get; init; }
    public required string Title { get; init; }

    /// <summary>What the mapping board groups by: a normalised subject for meetings, the project for agent work.</summary>
    public required string GroupKey { get; init; }

    public required string GroupLabel { get; init; }

    public bool IsAllDay { get; init; }

    /// <summary>Time-less activities (all-day events, manual entries without a start) don't occupy the timeline.</summary>
    public bool IsTimed { get; init; } = true;

    /// <summary>Minutes a time-less activity counts for.</summary>
    public int? FixedMinutes { get; init; }

    // Calendar
    public string? Organizer { get; init; }
    public string? OrganizerEmail { get; init; }
    public string? EventLocation { get; init; }
    public IReadOnlyList<string> Categories { get; init; } = [];
    public string? ShowAs { get; init; }
    public string? Response { get; init; }
    public bool IsOrganizer { get; init; }
    public int AttendeeCount { get; init; }
    public bool IsOnlineMeeting { get; init; }
    public bool IsRecurring { get; init; }
    public string? Sensitivity { get; init; }
    public bool IsCancelled { get; init; }

    // Agent
    public string? Agent { get; init; }
    public string? SessionId { get; init; }
    public string? SessionTitle { get; init; }

    /// <summary>The project's own name (repository or folder), before any friendly name.</summary>
    public string? ProjectName { get; init; }

    public string? ProjectPath { get; init; }
    public string? Repository { get; init; }
    public string? Branch { get; init; }
    public int MessageCount { get; init; }

    /// <summary>The commits attributed to this block of agent work, oldest first.</summary>
    public IReadOnlyList<Commit> Commits { get; init; } = [];

    // Manual
    public string? ManualCategoryId { get; init; }
    public string? ManualLocation { get; init; }

    public TimeSpan Duration => End > Start ? End - Start : TimeSpan.Zero;
}
