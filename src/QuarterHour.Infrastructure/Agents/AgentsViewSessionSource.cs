using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using QuarterHour.Core.Configuration;
using QuarterHour.Core.Model;
using QuarterHour.Core.Sources;

namespace QuarterHour.Infrastructure.Agents;

/// <summary>
/// Reads sessions from a running AgentsView (agentsview serve), which already indexes 50+ agents. Sessions active
/// in the range come from /api/v1/sessions; their message timestamps from /api/v1/sessions/{id}/messages.
/// Claims come from one regex content search over tool inputs and results for the whole range; an AgentsView too old
/// to have that search leaves attribution to time.
/// </summary>
public sealed class AgentsViewSessionSource : IAgentSessionSource
{
    private const int SessionPageSize = 200;
    private const int MessagePageSize = 500;
    private const int MatchPageSize = 500;

    /// <summary>The claim markers as one RE2 pattern; <see cref="ClaimMatcher"/> reads the claims out of the snippets.</summary>
    internal const string ClaimPattern = @"\[[^\]]+ [0-9a-f]{7,40}\] |pullRequestId|/pullrequest/[0-9]+";

    private static readonly Dictionary<string, AgentKind> BuiltInAgents = new(StringComparer.OrdinalIgnoreCase)
    {
        ["claude"] = AgentKind.ClaudeCode,
        ["claude-code"] = AgentKind.ClaudeCode,
        ["claude_code"] = AgentKind.ClaudeCode,
        ["codex"] = AgentKind.Codex,
        ["copilot"] = AgentKind.CopilotCli,
        ["copilot-cli"] = AgentKind.CopilotCli,
        ["copilot_cli"] = AgentKind.CopilotCli,
        ["opencode"] = AgentKind.OpenCode,
        ["pi"] = AgentKind.Pi,
    };

    private readonly HttpClient _http;

    public AgentsViewSessionSource(HttpClient http)
    {
        _http = http;
    }

    public string Name => "AgentsView";

