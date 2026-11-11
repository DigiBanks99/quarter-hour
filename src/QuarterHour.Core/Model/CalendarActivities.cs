using System.Text.RegularExpressions;

namespace QuarterHour.Core.Model;

/// <summary>Turns calendar events into activities, and groups similar meetings for mapping.</summary>
public static partial class CalendarActivities
{
    public static Activity ToActivity(CalendarEvent calendarEvent, TimeZoneInfo timeZone)
    {
        DateTime start;
        DateTime end;
        if (calendarEvent.IsAllDay)
        {
            // All-day events are floating dates: take the date as written, not converted.
            start = calendarEvent.Start.DateTime.Date;
            end = calendarEvent.End.DateTime.Date;
            if (end <= start)
            {
                end = start.AddDays(1);
            }
        }
        else
        {
            start = TimeZoneInfo.ConvertTime(calendarEvent.Start, timeZone).DateTime;
            end = TimeZoneInfo.ConvertTime(calendarEvent.End, timeZone).DateTime;
            if (end < start)
            {
                end = start;
            }
        }

        string subject = string.IsNullOrWhiteSpace(calendarEvent.Subject) ? "(no subject)" : calendarEvent.Subject.Trim();
        return new Activity
        {
            Id = $"cal:{calendarEvent.Id}",
            Source = ActivitySource.Calendar,
            Start = start,
            End = end,
            IsAllDay = calendarEvent.IsAllDay,
            IsTimed = !calendarEvent.IsAllDay,
            Title = subject,
            GroupKey = GroupKeyFor(subject),
            GroupLabel = CleanSubject(subject),
            Organizer = calendarEvent.Organizer,
            OrganizerEmail = calendarEvent.OrganizerEmail,
            EventLocation = calendarEvent.Location,
            Categories = calendarEvent.Categories,
            ShowAs = calendarEvent.ShowAs,
            Response = calendarEvent.Response,
            IsOrganizer = calendarEvent.IsOrganizer,
            AttendeeCount = calendarEvent.AttendeeCount,
            IsOnlineMeeting = calendarEvent.IsOnlineMeeting,
            IsRecurring = calendarEvent.IsRecurring,
            Sensitivity = calendarEvent.Sensitivity,
            IsCancelled = calendarEvent.IsCancelled,
        };
    }

    /// <summary>
    /// "RE: Sprint 42 Planning " and "Sprint 43 planning" share the key "cal:sprint # planning", so one mapping
    /// covers every occurrence and numbered variant.
    /// </summary>
    public static string GroupKeyFor(string subject)
    {
        string cleaned = CleanSubject(subject).ToLowerInvariant();
        cleaned = Digits().Replace(cleaned, "#");
        return "cal:" + cleaned;
    }

    /// <summary>Strips reply/forward/status prefixes and squeezes whitespace.</summary>
    public static string CleanSubject(string subject)
    {
        string cleaned = subject.Trim();
        string previous;
        do
        {
            previous = cleaned;
            cleaned = Prefix().Replace(cleaned, "").Trim();
        }
        while (cleaned != previous);

        cleaned = Whitespace().Replace(cleaned, " ");
        return cleaned.Length == 0 ? subject.Trim() : cleaned;
    }

    [GeneratedRegex(@"^(re|fw|fwd|updated|update|canceled|cancelled|accepted|declined|tentative|new time proposed|invitation)\s*:\s*", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Prefix();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    [GeneratedRegex(@"\d+")]
    private static partial Regex Digits();
}
