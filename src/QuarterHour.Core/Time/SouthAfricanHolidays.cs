namespace QuarterHour.Core.Time;

/// <summary>
/// South African public holidays under the Public Holidays Act, including the rule that a holiday falling on a
/// Sunday moves to the Monday (or the next day that isn't already a holiday). One-off holidays, such as election
/// days, are added in settings.
/// </summary>
public static class SouthAfricanHolidays
{
    public static IReadOnlyList<(DateOnly Date, string Name)> For(int year)
    {
        DateOnly easter = EasterSunday(year);
        List<(DateOnly Date, string Name)> holidays =
        [
            (new DateOnly(year, 1, 1), "New Year's Day"),
            (new DateOnly(year, 3, 21), "Human Rights Day"),
            (easter.AddDays(-2), "Good Friday"),
            (easter.AddDays(1), "Family Day"),
            (new DateOnly(year, 4, 27), "Freedom Day"),
            (new DateOnly(year, 5, 1), "Workers' Day"),
            (new DateOnly(year, 6, 16), "Youth Day"),
            (new DateOnly(year, 8, 9), "National Women's Day"),
            (new DateOnly(year, 9, 24), "Heritage Day"),
            (new DateOnly(year, 12, 16), "Day of Reconciliation"),
            (new DateOnly(year, 12, 25), "Christmas Day"),
            (new DateOnly(year, 12, 26), "Day of Goodwill"),
        ];

        HashSet<DateOnly> taken = holidays.Select(holiday => holiday.Date).ToHashSet();
        List<(DateOnly Date, string Name)> observed = [];
        foreach ((DateOnly date, string name) in holidays.OrderBy(holiday => holiday.Date))
        {
            if (date.DayOfWeek != DayOfWeek.Sunday)
            {
                continue;
            }

            DateOnly substitute = date.AddDays(1);
            while (taken.Contains(substitute))
            {
                substitute = substitute.AddDays(1);
            }

            taken.Add(substitute);
            observed.Add((substitute, $"{name} (observed)"));
        }

        return [.. holidays.Concat(observed).OrderBy(holiday => holiday.Date)];
    }

    /// <summary>Western Easter Sunday (anonymous Gregorian algorithm).</summary>
    public static DateOnly EasterSunday(int year)
    {
        int a = year % 19;
        int b = year / 100;
        int c = year % 100;
        int d = b / 4;
        int e = b % 4;
        int f = (b + 8) / 25;
        int g = (b - f + 1) / 3;
        int h = ((19 * a) + b - d - g + 15) % 30;
        int i = c / 4;
        int k = c % 4;
        int l = (32 + (2 * e) + (2 * i) - h - k) % 7;
        int m = (a + (11 * h) + (22 * l)) / 451;
        int month = (h + l - (7 * m) + 114) / 31;
        int day = ((h + l - (7 * m) + 114) % 31) + 1;
        return new DateOnly(year, month, day);
    }
}
