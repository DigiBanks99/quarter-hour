using QuarterHour.Core.Configuration;
using QuarterHour.Core.Model;
using QuarterHour.Core.Sources;

namespace QuarterHour.Infrastructure.Agents;

/// <summary>
/// The Pi coding agent writes ~/.pi/agent/sessions/--&lt;path&gt;--/&lt;timestamp&gt;_&lt;id&gt;.jsonl: a "session" header
/// (id, cwd), "message" entries with ISO timestamps and "session_info" entries that name the session.
/// PI_CODING_AGENT_SESSION_DIR moves it.
/// </summary>
public sealed class PiSessionSource : FileSessionSource
{
    private static readonly string[] Wanted = ["type", "timestamp", "cwd", "id", "name"];

    public override AgentKind Kind => AgentKind.Pi;
    public override string Name => "Pi";

    public override IReadOnlyList<string> DefaultLocations() =>
        Environment.GetEnvironmentVariable("PI_CODING_AGENT_SESSION_DIR") is { Length: > 0 } configured
            ? [configured]
            : [Path.Combine(Environment.GetEnvironmentVariable("PI_CODING_AGENT_DIR") is { Length: > 0 } agentDirectory ? agentDirectory : Path.Combine(Home, ".pi", "agent"), "sessions")];

    protected override IEnumerable<AgentSession> ReadLocation(string location, DateTime modifiedSince, AgentScanRequest request, CancellationToken cancellationToken)
    {
        foreach (string file in SafeFiles(location, "*.jsonl", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (ModifiedSince(file, modifiedSince) && Parsed(file, Parse) is { } session)
            {
                yield return session;
            }
        }
    }

    internal static AgentSession? Parse(string path)
    {
        List<DateTimeOffset> timestamps = [];
        string? id = null;
        string? cwd = null;
        string? name = null;
        foreach (ReadOnlyMemory<byte> line in JsonLines.Lines(JsonLines.ReadShared(path)))
        {
            Dictionary<string, string?>? values = JsonLines.TopLevel(line.Span, Wanted);
            switch (values?.GetValueOrDefault("type"))
            {
                case "session":
                    id = values.GetValueOrDefault("id") ?? id;
                    cwd = values.GetValueOrDefault("cwd") ?? cwd;
                    break;
                case "message" or "custom_message":
                    if (JsonLines.TryTimestamp(values.GetValueOrDefault("timestamp"), out DateTimeOffset timestamp))
                    {
                        timestamps.Add(timestamp);
                    }

                    break;
                case "session_info":
                    name = values.GetValueOrDefault("name") ?? name;
                    break;
            }
        }

        return timestamps.Count == 0
            ? null
            : new AgentSession
            {
                Agent = "pi",
                SessionId = id ?? Path.GetFileNameWithoutExtension(path),
                Title = name,
                ProjectPath = cwd,
                Timestamps = timestamps,
            };
    }
}
