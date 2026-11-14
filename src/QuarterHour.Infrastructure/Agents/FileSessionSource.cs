using System.Collections.Concurrent;
using QuarterHour.Core.Configuration;
using QuarterHour.Core.Model;
using QuarterHour.Core.Sources;

namespace QuarterHour.Infrastructure.Agents;

/// <summary>
/// Base for agents that keep sessions as files. Only files written to since the start of the range are read,
/// and parsed files are remembered (by size and modification time) so rescans are quick.
/// </summary>
public abstract class FileSessionSource : IAgentSessionSource
{
    private readonly ConcurrentDictionary<string, (long Length, DateTime Modified, AgentSession? Session)> _parsed = new(StringComparer.OrdinalIgnoreCase);

    public abstract AgentKind Kind { get; }
    public abstract string Name { get; }

    /// <summary>The agent's usual session locations on this machine.</summary>
    public abstract IReadOnlyList<string> DefaultLocations();

    public Task<AgentScanResult> ScanAsync(AgentScanRequest request, CancellationToken cancellationToken)
    {
        AgentScannerSettings? settings = request.Settings.Scanners.FirstOrDefault(scanner => scanner.Kind == Kind);
        IReadOnlyList<string> locations = settings is { Paths.Count: > 0 } ? settings.Paths : DefaultLocations();
        if (settings is { Enabled: false })
        {
            return Task.FromResult(AgentScanResult.Empty(Name, locations));
        }

        return Task.Run(() => Scan(request, locations, cancellationToken), cancellationToken);
    }

    private AgentScanResult Scan(AgentScanRequest request, IReadOnlyList<string> locations, CancellationToken cancellationToken)
    {
        List<AgentSession> sessions = [];
        List<string> problems = [];
        DateTime modifiedSince = request.From.UtcDateTime.AddDays(-1);
        foreach (string location in locations.Select(Environment.ExpandEnvironmentVariables).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!Directory.Exists(location) && !File.Exists(location))
            {
                continue;
            }

            try
            {
                foreach (AgentSession session in ReadLocation(location, modifiedSince, request, cancellationToken))
                {
                    AgentSession? trimmed = Trim(session, request.From, request.To);
                    if (trimmed is not null)
                    {
                        sessions.Add(trimmed);
                    }
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                problems.Add($"{location}: {exception.Message}");
            }
        }

        return new AgentScanResult(Name, sessions, locations, problems);
    }

    /// <summary>Reads the sessions in one location. Files older than <paramref name="modifiedSince"/> can be skipped.</summary>
    protected abstract IEnumerable<AgentSession> ReadLocation(string location, DateTime modifiedSince, AgentScanRequest request, CancellationToken cancellationToken);

    /// <summary>Parses a file once per change.</summary>
    protected AgentSession? Parsed(string path, Func<string, AgentSession?> parse)
    {
        FileInfo file = new(path);
        if (!file.Exists)
        {
            return null;
        }

        DateTime modified = file.LastWriteTimeUtc;
        long length = file.Length;
        if (_parsed.TryGetValue(path, out (long Length, DateTime Modified, AgentSession? Session) cached) && cached.Length == length && cached.Modified == modified)
        {
            return cached.Session;
        }

        AgentSession? session;
        try
        {
            session = parse(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        _parsed[path] = (length, modified, session);
        return session;
    }

    protected static bool ModifiedSince(string path, DateTime modifiedSince)
    {
        try
        {
            return File.GetLastWriteTimeUtc(path) >= modifiedSince;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    protected static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    protected static IEnumerable<string> SafeFiles(string directory, string pattern, SearchOption option)
    {
        try
        {
            return Directory.EnumerateFiles(directory, pattern, new EnumerationOptions
            {
                RecurseSubdirectories = option == SearchOption.AllDirectories,
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.System,
            }).ToList();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    protected static IEnumerable<string> SafeDirectories(string directory)
    {
        try
        {
            return Directory.EnumerateDirectories(directory, "*", new EnumerationOptions { IgnoreInaccessible = true }).ToList();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static AgentSession? Trim(AgentSession session, DateTimeOffset from, DateTimeOffset to)
    {
        List<DateTimeOffset> inRange = session.Timestamps.Where(timestamp => timestamp >= from && timestamp < to).ToList();
        return inRange.Count == 0
            ? null
            : session with { Timestamps = inRange, Claims = session.Claims.Where(claim => claim.At >= from && claim.At < to).ToList() };
    }
}
