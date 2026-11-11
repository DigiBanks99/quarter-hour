using QuarterHour.Core.Configuration;
using QuarterHour.Core.Time;

namespace QuarterHour.Core.Tests.Time;

public sealed class RoundingTests
{
    private readonly RoundingSettings _settings = new() { IncrementMinutes = 15, RoundUpFromMinutes = 7 };

    [Test]
    [Arguments(50, 45)]
    [Arguments(202, 210)]
    [Arguments(60, 60)]
    [Arguments(7, 15)]
    [Arguments(6, 0)]
    [Arguments(0, 0)]
    [Arguments(481, 480)]
    public void ADurationIsLogged(int minutes, int expected)
    {
        int rounded = Rounding.RoundMinutes(TimeSpan.FromMinutes(minutes), _settings);

        ItShouldLandOnTheQuarterHourTheTimesheetExpects();

        void ItShouldLandOnTheQuarterHourTheTimesheetExpects() => rounded.ShouldBe(expected);
    }

    [Test]
    public void TheLeftoverIsJustShortOfTheRoundUpPoint()
    {
        int rounded = Rounding.RoundMinutes(new TimeSpan(0, 21, 59), _settings);

        ItShouldRoundDown();

        void ItShouldRoundDown() => rounded.ShouldBe(15);
    }

    [Test]
    public void TheRoundUpPointIsMovedToHalfTheIncrement()
    {
        int rounded = Rounding.RoundMinutes(TimeSpan.FromMinutes(202), 15, 7.5);

        ItShouldRoundToTheStrictlyNearestQuarter();

        void ItShouldRoundToTheStrictlyNearestQuarter() => rounded.ShouldBe(195);
    }

    [Test]
    public void ADurationIsNegative()
    {
        int rounded = Rounding.RoundMinutes(TimeSpan.FromMinutes(-20), _settings);

        ItShouldLogNothing();

        void ItShouldLogNothing() => rounded.ShouldBe(0);
    }
}
