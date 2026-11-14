namespace QuarterHour.Core.Model;

/// <summary>A pull request the user created for a project: a candidate for describing a block of agent work.</summary>
public sealed record PullRequest
{
    public required int Id { get; init; }
    public required string Title { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>The project the pull request was found for, keyed like its agent group (repository name, lower case).</summary>
    public required string ProjectKey { get; init; }

    /// <summary>The branch the pull request merges from, without "refs/heads/".</summary>
    public string? SourceBranch { get; init; }

    /// <summary>The commits the pull request is made of, so a description doesn't repeat them after its title.</summary>
    public IReadOnlyList<string> CommitShas { get; init; } = [];

    /// <summary>"Add SSO login (PR 482)".</summary>
    public string Describe() => $"{CommitSubjects.Clean(Title)} (PR {Id})";
}
