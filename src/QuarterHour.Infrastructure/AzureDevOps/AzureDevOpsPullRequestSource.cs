using System.Text.Json;
using QuarterHour.Core.Model;
using QuarterHour.Core.Sources;
using QuarterHour.Infrastructure.Processes;

namespace QuarterHour.Infrastructure.AzureDevOps;

/// <summary>
/// Reads the user's pull requests from Azure DevOps with the Azure CLI. Every Azure DevOps remote of a project's folders
/// (origin, upstream, …) and its configured extra remotes is searched, because a fork's pull requests live in the
/// upstream repository. The creator is the signed-in Azure CLI account. A pull request found through two projects
/// belongs to the one whose remotes hold its source branch: the fork it came from, else the repository itself.
/// </summary>
public sealed class AzureDevOpsPullRequestSource : IPullRequestSource
{
    private const int MaxPullRequestsPerRepository = 100;
    private const int MaxCommitsPerPullRequest = 250;

    private readonly ICommandRunner _runner;
    private readonly string _git;
    private readonly string _az;

    public AzureDevOpsPullRequestSource()
        : this(new CommandLine(TimeSpan.FromSeconds(60)), "git", CommandLine.Find(OperatingSystem.IsWindows() ? "az.cmd" : "az"))
    {
    }

    internal AzureDevOpsPullRequestSource(ICommandRunner runner, string git, string az)
    {
        _runner = runner;
        _git = git;
        _az = az;
    }

    public async Task<PullRequestScanResult> ScanAsync(PullRequestScanRequest request, CancellationToken cancellationToken)
    {
        List<string> problems = [];
        Dictionary<string, ProjectRemotes> projects = [];
        foreach (ProjectFolders project in request.Projects)
        {
            projects[project.ProjectKey] = await RemotesAsync(project, problems, cancellationToken);
        }

        Dictionary<string, IReadOnlyList<string>> searched = projects
            .Where(pair => pair.Value.Repositories.Count > 0)
            .ToDictionary(pair => pair.Key, pair => (IReadOnlyList<string>)pair.Value.Repositories.Select(repository => repository.ToString()).ToList(), StringComparer.OrdinalIgnoreCase);
        List<AzureDevOpsRepository> repositories = projects.Values
            .SelectMany(project => project.Repositories)
            .DistinctBy(repository => repository.ToString().ToLowerInvariant())
            .ToList();
        if (repositories.Count == 0)
        {
            return new PullRequestScanResult([], searched, problems);
        }

        CommandResult account = await _runner.RunAsync(_az, null, ["account", "show", "--query", "user.name", "-o", "tsv"], cancellationToken);
        string creator = account.Output.Trim();
        if (!account.Succeeded || creator.Length == 0)
        {
            problems.Insert(0, AccountProblem(account));
            return new PullRequestScanResult([], searched, problems);
        }

        List<FoundPullRequest> found = [];
        object gate = new();
        await Parallel.ForEachAsync(repositories, new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = cancellationToken }, async (repository, token) =>
        {
            CommandResult list = await _runner.RunAsync(_az, null,
            [
                "repos", "pr", "list",
                "--org", repository.OrganizationUrl,
                "--project", repository.Project,
                "--repository", repository.Name,
                "--creator", creator,
                "--status", "all",
                "--top", MaxPullRequestsPerRepository.ToString(System.Globalization.CultureInfo.InvariantCulture),
                "-o", "json",
            ], token);
            lock (gate)
            {
                if (!list.Succeeded)
                {
                    problems.Add($"{repository}: {FirstLine(list.Error)}{ExtensionHint(list.Error)}");
                    return;
                }

                try
                {
                    found.AddRange(Parse(list.Output, repository).Where(pullRequest => pullRequest.CreatedAt >= request.From && pullRequest.CreatedAt < request.To));
                }
                catch (JsonException exception)
                {
                    problems.Add($"{repository}: the Azure CLI's answer couldn't be read ({exception.Message})");
                }
            }
        });

        List<PullRequest> pullRequests = [];
        foreach (FoundPullRequest pullRequest in found
                     .DistinctBy(pullRequest => (pullRequest.Repository.Organization.ToLowerInvariant(), pullRequest.Id))
                     .OrderBy(pullRequest => pullRequest.CreatedAt))
        {
            List<KeyValuePair<string, ProjectRemotes>> owners = projects
                .Where(pair => pair.Value.Repositories.Any(repository => repository.SameAs(pullRequest.Repository)))
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .ToList();
            if (owners.Count == 0)
            {
                continue;
            }

            KeyValuePair<string, ProjectRemotes> owner = owners.FirstOrDefault(pair => pair.Value.Repositories.Any(repository => pullRequest.Holds(repository)));
            if (owner.Key is null)
            {
                owner = owners[0];
            }

            pullRequests.Add(new PullRequest
            {
                Id = pullRequest.Id,
                Title = pullRequest.Title,
                CreatedAt = pullRequest.CreatedAt,
                ProjectKey = owner.Key,
                SourceBranch = pullRequest.SourceBranch,
                CommitShas = await CommitsAsync(owner.Value.Folders, pullRequest, cancellationToken),
            });
        }

