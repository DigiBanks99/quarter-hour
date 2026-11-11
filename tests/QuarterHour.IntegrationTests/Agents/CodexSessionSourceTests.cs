using QuarterHour.Core.Configuration;
using QuarterHour.Core.Model;
using QuarterHour.Core.Sources;
using QuarterHour.Infrastructure.Agents;

namespace QuarterHour.IntegrationTests.Agents;

public sealed class CodexSessionSourceTests : IDisposable
{
    private readonly TemporaryDirectory _home = new();

    [Test]
    public async Task ARolloutFileIsWritten()
    {
        _home.File(@"sessions\2026\11\10\rollout-2026-11-10T08-00-00-abc.jsonl", string.Join('\n',
            """{"timestamp":"2026-11-10T06:00:00.000Z","type":"session_meta","payload":{"id":"abc","cwd":"C:\\source\\portal","git":{"branch":"main","repository_url":"https://github.com/acme/portal.git"}}}""",
            """{"timestamp":"2026-11-10T06:00:01.000Z","type":"turn_context","payload":{}}""",
            """{"timestamp":"2026-11-10T06:00:02.000Z","type":"response_item","payload":{"type":"message"}}""",
            """{"timestamp":"2026-11-10T06:03:00.000Z","type":"event_msg","payload":{"type":"token_count"}}"""));

        AgentScanResult result = await new CodexSessionSource().ScanAsync(Scans.November(AgentKind.Codex, _home.Path), CancellationToken.None);
        AgentSession session = result.Sessions.Single();

        ItShouldReadTheSessionMetadata();
        ItShouldCountTheWork();

        void ItShouldReadTheSessionMetadata() => session.ShouldSatisfyAllConditions(
            () => session.SessionId.ShouldBe("abc"),
            () => session.Repository.ShouldBe("https://github.com/acme/portal.git"),
            () => session.Branch.ShouldBe("main"));

        void ItShouldCountTheWork() => session.Timestamps.Count.ShouldBe(2);
    }

    public void Dispose() => _home.Dispose();
}
