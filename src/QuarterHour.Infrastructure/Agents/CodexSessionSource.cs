using System.Text.Json;
using QuarterHour.Core.Configuration;
using QuarterHour.Core.Model;
using QuarterHour.Core.Sources;

namespace QuarterHour.Infrastructure.Agents;

/// <summary>
/// OpenAI Codex CLI writes ~/.codex/sessions/YYYY/MM/DD/rollout-*.jsonl. The session_meta line has the working folder
/// and git details; every other line carries a timestamp.
/// </summary>
public sealed class CodexSessionSource : FileSessionSource
{
    private static readonly string[] Wanted = ["type", "timestamp"];

    public override AgentKind Kind => AgentKind.Codex;
    public override string Name => "OpenAI Codex";

    public override IReadOnlyList<string> DefaultLocations()
    {
        string root = Environment.GetEnvironmentVariable("CODEX_HOME") is { Length: > 0 } configured ? configured : Path.Combine(Home, ".codex");
        return [Path.Combine(root, "sessions"), Path.Combine(root, "archived_sessions")];
    }

    protected override IEnumerable<AgentSession> ReadLocation(string location, DateTime modifiedSince, AgentScanRequest request, CancellationToken cancellationToken)
    {
        string root = Directory.Exists(Path.Combine(location, "sessions")) ? Path.Combine(location, "sessions") : location;
        foreach (string file in SafeFiles(root, "*.jsonl", SearchOption.AllDirectories))
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
        string? branch = null;
        string? repository = null;
        foreach (ReadOnlyMemory<byte> line in JsonLines.Lines(JsonLines.ReadShared(path)))
        {
            Dictionary<string, string?>? values = JsonLines.TopLevel(line.Span, Wanted);
            string? type = values?.GetValueOrDefault("type");
            if (type is null)
            {
                continue;
            }

            if (type == "session_meta")
            {
                using JsonDocument? document = JsonLines.Document(line);
                if (document is not null)
                {
                    id = document.RootElement.Text("payload", "id");
                    cwd = document.RootElement.Text("payload", "cwd");
                    branch = document.RootElement.Text("payload", "git", "branch");
                    repository = document.RootElement.Text("payload", "git", "repository_url");
                }

                continue;
            }

            if (type is "response_item" or "event_msg" && JsonLines.TryTimestamp(values!.GetValueOrDefault("timestamp"), out DateTimeOffset timestamp))
            {
                timestamps.Add(timestamp);
            }
        }

        return timestamps.Count == 0
            ? null
            : new AgentSession
            {
                Agent = "codex",
                SessionId = id ?? Path.GetFileNameWithoutExtension(path),
                ProjectPath = cwd,
                Repository = repository,
                Branch = branch,
                Timestamps = timestamps,
            };
    }
}
