using QuarterHour.Core.Configuration;
using QuarterHour.Core.Model;
using QuarterHour.Core.Rules;
using QuarterHour.Core.Time;
using QuarterHour.Core.Timesheets;

namespace QuarterHour.Core.Tests.Timesheets;

public sealed class CandidateEntriesTests
{
    private static readonly TimesheetPeriod November = new(2026, 11);
    private static readonly DateTime Monday = new(2026, 11, 9);

    private readonly WorkspaceConfig _config = TestData.Workspace();

    [Test]
    public void TwoOrphansFromOneEveningBecomeAnEntry()
    {
        _config.Projects.Add(new ProjectSettings { Key = "portal-api", FriendlyName = "Portal" });
        _config.Rules.Add(TestData.Direct("agent:portal-api", DefaultWorkspace.ClientDevelopment));
        PeriodTimesheet sheet = Build(TestData.AgentBlock("s1", "portal-api", Monday.AddHours(9), 60));
        Placement[] orphans = [Orphan("b2", Monday.AddHours(21), "fix: tidy the styles"), Orphan("a1", Monday.AddHours(20), "feat(ui): add a footer")];

        ManualEntry entry = CandidateEntries.Draft(orphans, sheet, _config, TimeZoneInfo.Utc);

        ItShouldBeOnTheirDayWithoutAStartTime();
        ItShouldLastOneRoundingIncrement();
        ItShouldJoinTheirSubjectsUnderTheFriendlyName();
        ItShouldUseTheProjectsCategory();
        ItShouldFollowTheDaysLocation();
        ItShouldRememberWhatItWasMadeFrom();

        void ItShouldBeOnTheirDayWithoutAStartTime() => entry.ShouldSatisfyAllConditions(
            () => entry.Date.ShouldBe(DateOnly.FromDateTime(Monday)),
            () => entry.Start.ShouldBeNull());

        void ItShouldLastOneRoundingIncrement() => entry.Minutes.ShouldBe(15);

        void ItShouldJoinTheirSubjectsUnderTheFriendlyName() => entry.Description.ShouldBe("Portal: Add a footer; Tidy the styles");

        void ItShouldUseTheProjectsCategory() => entry.CategoryId.ShouldBe(DefaultWorkspace.ClientDevelopment);

        void ItShouldFollowTheDaysLocation() => entry.Location.ShouldBeNull();

        void ItShouldRememberWhatItWasMadeFrom() => entry.Candidates.ShouldBe(["commit:a1", "commit:b2"]);
    }

    [Test]
    public void AProjectHasNoAgentWorkThisPeriod()
    {
        _config.Rules.Add(TestData.Heuristic("Portal work", "project", ConditionOperator.Equals, "portal-api", DefaultWorkspace.ClientDevelopment, scope: RuleScope.Agent));

        ManualEntry entry = CandidateEntries.Draft([Orphan("a1", Monday.AddHours(20), "Add a footer")], Build(), _config, TimeZoneInfo.Utc);

        ItShouldAskTheRulesForACategory();

        void ItShouldAskTheRulesForACategory() => entry.CategoryId.ShouldBe(DefaultWorkspace.ClientDevelopment);
    }

    [Test]
    public void OrphansAreFromDifferentDays()
    {
        Placement[] orphans = [Orphan("a1", Monday.AddHours(20), "One"), Orphan("b2", Monday.AddDays(1).AddHours(8), "Two")];

        bool combines = CandidateEntries.CanCombine(orphans, TimeZoneInfo.Utc, new Dictionary<string, string>());

        ItShouldNotCombineThem();

        void ItShouldNotCombineThem() => combines.ShouldBeFalse();
    }

    private PeriodTimesheet Build(params Activity[] activities) => new TimesheetBuilder().Build(November, _config, new Journal(), activities);

    private static Placement Orphan(string sha, DateTime at, string subject) => new()
    {
        Key = "commit:" + sha,
        Commit = new Commit { Sha = sha, Subject = subject, AuthoredAt = new DateTimeOffset(at, TimeSpan.Zero), ProjectKey = "portal-api" },
        Place = CandidatePlace.Unattributed,
    };
}
