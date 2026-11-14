using QuarterHour.Core.Model;

namespace QuarterHour.Core.Tests.Model;

public sealed class CandidateAttributionTests
{
    private static readonly TimeSpan IdleGap = TimeSpan.FromMinutes(15);
    private static readonly DateTime Monday = new(2026, 11, 9);

    [Test]
    public void ACommitIsMadeDuringABlock()
    {
        Activity block = TestData.AgentBlock("s1", "portal", Monday.AddHours(9), 60);

        AttributionResult result = Attribute([block], Commit("a1", "portal", Monday.AddHours(9).AddMinutes(30)));

        ItShouldDescribeThatBlock();

        void ItShouldDescribeThatBlock() => result.Activities.Single().Commits.Select(commit => commit.Sha).ShouldBe(["a1"]);
    }

    [Test]
    public void ACommitIsMadeShortlyAfterTheBlockEnds()
    {
        Activity block = TestData.AgentBlock("s1", "portal", Monday.AddHours(9), 60);

        AttributionResult result = Attribute([block], Commit("a1", "portal", Monday.AddHours(10).AddMinutes(10)));

        ItShouldStillBelongToTheBlock();

        void ItShouldStillBelongToTheBlock() => result.Activities.Single().Commits.ShouldHaveSingleItem();
    }

    [Test]
    public void ACommitIsMadeLongAfterAnyBlock()
    {
        Activity block = TestData.AgentBlock("s1", "portal", Monday.AddHours(9), 60);

        AttributionResult result = Attribute([block], Commit("a1", "portal", Monday.AddHours(14)));

        ItShouldBeAnOrphan();
        ItShouldNotDescribeTheBlock();

        void ItShouldBeAnOrphan() => result.Orphans.Select(commit => commit.Sha).ShouldBe(["a1"]);

        void ItShouldNotDescribeTheBlock() => result.Activities.Single().Commits.ShouldBeEmpty();
    }

    [Test]
    public void ACommitIsMadeInAnotherProject()
    {
        Activity block = TestData.AgentBlock("s1", "portal", Monday.AddHours(9), 60);

        AttributionResult result = Attribute([block], Commit("a1", "billing", Monday.AddHours(9).AddMinutes(30)));

        ItShouldBeAnOrphan();

        void ItShouldBeAnOrphan() => result.Orphans.ShouldHaveSingleItem();
    }

    [Test]
    public void TwoBlocksCouldOwnTheCommit()
    {
        Activity earlier = TestData.AgentBlock("s1", "portal", Monday.AddHours(9), 30);
        Activity active = TestData.AgentBlock("s2", "portal", Monday.AddHours(9).AddMinutes(20), 60, agent: "pi");

        AttributionResult result = Attribute([earlier, active], Commit("a1", "portal", Monday.AddHours(9).AddMinutes(40)));

        ItShouldGoToTheBlockActiveAtTheTime();

        void ItShouldGoToTheBlockActiveAtTheTime() =>
            result.Activities.Single(activity => activity.Commits.Count > 0).Id.ShouldBe(active.Id);
    }

    [Test]
    public void TheCommitIsAFixup()
    {
        Activity block = TestData.AgentBlock("s1", "portal", Monday.AddHours(9), 60);

        AttributionResult result = Attribute([block], Commit("a1", "portal", Monday.AddHours(9).AddMinutes(30), "fixup! feat: add login"));

        ItShouldBeIgnoredEntirely();

        void ItShouldBeIgnoredEntirely()
        {
            result.Activities.Single().Commits.ShouldBeEmpty();
            result.Orphans.ShouldBeEmpty();
        }
    }

    [Test]
    public void APullRequestIsRaisedLaterFromTheBlocksBranch()
    {
        Activity morning = TestData.AgentBlock("s1", "portal", Monday.AddHours(9), 60) with { Branch = "feature/sso" };
        Activity afternoon = TestData.AgentBlock("s2", "portal", Monday.AddHours(15), 30) with { Branch = "main" };

        AttributionResult result = AttributePullRequests([morning, afternoon], PullRequest(482, "portal", Monday.AddHours(15).AddMinutes(10), "feature/sso"));

        ItShouldGoToTheBlockOnThatBranch();

        void ItShouldGoToTheBlockOnThatBranch() =>
            result.Activities.Single(activity => activity.PullRequests.Count > 0).Id.ShouldBe(morning.Id);
    }

    [Test]
    public void APullRequestIsRaisedFromABranchNoBlockWorkedOn()
    {
        Activity block = TestData.AgentBlock("s1", "portal", Monday.AddHours(9), 60) with { Branch = "main" };

        AttributionResult result = AttributePullRequests([block],
            PullRequest(482, "portal", Monday.AddHours(9).AddMinutes(50), "feature/sso"),
            PullRequest(483, "portal", Monday.AddHours(13), "feature/other"));

        ItShouldGoToTheBlockActiveAtTheTime();
        ItShouldLeaveTheLaterOneAnOrphan();

        void ItShouldGoToTheBlockActiveAtTheTime() => result.Activities.Single().PullRequests.Select(pullRequest => pullRequest.Id).ShouldBe([482]);

        void ItShouldLeaveTheLaterOneAnOrphan() => result.OrphanPullRequests.Select(pullRequest => pullRequest.Id).ShouldBe([483]);
    }