    public async Task<AgentScanResult> ScanAsync(AgentScanRequest request, CancellationToken cancellationToken)
    {
        AgentsViewSettings settings = request.Settings.AgentsView;
        string baseUrl = settings.BaseUrl.TrimEnd('/');
        if (!settings.Enabled)
        {
            return AgentScanResult.Empty(Name, [baseUrl]);
        }

        HashSet<AgentKind> covered = settings.SkipAgentsCoveredByScanners
            ? request.Settings.Scanners.Where(scanner => scanner.Enabled).Select(scanner => scanner.Kind).ToHashSet()
            : [];

        List<SessionDto> listed;
        try
        {
            listed = await ListSessionsAsync(baseUrl, request, cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            return new AgentScanResult(Name, [], [baseUrl],
                [$"AgentsView isn't answering at {baseUrl} ({exception.Message}). Start it with \"agentsview serve\", or switch it off in Settings."]);
        }

        List<SessionDto> wanted = listed
            .Where(session => session.Id is { Length: > 0 })
            .Where(session => !(BuiltInAgents.TryGetValue(session.Agent ?? "", out AgentKind kind) && covered.Contains(kind)))
            .DistinctBy(session => session.Id)
            .ToList();

        List<string> problems = [];
        List<AgentSession> sessions = [];
        using SemaphoreSlim throttle = new(4);
        IEnumerable<Task> fetches = wanted.Select(async session =>
        {
            await throttle.WaitAsync(cancellationToken);
            try
            {
                List<DateTimeOffset> timestamps = await MessageTimesAsync(baseUrl, session.Id!, cancellationToken);
                if (timestamps.Count == 0)
                {
                    timestamps.AddRange(new[] { session.StartedAt, session.EndedAt }.OfType<string>()
                        .Select(text => JsonLines.TryTimestamp(text, out DateTimeOffset value) ? value : (DateTimeOffset?)null)
                        .OfType<DateTimeOffset>());
                }

                List<DateTimeOffset> inRange = timestamps.Where(timestamp => timestamp >= request.From && timestamp < request.To).ToList();
                if (inRange.Count > 0)
                {
                    lock (sessions)
                    {
                        sessions.Add(new AgentSession
                        {
                            Agent = string.IsNullOrWhiteSpace(session.Agent) ? "agentsview" : session.Agent,
                            SessionId = session.Id!,
                            Title = Title(session),
                            ProjectPath = string.IsNullOrWhiteSpace(session.Cwd) ? session.Project : session.Cwd,
                            Repository = string.IsNullOrWhiteSpace(session.Cwd) ? null : session.Project,
                            Branch = session.GitBranch,
                            Timestamps = inRange,
                        });
                    }
                }
            }
            catch (Exception exception) when (exception is HttpRequestException or System.Text.Json.JsonException)
            {
                lock (problems)
                {
                    problems.Add($"Session {session.Id}: {exception.Message}");
                }
            }
            finally
            {
                throttle.Release();
            }
        });
        await Task.WhenAll(fetches);

        if (sessions.Count > 0)
        {
            Dictionary<string, List<Claim>> claims = await ClaimsAsync(baseUrl, request, problems, cancellationToken);
            sessions = sessions
                .Select(session => claims.TryGetValue(session.SessionId, out List<Claim>? found)
                    ? session with { Claims = found.Where(claim => claim.At >= request.From && claim.At < request.To).ToList() }
                    : session)
                .ToList();
        }

        return new AgentScanResult(Name, sessions, [baseUrl], problems);
    }

    private async Task<Dictionary<string, List<Claim>>> ClaimsAsync(string baseUrl, AgentScanRequest request, List<string> problems, CancellationToken cancellationToken)
    {
        (string dateFrom, string dateTo, string timeZone) = Dates(request);
        Dictionary<string, List<Claim>> claims = new(StringComparer.Ordinal);
        int cursor = 0;
        try
        {
            for (int page = 0; page < 20; page++)
            {
                string url = $"{baseUrl}/api/v1/search/content?pattern={Uri.EscapeDataString(ClaimPattern)}&mode=regex&in=tool_input,tool_result" +
                             $"&date_from={dateFrom}&date_to={dateTo}&timezone={Uri.EscapeDataString(timeZone)}&include_children=true&include_one_shot=true" +
                             $"&limit={MatchPageSize}" + (cursor > 0 ? $"&cursor={cursor}" : "");
                using HttpResponseMessage response = await _http.GetAsync(url, cancellationToken);
                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    problems.Add("This AgentsView can't search tool output, so its sessions' commits and pull requests are attributed by time. Update AgentsView to fix that.");
                    return claims;
                }

                response.EnsureSuccessStatusCode();
                MatchPage? result = await response.Content.ReadFromJsonAsync<MatchPage>(cancellationToken);
                foreach (MatchDto match in result?.Matches ?? [])
                {
                    if (match.SessionId is not { Length: > 0 } sessionId || match.Snippet is null || !JsonLines.TryTimestamp(match.Timestamp, out DateTimeOffset at))
                    {
                        continue;
                    }

                    if (!claims.TryGetValue(sessionId, out List<Claim>? list))
                    {
                        claims[sessionId] = list = [];
                    }

                    list.AddRange(ClaimMatcher.Find(match.Snippet, at));
                }

                if (result?.NextCursor is not > 0 || result.NextCursor <= cursor)
                {
                    break;
                }

                cursor = result.NextCursor.Value;
            }
        }
        catch (Exception exception) when (exception is HttpRequestException or System.Text.Json.JsonException)
        {
            problems.Add($"AgentsView's tool output search failed ({exception.Message}), so its sessions' commits and pull requests are attributed by time.");
        }

        return claims;
    }

    private static (string From, string To, string TimeZone) Dates(AgentScanRequest request)
    {
        string timeZone = TimeZoneInfo.TryConvertWindowsIdToIanaId(request.TimeZone.Id, out string? iana) ? iana : request.TimeZone.Id;
        string dateFrom = TimeZoneInfo.ConvertTime(request.From, request.TimeZone).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        string dateTo = TimeZoneInfo.ConvertTime(request.To.AddTicks(-1), request.TimeZone).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        return (dateFrom, dateTo, timeZone);
    }

