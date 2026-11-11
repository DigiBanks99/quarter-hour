using System.Net;
using QuarterHour.Core.Configuration;
using QuarterHour.Core.Model;
using QuarterHour.Core.Sources;
using QuarterHour.Infrastructure.Agents;

namespace QuarterHour.IntegrationTests.Agents;

public sealed class AgentsViewSessionSourceTests : IDisposable
{
    private readonly ScriptedHandler _agentsView = new(request =>
    {
        string path = request.RequestUri!.AbsolutePath;
        string query = request.RequestUri.Query;
        if (path == "/api/v1/sessions" && !query.Contains("cursor=", StringComparison.Ordinal))
        {
            return ScriptedHandler.Json("""
                {"sessions":[
                  {"id":"g-1","agent":"gemini","project":"portal","cwd":"C:\\source\\portal","git_branch":"main","display_name":"Tidy the styles","started_at":"2026-11-10T06:00:00Z","ended_at":"2026-11-10T07:00:00Z"},
                  {"id":"c-1","agent":"claude","project":"portal","cwd":"C:\\source\\portal","first_message":"Already read by the built-in scanner"}
                ],"total":3,"next_cursor":"page-2"}
                """);
        }

        if (path == "/api/v1/sessions")
        {
            return ScriptedHandler.Json("""{"sessions":[{"id":"x-1","agent":"cursor","project":"api","cwd":"","first_message":"Line one\nline two"}],"total":3,"next_cursor":""}""");
        }

        return path switch
        {
            "/api/v1/sessions/g-1/messages" => ScriptedHandler.Json("""
                {"messages":[
                  {"id":1,"ordinal":0,"role":"user","timestamp":"2026-11-10T06:00:00Z","content":"go"},
                  {"id":2,"ordinal":1,"role":"assistant","timestamp":"2026-11-10T06:04:00Z","content":"done"}
                ],"count":2,"last_ordinal":1}
                """),
            "/api/v1/sessions/x-1/messages" => ScriptedHandler.Json("""{"messages":[{"ordinal":0,"role":"user","timestamp":"2026-11-11T09:00:00Z"}],"count":1,"last_ordinal":0}"""),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        };
    });

    [Test]
    public async Task AgentsViewIsRunning()
    {
        AgentsViewSessionSource source = new(new HttpClient(_agentsView));
        AgentSettings settings = new()
        {
            AgentsView = new AgentsViewSettings { Enabled = true, BaseUrl = "http://127.0.0.1:8080/" },
            Scanners = [new AgentScannerSettings { Kind = AgentKind.ClaudeCode, Enabled = true }],
        };

        AgentScanResult result = await source.ScanAsync(Scans.November(settings), CancellationToken.None);
        AgentSession gemini = result.Sessions.Single(session => session.SessionId == "g-1");
        AgentSession cursor = result.Sessions.Single(session => session.SessionId == "x-1");

        ItShouldSkipAgentsAlreadyScanned();
        ItShouldReadEveryPage();
        ItShouldReadMessageTimes();
        ItShouldNameTheSessions();

        void ItShouldSkipAgentsAlreadyScanned() => result.Sessions.ShouldNotContain(session => session.SessionId == "c-1");

        void ItShouldReadEveryPage() => result.Sessions.Count.ShouldBe(2);

        void ItShouldReadMessageTimes() => gemini.Timestamps.Count.ShouldBe(2);

        void ItShouldNameTheSessions() => gemini.ShouldSatisfyAllConditions(
            () => gemini.Title.ShouldBe("Tidy the styles"),
            () => gemini.Agent.ShouldBe("gemini"),
            () => cursor.Title.ShouldBe("Line one line two"),
            () => cursor.ProjectPath.ShouldBe("api"));
    }

    [Test]
    public async Task AgentsViewIsNotRunning()
    {
        AgentsViewSessionSource source = new(new HttpClient(new ScriptedHandler(_ => throw new HttpRequestException("Connection refused"))));
        AgentSettings settings = new() { AgentsView = new AgentsViewSettings { Enabled = true } };

        AgentScanResult result = await source.ScanAsync(Scans.November(settings), CancellationToken.None);

        ItShouldSayHowToStartIt();

        void ItShouldSayHowToStartIt() => result.Problems.Single().ShouldContain("agentsview serve");
    }

    public void Dispose() => _agentsView.Dispose();
}
