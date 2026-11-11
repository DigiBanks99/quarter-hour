using QuarterHour.Core.Configuration;

namespace QuarterHour.Core.Time;

public enum DayKind
{
    WorkDay,
    Weekend,
    PublicHoliday,
}

/// <summary>Knows which days are working days, so it can tell how many hours a day (and a period) should have.</summary>
public sealed class WorkCalendar
{
    private readonly TargetSettings _targets;
    private readonly Dictionary<int, Dictionary<DateOnly, string>> _holidaysByYear = [];

    public WorkCalendar(TargetSettings targets)
    {
        _targets = targets;
    }

    public DayKind KindOf(DateOnly date)
    {
        if (HolidayName(date) is not null)
        {
            return DayKind.PublicHoliday;
        }

        return _targets.WorkDays.Contains(date.DayOfWeek) ? DayKind.WorkDay : DayKind.Weekend;
    }

    public bool IsWorkDay(DateOnly date) => KindOf(date) == DayKind.WorkDay;

    public string? HolidayName(DateOnly date)
    {
        if (!_holidaysByYear.TryGetValue(date.Year, out Dictionary<DateOnly, string>? holidays))
        {
            holidays = [];
            if (_targets.SouthAfricanPublicHolidays)
            {
                foreach ((DateOnly day, string name) in SouthAfricanHolidays.For(date.Year))
                {
                    holidays.TryAdd(day, name);
                }
            }

            foreach (Holiday custom in _targets.CustomHolidays.Where(holiday => holiday.Date.Year == date.Year))
            {
                holidays[custom.Date] = string.IsNullOrWhiteSpace(custom.Name) ? "Holiday" : custom.Name;
            }

            _holidaysByYear[date.Year] = holidays;
        }

        return holidays.GetValueOrDefault(date);
    }

    public int ExpectedMinutes(DateOnly date) =>
        IsWorkDay(date) ? (int)Math.Round(_targets.StandardDayHours * 60) : 0;
}
