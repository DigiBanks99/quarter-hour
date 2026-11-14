namespace QuarterHour.Core.Model;

/// <summary>An AI agent coding session reduced to what a timesheet needs: when there was activity, and where.</summary>
public sealed record AgentSession
{
    /// <summary>A short agent id such as "claude-code", "copilot-cli", "codex", "opencode" or "pi".</summary>
    public required string Agent { get; init; }

    public required string SessionId { get; init; }
    public string? Title { get; init; }

    /// <summary>The folder the agent worked in (git root when known, otherwise the working directory).</summary>
    public string? ProjectPath { get; init; }

    /// <summary>"owner/repo" or a remote URL, when the agent recorded one.</summary>
    public string? Repository { get; init; }

    public string? Branch { get; init; }

    /// <summary>Timestamps of the session's messages and tool calls.</summary>
    public required IReadOnlyList<DateTimeOffset> Timestamps { get; init; }

    /// <summary>Commits and pull requests the session's tool output shows it made.</summary>
    public IReadOnlyList<Claim> Claims { get; init; } = [];
}
