using System.Diagnostics;
using System.Globalization;
using System.Text;
using QuarterHour.Core.Model;
using QuarterHour.Core.Sources;

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
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    private readonly string _git;

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
            GitResult commonDirectory = await RunAsync(folder, cancellationToken, "rev-parse", "--path-format=absolute", "--git-common-dir");
            if (!commonDirectory.Succeeded || !repositories.Add(Path.GetFullPath(commonDirectory.Output.Trim())))
            {
                continue;
            }

            HashSet<string> emails = new(request.ExtraEmails, StringComparer.OrdinalIgnoreCase);
            GitResult email = await RunAsync(folder, cancellationToken, "config", "user.email");
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
            GitResult log = await RunAsync(folder, cancellationToken, "log", "--all", "--reflog", "--no-merges",
                "--since=" + request.From.ToString("o", CultureInfo.InvariantCulture),
                "--format=%H%x1f%aI%x1f%ae%x1f%s%x1e");
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

            yield return (new Commit { Sha = fields[0], AuthoredAt = authored, Subject = fields[3], ProjectKey = projectKey }, fields[2]);
        }
    }

    private async Task<GitResult> RunAsync(string folder, CancellationToken cancellationToken, params string[] arguments)
    {
        ProcessStartInfo start = new(_git)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        start.ArgumentList.Add("-C");
        start.ArgumentList.Add(folder);
        foreach (string argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        // Never wait on a credential or pager prompt.
        start.Environment["GIT_TERMINAL_PROMPT"] = "0";
        start.Environment["GIT_PAGER"] = "cat";

        using Process process = new() { StartInfo = start };
        try
        {
            process.Start();
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return new GitResult(false, "", $"git couldn't be started: {exception.Message}");
        }

        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Timeout);
        Task<string> output = process.StandardOutput.ReadToEndAsync(timeout.Token);
        Task<string> error = process.StandardError.ReadToEndAsync(timeout.Token);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
            return new GitResult(process.ExitCode == 0, await output, await error);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            process.Kill(entireProcessTree: true);
            return new GitResult(false, "", $"git took longer than {Timeout.TotalSeconds:0} seconds.");
        }
    }

    private sealed record GitResult(bool Succeeded, string Output, string Error);
}
