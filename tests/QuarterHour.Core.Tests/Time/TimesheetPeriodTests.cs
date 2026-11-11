using QuarterHour.Core.Time;

namespace QuarterHour.Core.Tests.Time;

public sealed class TimesheetPeriodTests
{
    [Test]
    public void ACalendarMonthIsUsed()
    {
        TimesheetPeriod period = new(2026, 11);

        ItShouldStartOnTheFirst();
        ItShouldEndBeforeTheNextMonth();
        ItShouldCoverEveryDay();

        void ItShouldStartOnTheFirst() => period.Start.ShouldBe(new DateOnly(2026, 11, 1));

        void ItShouldEndBeforeTheNextMonth() => period.LastDay.ShouldBe(new DateOnly(2026, 11, 30));

        void ItShouldCoverEveryDay() => period.Days().Count().ShouldBe(30);
    }

    [Test]
    public void ThePeriodStartsMidMonth()
    {
        TimesheetPeriod period = new(2026, 11, startDay: 26);

        ItShouldStartInThePreviousMonth();
        ItShouldEndTheDayBeforeTheStartDay();

        void ItShouldStartInThePreviousMonth() => period.Start.ShouldBe(new DateOnly(2026, 10, 26));

        void ItShouldEndTheDayBeforeTheStartDay() => period.LastDay.ShouldBe(new DateOnly(2026, 11, 25));
    }

    [Test]
    public void ADayAfterTheCutOffIsLookedUp()
    {
        TimesheetPeriod period = TimesheetPeriod.Containing(new DateOnly(2026, 11, 27), startDay: 26);

        ItShouldBelongToTheNextMonthsPeriod();

        void ItShouldBelongToTheNextMonthsPeriod() => period.Key.ShouldBe("2026-12");
    }

    [Test]
    public void APeriodKeyIsRead()
    {
        bool parsed = TimesheetPeriod.TryParse("2026-02", 1, out TimesheetPeriod period);
        bool rejected = TimesheetPeriod.TryParse("February", 1, out _);

        ItShouldReadTheYearAndMonth();
        ItShouldRejectAnythingElse();

        void ItShouldReadTheYearAndMonth()
        {
            parsed.ShouldBeTrue();
            period.LastDay.ShouldBe(new DateOnly(2026, 2, 28));
        }

        void ItShouldRejectAnythingElse() => rejected.ShouldBeFalse();
    }
}
