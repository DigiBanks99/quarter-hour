using System.Text.Json;
using QuarterHour.Core.Configuration;
using QuarterHour.Core.Model;
using QuarterHour.Core.Sources;

namespace QuarterHour.Infrastructure.Agents;

/// <summary>
/// GitHub Copilot CLI keeps each session in ~/.copilot/session-state/&lt;id&gt;/ with an events.jsonl log and a
/// workspace.yaml (working folder, git root, repository, branch, name). Older versions wrote one &lt;id&gt;.jsonl file.
/// Tool execution events (tool.execution_start's arguments, tool.execution_complete's result) hold the claims.
/// </summary>
public sealed class CopilotCliSessionSource : FileSessionSource
{
    private static readonly string[] Wanted = ["type", "timestamp"];

    public override AgentKind Kind => AgentKind.CopilotCli;
    public override string Name => "GitHub Copilot CLI";

    public override IReadOnlyList<string> DefaultLocations()
    {
        string root = Environment.GetEnvironmentVariable("COPILOT_HOME") is { Length: > 0 } configured ? configured : Path.Combine(Home, ".copilot");
        return [Path.Combine(root, "session-state"), Path.Combine(root, "history-session-state")];
    }

    protected override IEnumerable<AgentSession> ReadLocation(string location, DateTime modifiedSince, AgentScanRequest request, CancellationToken cancellationToken)
    {
        string root = Directory.Exists(Path.Combine(location, "session-state")) ? Path.Combine(location, "session-state") : location;
        foreach (string directory in SafeDirectories(root))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string events = Path.Combine(directory, "events.jsonl");
            if (File.Exists(events) && ModifiedSince(events, modifiedSince) && Parsed(events, Parse) is { } session)
            {
                yield return session;
            }
        }

        foreach (string file in SafeFiles(root, "*.jsonl", SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (ModifiedSince(file, modifiedSince) && Parsed(file, Parse) is { } session)
            {
                yield return session;
            }
        }
    }

    internal static AgentSession? Parse(string eventsPath)
    {
        bool isFolderSession = string.Equals(Path.GetFileName(eventsPath), "events.jsonl", StringComparison.OrdinalIgnoreCase);
        string sessionId = isFolderSession
            ? Path.GetFileName(Path.GetDirectoryName(eventsPath)) ?? Path.GetFileNameWithoutExtension(eventsPath)
            : Path.GetFileNameWithoutExtension(eventsPath);
        Dictionary<string, string> workspace = isFolderSession
            ? ReadWorkspace(Path.Combine(Path.GetDirectoryName(eventsPath)!, "workspace.yaml"))
            : [];

        List<DateTimeOffset> timestamps = [];
        List<Claim> claims = [];
        string? cwd = null;
        string? gitRoot = null;
        string? repository = null;
        string? branch = null;
        foreach (ReadOnlyMemory<byte> line in JsonLines.Lines(JsonLines.ReadShared(eventsPath)))
        {
            Dictionary<string, string?>? values = JsonLines.TopLevel(line.Span, Wanted);
            string? type = values?.GetValueOrDefault("type");
            if (type is null)
            {
                continue;
            }

            if (type == "session.start")
            {
                using JsonDocument? document = JsonLines.Document(line);
                if (document is not null)
                {
                    cwd = document.RootElement.Text("data", "context", "cwd");
                    gitRoot = document.RootElement.Text("data", "context", "gitRoot");
                    repository = document.RootElement.Text("data", "context", "repository");
                    branch = document.RootElement.Text("data", "context", "branch");
                }

                continue;
            }

            if ((type.StartsWith("user.", StringComparison.Ordinal) || type.StartsWith("assistant.", StringComparison.Ordinal) || type.StartsWith("tool.", StringComparison.Ordinal))
                && JsonLines.TryTimestamp(values!.GetValueOrDefault("timestamp"), out DateTimeOffset timestamp))
            {
                timestamps.Add(timestamp);
                if (type.StartsWith("tool.execution_", StringComparison.Ordinal))
                {
                    claims.AddRange(ClaimMatcher.Find(line.Span, timestamp));
                }
            }
        }

        if (timestamps.Count == 0)
        {
            return null;
        }

        string? title = workspace.GetValueOrDefault("name") ?? workspace.GetValueOrDefault("summary");
        return new AgentSession
        {
            Agent = "copilot-cli",
            SessionId = sessionId,
            Title = string.IsNullOrWhiteSpace(title) ? null : title,
            ProjectPath = workspace.GetValueOrDefault("git_root") ?? gitRoot ?? workspace.GetValueOrDefault("cwd") ?? cwd,
            Repository = workspace.GetValueOrDefault("repository") ?? repository,
            Branch = workspace.GetValueOrDefault("branch") ?? branch,
            Timestamps = timestamps,
            Claims = claims,
        };
    }

    /// <summary>workspace.yaml is flat "key: value" lines; nothing fancier is needed.</summary>
    internal static Dictionary<string, string> ReadWorkspace(string path)
    {
        Dictionary<string, string> values = new(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(path))
        {
            return values;
        }

        foreach (string line in File.ReadLines(path))
        {
            if (line.Length == 0 || char.IsWhiteSpace(line[0]) || line.StartsWith('#'))
            {
                continue;
            }

            int colon = line.IndexOf(':');
            if (colon <= 0)
            {
                continue;
            }

            string key = line[..colon].Trim();
            string value = line[(colon + 1)..].Trim();
            if (value is "|" or ">" or "|-" or ">-" or "")
            {
                continue;
            }

            if (value.Length >= 2 && (value[0] == '"' || value[0] == '\'') && value[^1] == value[0])
            {
                value = value[1..^1];
            }

            values[key] = value;
        }

        return values;
    }
}
