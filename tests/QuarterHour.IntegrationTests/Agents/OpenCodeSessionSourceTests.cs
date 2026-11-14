using Microsoft.Data.Sqlite;
using QuarterHour.Core.Configuration;
using QuarterHour.Core.Model;
using QuarterHour.Core.Sources;
using QuarterHour.Infrastructure.Agents;

namespace QuarterHour.IntegrationTests.Agents;

public sealed class OpenCodeSessionSourceTests : IDisposable
{
    private readonly TemporaryDirectory _home = new();

    [Test]
    public async Task SessionsAreInTheDatabase()
    {
        string database = Path.Combine(_home.Folder("opencode"), "opencode.db");
        long nine = new DateTimeOffset(2026, 11, 10, 7, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();
        using (SqliteConnection connection = new($"Data Source={database};Pooling=False"))
        {
            connection.Open();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = $$"""
                CREATE TABLE session (id TEXT PRIMARY KEY, project_id TEXT, parent_id TEXT, directory TEXT, title TEXT, time_created INTEGER, time_updated INTEGER, time_archived INTEGER);
                CREATE TABLE message (id TEXT PRIMARY KEY, session_id TEXT, time_created INTEGER, time_updated INTEGER, data TEXT);
                CREATE TABLE part (id TEXT PRIMARY KEY, message_id TEXT, session_id TEXT, time_created INTEGER, time_updated INTEGER, data TEXT);
                INSERT INTO part VALUES ('p1', 'm2', 'ses_1', {{nine + 300_000}}, {{nine + 300_000}}, '{"type":"tool","callID":"c1","tool":"bash","state":{"status":"completed","input":{"command":"git commit -m \"feat: wire up the api\""},"output":"[main 5e6f7a8] feat: wire up the api\n 3 files changed","time":{"start":1,"end":2} } }');
                INSERT INTO part VALUES ('p2', 'm2', 'ses_1', {{nine + 300_000}}, {{nine + 300_000}}, '{"type":"text","text":"I made [main 9999999] earlier"}');
                INSERT INTO session VALUES ('ses_1', 'p', NULL, 'C:\source\portal', 'Wire up the API', {{nine}}, {{nine + 600_000}}, NULL);
                INSERT INTO session VALUES ('ses_child', 'p', 'ses_1', 'C:\source\portal', 'Subtask', {{nine}}, {{nine + 600_000}}, NULL);
                INSERT INTO message VALUES ('m1', 'ses_1', {{nine}}, {{nine}}, '{"role":"user"}');
                INSERT INTO message VALUES ('m2', 'ses_1', {{nine + 300_000}}, {{nine + 300_000}}, '{"role":"assistant"}');
                INSERT INTO message VALUES ('m3', 'ses_child', {{nine + 60_000}}, {{nine + 60_000}}, '{"role":"assistant"}');
                """;
            command.ExecuteNonQuery();
        }

        AgentScanResult result = await new OpenCodeSessionSource().ScanAsync(Scans.November(AgentKind.OpenCode, Path.GetDirectoryName(database)!), CancellationToken.None);
        AgentSession session = result.Sessions.Single();

        ItShouldReadTheTopLevelSession();
        ItShouldReadItsMessageTimes();
        ItShouldClaimWhatItsToolsMade();

        void ItShouldClaimWhatItsToolsMade() => session.Claims.Select(claim => claim.Value).ShouldBe(["5e6f7a8"]);

        void ItShouldReadTheTopLevelSession() => session.ShouldSatisfyAllConditions(
            () => session.SessionId.ShouldBe("ses_1"),
            () => session.Title.ShouldBe("Wire up the API"),
            () => session.ProjectPath.ShouldBe(@"C:\source\portal"));

        void ItShouldReadItsMessageTimes() => session.Timestamps.Count.ShouldBe(2);
    }

    [Test]
    public async Task SessionsAreInTheOldJsonStorage()
    {
        long nine = new DateTimeOffset(2026, 11, 10, 7, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();
        _home.File(@"opencode\storage\session\proj\ses_9.json", $$$"""{"id":"ses_9","directory":"C:\\source\\portal","title":"Old style","time":{"created":{{{nine}}},"updated":{{{nine}}}}}""");
        _home.File(@"opencode\storage\message\ses_9\msg_1.json", $$$"""{"id":"msg_1","sessionID":"ses_9","role":"user","time":{"created":{{{nine}}}}}""");
        _home.File(@"opencode\storage\part\msg_1\prt_1.json", """
            {
              "id": "prt_1",
              "sessionID": "ses_9",
              "messageID": "msg_1",
              "type": "tool",
              "tool": "bash",
              "state": {
                "status": "completed",
                "output": "{\"pullRequestId\": 482}"
              }
            }
            """);

        AgentScanResult result = await new OpenCodeSessionSource().ScanAsync(Scans.November(AgentKind.OpenCode, Path.Combine(_home.Path, "opencode")), CancellationToken.None);

        ItShouldReadTheJsonFiles();
        ItShouldClaimFromThePartFiles();

        void ItShouldReadTheJsonFiles() => result.Sessions.Single().Title.ShouldBe("Old style");

        void ItShouldClaimFromThePartFiles() => result.Sessions.Single().Claims.Select(claim => claim.Value).ShouldBe(["482"]);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        _home.Dispose();
    }
}