    [Test]
    public void ABlockRaisedAPullRequestAndMadeOtherCommits()
    {
        Activity block = TestData.AgentBlock("s1", "portal", Monday.AddHours(9), 60) with
        {
            Commits = [Commit("a1", "portal", Monday.AddHours(9).AddMinutes(10), "feat: add sso login"), Commit("a2", "portal", Monday.AddHours(9).AddMinutes(40), "fix: login redirect")],
            PullRequests = [PullRequest(482, "portal", Monday.AddHours(9).AddMinutes(30), "feature/sso", title: "feat: add SSO login", commits: ["a1"])],
        };

        string description = AgentWork.Describe(block);

        ItShouldNameThePullRequestFirstAndNotRepeatItsCommits();

        void ItShouldNameThePullRequestFirstAndNotRepeatItsCommits() => description.ShouldBe("Add SSO login (PR 482); Login redirect");
    }

    [Test]
    public void TwoSessionsWorkInTheRepositoryAtOnce()
    {
        Activity mine = TestData.AgentBlock("s1", "portal", Monday.AddHours(9), 60) with
        {
            Claims = [new Claim(ClaimKind.Commit, "a1b2c3d", Stamp(Monday.AddHours(9).AddMinutes(30)))],
        };
        Activity other = TestData.AgentBlock("s2", "portal", Monday.AddHours(9).AddMinutes(20), 60, agent: "pi");

        AttributionResult result = Attribute([mine, other], Commit("a1b2c3d4e5f6", "portal", Monday.AddHours(9).AddMinutes(30)));

        ItShouldGoToTheSessionThatMadeIt();
        ItShouldSayItWasClaimed();

        void ItShouldGoToTheSessionThatMadeIt() =>
            result.Activities.Single(activity => activity.Commits.Count > 0).Id.ShouldBe(mine.Id);

        void ItShouldSayItWasClaimed() => result.Activities.SelectMany(activity => activity.Commits).Single().Claimed.ShouldBeTrue();
    }

    [Test]
    public void ALaterSessionListsAPullRequest()
    {
        Activity creator = TestData.AgentBlock("s1", "portal", Monday.AddHours(9), 60) with { Branch = "feature/sso" };
        Activity reviewer = TestData.AgentBlock("s2", "portal", Monday.AddHours(15), 30, agent: "pi") with
        {
            Claims = [new Claim(ClaimKind.PullRequest, "482", Stamp(Monday.AddHours(15).AddMinutes(5)))],
        };

        AttributionResult result = AttributePullRequests([creator, reviewer], PullRequest(482, "portal", Monday.AddHours(9).AddMinutes(50), "feature/sso"));

        ItShouldNotCountAsAClaim();

        void ItShouldNotCountAsAClaim() =>
            result.Activities.Single(activity => activity.PullRequests.Count > 0).ShouldSatisfyAllConditions(
                activity => activity.Id.ShouldBe(creator.Id),
                activity => activity.PullRequests.Single().Claimed.ShouldBeFalse());
    }

    [Test]
    [Arguments("feat(ui): follow the browser colour scheme", "Follow the browser colour scheme")]
    [Arguments("fix!: drop the old export", "Drop the old export")]
    [Arguments("Add SSO login", "Add SSO login")]
    [Arguments("chore:", "chore:")]
    public void ACommitSubjectIsCleaned(string subject, string expected)
    {
        string cleaned = CommitSubjects.Clean(subject);

        ItShouldReadLikeATimesheetEntry();

        void ItShouldReadLikeATimesheetEntry() => cleaned.ShouldBe(expected);
    }

    private static AttributionResult Attribute(IReadOnlyList<Activity> blocks, params Commit[] commits) =>
        CandidateAttribution.Attribute(blocks, commits, IdleGap, TimeZoneInfo.Utc);

    private static AttributionResult AttributePullRequests(IReadOnlyList<Activity> blocks, params PullRequest[] pullRequests) =>
        CandidateAttribution.Attribute(blocks, [], IdleGap, TimeZoneInfo.Utc, pullRequests);

    private static PullRequest PullRequest(int id, string project, DateTime at, string branch, string title = "Add SSO login", IReadOnlyList<string>? commits = null) => new()
    {
        Id = id,
        Title = title,
        CreatedAt = new DateTimeOffset(at, TimeSpan.Zero),
        ProjectKey = project,
        SourceBranch = branch,
        CommitShas = commits ?? [],
    };

    private static DateTimeOffset Stamp(DateTime at) => new(at, TimeSpan.Zero);

    private static Commit Commit(string sha, string project, DateTime at, string subject = "feat: add login") => new()
    {
        Sha = sha,
        Subject = subject,
        AuthoredAt = new DateTimeOffset(at, TimeSpan.Zero),
        ProjectKey = project,
    };
}
