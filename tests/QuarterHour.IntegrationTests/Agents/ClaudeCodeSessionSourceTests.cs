using QuarterHour.Core.Configuration;
using QuarterHour.Core.Model;
using QuarterHour.Core.Sources;
using QuarterHour.Infrastructure.Agents;

namespace QuarterHour.IntegrationTests.Agents;

public sealed class ClaudeCodeSessionSourceTests : IDisposable
{
    private readonly TemporaryDirectory _home = new();
    private readonly ClaudeCodeSessionSource _source = new();

    [Test]
    public async Task ASessionIsWrittenInNovember()
    {
        _home.File(@"projects\C--source-portal\abc123.jsonl", string.Join('\n',
            """{"type":"file-history-snapshot","messageId":"x","snapshot":{}}""",
            """{"parentUuid":null,"isSidechain":false,"type":"user","message":{"role":"user","content":"hi","timestamp":"2020-01-01T00:00:00Z"},"uuid":"1","timestamp":"2026-11-09T07:00:00.000Z","cwd":"C:\\source\\portal","gitBranch":"feature/login","sessionId":"abc123"}""",
            """{"parentUuid":"1","isSidechain":false,"type":"assistant","message":{"role":"assistant","content":[{"type":"text","text":"hello"}]},"uuid":"2","timestamp":"2026-11-09T07:05:00.000Z","cwd":"C:\\source\\portal","gitBranch":"feature/login","sessionId":"abc123"}""",
            """{"parentUuid":"2","isSidechain":true,"type":"assistant","message":{},"uuid":"3","timestamp":"2026-11-09T09:00:00.000Z","cwd":"C:\\source\\portal","sessionId":"abc123"}""",
            """{"type":"ai-title","aiTitle":"Fix the login page","sessionId":"abc123"}""",
            """{"type":"user","timestamp":"2026-11-09T07:1"""));
        _home.File(@"projects\C--source-portal\abc123\subagents\agent-1.jsonl",
            """{"type":"user","timestamp":"2026-11-09T08:00:00.000Z","cwd":"C:\\source\\portal","isSidechain":true}""");

        AgentScanResult result = await _source.ScanAsync(Scans.November(AgentKind.ClaudeCode, _home.Path), CancellationToken.None);
        AgentSession session = result.Sessions.Single();

        ItShouldReadTheSessionsMessages();
        ItShouldKnowWhereAndWhatItWas();

        void ItShouldReadTheSessionsMessages() => session.Timestamps.ShouldBe([
            new DateTimeOffset(2026, 11, 9, 7, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 11, 9, 7, 5, 0, TimeSpan.Zero),
        ]);

        void ItShouldKnowWhereAndWhatItWas() => session.ShouldSatisfyAllConditions(
            () => session.SessionId.ShouldBe("abc123"),
            () => session.Title.ShouldBe("Fix the login page"),
            () => session.ProjectPath.ShouldBe(@"C:\source\portal"),
            () => session.Branch.ShouldBe("feature/login"));
    }

    [Test]
    public async Task TheAgentMovesToAnotherFolderMidSession()
    {
        _home.File(@"projects\C--source-portal\moving.jsonl", string.Join('\n',
            """{"type":"user","timestamp":"2026-11-09T07:00:00.000Z","cwd":"C:\\source\\portal"}""",
            """{"type":"assistant","timestamp":"2026-11-09T07:01:00.000Z","cwd":"C:\\source\\portal\\samples"}"""));

        AgentScanResult result = await _source.ScanAsync(Scans.November(AgentKind.ClaudeCode, _home.Path), CancellationToken.None);

        ItShouldKeepTheFolderTheSessionStartedIn();

        void ItShouldKeepTheFolderTheSessionStartedIn() => result.Sessions.Single().ProjectPath.ShouldBe(@"C:\source\portal");
    }

    [Test]
    public async Task ASessionWasLastTouchedInSeptember()
    {
        _home.File(@"projects\p\old.jsonl",
            """{"type":"user","timestamp":"2026-11-09T07:00:00.000Z","cwd":"C:\\source\\portal"}""",
            modifiedUtc: new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc));

        AgentScanResult result = await _source.ScanAsync(Scans.November(AgentKind.ClaudeCode, _home.Path), CancellationToken.None);

        ItShouldNotBeRead();

        void ItShouldNotBeRead() => result.Sessions.ShouldBeEmpty();
    }

    [Test]
    public async Task TheScannerIsSwitchedOff()
    {
        _home.File(@"projects\p\s.jsonl", """{"type":"user","timestamp":"2026-11-09T07:00:00.000Z"}""");
        AgentSettings settings = new() { Scanners = [new AgentScannerSettings { Kind = AgentKind.ClaudeCode, Enabled = false, Paths = [_home.Path] }] };

        AgentScanResult result = await _source.ScanAsync(Scans.November(settings), CancellationToken.None);

        ItShouldFindNothing();

        void ItShouldFindNothing() => result.Sessions.ShouldBeEmpty();
    }

    public void Dispose() => _home.Dispose();
}
