using System.Diagnostics;
using QuarterHour.Core.Model;
using QuarterHour.Core.Sources;
using QuarterHour.Infrastructure.Git;

namespace QuarterHour.IntegrationTests.Git;

public sealed class GitCommitSourceTests : IDisposable
{
    private static readonly DateTimeOffset November = new(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset December = new(2026, 12, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly TemporaryDirectory _root = new();

    [Test]
    public async Task AProjectHasCommitsBySeveralPeople()
    {
        string repository = Repository("portal");
        Commit(repository, "feat: add login", "2026-11-09T09:30:00+02:00");
        Commit(repository, "fix: their change", "2026-11-09T10:00:00+02:00", email: "someone@example.com");
        Commit(repository, "chore: last month", "2026-10-30T10:00:00+02:00");
        Commit(repository, "fix: as my other self", "2026-11-10T10:00:00+02:00", email: "me@client.example");
        string worktree = Path.Combine(_root.Path, "portal-wt");
        Git(repository, "worktree", "add", "-q", worktree, "-b", "wt");

        CommitScanResult result = await Scan(new ProjectFolders("portal", [repository, worktree]), extraEmails: ["ME@client.example"]);

        ItShouldReadOnlyMyCommitsInThePeriod();
        ItShouldUseTheAuthorDate();

        void ItShouldReadOnlyMyCommitsInThePeriod() =>
            result.Commits.Select(commit => commit.Subject).ShouldBe(["feat: add login", "fix: as my other self"]);

        void ItShouldUseTheAuthorDate() => result.Commits[0].AuthoredAt.ShouldBe(new DateTimeOffset(2026, 11, 9, 9, 30, 0, TimeSpan.FromHours(2)));
    }

    [Test]
    public async Task ABranchWasDeletedAfterASquashMerge()
    {
        string repository = Repository("portal");
        Commit(repository, "chore: start", "2026-11-02T08:00:00+02:00");
        Git(repository, "switch", "-q", "-c", "feature/login");
        Commit(repository, "feat: add login", "2026-11-09T09:30:00+02:00");
        Git(repository, "switch", "-q", "-");
        Git(repository, "branch", "-q", "-D", "feature/login");

        CommitScanResult result = await Scan(new ProjectFolders("portal", [repository]));

        ItShouldStillFindTheCommitInTheReflog();

        void ItShouldStillFindTheCommitInTheReflog() => result.Commits.Select(commit => commit.Subject).ShouldContain("feat: add login");
    }

    [Test]
    public async Task AProjectFolderIsGone()
    {
        CommitScanResult result = await Scan(new ProjectFolders("portal", [Path.Combine(_root.Path, "deleted")]));

        ItShouldReportTheProjectAsMissing();

        void ItShouldReportTheProjectAsMissing() => result.MissingProjects.ShouldBe(["portal"]);
    }

    private static Task<CommitScanResult> Scan(ProjectFolders project, IReadOnlyList<string>? extraEmails = null) =>
        new GitCommitSource().ScanAsync(new CommitScanRequest(November, December, [project], extraEmails ?? []), CancellationToken.None);

    private string Repository(string name)
    {
        string folder = _root.Folder(name);
        Git(folder, "init", "-q", "-b", "main");
        Git(folder, "config", "user.email", "me@example.com");
        Git(folder, "config", "user.name", "Me");
        Git(folder, "config", "commit.gpgsign", "false");
        return folder;
    }

    private static void Commit(string folder, string subject, string date, string email = "me@example.com") =>
        Git(folder, new Dictionary<string, string>
        {
            ["GIT_AUTHOR_DATE"] = date,
            ["GIT_COMMITTER_DATE"] = date,
            ["GIT_AUTHOR_EMAIL"] = email,
            ["GIT_COMMITTER_EMAIL"] = email,
        }, "commit", "-q", "--allow-empty", "-m", subject);

    private static void Git(string folder, params string[] arguments) => Git(folder, [], arguments);

    private static void Git(string folder, Dictionary<string, string> environment, params string[] arguments)
    {
        ProcessStartInfo start = new("git") { RedirectStandardError = true, RedirectStandardOutput = true, UseShellExecute = false };
        start.ArgumentList.Add("-C");
        start.ArgumentList.Add(folder);
        foreach (string argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        // Keep the developer's global hooks and templates out of the test repositories.
        start.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
        start.Environment["GIT_CONFIG_GLOBAL"] = Path.Combine(Path.GetTempPath(), "quarter-hour-tests", "empty.gitconfig");
        foreach ((string key, string value) in environment)
        {
            start.Environment[key] = value;
        }

        using Process process = Process.Start(start)!;
        string error = process.StandardError.ReadToEnd();
        process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        process.ExitCode.ShouldBe(0, $"git {string.Join(' ', arguments)} failed: {error}");
    }

    public void Dispose() => _root.Dispose();
}
