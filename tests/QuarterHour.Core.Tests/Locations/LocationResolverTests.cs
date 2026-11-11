using QuarterHour.Core.Configuration;
using QuarterHour.Core.Locations;

namespace QuarterHour.Core.Tests.Locations;

public sealed class LocationResolverTests
{
    private static readonly DateOnly Monday = new(2026, 11, 9);
    private static readonly DateOnly Tuesday = new(2026, 11, 10);

    private readonly LocationSettings _settings = new() { Default = "Home" };
    private readonly Journal _journal = new();

    [Test]
    public void NothingIsScheduled()
    {
        ResolvedLocation location = new LocationResolver(_settings, _journal).ForDay(Monday);

        ItShouldUseTheDefault();

        void ItShouldUseTheDefault() => location.ShouldBe(new ResolvedLocation("Home", LocationOrigin.Default, "Default location"));
    }

    [Test]
    public void TuesdaysAreAtTheClient()
    {
        _settings.Schedule.Add(new LocationScheduleRule { Location = "Client", Days = [DayOfWeek.Tuesday, DayOfWeek.Wednesday] });
        LocationResolver resolver = new(_settings, _journal);

        ResolvedLocation tuesday = resolver.ForDay(Tuesday);
        ResolvedLocation monday = resolver.ForDay(Monday);

        ItShouldPutTuesdayAtTheClient();
        ItShouldLeaveMondayAtHome();

        void ItShouldPutTuesdayAtTheClient() => tuesday.ShouldSatisfyAllConditions(
            () => tuesday.Name.ShouldBe("Client"),
            () => tuesday.Origin.ShouldBe(LocationOrigin.Schedule));

        void ItShouldLeaveMondayAtHome() => monday.Name.ShouldBe("Home");
    }

    [Test]
    public void EveryOtherFridayIsAtTheOffice()
    {
        _settings.Schedule.Add(new LocationScheduleRule
        {
            Location = "Entelect",
            Days = [DayOfWeek.Friday],
            Recurrence = ScheduleRecurrence.EveryNWeeks,
            IntervalWeeks = 2,
            AnchorDate = new DateOnly(2026, 11, 2),
        });
        LocationResolver resolver = new(_settings, _journal);

        string[] fridays = [.. new[] { 6, 13, 20, 27 }.Select(day => resolver.ForDay(new DateOnly(2026, 11, day)).Name)];

        ItShouldAlternate();

        void ItShouldAlternate() => fridays.ShouldBe(["Entelect", "Home", "Entelect", "Home"]);
    }

    [Test]
    public void TheFirstAndLastFridaysOfTheMonthAreSpecial()
    {
        _settings.Schedule.Add(new LocationScheduleRule { Location = "Entelect", Days = [DayOfWeek.Friday], Recurrence = ScheduleRecurrence.MonthlyNthWeekday, WeekOfMonth = 1 });
        _settings.Schedule.Add(new LocationScheduleRule { Location = "Other", Days = [DayOfWeek.Friday], Recurrence = ScheduleRecurrence.MonthlyNthWeekday, WeekOfMonth = -1 });
        LocationResolver resolver = new(_settings, _journal);

        string[] fridays = [.. new[] { 6, 13, 20, 27 }.Select(day => resolver.ForDay(new DateOnly(2026, 11, day)).Name)];

        ItShouldOnlyMoveThoseFridays();

        void ItShouldOnlyMoveThoseFridays() => fridays.ShouldBe(["Entelect", "Home", "Home", "Other"]);
    }

    [Test]
    public void AScheduledDayIsOverridden()
    {
        _settings.Schedule.Add(new LocationScheduleRule { Location = "Client", Days = [DayOfWeek.Tuesday] });
        _journal.Days[Journal.DayKey(Tuesday)] = new DayOverride { Location = "Entelect" };

        ResolvedLocation location = new LocationResolver(_settings, _journal).ForDay(Tuesday);

        ItShouldUseTheOverride();

        void ItShouldUseTheOverride() => location.ShouldSatisfyAllConditions(
            () => location.Name.ShouldBe("Entelect"),
            () => location.Origin.ShouldBe(LocationOrigin.DayOverride));
    }

    [Test]
    public void TheScheduleHasEnded()
    {
        _settings.Schedule.Add(new LocationScheduleRule { Location = "Client", Days = [DayOfWeek.Tuesday], To = new DateOnly(2026, 10, 31) });

        ResolvedLocation location = new LocationResolver(_settings, _journal).ForDay(Tuesday);

        ItShouldFallBackToTheDefault();

        void ItShouldFallBackToTheDefault() => location.Name.ShouldBe("Home");
    }
}
