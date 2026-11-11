using Ical.Net.CalendarComponents;
using Ical.Net.DataTypes;
using CoreCalendarEvent = QuarterHour.Core.Model.CalendarEvent;
using IcalCalendar = Ical.Net.Calendar;

namespace QuarterHour.Infrastructure.Calendar;

/// <summary>
/// Reads an exported calendar (.ics), expanding recurring meetings. Works with Outlook's "Save calendar" export,
/// which also carries Outlook's busy status. It's the fallback when Graph access isn't available.
/// </summary>
public static class IcsCalendarReader
{
    private const int MaxOccurrences = 20_000;

    public static IReadOnlyList<CoreCalendarEvent> Read(string ics, DateTimeOffset from, DateTimeOffset to)
    {
        IcalCalendar calendar = IcalCalendar.Load(ics) ?? throw new FormatException("That file doesn't contain a calendar.");
        CalDateTime start = new(from.UtcDateTime.AddDays(-1), "UTC", hasTime: true);
        DateTime endUtc = to.UtcDateTime;
        List<CoreCalendarEvent> events = [];
        foreach (Occurrence occurrence in calendar.GetOccurrences(start).Take(MaxOccurrences))
        {
            if (occurrence.Period.StartTime.AsUtc >= endUtc)
            {
                break;
            }

            if (occurrence.Source is not CalendarEvent source)
            {
                continue;
            }

            CoreCalendarEvent calendarEvent = ToEvent(source, occurrence);
            if (calendarEvent.End > from && calendarEvent.Start < to)
            {
                events.Add(calendarEvent);
            }
        }

        return events;
    }

    private static CoreCalendarEvent ToEvent(CalendarEvent source, Occurrence occurrence)
    {
        CalDateTime startTime = occurrence.Period.StartTime;
        bool allDay = source.IsAllDay || !startTime.HasTime;
        DateTimeOffset start;
        DateTimeOffset end;
        if (allDay)
        {
            start = new DateTimeOffset(startTime.Date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            CalDateTime? endTime = occurrence.Period.EffectiveEndTime;
            end = endTime is not null
                ? new DateTimeOffset(endTime.Date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero)
                : start.AddDays(1);
        }
        else
        {
            start = new DateTimeOffset(DateTime.SpecifyKind(startTime.AsUtc, DateTimeKind.Utc));
            CalDateTime? endTime = occurrence.Period.EffectiveEndTime;
            end = endTime is not null ? new DateTimeOffset(DateTime.SpecifyKind(endTime.AsUtc, DateTimeKind.Utc)) : start;
        }

        string busy = Property(source, "X-MICROSOFT-CDO-BUSYSTATUS")?.ToUpperInvariant() switch
        {
            "FREE" => "free",
            "TENTATIVE" => "tentative",
            "OOF" => "oof",
            "WORKINGELSEWHERE" => "workingElsewhere",
            "BUSY" => "busy",
            _ => string.Equals(source.Transparency, "TRANSPARENT", StringComparison.OrdinalIgnoreCase) ? "free" : "busy",
        };

        string? organizerEmail = source.Organizer?.Value?.ToString();
        if (organizerEmail?.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase) == true)
        {
            organizerEmail = organizerEmail[7..];
        }

        return new CoreCalendarEvent
        {
            Id = $"{source.Uid}:{startTime.AsUtc:yyyyMMddTHHmmss}",
            Subject = source.Summary ?? "",
            Start = start,
            End = end < start ? start : end,
            IsAllDay = allDay,
            ShowAs = busy,
            Response = "none",
            Organizer = source.Organizer?.CommonName,
            OrganizerEmail = organizerEmail,
            AttendeeCount = source.Attendees?.Count ?? 0,
            Location = source.Location,
            Categories = [.. source.Categories ?? []],
            IsOnlineMeeting = Property(source, "X-MICROSOFT-SKYPETEAMSMEETINGURL") is not null
                              || source.Location?.Contains("Teams", StringComparison.OrdinalIgnoreCase) == true,
            IsRecurring = source.RecurrenceRule is not null || source.RecurrenceIdentifier is not null,
            SeriesId = source.Uid,
            Sensitivity = source.Class?.ToLowerInvariant() switch
            {
                "private" => "private",
                "confidential" => "confidential",
                _ => "normal",
            },
            IsCancelled = string.Equals(source.Status, "CANCELLED", StringComparison.OrdinalIgnoreCase),
        };
    }

    private static string? Property(CalendarEvent source, string name) =>
        source.Properties.FirstOrDefault(property => string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))?.Value?.ToString();
}
