using QuarterHour.Core.Configuration;
using QuarterHour.Core.Timeline;

namespace QuarterHour.Core.Tests.Timeline;

public sealed class TimelineResolverTests
{
    private static readonly DateTime Nine = new(2026, 11, 9, 9, 0, 0);

    [Test]
    public void AMeetingHappensDuringAgentWork()
    {
        IReadOnlyDictionary<string, TimelineSlot> slots = TimelineResolver.Resolve(
        [
            new TimelineItem("agent", Nine, Nine.AddHours(2), Priority: 1),
            new TimelineItem("meeting", Nine.AddHours(1), Nine.AddHours(1.5), Priority: 2),
        ], OverlapPolicy.HigherPriorityWins);

        ItShouldGiveTheMeetingItsFullTime();
        ItShouldTakeTheMeetingOutOfTheAgentWork();

        void ItShouldGiveTheMeetingItsFullTime() => slots["meeting"].Effective.ShouldBe(TimeSpan.FromMinutes(30));

        void ItShouldTakeTheMeetingOutOfTheAgentWork() => slots["agent"].ShouldSatisfyAllConditions(
            () => slots["agent"].Effective.ShouldBe(TimeSpan.FromMinutes(90)),
            () => slots["agent"].OverlappedBy.ShouldBe(["meeting"]),
            () => slots["agent"].Segments.Count.ShouldBe(2));
    }

    [Test]
    public void TwoMeetingsAreDoubleBooked()
    {
        IReadOnlyDictionary<string, TimelineSlot> slots = TimelineResolver.Resolve(
        [
            new TimelineItem("later", Nine.AddMinutes(30), Nine.AddMinutes(90), Priority: 2),
            new TimelineItem("earlier", Nine, Nine.AddMinutes(60), Priority: 2),
        ], OverlapPolicy.HigherPriorityWins);

        ItShouldLetTheEarlierMeetingKeepItsTime();
        ItShouldOnlyGiveTheLaterMeetingWhatIsLeft();

        void ItShouldLetTheEarlierMeetingKeepItsTime() => slots["earlier"].Effective.ShouldBe(TimeSpan.FromMinutes(60));

        void ItShouldOnlyGiveTheLaterMeetingWhatIsLeft() => slots["later"].Effective.ShouldBe(TimeSpan.FromMinutes(30));
    }

    [Test]
    public void TwoAgentsRunSideBySide()
    {
        IReadOnlyDictionary<string, TimelineSlot> slots = TimelineResolver.Resolve(
        [
            new TimelineItem("first", Nine, Nine.AddMinutes(60), Priority: 1),
            new TimelineItem("second", Nine.AddMinutes(30), Nine.AddMinutes(90), Priority: 1),
            new TimelineItem("meeting", Nine.AddMinutes(80), Nine.AddMinutes(90), Priority: 2),
        ], OverlapPolicy.HigherPriorityWins, new HashSet<int> { 1 });

        ItShouldSplitTheOverlapEvenly();
        ItShouldStillGiveTheMeetingPrecedence();
        ItShouldNeverCountAMinuteTwice();

        void ItShouldSplitTheOverlapEvenly() => slots["first"].Effective.ShouldBe(TimeSpan.FromMinutes(45));

        void ItShouldStillGiveTheMeetingPrecedence() => slots["second"].ShouldSatisfyAllConditions(
            () => slots["second"].Effective.ShouldBe(TimeSpan.FromMinutes(35)),
            () => slots["second"].OverlappedBy.ShouldBe(["meeting", "first"], ignoreOrder: true));

        void ItShouldNeverCountAMinuteTwice() => slots.Values.Aggregate(TimeSpan.Zero, (total, slot) => total + slot.Effective).ShouldBe(TimeSpan.FromMinutes(90));
    }

    [Test]
    public void OverlapsAreAllowedToCountTwice()
    {
        IReadOnlyDictionary<string, TimelineSlot> slots = TimelineResolver.Resolve(
        [
            new TimelineItem("agent", Nine, Nine.AddHours(2), Priority: 1),
            new TimelineItem("meeting", Nine.AddHours(1), Nine.AddHours(1.5), Priority: 2),
        ], OverlapPolicy.CountOverlaps);

        ItShouldKeepEveryMinute();
        ItShouldStillReportTheOverlap();

        void ItShouldKeepEveryMinute() => slots["agent"].Effective.ShouldBe(TimeSpan.FromHours(2));

        void ItShouldStillReportTheOverlap() => slots["agent"].OverlappedBy.ShouldBe(["meeting"]);
    }
}
