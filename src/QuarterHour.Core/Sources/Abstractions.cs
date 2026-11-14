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
public sealed record ProjectFolders(string ProjectKey, IReadOnlyList<string> Folders);

public sealed record CommitScanRequest(DateTimeOffset From, DateTimeOffset To, IReadOnlyList<ProjectFolders> Projects, IReadOnlyList<string> ExtraEmails);

/// <param name="MissingProjects">Projects none of whose folders is a readable git repository any more.</param>
public sealed record CommitScanResult(IReadOnlyList<Commit> Commits, IReadOnlyList<string> MissingProjects, IReadOnlyList<string> Problems)
{
    public static CommitScanResult Empty { get; } = new([], [], []);
}

public sealed record AgentScanRequest(DateTimeOffset From, DateTimeOffset To, AgentSettings Settings, TimeZoneInfo TimeZone);

public sealed record AgentScanResult(string Source, IReadOnlyList<AgentSession> Sessions, IReadOnlyList<string> Locations, IReadOnlyList<string> Problems)
{
    public static AgentScanResult Empty(string source, IReadOnlyList<string> locations) => new(source, [], locations, []);
}
