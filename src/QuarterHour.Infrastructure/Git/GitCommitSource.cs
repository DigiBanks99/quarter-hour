using System.Globalization;
using QuarterHour.Core.Model;
using QuarterHour.Core.Sources;
using QuarterHour.Infrastructure.Processes;

namespace QuarterHour.Infrastructure.Git;

/// <summary>
/// Reads the user's commits from each project's local git history with the git command line. Every ref and the
/// reflog are read, so commits on branches deleted after a squash merge still count while the reflog keeps them.
/// The author is the repository's own user.email (plus any configured extra emails) and the time is the author date.
/// Worktrees and clones of one repository share its history, so each repository is read once per project.
/// </summary>
public sealed class GitCommitSource : ICommitSource
{
    private const char FieldSeparator = '\u001f';
    private const char RecordSeparator = '\u001e';
    private readonly string _git;
    private readonly ICommandRunner _runner = new CommandLine();

    public GitCommitSource(string git = "git")
    {
        _git = git;
    }

    public async Task<CommitScanResult> ScanAsync(CommitScanRequest request, CancellationToken cancellationToken)
    {
        List<Commit> commits = [];
        List<string> missing = [];
        List<string> problems = [];
        object gate = new();
        await Parallel.ForEachAsync(request.Projects, new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = cancellationToken }, async (project, token) =>
        {
            (IReadOnlyList<Commit> found, bool readable, IReadOnlyList<string> projectProblems) = await ScanProjectAsync(project, request, token);
            lock (gate)
            {
                commits.AddRange(found);
                problems.AddRange(projectProblems);
                if (!readable)
                {
                    missing.Add(project.ProjectKey);
                }
            }
        });

        return new CommitScanResult(
            commits.DistinctBy(commit => (commit.ProjectKey, commit.Sha)).OrderBy(commit => commit.AuthoredAt).ToList(),
            missing.Order(StringComparer.Ordinal).ToList(),
            problems);
    }

    private async Task<(IReadOnlyList<Commit> Commits, bool Readable, IReadOnlyList<string> Problems)> ScanProjectAsync(
        ProjectFolders project, CommitScanRequest request, CancellationToken cancellationToken)
    {
        List<Commit> commits = [];
        List<string> problems = [];
        HashSet<string> repositories = new(StringComparer.OrdinalIgnoreCase);
        foreach (string folder in project.Folders.Where(Directory.Exists))
        {
            CommandResult commonDirectory = await RunAsync(folder, cancellationToken, "rev-parse", "--path-format=absolute", "--git-common-dir");
            if (!commonDirectory.Succeeded || !repositories.Add(Path.GetFullPath(commonDirectory.Output.Trim())))
            {
                continue;
            }

            HashSet<string> emails = new(request.ExtraEmails, StringComparer.OrdinalIgnoreCase);
            CommandResult email = await RunAsync(folder, cancellationToken, "config", "user.email");
            if (email.Succeeded && email.Output.Trim() is { Length: > 0 } own)
            {
                emails.Add(own);
            }

            if (emails.Count == 0)
            {
                problems.Add($"{project.ProjectKey}: no user.email is set for {folder}, so its commits can't be told apart.");
                continue;
            }

            // --since filters on the committer date, which is never earlier than the author date, so nothing in range is lost.
            CommandResult log = await RunAsync(folder, cancellationToken, "log", "--all", "--reflog", "--no-merges",
                "--since=" + request.From.ToString("o", CultureInfo.InvariantCulture),
                "--format=%H%x1f%aI%x1f%ae%x1f%s%x1f%b%x1e");
            if (!log.Succeeded)
            {
                // A repository without any commits yet has nothing to read.
                if (!log.Error.Contains("does not have any commits", StringComparison.OrdinalIgnoreCase))
                {
                    problems.Add($"{project.ProjectKey}: git log failed in {folder}: {log.Error.Trim()}");
                }

                continue;
            }

            commits.AddRange(Parse(log.Output, project.ProjectKey)
                .Where(entry => emails.Contains(entry.Email) && entry.Commit.AuthoredAt >= request.From && entry.Commit.AuthoredAt < request.To)
                .Select(entry => entry.Commit));
        }

        return (commits, repositories.Count > 0, problems);
    }

    internal static IEnumerable<(Commit Commit, string Email)> Parse(string output, string projectKey)
    {
        foreach (string record in output.Split(RecordSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            string[] fields = record.Trim('\r', '\n').Split(FieldSeparator);
            if (fields.Length < 4 || !DateTimeOffset.TryParse(fields[1], CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTimeOffset authored))
            {
                continue;
            }

            string? body = fields.Length > 4 && fields[4].Trim() is { Length: > 0 } text ? text.ReplaceLineEndings("\n") : null;
            yield return (new Commit { Sha = fields[0], AuthoredAt = authored, Subject = fields[3], Body = body, ProjectKey = projectKey }, fields[2]);
        }
    }

    private Task<CommandResult> RunAsync(string folder, CancellationToken cancellationToken, params string[] arguments) =>
        _runner.RunAsync(_git, null, ["-C", folder, .. arguments], cancellationToken);
}
