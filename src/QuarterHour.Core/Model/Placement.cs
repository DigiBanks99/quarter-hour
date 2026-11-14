namespace QuarterHour.Core.Model;

/// <summary>Where a candidate ended up.</summary>
public enum CandidatePlace
{
    /// <summary>The block's transcript shows it made the candidate.</summary>
    Claimed,

    /// <summary>The block was working in the project when the candidate was made.</summary>
    ByTime,

    /// <summary>The user moved it to the block.</summary>
    Moved,

    /// <summary>No block could take it: an orphan.</summary>
    Unattributed,

    /// <summary>The user said it describes nothing.</summary>
    NotRelevant,

    /// <summary>The user made it into a manual entry.</summary>
    InEntry,
}

/// <summary>A commit or pull request and where attribution put it.</summary>
public sealed record Placement
{
    /// <summary>See <see cref="CandidateKeys"/>.</summary>
    public required string Key { get; init; }

    public Commit? Commit { get; init; }
    public PullRequest? PullRequest { get; init; }
    public required CandidatePlace Place { get; init; }

    /// <summary>The block of agent work it describes, when it describes one.</summary>
    public string? BlockId { get; init; }

    /// <summary>The manual entry made from it, when <see cref="Place"/> is <see cref="CandidatePlace.InEntry"/>.</summary>
    public string? EntryId { get; init; }

    public string ProjectKey => Commit?.ProjectKey ?? PullRequest!.ProjectKey;
    public DateTimeOffset At => Commit?.AuthoredAt ?? PullRequest!.CreatedAt;

    /// <summary>"Fix login redirect", or "Add SSO login (PR 482)".</summary>
    public string Text => Commit is not null ? CommitSubjects.Clean(Commit.Subject) : PullRequest!.Describe();

    /// <summary>"3c4d5e6", or "PR 482".</summary>
    public string Reference => Commit?.ShortSha ?? $"PR {PullRequest!.Id}";

    /// <summary>Everything there is to say about it: the commit's whole message and SHA, or the pull request's title and link.</summary>
    public string Details => Commit is { } commit
        ? $"{commit.Subject}{(commit.Body is { Length: > 0 } body ? "\n\n" + body : "")}\n\n{commit.Sha}"
        : $"{PullRequest!.Title}{(PullRequest.Url is { } url ? "\n\n" + url : "")}";

    public bool IsUnattributed => Place == CandidatePlace.Unattributed;

    /// <summary>The local day it was made.</summary>
    public DateOnly Day(TimeZoneInfo timeZone) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(At, timeZone).DateTime);
}

/// <summary>How candidates are named in the journal: "commit:&lt;sha&gt;" and "pr:&lt;id&gt;".</summary>
public static class CandidateKeys
{
    public static string Of(Commit commit) => "commit:" + commit.Sha.ToLowerInvariant();

    public static string Of(PullRequest pullRequest) => "pr:" + pullRequest.Id.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
