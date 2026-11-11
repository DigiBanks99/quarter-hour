using QuarterHour.Core.Configuration;

namespace QuarterHour.Core.Locations;

/// <summary>Where a location came from, from weakest to strongest.</summary>
public enum LocationOrigin
{
    Default,
    Schedule,
    DayOverride,
    MeetingPin,
    ManualEntry,
    EntryOverride,
}

public sealed record ResolvedLocation(string Name, LocationOrigin Origin, string? Detail = null);

/// <summary>
/// Resolves where you worked. For a day: a day override, else the first schedule rule that applies, else the default.
/// For an activity: its own override, a manual entry's location or a rule's pin beat the day's location.
/// </summary>
public sealed class LocationResolver
{
    private readonly LocationSettings _settings;
    private readonly Journal _journal;

    public LocationResolver(LocationSettings settings, Journal journal)
    {
        _settings = settings;
        _journal = journal;
    }

    public ResolvedLocation ForDay(DateOnly date)
    {
        DayOverride? day = _journal.FindDay(date);
        if (!string.IsNullOrWhiteSpace(day?.Location))
        {
            return new ResolvedLocation(day.Location, LocationOrigin.DayOverride, "Set for this day");
        }

        foreach (LocationScheduleRule rule in _settings.Schedule)
        {
            if (rule.Enabled && !string.IsNullOrWhiteSpace(rule.Location) && Applies(rule, date))
            {
                return new ResolvedLocation(rule.Location, LocationOrigin.Schedule, Describe(rule));
            }
        }

        return new ResolvedLocation(_settings.Default, LocationOrigin.Default, "Default location");
    }

    public static bool Applies(LocationScheduleRule rule, DateOnly date)
    {
        if (!rule.Days.Contains(date.DayOfWeek))
        {
            return false;
        }

        if ((rule.From is { } from && date < from) || (rule.To is { } to && date > to))
        {
            return false;
        }

        switch (rule.Recurrence)
        {
            case ScheduleRecurrence.Weekly:
                return true;
            case ScheduleRecurrence.EveryNWeeks:
            {
                int interval = Math.Max(1, rule.IntervalWeeks);
                DateOnly anchor = rule.AnchorDate ?? rule.From ?? date;
                int weeks = (WeekStart(date).DayNumber - WeekStart(anchor).DayNumber) / 7;
                return ((weeks % interval) + interval) % interval == 0;
            }
            case ScheduleRecurrence.MonthlyNthWeekday:
            {
                if (rule.WeekOfMonth < 0)
                {
                    return date.AddDays(7).Month != date.Month;
                }

                int occurrence = ((date.Day - 1) / 7) + 1;
                return occurrence == rule.WeekOfMonth;
            }
            default:
                return false;
        }
    }

    public static string Describe(LocationScheduleRule rule)
    {
        string days = string.Join(", ", rule.Days.OrderBy(day => ((int)day + 6) % 7).Select(day => day.ToString()[..3]));
        string when = rule.Recurrence switch
        {
            ScheduleRecurrence.EveryNWeeks => $"every {Math.Max(1, rule.IntervalWeeks)} weeks on {days}",
            ScheduleRecurrence.MonthlyNthWeekday => $"{Ordinal(rule.WeekOfMonth)} {days} of the month",
            _ => $"every {days}",
        };

        return string.IsNullOrWhiteSpace(rule.Note) ? $"{rule.Location} {when}" : $"{rule.Note} ({rule.Location} {when})";
    }

    private static DateOnly WeekStart(DateOnly date) => date.AddDays(-(((int)date.DayOfWeek + 6) % 7));

    private static string Ordinal(int week) => week switch
    {
        < 0 => "last",
        1 => "1st",
        2 => "2nd",
        3 => "3rd",
        _ => $"{week}th",
    };
}
