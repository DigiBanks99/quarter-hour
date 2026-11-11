using QuarterHour.Core.Configuration;
using QuarterHour.Core.Time;

namespace QuarterHour.Core.Tests.Time;

public sealed class SouthAfricanHolidaysTests
{
    [Test]
    public void TheHolidaysOf2026AreListed()
    {
        IReadOnlyList<(DateOnly Date, string Name)> holidays = SouthAfricanHolidays.For(2026);

        ItShouldIncludeTheEasterWeekend();
        ItShouldMoveWomensDayOffTheSunday();
        ItShouldListEachHolidayOnce();

        void ItShouldIncludeTheEasterWeekend() => holidays.ShouldSatisfyAllConditions(
            () => holidays.ShouldContain((new DateOnly(2026, 4, 3), "Good Friday")),
            () => holidays.ShouldContain((new DateOnly(2026, 4, 6), "Family Day")));

        void ItShouldMoveWomensDayOffTheSunday() =>
            holidays.ShouldContain((new DateOnly(2026, 8, 10), "National Women's Day (observed)"));

        void ItShouldListEachHolidayOnce() => holidays.Count.ShouldBe(13);
    }

    [Test]
    public void ChristmasFallsOnASunday()
    {
        IReadOnlyList<(DateOnly Date, string Name)> holidays = SouthAfricanHolidays.For(2022);

        ItShouldObserveItAfterTheDayOfGoodwill();

        void ItShouldObserveItAfterTheDayOfGoodwill() =>
            holidays.ShouldContain((new DateOnly(2022, 12, 27), "Christmas Day (observed)"));
    }

    [Test]
    [Arguments(2024, 3, 31)]
    [Arguments(2025, 4, 20)]
    [Arguments(2026, 4, 5)]
    public void EasterIsWorkedOut(int year, int month, int day)
    {
        DateOnly easter = SouthAfricanHolidays.EasterSunday(year);

        ItShouldMatchTheChurchCalendar();

        void ItShouldMatchTheChurchCalendar() => easter.ShouldBe(new DateOnly(year, month, day));
    }

    [Test]
    public void AnElectionDayIsAddedByHand()
    {
        WorkCalendar calendar = new(new TargetSettings
        {
            CustomHolidays = [new Holiday { Date = new DateOnly(2026, 11, 4), Name = "Municipal elections" }],
        });

        DayKind kind = calendar.KindOf(new DateOnly(2026, 11, 4));
        int expected = calendar.ExpectedMinutes(new DateOnly(2026, 11, 4));

        ItShouldBeAHoliday();
        ItShouldNotExpectAnyHours();

        void ItShouldBeAHoliday() => kind.ShouldBe(DayKind.PublicHoliday);

        void ItShouldNotExpectAnyHours() => expected.ShouldBe(0);
    }
}
