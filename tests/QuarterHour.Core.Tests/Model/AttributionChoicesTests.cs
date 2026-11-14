using QuarterHour.Core.Configuration;
using QuarterHour.Core.Model;

namespace QuarterHour.Core.Tests.Model;

public sealed class AttributionChoicesTests
{
    private static readonly TimeSpan IdleGap = TimeSpan.FromMinutes(15);
    private static readonly DateTime Monday = new(2026, 11, 9);

    [Test]
    public void AClaimedCommitIsMovedToAnotherBlock()
    {
        Activity morning = TestData.AgentBlock("s1", "portal", Monday.AddHours(9), 60) with { Claims = [new Claim(ClaimKind.Commit, "a1", Stamp(Monday.AddHours(9.5)))] };
        Activity afternoon = TestData.AgentBlock("s2", "portal", Monday.AddHours(14), 60);
        Journal journal = new() { Candidates = { ["commit:a1"] = new CandidateOverride { BlockId = afternoon.Id } } };

        AttributionResult result = Attribute([morning, afternoon], journal, Commit("a1", "portal", Monday.AddHours(9.5)));

        ItShouldDescribeTheChosenBlock();
        ItShouldSayTheUserMovedIt();

        void ItShouldDescribeTheChosenBlock()
        {
            result.Activities.Single(block => block.Id == afternoon.Id).Commits.ShouldHaveSingleItem();
            result.Activities.Single(block => block.Id == morning.Id).Commits.ShouldBeEmpty();
        }

        void ItShouldSayTheUserMovedIt() => result.Placements.Single().ShouldSatisfyAllConditions(
            placement => placement.Place.ShouldBe(CandidatePlace.Moved),
            placement => placement.BlockId.ShouldBe(afternoon.Id),
            placement => placement.Commit!.Claimed.ShouldBeFalse());
    }

    [Test]
    [Arguments(2, false)]
    [Arguments(1, true)]
    [Arguments(-1, true)]
    public void ACommitIsMovedToABlockDaysAway(int days, bool moves)
    {
        Activity today = TestData.AgentBlock("s1", "portal", Monday.AddHours(9), 60);
        Activity other = TestData.AgentBlock("s2", "portal", Monday.AddDays(days).AddHours(9), 60);
        Journal journal = new() { Candidates = { ["commit:a1"] = new CandidateOverride { BlockId = other.Id } } };

        AttributionResult result = Attribute([today, other], journal, Commit("a1", "portal", Monday.AddHours(9.5)));

        ItShouldOnlyMoveWithinADayEitherSide();

        void ItShouldOnlyMoveWithinADayEitherSide() => result.Placements.Single().BlockId.ShouldBe(moves ? other.Id : today.Id);
    }

    [Test]
    public void ACommitIsMovedToAnotherRepositoryOfTheSameProject()
    {
        Activity api = TestData.AgentBlock("s1", "portal-api", Monday.AddHours(9), 60);
        Activity web = TestData.AgentBlock("s2", "portal-web", Monday.AddHours(11), 60);
        Activity billing = TestData.AgentBlock("s3", "billing", Monday.AddHours(13), 60);
        Dictionary<string, string> names = new(StringComparer.OrdinalIgnoreCase) { ["portal-api"] = "Portal", ["portal-web"] = "Portal" };

        bool toWeb = CandidateAttribution.CanTake(web, "portal-api", Stamp(Monday.AddHours(9.5)), TimeZoneInfo.Utc, names);
        bool toBilling = CandidateAttribution.CanTake(billing, "portal-api", Stamp(Monday.AddHours(9.5)), TimeZoneInfo.Utc, names);

        ItShouldFollowTheFriendlyName();
        ItShouldNotCrossIntoOtherProjects();
        ItShouldNotNeedTheSameRepository();

        void ItShouldFollowTheFriendlyName() => toWeb.ShouldBeTrue();

        void ItShouldNotCrossIntoOtherProjects() => toBilling.ShouldBeFalse();

        void ItShouldNotNeedTheSameRepository() => CandidateAttribution.CanTake(api, "portal-api", Stamp(Monday.AddHours(9.5)), TimeZoneInfo.Utc).ShouldBeTrue();
    }

