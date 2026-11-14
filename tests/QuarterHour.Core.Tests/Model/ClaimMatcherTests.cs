using QuarterHour.Core.Model;

namespace QuarterHour.Core.Tests.Model;

public sealed class ClaimMatcherTests
{
    private static readonly DateTimeOffset At = new(2026, 11, 9, 9, 30, 0, TimeSpan.Zero);

    [Test]
    [Arguments("""{"success":true,"result":{"content":"[feat/321-design-system 907773d] feat(lib): add chart palette\n 2 files changed"}}""", ClaimKind.Commit, "907773d")]
    [Arguments("[main (root-commit) 1a2b3c4d] chore: start", ClaimKind.Commit, "1a2b3c4d")]
    [Arguments("[detached HEAD abcdef1] fix: hotfix", ClaimKind.Commit, "abcdef1")]
    [Arguments("""{"pullRequestId": 482, "status": "active"}""", ClaimKind.PullRequest, "482")]
    [Arguments("""{"content":"{\"pullRequestId\":482,\"title\":\"Add SSO login\"}"}""", ClaimKind.PullRequest, "482")]
    [Arguments("Opened https://dev.azure.com/contoso/Portal/_git/portal/pullrequest/482 for review", ClaimKind.PullRequest, "482")]
    public void ToolOutputShowsTheSessionMadeSomething(string output, ClaimKind kind, string value)
    {
        IReadOnlyList<Claim> claims = ClaimMatcher.Find(output, At);

        ItShouldClaimIt();

        void ItShouldClaimIt() => claims.ShouldBe([new Claim(kind, value, At)]);
    }

    [Test]
    [Arguments("[1, 2] and [3, 4] ")]
    [Arguments("commit 907773d0 was mentioned in passing")]
    [Arguments("See https://github.com/someone/portal/pull/482")]
    public void ToolOutputOnlyMentionsSomething(string output)
    {
        IReadOnlyList<Claim> claims = ClaimMatcher.Find(output, At);

        ItShouldClaimNothing();

        void ItShouldClaimNothing() => claims.ShouldBeEmpty();
    }

    [Test]
    public void ALineHasNoMarkers()
    {
        bool might = ClaimMatcher.MightClaim("""{"type":"tool.execution_complete","data":{"result":{"content":"ok"}}}"""u8);

        ItShouldBeSkippedWithoutDecoding();

        void ItShouldBeSkippedWithoutDecoding() => might.ShouldBeFalse();
    }
}
