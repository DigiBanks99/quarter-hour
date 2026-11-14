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

    [Test]
    public async Task ASessionCommittedAndRaisedAPullRequest()
    {
        _home.File(@"session-state\s-2\events.jsonl", string.Join('\n',
            """{"type":"session.start","data":{"sessionId":"s-2","context":{"cwd":"C:\\source\\portal","gitRoot":"C:\\source\\portal","branch":"feature/sso"}},"id":"1","timestamp":"2026-11-10T06:00:00.000Z","parentId":null}""",
            """{"type":"user.message","data":{"content":"commit [main 1111111] this and open a PR"},"id":"2","timestamp":"2026-11-10T06:00:05.000Z","parentId":"1"}""",
            """{"type":"tool.execution_start","data":{"toolCallId":"call_1","toolName":"powershell","arguments":{"command":"git commit -m \"feat: add sso login\"","intent":"Commit"},"turnId":"0"},"id":"3","timestamp":"2026-11-10T06:10:00.000Z","parentId":"2"}""",
            """{"type":"tool.execution_complete","data":{"toolCallId":"call_1","model":"gpt-5.5","interactionId":"i-1","turnId":"0","success":true,"result":{"content":"[feature/sso 907773d] feat: add sso login\n 2 files changed, 40 insertions(+)\n<exited with exit code 0>","detailedContent":"[feature/sso 907773d] feat: add sso login"},"toolTelemetry":{}},"id":"4","timestamp":"2026-11-10T06:10:02.000Z","parentId":"3"}""",
            """{"type":"tool.execution_complete","data":{"toolCallId":"call_2","success":true,"result":{"content":"{\n  \"pullRequestId\": 482,\n  \"status\": \"active\"\n}"}},"id":"5","timestamp":"2026-11-10T06:12:00.000Z","parentId":"4"}"""));

        AgentScanResult result = await new CopilotCliSessionSource().ScanAsync(Scans.November(AgentKind.CopilotCli, _home.Path), CancellationToken.None);
        AgentSession session = result.Sessions.Single();

        ItShouldClaimWhatTheToolsMade();

        void ItShouldClaimWhatTheToolsMade() =>
            session.Claims.Select(claim => (claim.Kind, claim.Value, claim.At)).ShouldBe([
                (ClaimKind.Commit, "907773d", new DateTimeOffset(2026, 11, 10, 6, 10, 2, TimeSpan.Zero)),
                (ClaimKind.PullRequest, "482", new DateTimeOffset(2026, 11, 10, 6, 12, 0, TimeSpan.Zero)),
            ]);
    }

    public void Dispose() => _home.Dispose();
}
