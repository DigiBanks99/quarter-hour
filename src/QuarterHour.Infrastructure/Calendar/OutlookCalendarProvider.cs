using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using QuarterHour.Core.Configuration;
using QuarterHour.Core.Model;
using QuarterHour.Core.Sources;

namespace QuarterHour.Infrastructure.Calendar;

/// <summary>
/// Reads the default calendar of classic Outlook on this machine through COM. It needs no app registration or
/// consent, only classic Outlook (the new Outlook has no COM interface). Windows only.
/// </summary>
public sealed class OutlookCalendarProvider : ICalendarProvider
{
    private const int CalendarFolder = 9;
    private const int MaxItems = 10_000;

    public CalendarProviderKind Kind => CalendarProviderKind.OutlookDesktop;

    public static bool IsAvailable => OperatingSystem.IsWindows() && Type.GetTypeFromProgID("Outlook.Application") is not null;

    public Task<IReadOnlyList<CalendarEvent>> GetEventsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Reading Outlook needs Windows.");
        }

        TaskCompletionSource<IReadOnlyList<CalendarEvent>> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Thread thread = new(() =>
        {
            try
            {
                completion.SetResult(OperatingSystem.IsWindows() ? Read(from, to) : throw new PlatformNotSupportedException());
            }
            catch (Exception exception)
            {
                completion.SetException(exception);
            }
        })
        {
            IsBackground = true,
            Name = "Outlook calendar reader",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task.WaitAsync(cancellationToken);
    }

    [SupportedOSPlatform("windows")]
    private static IReadOnlyList<CalendarEvent> Read(DateTimeOffset from, DateTimeOffset to)
    {
        Type type = Type.GetTypeFromProgID("Outlook.Application")
                    ?? throw new InvalidOperationException("Classic Outlook isn't installed on this machine.");
        dynamic outlook = Activator.CreateInstance(type)
                          ?? throw new InvalidOperationException("Outlook couldn't be started.");
        dynamic session = outlook.GetNamespace("MAPI");
        dynamic folder = session.GetDefaultFolder(CalendarFolder);
        dynamic items = folder.Items;
        items.Sort("[Start]");
        items.IncludeRecurrences = true;

        DateTime localFrom = from.LocalDateTime;
        DateTime localTo = to.LocalDateTime;
        string filter = $"[Start] < '{localTo.ToString("g", CultureInfo.CurrentCulture)}' AND [End] > '{localFrom.ToString("g", CultureInfo.CurrentCulture)}'";
        dynamic restricted = items.Restrict(filter);

        List<CalendarEvent> events = [];
        int seen = 0;
        foreach (dynamic item in restricted)
        {
            if (++seen > MaxItems)
            {
                break;
            }

            try
            {
                CalendarEvent? calendarEvent = ToEvent(item, from, to);
                if (calendarEvent is not null)
                {
                    events.Add(calendarEvent);
                }
            }
            finally
            {
                if (OperatingSystem.IsWindows())
                {
                    Marshal.ReleaseComObject(item);
                }
            }
        }

        return events;
    }

    private static CalendarEvent? ToEvent(dynamic item, DateTimeOffset from, DateTimeOffset to)
    {
        DateTime start = (DateTime)item.Start;
        DateTime end = (DateTime)item.End;
        bool allDay = (bool)item.AllDayEvent;
        DateTimeOffset startOffset = allDay ? new DateTimeOffset(start.Date, TimeSpan.Zero) : new DateTimeOffset(start, TimeZoneInfo.Local.GetUtcOffset(start));
        DateTimeOffset endOffset = allDay ? new DateTimeOffset(end.Date, TimeSpan.Zero) : new DateTimeOffset(end, TimeZoneInfo.Local.GetUtcOffset(end));
        if (!allDay && (endOffset <= from || startOffset >= to))
        {
            return null;
        }

        int busyStatus = (int)item.BusyStatus;
        int responseStatus = (int)item.ResponseStatus;
        int meetingStatus = (int)item.MeetingStatus;
        string categories = (string?)item.Categories ?? "";
        string globalId = Safe(() => (string)item.GlobalAppointmentID) ?? Safe(() => (string)item.EntryID) ?? Guid.NewGuid().ToString("N");

        return new CalendarEvent
        {
            Id = $"{globalId}:{start:yyyyMMddTHHmm}",
            Subject = (string?)item.Subject ?? "",
            Start = startOffset,
            End = endOffset,
            IsAllDay = allDay,
            ShowAs = busyStatus switch
            {
                0 => "free",
                1 => "tentative",
                3 => "oof",
                4 => "workingElsewhere",
                _ => "busy",
            },
            Response = responseStatus switch
            {
                1 => "organizer",
                2 => "tentativelyAccepted",
                3 => "accepted",
                4 => "declined",
                5 => "notResponded",
                _ => "none",
            },
            Organizer = Safe(() => (string)item.Organizer),
            IsOrganizer = responseStatus == 1 || meetingStatus == 0,
            AttendeeCount = Math.Max(0, (Safe(() => (int?)item.Recipients.Count) ?? 1) - 1),
            Location = Safe(() => (string)item.Location),
            Categories = [.. categories.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)],
            IsOnlineMeeting = Safe(() => (string)item.Location)?.Contains("Teams", StringComparison.OrdinalIgnoreCase) == true,
            IsRecurring = (bool)item.IsRecurring,
            SeriesId = globalId,
            Sensitivity = (int)item.Sensitivity switch
            {
                1 => "personal",
                2 => "private",
                3 => "confidential",
                _ => "normal",
            },
            IsCancelled = meetingStatus is 5 or 7,
        };
    }

    private static T? Safe<T>(Func<T?> read)
    {
        try
        {
            return read();
        }
        catch (Exception)
        {
            return default;
        }
    }
}
