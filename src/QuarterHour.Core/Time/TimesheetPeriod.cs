using System.Globalization;

namespace QuarterHour.Core.Time;

/// <summary>The span of days one timesheet covers: a calendar month, from its first to its last day.</summary>
public readonly record struct TimesheetPeriod
{
    public TimesheetPeriod(int year, int month)
    {
        Year = year;
        Month = month;
        Start = new DateOnly(year, month, 1);
        End = Start.AddMonths(1);
    }

    public int Year { get; }
    public int Month { get; }

    /// <summary>The first day in the period.</summary>
    public DateOnly Start { get; }

    /// <summary>The first day after the period.</summary>
    public DateOnly End { get; }

    public DateOnly LastDay => End.AddDays(-1);

    public string Key => $"{Year:D4}-{Month:D2}";

    public string Name => Start.ToString("MMMM yyyy", CultureInfo.InvariantCulture);

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

    public static TimesheetPeriod Containing(DateOnly date) => new(date.Year, date.Month);

    public static bool TryParse(string? key, out TimesheetPeriod period)
    {
        period = default;
        if (string.IsNullOrWhiteSpace(key)
            || !DateOnly.TryParseExact(key + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly first))
        {
            return false;
        }

        period = new TimesheetPeriod(first.Year, first.Month);
        return true;
    }

    private TimesheetPeriod Shift(int months)
    {
        DateOnly first = Start.AddMonths(months);
        return new TimesheetPeriod(first.Year, first.Month);
    }

    public override string ToString() => Key;
}
