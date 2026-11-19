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
    public void TheDaysEitherSideOfAMonthEndAreLookedUp()
    {
        TimesheetPeriod lastDay = TimesheetPeriod.Containing(new DateOnly(2026, 11, 30));
        TimesheetPeriod firstDay = TimesheetPeriod.Containing(new DateOnly(2026, 12, 1));

        ItShouldPutTheLastDayInItsOwnMonth();
        ItShouldPutTheFirstDayInTheNextMonth();

        void ItShouldPutTheLastDayInItsOwnMonth() => lastDay.Key.ShouldBe("2026-11");

        void ItShouldPutTheFirstDayInTheNextMonth() => firstDay.Key.ShouldBe("2026-12");
    }

    [Test]
    [Arguments(2026, 28)]
    [Arguments(2028, 29)]
    public void FebruaryIsUsed(int year, int days)
    {
        TimesheetPeriod period = new(year, 2);

        ItShouldCoverTheWholeOfFebruary();

        void ItShouldCoverTheWholeOfFebruary() => period.ShouldSatisfyAllConditions(
            () => period.Start.ShouldBe(new DateOnly(year, 2, 1)),
            () => period.LastDay.ShouldBe(new DateOnly(year, 2, days)),
            () => period.Days().Count().ShouldBe(days));
    }

    [Test]
    public void TheYearTurns()
    {
        TimesheetPeriod january = new TimesheetPeriod(2026, 12).Next();
        TimesheetPeriod december = new TimesheetPeriod(2027, 1).Previous();

        ItShouldMoveOnToJanuaryOfTheNextYear();
        ItShouldMoveBackToDecemberOfThePreviousYear();

        void ItShouldMoveOnToJanuaryOfTheNextYear() => january.ShouldSatisfyAllConditions(
            () => january.Key.ShouldBe("2027-01"),
            () => january.Start.ShouldBe(new DateOnly(2027, 1, 1)));

        void ItShouldMoveBackToDecemberOfThePreviousYear() => december.ShouldSatisfyAllConditions(
            () => december.Key.ShouldBe("2026-12"),
            () => december.LastDay.ShouldBe(new DateOnly(2026, 12, 31)));
    }

    [Test]
    public void APeriodKeyIsRead()
    {
        bool parsed = TimesheetPeriod.TryParse("2026-02", out TimesheetPeriod period);
        bool rejected = TimesheetPeriod.TryParse("February", out _);

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
