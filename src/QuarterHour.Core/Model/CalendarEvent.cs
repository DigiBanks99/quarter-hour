namespace QuarterHour.Core.Model;

/// <summary>
/// A calendar event occurrence as a calendar provider (Graph, Outlook, .ics) reports it.
/// Timed events carry real instants; all-day events carry midnight-to-midnight dates (the offset is ignored).
/// </summary>
public sealed record CalendarEvent
{
    public required string Id { get; init; }
    public required string Subject { get; init; }
    public required DateTimeOffset Start { get; init; }
    public required DateTimeOffset End { get; init; }
    public bool IsAllDay { get; init; }

    /// <summary>free, tentative, busy, oof, workingElsewhere or unknown.</summary>
    public string ShowAs { get; init; } = "busy";

    /// <summary>none, organizer, tentativelyAccepted, accepted, declined or notResponded.</summary>
    public string Response { get; init; } = "none";

    public string? Organizer { get; init; }
    public string? OrganizerEmail { get; init; }
    public bool IsOrganizer { get; init; }
    public int AttendeeCount { get; init; }
    public string? Location { get; init; }
    public List<string> Categories { get; init; } = [];
    public bool IsOnlineMeeting { get; init; }
    public bool IsRecurring { get; init; }
    public string? SeriesId { get; init; }
    public string? Sensitivity { get; init; }
    public bool IsCancelled { get; init; }
}
