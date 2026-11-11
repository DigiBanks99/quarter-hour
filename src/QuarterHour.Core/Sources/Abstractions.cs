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

public sealed record AgentScanRequest(DateTimeOffset From, DateTimeOffset To, AgentSettings Settings, TimeZoneInfo TimeZone);

public sealed record AgentScanResult(string Source, IReadOnlyList<AgentSession> Sessions, IReadOnlyList<string> Locations, IReadOnlyList<string> Problems)
{
    public static AgentScanResult Empty(string source, IReadOnlyList<string> locations) => new(source, [], locations, []);
}
