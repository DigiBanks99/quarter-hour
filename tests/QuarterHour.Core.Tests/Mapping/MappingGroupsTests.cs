using QuarterHour.Core.Configuration;
using QuarterHour.Core.Mapping;
using QuarterHour.Core.Model;
using QuarterHour.Core.Rules;
using QuarterHour.Core.Time;
using QuarterHour.Core.Timesheets;

namespace QuarterHour.Core.Tests.Mapping;

public sealed class MappingGroupsTests
{
    private static readonly TimesheetPeriod November = new(2026, 11);
    private static readonly DateTime Monday = new(2026, 11, 9, 9, 0, 0);

    private readonly WorkspaceConfig _config = TestData.Workspace();

    [Test]
    public void AStandupHappensEveryDay()
    {
        IReadOnlyList<MappingGroup> groups = Groups(
            TestData.Meeting("m1", "Daily standup", Monday, 15),
            TestData.Meeting("m2", "Daily Standup", Monday.AddDays(1), 15));
        MappingGroup standup = groups.Single();

        ItShouldGroupTheOccurrences();
        ItShouldStillNeedMapping();

        void ItShouldGroupTheOccurrences() => standup.ShouldSatisfyAllConditions(
            () => standup.Occurrences.ShouldBe(2),
            () => standup.RawTime.ShouldBe(TimeSpan.FromMinutes(30)));

        void ItShouldStillNeedMapping() => standup.ShouldSatisfyAllConditions(
            () => standup.IsUnmapped.ShouldBeTrue(),
            () => standup.TargetId.ShouldBeNull());
    }

    [Test]
    public void AStandupIsDroppedOnACategory()
    {
        Activity standup = TestData.Meeting("m1", "Daily standup", Monday, 15);
        DirectMappings.Link(_config.Rules, standup.GroupKey, standup.GroupLabel, ActivitySource.Calendar, DefaultWorkspace.ClientMeetings);

        MappingGroup group = Groups(standup).Single();

        ItShouldPointAtTheCategory();
        ItShouldBeADirectMapping();

        void ItShouldPointAtTheCategory() => group.TargetId.ShouldBe(DefaultWorkspace.ClientMeetings);

        void ItShouldBeADirectMapping() => group.IsDirect.ShouldBeTrue();
    }

    [Test]
    public void OneOccurrenceWasDeclined()
    {
        _config.Rules.Add(TestData.Filter("Declined", "response", ConditionOperator.Equals, "declined"));
        _config.Rules.Add(TestData.Heuristic("Standups", "title", ConditionOperator.Contains, "standup", DefaultWorkspace.ClientMeetings));

        MappingGroup group = Groups(
            TestData.Meeting("m1", "Daily standup", Monday, 15),
            TestData.Meeting("m2", "Daily standup", Monday.AddDays(1), 15, response: "declined")).Single();

        ItShouldStillShowTheMapping();
        ItShouldCountTheFilteredOccurrence();

        void ItShouldStillShowTheMapping() => group.ShouldSatisfyAllConditions(
            () => group.TargetId.ShouldBe(DefaultWorkspace.ClientMeetings),
            () => group.IsDirect.ShouldBeFalse());

        void ItShouldCountTheFilteredOccurrence() => group.FilteredCount.ShouldBe(1);
    }

    private IReadOnlyList<MappingGroup> Groups(params Activity[] activities)
    {
        PeriodTimesheet sheet = new TimesheetBuilder().Build(November, _config.Normalize(), new Journal(), activities);
        return MappingGroups.Build(sheet, _config);
    }
}
