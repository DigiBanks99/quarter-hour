using QuarterHour.Core.Configuration;
using QuarterHour.Core.Model;
using QuarterHour.Core.Sources;

namespace QuarterHour.Infrastructure.Agents;

/// <summary>
/// Claude Code keeps one JSONL file per session in ~/.claude/projects/&lt;project&gt;/&lt;session&gt;.jsonl.
/// User and assistant lines carry a timestamp, the working folder and the git branch; ai-title lines name the session.
/// Sub-agent transcripts (in a session's subagents folder) are left out: the parent session already covers that time.
/// </summary>
public sealed class ClaudeCodeSessionSource : FileSessionSource
{
    private static readonly string[] Wanted = ["type", "timestamp", "cwd", "gitBranch", "isSidechain", "aiTitle", "summary"];

    public override AgentKind Kind => AgentKind.ClaudeCode;
    public override string Name => "Claude Code";

    public override IReadOnlyList<string> DefaultLocations() =>
        Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR") is { Length: > 0 } configured
            ? [Path.Combine(configured, "projects")]
            : [Path.Combine(Home, ".claude", "projects")];

    protected override IEnumerable<AgentSession> ReadLocation(string location, DateTime modifiedSince, AgentScanRequest request, CancellationToken cancellationToken)
    {
        string projects = Directory.Exists(Path.Combine(location, "projects")) ? Path.Combine(location, "projects") : location;
        foreach (string projectDirectory in SafeDirectories(projects))
        {
            foreach (string file in SafeFiles(projectDirectory, "*.jsonl", SearchOption.TopDirectoryOnly))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (ModifiedSince(file, modifiedSince) && Parsed(file, Parse) is { } session)
                {
                    yield return session;
                }
            }
        }
    }

    internal static AgentSession? Parse(string path)
    {
        List<DateTimeOffset> timestamps = [];
        string? cwd = null;
        string? branch = null;
        string? title = null;
        string? summary = null;
        foreach (ReadOnlyMemory<byte> line in JsonLines.Lines(JsonLines.ReadShared(path)))
        {
            Dictionary<string, string?>? values = JsonLines.TopLevel(line.Span, Wanted);
            if (values is null)
            {
                continue;
            }

            string? type = values.GetValueOrDefault("type");
            switch (type)
            {
                case "user" or "assistant":
                    if (values.GetValueOrDefault("isSidechain") == "true")
                    {
                        continue;
                    }

                    if (JsonLines.TryTimestamp(values.GetValueOrDefault("timestamp"), out DateTimeOffset timestamp))
                    {
                        timestamps.Add(timestamp);
                    }

                    // The folder the session was started in identifies the project; later lines follow the agent around.
                    cwd ??= values.GetValueOrDefault("cwd") is { Length: > 0 } lineCwd ? lineCwd : null;
                    branch = values.GetValueOrDefault("gitBranch") is { Length: > 0 } lineBranch ? lineBranch : branch;
                    break;
                case "ai-title":
                    title = values.GetValueOrDefault("aiTitle") ?? title;
                    break;
                case "summary":
                    summary ??= values.GetValueOrDefault("summary");
                    break;
            }
        }

        return timestamps.Count == 0
            ? null
            : new AgentSession
            {
                Agent = "claude-code",
                SessionId = Path.GetFileNameWithoutExtension(path),
                Title = title ?? summary,
                ProjectPath = cwd,
                Branch = branch is "HEAD" ? null : branch,
                Timestamps = timestamps,
            };
    }
}
