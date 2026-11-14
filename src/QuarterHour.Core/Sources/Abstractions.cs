using QuarterHour.Core.Configuration;
using QuarterHour.Core.Model;

namespace QuarterHour.Core.Sources;

/// <summary>Reads calendar event occurrences for a time range.</summary>
public interface ICalendarProvider
{
    CalendarProviderKind Kind { get; }

    Task<IReadOnlyList<CalendarEvent>> GetEventsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);
}

/// <summary>Reads AI agent sessions that had activity in a time range.</summary>
public interface IAgentSessionSource
{
    /// <summary>A display name, such as "Claude Code" or "AgentsView".</summary>
    string Name { get; }

    Task<AgentScanResult> ScanAsync(AgentScanRequest request, CancellationToken cancellationToken);
}

/// <summary>Reads the user's commits in the projects agents worked in.</summary>
public interface ICommitSource
{
    Task<CommitScanResult> ScanAsync(CommitScanRequest request, CancellationToken cancellationToken);
}

/// <summary>The folders a project was worked in; any of them leads to the same repository history.</summary>
/// <param name="ExtraRemotes">Repository URLs to search for pull requests besides the folders' own git remotes.</param>
public sealed record ProjectFolders(string ProjectKey, IReadOnlyList<string> Folders, IReadOnlyList<string>? ExtraRemotes = null);

public sealed record CommitScanRequest(DateTimeOffset From, DateTimeOffset To, IReadOnlyList<ProjectFolders> Projects, IReadOnlyList<string> ExtraEmails);

/// <param name="MissingProjects">Projects none of whose folders is a readable git repository any more.</param>
public sealed record CommitScanResult(IReadOnlyList<Commit> Commits, IReadOnlyList<string> MissingProjects, IReadOnlyList<string> Problems)
{
    public static CommitScanResult Empty { get; } = new([], [], []);
}

/// <summary>Reads the pull requests the user created in the projects' remote repositories.</summary>
public interface IPullRequestSource
{
    Task<PullRequestScanResult> ScanAsync(PullRequestScanRequest request, CancellationToken cancellationToken);
}

public sealed record PullRequestScanRequest(DateTimeOffset From, DateTimeOffset To, IReadOnlyList<ProjectFolders> Projects);

/// <param name="Repositories">Per project key, the remote repositories that were searched, such as "contoso/Portal/portal-api".</param>
/// <param name="Problems">Remotes that couldn't be read, or why nothing could be (such as the Azure CLI not being signed in).</param>
public sealed record PullRequestScanResult(
    IReadOnlyList<PullRequest> PullRequests,
    IReadOnlyDictionary<string, IReadOnlyList<string>> Repositories,
    IReadOnlyList<string> Problems)
{
    public static PullRequestScanResult Empty { get; } = new([], new Dictionary<string, IReadOnlyList<string>>(), []);

    public static PullRequestScanResult Failed(string problem) => new([], new Dictionary<string, IReadOnlyList<string>>(), [problem]);
}

public sealed record AgentScanRequest(DateTimeOffset From, DateTimeOffset To, AgentSettings Settings, TimeZoneInfo TimeZone);

public sealed record AgentScanResult(string Source, IReadOnlyList<AgentSession> Sessions, IReadOnlyList<string> Locations, IReadOnlyList<string> Problems)
{
    public static AgentScanResult Empty(string source, IReadOnlyList<string> locations) => new(source, [], locations, []);
}
