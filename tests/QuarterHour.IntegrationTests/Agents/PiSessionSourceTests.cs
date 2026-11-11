using QuarterHour.Core.Configuration;
using QuarterHour.Core.Model;
using QuarterHour.Core.Sources;
using QuarterHour.Infrastructure.Agents;

namespace QuarterHour.IntegrationTests.Agents;

public sealed class PiSessionSourceTests : IDisposable
{
    private readonly TemporaryDirectory _home = new();

    [Test]
    public async Task APiSessionIsNamed()
    {
        _home.File(@"--C--source-portal--\2026-11-10T06-00-00-000Z_uuid-1.jsonl", string.Join('\n',
            """{"type":"session","version":3,"id":"uuid-1","timestamp":"2026-11-10T06:00:00.000Z","cwd":"C:\\source\\portal"}""",
            """{"type":"message","id":"a1","parentId":null,"timestamp":"2026-11-10T06:00:10.000Z","message":{"role":"user","content":"Hello","timestamp":1794290410000}}""",
            """{"type":"model_change","id":"a2","parentId":"a1","timestamp":"2026-11-10T06:00:20.000Z","provider":"x","modelId":"y"}""",
            """{"type":"message","id":"a3","parentId":"a2","timestamp":"2026-11-10T06:02:00.000Z","message":{"role":"assistant","content":[]}}""",
            """{"type":"session_info","id":"a4","parentId":"a3","timestamp":"2026-11-10T06:02:01.000Z","name":"Refactor auth module"}"""));

        AgentScanResult result = await new PiSessionSource().ScanAsync(Scans.November(AgentKind.Pi, _home.Path), CancellationToken.None);
        AgentSession session = result.Sessions.Single();

        ItShouldUseTheSessionName();
        ItShouldCountOnlyMessages();

        void ItShouldUseTheSessionName() => session.ShouldSatisfyAllConditions(
            () => session.Title.ShouldBe("Refactor auth module"),
            () => session.SessionId.ShouldBe("uuid-1"),
            () => session.ProjectPath.ShouldBe(@"C:\source\portal"));

        void ItShouldCountOnlyMessages() => session.Timestamps.Count.ShouldBe(2);
    }

    public void Dispose() => _home.Dispose();
}