    private async Task<List<SessionDto>> ListSessionsAsync(string baseUrl, AgentScanRequest request, CancellationToken cancellationToken)
    {
        (string dateFrom, string dateTo, string timeZone) = Dates(request);
        List<SessionDto> sessions = [];
        string? cursor = null;
        for (int page = 0; page < 100; page++)
        {
            string url = $"{baseUrl}/api/v1/sessions?date_from={dateFrom}&date_to={dateTo}&timezone={Uri.EscapeDataString(timeZone)}" +
                         $"&limit={SessionPageSize}&include_one_shot=true&include_children=false" +
                         (cursor is null ? "" : $"&cursor={Uri.EscapeDataString(cursor)}");
            SessionPage? result = await _http.GetFromJsonAsync<SessionPage>(url, cancellationToken);
            if (result?.Sessions is null || result.Sessions.Count == 0)
            {
                break;
            }

            sessions.AddRange(result.Sessions);
            cursor = result.NextCursor;
            if (string.IsNullOrEmpty(cursor))
            {
                break;
            }
        }

        return sessions;
    }

    private async Task<List<DateTimeOffset>> MessageTimesAsync(string baseUrl, string sessionId, CancellationToken cancellationToken)
    {
        List<DateTimeOffset> timestamps = [];
        long from = 0;
        for (int page = 0; page < 200; page++)
        {
            string url = $"{baseUrl}/api/v1/sessions/{Uri.EscapeDataString(sessionId)}/messages?limit={MessagePageSize}&direction=asc&from={from}";
            MessagePage? result = await _http.GetFromJsonAsync<MessagePage>(url, cancellationToken);
            if (result?.Messages is null || result.Messages.Count == 0)
            {
                break;
            }

            foreach (MessageDto message in result.Messages)
            {
                if (message.IsSystem != true && JsonLines.TryTimestamp(message.Timestamp, out DateTimeOffset timestamp))
                {
                    timestamps.Add(timestamp);
                }
            }

            long last = result.LastOrdinal ?? result.Messages.Max(message => message.Ordinal ?? 0);
            if (result.Messages.Count < MessagePageSize || last < from)
            {
                break;
            }

            from = last + 1;
        }

        return timestamps;
    }

    private static string? Title(SessionDto session)
    {
        string? title = !string.IsNullOrWhiteSpace(session.DisplayName) ? session.DisplayName : session.FirstMessage;
        if (string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        title = title.ReplaceLineEndings(" ").Trim();
        return title.Length <= 80 ? title : title[..79] + "…";
    }

    private sealed record SessionPage(
        [property: JsonPropertyName("sessions")] List<SessionDto>? Sessions,
        [property: JsonPropertyName("next_cursor")] string? NextCursor);

    private sealed record SessionDto(
        [property: JsonPropertyName("id")] string? Id,
        [property: JsonPropertyName("agent")] string? Agent,
        [property: JsonPropertyName("project")] string? Project,
        [property: JsonPropertyName("cwd")] string? Cwd,
        [property: JsonPropertyName("git_branch")] string? GitBranch,
        [property: JsonPropertyName("display_name")] string? DisplayName,
        [property: JsonPropertyName("first_message")] string? FirstMessage,
        [property: JsonPropertyName("started_at")] string? StartedAt,
        [property: JsonPropertyName("ended_at")] string? EndedAt);

    private sealed record MatchPage(
        [property: JsonPropertyName("matches")] List<MatchDto>? Matches,
        [property: JsonPropertyName("next_cursor")] int? NextCursor);

    private sealed record MatchDto(
        [property: JsonPropertyName("session_id")] string? SessionId,
        [property: JsonPropertyName("timestamp")] string? Timestamp,
        [property: JsonPropertyName("snippet")] string? Snippet);

    private sealed record MessagePage(
        [property: JsonPropertyName("messages")] List<MessageDto>? Messages,
        [property: JsonPropertyName("last_ordinal")] long? LastOrdinal);

    private sealed record MessageDto(
        [property: JsonPropertyName("ordinal")] long? Ordinal,
        [property: JsonPropertyName("timestamp")] string? Timestamp,
        [property: JsonPropertyName("is_system")] bool? IsSystem);
}
