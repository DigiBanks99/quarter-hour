using QuarterHour.Core.Model;

namespace QuarterHour.Core.Tests.Model;

public sealed class CalendarActivitiesTests
{
    private static readonly TimeZoneInfo SouthAfrica = TimeZoneInfo.CreateCustomTimeZone("SAST", TimeSpan.FromHours(2), "SAST", "SAST");

    [Test]
    [Arguments("RE: FW: Sprint 42 Planning", "Sprint 42 Planning")]
    [Arguments("Updated: Sprint 43   planning ", "Sprint 43 planning")]
    public void ASubjectHasReplyPrefixesAndNumbers(string subject, string label)
    {
        string key = CalendarActivities.GroupKeyFor(subject);
        string cleaned = CalendarActivities.CleanSubject(subject);

        ItShouldGroupEverySprintTogether();
        ItShouldShowTheCleanedSubject();

        void ItShouldGroupEverySprintTogether() => key.ShouldBe("cal:sprint # planning");

        void ItShouldShowTheCleanedSubject() => cleaned.ShouldBe(label);
    }

    [Test]
    public void ATimedEventIsReadInUtc()
    {
        CalendarEvent calendarEvent = new()
        {
            Id = "e1",
            Subject = "Refinement",
            Start = new DateTimeOffset(2026, 11, 9, 8, 0, 0, TimeSpan.Zero),
            End = new DateTimeOffset(2026, 11, 9, 9, 0, 0, TimeSpan.Zero),
        };

        Activity activity = CalendarActivities.ToActivity(calendarEvent, SouthAfrica);

        ItShouldUseLocalWorkingTime();

        void ItShouldUseLocalWorkingTime() => activity.ShouldSatisfyAllConditions(
            () => activity.Start.ShouldBe(new DateTime(2026, 11, 9, 10, 0, 0)),
            () => activity.IsTimed.ShouldBeTrue());
    }

    [Test]
    public void AnAllDayEventIsRead()
    {
        CalendarEvent calendarEvent = new()
        {
            Id = "e2",
            Subject = "Leave",
            Start = new DateTimeOffset(2026, 11, 9, 0, 0, 0, TimeSpan.Zero),
            End = new DateTimeOffset(2026, 11, 11, 0, 0, 0, TimeSpan.Zero),
            IsAllDay = true,
        };

        Activity activity = CalendarActivities.ToActivity(calendarEvent, SouthAfrica);

        ItShouldKeepTheDatesAsWritten();
        ItShouldNotSitOnTheTimeline();

        void ItShouldKeepTheDatesAsWritten() => activity.ShouldSatisfyAllConditions(
            () => activity.Start.ShouldBe(new DateTime(2026, 11, 9)),
            () => activity.End.ShouldBe(new DateTime(2026, 11, 11)));

        void ItShouldNotSitOnTheTimeline() => activity.IsTimed.ShouldBeFalse();
    }
}
