using QuarterHour.Core.Model;
using QuarterHour.Core.Sources;

namespace QuarterHour.Infrastructure.Agents;

public sealed record AgentScanSummary(IReadOnlyList<AgentSession> Sessions, IReadOnlyList<AgentScanResult> Results, DateTimeOffset ScannedAt)
{
    public IReadOnlyList<string> Problems => [.. Results.SelectMany(result => result.Problems.Select(problem => $"{result.Source}: {problem}"))];
}

/// <summary>Runs every agent session source and merges their sessions, dropping duplicates.</summary>
public sealed class AgentSessionScanner
{
    private readonly IReadOnlyList<IAgentSessionSource> _sources;
    private readonly TimeProvider _time;

    public AgentSessionScanner(IEnumerable<IAgentSessionSource> sources, TimeProvider time)
    {
        _sources = [.. sources];
        _time = time;
    }

    public IReadOnlyList<IAgentSessionSource> Sources => _sources;

    public async Task<AgentScanSummary> ScanAsync(AgentScanRequest request, CancellationToken cancellationToken)
    {
        AgentScanResult[] results = await Task.WhenAll(_sources.Select(async source =>
        {
            try
            {
                return await source.ScanAsync(request, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                return new AgentScanResult(source.Name, [], [], [exception.Message]);
            }
        }));

        List<AgentSession> sessions = results
            .SelectMany(result => result.Sessions)
            .GroupBy(session => (session.Agent, session.SessionId))
            .Select(group => group.First())
            .ToList();

        return new AgentScanSummary(sessions, results, _time.GetUtcNow());
    }
}