    [Test]
    public void APullRequestIsMarkedNotRelevant()
    {
        Activity block = TestData.AgentBlock("s1", "portal", Monday.AddHours(9), 60) with { Branch = "feature/sso" };
        Journal journal = new() { Candidates = { ["pr:482"] = new CandidateOverride { NotRelevant = true } } };
        PullRequest pullRequest = new() { Id = 482, Title = "Bump packages", CreatedAt = Stamp(Monday.AddHours(9.5)), ProjectKey = "portal", SourceBranch = "feature/sso" };

        AttributionResult result = CandidateAttribution.Attribute([block], [], IdleGap, TimeZoneInfo.Utc, [pullRequest], AttributionChoices.From(journal, Names()));

        ItShouldDescribeNothing();
        ItShouldNotBeAnOrphan();

        void ItShouldDescribeNothing() => result.Activities.Single().PullRequests.ShouldBeEmpty();

        void ItShouldNotBeAnOrphan() => result.ShouldSatisfyAllConditions(
            () => result.OrphanPullRequests.ShouldBeEmpty(),
            () => result.Placements.Single().Place.ShouldBe(CandidatePlace.NotRelevant));
    }

    [Test]
    public void AnOrphanBecameAManualEntry()
    {
        Journal journal = new() { ManualEntries = [new ManualEntry { Id = "e1", Date = DateOnly.FromDateTime(Monday), Candidates = ["commit:a1"] }] };
        Commit orphan = Commit("a1", "portal", Monday.AddHours(20));

        Placement converted = Attribute([], journal, orphan).Placements.Single();
        journal.ManualEntries.Clear();
        Placement afterDeleting = Attribute([], journal, orphan).Placements.Single();

        ItShouldLeaveTheUnattributedColumn();
        ItShouldComeBackWhenTheEntryIsDeleted();

        void ItShouldLeaveTheUnattributedColumn() => converted.ShouldSatisfyAllConditions(
            () => converted.Place.ShouldBe(CandidatePlace.InEntry),
            () => converted.EntryId.ShouldBe("e1"));

        void ItShouldComeBackWhenTheEntryIsDeleted() => afterDeleting.Place.ShouldBe(CandidatePlace.Unattributed);
    }

    [Test]
    public void TheBlockACommitWasMovedToIsGone()
    {
        Activity block = TestData.AgentBlock("s1", "portal", Monday.AddHours(9), 60);
        Journal journal = new() { Candidates = { ["commit:a1"] = new CandidateOverride { BlockId = "agent:copilot-cli:gone:20261109T090000" } } };

        Placement placement = Attribute([block], journal, Commit("a1", "portal", Monday.AddHours(9.5))).Placements.Single();

        ItShouldFallBackToTime();

        void ItShouldFallBackToTime() => placement.ShouldSatisfyAllConditions(
            () => placement.Place.ShouldBe(CandidatePlace.ByTime),
            () => placement.BlockId.ShouldBe(block.Id));
    }

    private static AttributionResult Attribute(IReadOnlyList<Activity> blocks, Journal journal, params Commit[] commits) =>
        CandidateAttribution.Attribute(blocks, commits, IdleGap, TimeZoneInfo.Utc, choices: AttributionChoices.From(journal, Names()));

    private static Dictionary<string, string> Names() => new(StringComparer.OrdinalIgnoreCase);

    private static DateTimeOffset Stamp(DateTime at) => new(at, TimeSpan.Zero);

    private static Commit Commit(string sha, string project, DateTime at) => new()
    {
        Sha = sha,
        Subject = "feat: add login",
        AuthoredAt = Stamp(at),
        ProjectKey = project,
    };
}
