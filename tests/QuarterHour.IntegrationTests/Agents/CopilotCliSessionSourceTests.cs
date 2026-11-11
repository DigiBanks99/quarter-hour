using QuarterHour.Core.Configuration;
using QuarterHour.Core.Model;
using QuarterHour.Core.Sources;
using QuarterHour.Infrastructure.Agents;

namespace QuarterHour.IntegrationTests.Agents;

public sealed class CopilotCliSessionSourceTests : IDisposable
{
    private readonly TemporaryDirectory _home = new();

    [Test]
    public async Task ASessionFolderHasAWorkspaceFile()
    {
        _home.File(@"session-state\s-1\workspace.yaml", """
            id: s-1
            cwd: C:\source\portal\src
            git_root: C:\source\portal
            repository: acme/portal
            branch: main
            summary_count: 0
            name: "Add the export page"
            """);
        _home.File(@"session-state\s-1\events.jsonl", string.Join('\n',
            """{"type":"session.start","data":{"sessionId":"s-1","context":{"cwd":"C:\\elsewhere"}},"id":"1","timestamp":"2026-11-10T06:00:00.000Z","parentId":null}""",
            """{"type":"user.message","data":{"content":"go"},"id":"2","timestamp":"2026-11-10T06:00:05.000Z","parentId":"1"}""",
            """{"type":"assistant.message","data":{"content":"ok"},"id":"3","timestamp":"2026-11-10T06:01:00.000Z","parentId":"2"}""",
            """{"type":"session.shutdown","data":{},"id":"4","timestamp":"2026-11-10T11:00:00.000Z","parentId":"3"}"""));

        AgentScanResult result = await new CopilotCliSessionSource().ScanAsync(Scans.November(AgentKind.CopilotCli, _home.Path), CancellationToken.None);
        AgentSession session = result.Sessions.Single();

        ItShouldUseTheWorkspaceDetails();
        ItShouldIgnoreTheShutdownHoursLater();

        void ItShouldUseTheWorkspaceDetails() => session.ShouldSatisfyAllConditions(
            () => session.Title.ShouldBe("Add the export page"),
            () => session.ProjectPath.ShouldBe(@"C:\source\portal"),
            () => session.Repository.ShouldBe("acme/portal"));

        void ItShouldIgnoreTheShutdownHoursLater() => session.Timestamps.Count.ShouldBe(2);
    }

    public void Dispose() => _home.Dispose();
}