        return new PullRequestScanResult(pullRequests, searched, problems);
    }

    private async Task<ProjectRemotes> RemotesAsync(ProjectFolders project, List<string> problems, CancellationToken cancellationToken)
    {
        List<string> folders = project.Folders.Where(Directory.Exists).ToList();
        List<string> urls = [.. project.ExtraRemotes ?? []];
        foreach (string folder in folders)
        {
            CommandResult remotes = await _runner.RunAsync(_git, null, ["-C", folder, "config", "--get-regexp", @"^remote\..*\.url$"], cancellationToken);
            if (remotes.Succeeded)
            {
                urls.AddRange(remotes.Output
                    .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(line => line.Split(' ', 2) is [_, string url] ? url : ""));
            }
        }

        foreach (string extra in project.ExtraRemotes ?? [])
        {
            if (AzureDevOpsRepository.TryParse(extra) is null)
            {
                problems.Add($"{project.ProjectKey}: {extra} isn't an Azure DevOps repository URL, so it was skipped.");
            }
        }

        List<AzureDevOpsRepository> repositories = urls
            .Select(AzureDevOpsRepository.TryParse)
            .OfType<AzureDevOpsRepository>()
            .DistinctBy(repository => repository.ToString().ToLowerInvariant())
            .ToList();
        return new ProjectRemotes(folders, repositories);
    }

    /// <summary>The pull request's own commits, from local history when it still has them; none otherwise.</summary>
    private async Task<IReadOnlyList<string>> CommitsAsync(IReadOnlyList<string> folders, FoundPullRequest pullRequest, CancellationToken cancellationToken)
    {
        if (pullRequest.SourceCommit is not { } source || pullRequest.TargetCommit is not { } target)
        {
            return [];
        }

        foreach (string folder in folders)
        {
            CommandResult commits = await _runner.RunAsync(_git, null,
                ["-C", folder, "rev-list", $"--max-count={MaxCommitsPerPullRequest}", source, "^" + target], cancellationToken);
            if (commits.Succeeded)
            {
                return commits.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            }
        }

        return [];
    }

    internal static IEnumerable<FoundPullRequest> Parse(string json, AzureDevOpsRepository repository)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (JsonElement element in document.RootElement.EnumerateArray())
        {
            if (!element.TryGetProperty("pullRequestId", out JsonElement id) || id.ValueKind != JsonValueKind.Number
                || Text(element, "creationDate") is not { } created || !DateTimeOffset.TryParse(created, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal, out DateTimeOffset createdAt))
            {
                continue;
            }

            string? forkRepository = element.TryGetProperty("forkSource", out JsonElement fork) && fork.ValueKind == JsonValueKind.Object
                ? Text(fork, "repository", "name")
                : null;
            string? forkProject = forkRepository is null ? null : Text(fork, "repository", "project", "name");
            yield return new FoundPullRequest(
                id.GetInt32(),
                Text(element, "title") ?? $"PR {id.GetInt32()}",
                createdAt,
                repository,
                Branch(Text(element, "sourceRefName")),
                forkRepository,
                forkProject,
                Text(element, "lastMergeSourceCommit", "commitId"),
                Text(element, "lastMergeTargetCommit", "commitId"));
        }
    }

    private static string? Branch(string? reference) =>
        reference is null ? null : reference.StartsWith("refs/heads/", StringComparison.Ordinal) ? reference["refs/heads/".Length..] : reference;

    private static string? Text(JsonElement element, params string[] path)
    {
        foreach (string name in path)
        {
            if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out element))
            {
                return null;
            }
        }

        return element.ValueKind == JsonValueKind.String ? element.GetString() : null;
    }

    private static string AccountProblem(CommandResult account)
    {
        if (account.Error.Contains("couldn't be started", StringComparison.Ordinal))
        {
            return "The Azure CLI (az) isn't installed or isn't on the PATH, so pull requests can't be read.";
        }

        return account.Error.Contains("az login", StringComparison.OrdinalIgnoreCase) || account.Succeeded
            ? "The Azure CLI isn't signed in, so pull requests can't be read. Run az login."
            : $"The Azure CLI failed, so pull requests can't be read: {FirstLine(account.Error)}";
    }

    private static string ExtensionHint(string error) =>
        error.Contains("misspelled or not recognized", StringComparison.OrdinalIgnoreCase)
            ? " (add the Azure DevOps extension with az extension add --name azure-devops)"
            : "";

    private static string FirstLine(string text) =>
        text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? "failed without saying why";

    private sealed record ProjectRemotes(IReadOnlyList<string> Folders, IReadOnlyList<AzureDevOpsRepository> Repositories);

    internal sealed record FoundPullRequest(
        int Id,
        string Title,
        DateTimeOffset CreatedAt,
        AzureDevOpsRepository Repository,
        string? SourceBranch,
        string? ForkRepository,
        string? ForkProject,
        string? SourceCommit,
        string? TargetCommit)
    {
        /// <summary>Whether the source branch lives in this repository: the fork it came from, else the target itself.</summary>
        public bool Holds(AzureDevOpsRepository candidate) =>
            ForkRepository is null
                ? candidate.SameAs(Repository)
                : string.Equals(candidate.Name, ForkRepository, StringComparison.OrdinalIgnoreCase)
                  && (ForkProject is null || string.Equals(candidate.Project, ForkProject, StringComparison.OrdinalIgnoreCase));
    }
}
