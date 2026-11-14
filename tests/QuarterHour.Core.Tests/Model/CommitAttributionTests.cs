using QuarterHour.Core.Model;

namespace QuarterHour.Core.Tests.Model;

public sealed class CommitAttributionTests
{
    private static readonly TimeSpan IdleGap = TimeSpan.FromMinutes(15);
    private static readonly DateTime Monday = new(2026, 11, 9);

    [Test]
    public void ACommitIsMadeDuringABlock()
    {
        Activity block = TestData.AgentBlock("s1", "portal", Monday.AddHours(9), 60);

        CommitAttributionResult result = Attribute([block], Commit("a1", "portal", Monday.AddHours(9).AddMinutes(30)));

        ItShouldDescribeThatBlock();

        void ItShouldDescribeThatBlock() => result.Activities.Single().Commits.Select(commit => commit.Sha).ShouldBe(["a1"]);
    }

    [Test]
    public void ACommitIsMadeShortlyAfterTheBlockEnds()
    {
        Activity block = TestData.AgentBlock("s1", "portal", Monday.AddHours(9), 60);

        CommitAttributionResult result = Attribute([block], Commit("a1", "portal", Monday.AddHours(10).AddMinutes(10)));

        ItShouldStillBelongToTheBlock();

        void ItShouldStillBelongToTheBlock() => result.Activities.Single().Commits.ShouldHaveSingleItem();
    }

    [Test]
    public void ACommitIsMadeLongAfterAnyBlock()
    {
        Activity block = TestData.AgentBlock("s1", "portal", Monday.AddHours(9), 60);

        CommitAttributionResult result = Attribute([block], Commit("a1", "portal", Monday.AddHours(14)));

        ItShouldBeAnOrphan();
        ItShouldNotDescribeTheBlock();

        void ItShouldBeAnOrphan() => result.Orphans.Select(commit => commit.Sha).ShouldBe(["a1"]);

        void ItShouldNotDescribeTheBlock() => result.Activities.Single().Commits.ShouldBeEmpty();
    }

    [Test]
    public void ACommitIsMadeInAnotherProject()
    {
        Activity block = TestData.AgentBlock("s1", "portal", Monday.AddHours(9), 60);

        CommitAttributionResult result = Attribute([block], Commit("a1", "billing", Monday.AddHours(9).AddMinutes(30)));

        ItShouldBeAnOrphan();

        void ItShouldBeAnOrphan() => result.Orphans.ShouldHaveSingleItem();
    }

    [Test]
    public void TwoBlocksCouldOwnTheCommit()
    {
        Activity earlier = TestData.AgentBlock("s1", "portal", Monday.AddHours(9), 30);
        Activity active = TestData.AgentBlock("s2", "portal", Monday.AddHours(9).AddMinutes(20), 60, agent: "pi");

        CommitAttributionResult result = Attribute([earlier, active], Commit("a1", "portal", Monday.AddHours(9).AddMinutes(40)));

        ItShouldGoToTheBlockActiveAtTheTime();

        void ItShouldGoToTheBlockActiveAtTheTime() =>
            result.Activities.Single(activity => activity.Commits.Count > 0).Id.ShouldBe(active.Id);
    }

    [Test]
    public void TheCommitIsAFixup()
    {
        Activity block = TestData.AgentBlock("s1", "portal", Monday.AddHours(9), 60);

        CommitAttributionResult result = Attribute([block], Commit("a1", "portal", Monday.AddHours(9).AddMinutes(30), "fixup! feat: add login"));

        ItShouldBeIgnoredEntirely();

        void ItShouldBeIgnoredEntirely()
        {
            result.Activities.Single().Commits.ShouldBeEmpty();
            result.Orphans.ShouldBeEmpty();
        }
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

    private static CommitAttributionResult Attribute(IReadOnlyList<Activity> blocks, params Commit[] commits) =>
        CommitAttribution.Attribute(blocks, commits, IdleGap, TimeZoneInfo.Utc);

    private static Commit Commit(string sha, string project, DateTime at, string subject = "feat: add login") => new()
    {
        Sha = sha,
        Subject = subject,
        AuthoredAt = new DateTimeOffset(at, TimeSpan.Zero),
        ProjectKey = project,
    };
}
