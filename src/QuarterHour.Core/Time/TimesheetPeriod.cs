using System.Globalization;

namespace QuarterHour.Core.Time;

/// <summary>
/// The span of days one timesheet covers. With a start day of 1 it is a calendar month. With a later start day,
/// the period named after month M runs from that day in the previous month up to the day before it in month M
/// (start day 26: "November" is 26 October to 25 November).
/// </summary>
public readonly record struct TimesheetPeriod
{
    public TimesheetPeriod(int year, int month, int startDay = 1)
    {
        Year = year;
        Month = month;
        StartDay = Math.Clamp(startDay, 1, 28);
        if (StartDay == 1)
        {
            Start = new DateOnly(year, month, 1);
            End = Start.AddMonths(1);
        }
        else
        {
            End = new DateOnly(year, month, StartDay);
            Start = End.AddMonths(-1);
        }
    }

    public int Year { get; }
    public int Month { get; }
    public int StartDay { get; }

    /// <summary>The first day in the period.</summary>
    public DateOnly Start { get; }

    /// <summary>The first day after the period.</summary>
    public DateOnly End { get; }

    public DateOnly LastDay => End.AddDays(-1);

    public string Key => $"{Year:D4}-{Month:D2}";

    public string Name => new DateOnly(Year, Month, 1).ToString("MMMM yyyy", CultureInfo.InvariantCulture);

    public string RangeLabel =>
        $"{Start.ToString("d MMM", CultureInfo.InvariantCulture)} – {LastDay.ToString("d MMM yyyy", CultureInfo.InvariantCulture)}";

    public bool Contains(DateOnly date) => date >= Start && date < End;

    public IEnumerable<DateOnly> Days()
    {
        for (DateOnly day = Start; day < End; day = day.AddDays(1))
        {
            yield return day;
        }
    }

    public TimesheetPeriod Next() => Shift(1);

    public TimesheetPeriod Previous() => Shift(-1);

    public static TimesheetPeriod Containing(DateOnly date, int startDay = 1)
    {
        TimesheetPeriod candidate = new(date.Year, date.Month, startDay);
        if (candidate.Contains(date))
        {
            return candidate;
        }

        return date >= candidate.End ? candidate.Next() : candidate.Previous();
    }

    public static bool TryParse(string? key, int startDay, out TimesheetPeriod period)
    {
        period = default;
        if (string.IsNullOrWhiteSpace(key)
            || !DateOnly.TryParseExact(key + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly first))
        {
            return false;
        }

        period = new TimesheetPeriod(first.Year, first.Month, startDay);
        return true;
    }

    private TimesheetPeriod Shift(int months)
    {
        DateOnly first = new DateOnly(Year, Month, 1).AddMonths(months);
        return new TimesheetPeriod(first.Year, first.Month, StartDay);
    }

    public override string ToString() => Key;
}
