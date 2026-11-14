using System.Text.Json;
using Microsoft.Data.Sqlite;
using QuarterHour.Core.Configuration;
using QuarterHour.Core.Model;
using QuarterHour.Core.Sources;

namespace QuarterHour.Infrastructure.Agents;

/// <summary>
/// OpenCode 1.2 and later keep sessions in a SQLite database (opencode.db: session and message tables, times in
/// milliseconds). Older versions wrote JSON files: storage/session/&lt;project&gt;/&lt;id&gt;.json and
/// storage/message/&lt;session&gt;/&lt;id&gt;.json. Both are read; the database wins when a session is in both.
/// Tool parts (the part table, or storage/part/&lt;message&gt;/&lt;id&gt;.json) hold the claims.
/// </summary>
public sealed class OpenCodeSessionSource : FileSessionSource
{
    public override AgentKind Kind => AgentKind.OpenCode;
    public override string Name => "OpenCode";

    public override IReadOnlyList<string> DefaultLocations()
    {
        List<string> locations = [Path.Combine(Home, ".local", "share", "opencode")];
        if (Environment.GetEnvironmentVariable("XDG_DATA_HOME") is { Length: > 0 } xdg)
        {
            locations.Insert(0, Path.Combine(xdg, "opencode"));
        }

        if (OperatingSystem.IsWindows())
        {
            locations.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "opencode"));
            locations.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "opencode"));
        }

        locations.Add(Path.Combine(Home, ".opencode"));
        return locations;
    }

    protected override IEnumerable<AgentSession> ReadLocation(string location, DateTime modifiedSince, AgentScanRequest request, CancellationToken cancellationToken)
    {
        Dictionary<string, AgentSession> sessions = new(StringComparer.Ordinal);
        string database = File.Exists(location) ? location : Path.Combine(location, "opencode.db");
        if (File.Exists(database))
        {
            foreach (AgentSession session in ReadDatabase(database, request.From, request.To))
            {
                sessions[session.SessionId] = session;
            }
        }

        string storage = Path.Combine(Directory.Exists(location) ? location : Path.GetDirectoryName(location) ?? location, "storage");
        if (Directory.Exists(Path.Combine(storage, "session")))
        {
            foreach (AgentSession session in ReadJsonStorage(storage, modifiedSince, cancellationToken))
            {
                sessions.TryAdd(session.SessionId, session);
            }
        }

        return sessions.Values;
    }

    internal static IReadOnlyList<AgentSession> ReadDatabase(string path, DateTimeOffset from, DateTimeOffset to)
    {
        string connectionString = new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString();
        using SqliteConnection connection = new(connectionString);
        connection.Open();

        HashSet<string> sessionColumns = Columns(connection, "session");
        if (!sessionColumns.Contains("id") || !Columns(connection, "message").Contains("session_id"))
        {
            return [];
        }

        long fromMs = from.ToUnixTimeMilliseconds();
        long toMs = to.ToUnixTimeMilliseconds();
        Dictionary<string, (string? Directory, string? Title)> details = new(StringComparer.Ordinal);
        using (SqliteCommand command = connection.CreateCommand())
        {
            string directory = sessionColumns.Contains("directory") ? "directory" : "NULL";
            string title = sessionColumns.Contains("title") ? "title" : "NULL";
            string children = sessionColumns.Contains("parent_id") ? " AND parent_id IS NULL" : "";
            string updated = sessionColumns.Contains("time_updated") ? " AND time_updated >= $from" : "";
            command.CommandText = $"SELECT id, {directory}, {title} FROM session WHERE 1 = 1{children}{updated}";
            command.Parameters.AddWithValue("$from", fromMs);
            using SqliteDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                details[reader.GetString(0)] = (reader.IsDBNull(1) ? null : reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2));
            }
        }

        Dictionary<string, List<DateTimeOffset>> timestamps = new(StringComparer.Ordinal);
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.CommandText = "SELECT session_id, time_created FROM message WHERE time_created >= $from AND time_created < $to";
            command.Parameters.AddWithValue("$from", fromMs);
            command.Parameters.AddWithValue("$to", toMs);
            using SqliteDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                string sessionId = reader.GetString(0);
                if (!details.ContainsKey(sessionId) || !JsonLines.TryUnix(reader.GetInt64(1), out DateTimeOffset timestamp))
                {
                    continue;
                }

                if (!timestamps.TryGetValue(sessionId, out List<DateTimeOffset>? list))
                {
                    timestamps[sessionId] = list = [];
                }

                list.Add(timestamp);
            }
        }

        Dictionary<string, List<Claim>> claims = new(StringComparer.Ordinal);
        HashSet<string> partColumns = Columns(connection, "part");
        if (partColumns.Contains("session_id") && partColumns.Contains("time_created") && partColumns.Contains("data"))
        {
            using SqliteCommand command = connection.CreateCommand();
            // The marker search runs inside SQLite, so only the few tool parts that might claim something are read.
            command.CommandText = """
                SELECT session_id, time_created, data FROM part
                WHERE time_created >= $from AND time_created < $to AND instr(data, '"tool"') > 0
                  AND (instr(data, '] ') > 0 OR instr(data, 'pullRequestId') > 0 OR instr(data, '/pullrequest/') > 0)
                """;
            command.Parameters.AddWithValue("$from", fromMs);
            command.Parameters.AddWithValue("$to", toMs);
            using SqliteDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                string sessionId = reader.GetString(0);
                if (!details.ContainsKey(sessionId) || reader.IsDBNull(2) || !JsonLines.TryUnix(reader.GetInt64(1), out DateTimeOffset at))
                {
                    continue;
                }

                if (!claims.TryGetValue(sessionId, out List<Claim>? list))
                {
                    claims[sessionId] = list = [];
                }

                list.AddRange(ClaimMatcher.Find(reader.GetString(2), at));
            }
        }

        return timestamps.Select(pair => new AgentSession
        {
            Agent = "opencode",
            SessionId = pair.Key,
            Title = details[pair.Key].Title,
            ProjectPath = details[pair.Key].Directory,
            Timestamps = pair.Value,
            Claims = claims.GetValueOrDefault(pair.Key) ?? [],
        }).ToList();
    }

    internal static IEnumerable<AgentSession> ReadJsonStorage(string storage, DateTime modifiedSince, CancellationToken cancellationToken)
    {
        foreach (string sessionFile in SafeFiles(Path.Combine(storage, "session"), "*.json", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!ModifiedSince(sessionFile, modifiedSince))
            {
                continue;
            }

            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(File.ReadAllBytes(sessionFile));
            }
            catch (Exception exception) when (exception is JsonException or IOException)
            {
                continue;
            }

            using (document)
            {
                JsonElement root = document.RootElement;
                string? id = root.Text("id");
                if (id is null || root.Text("parentID") is not null)
                {
                    continue;
                }

                List<DateTimeOffset> timestamps = [];
                List<Claim> claims = [];
                foreach (string messageFile in SafeFiles(Path.Combine(storage, "message", id), "*.json", SearchOption.TopDirectoryOnly))
                {
                    try
                    {
                        using JsonDocument message = JsonDocument.Parse(File.ReadAllBytes(messageFile));
                        if (long.TryParse(message.RootElement.Text("time", "created"), out long created) && JsonLines.TryUnix(created, out DateTimeOffset timestamp))
                        {
                            timestamps.Add(timestamp);
                            claims.AddRange(PartClaims(storage, Path.GetFileNameWithoutExtension(messageFile), timestamp));
                        }
                    }
                    catch (Exception exception) when (exception is JsonException or IOException)
                    {
                    }
                }

                if (timestamps.Count > 0)
                {
                    yield return new AgentSession
                    {
                        Agent = "opencode",
                        SessionId = id,
                        Title = root.Text("title"),
                        ProjectPath = root.Text("directory"),
                        Timestamps = timestamps,
                        Claims = claims,
                    };
                }
            }
        }
    }

    // Storage files are pretty-printed, so look for the "tool" value alone rather than "type":"tool".
    private static readonly byte[] ToolPart = "\"tool\""u8.ToArray();

    /// <summary>Claims in a message's tool parts, read only when a cheap byte search says they might hold one.</summary>
    private static IEnumerable<Claim> PartClaims(string storage, string messageId, DateTimeOffset at)
    {
        foreach (string partFile in SafeFiles(Path.Combine(storage, "part", messageId), "*.json", SearchOption.TopDirectoryOnly))
        {
            byte[] bytes;
            try
            {
                bytes = File.ReadAllBytes(partFile);
            }
            catch (IOException)
            {
                continue;
            }

            if (bytes.AsSpan().IndexOf(ToolPart) >= 0)
            {
                foreach (Claim claim in ClaimMatcher.Find(bytes, at))
                {
                    yield return claim;
                }
            }
        }
    }

    private static HashSet<string> Columns(SqliteConnection connection, string table)
    {
        HashSet<string> columns = new(StringComparer.OrdinalIgnoreCase);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({table})";
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            columns.Add(reader.GetString(1));
        }

        return columns;
    }
}
