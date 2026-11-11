using System.Net;
using System.Text;
using QuarterHour.Core.Configuration;
using QuarterHour.Core.Sources;

namespace QuarterHour.IntegrationTests;

internal sealed class TemporaryDirectory : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "quarter-hour-tests", Guid.NewGuid().ToString("N"));

    public TemporaryDirectory() => Directory.CreateDirectory(Path);

    public string File(string relativePath, string content, DateTime? modifiedUtc = null)
    {
        string full = System.IO.Path.Combine(Path, relativePath);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
        System.IO.File.WriteAllText(full, content);
        if (modifiedUtc is { } modified)
        {
            System.IO.File.SetLastWriteTimeUtc(full, modified);
        }

        return full;
    }

    public string Folder(string relativePath)
    {
        string full = System.IO.Path.Combine(Path, relativePath);
        Directory.CreateDirectory(full);
        return full;
    }

    public void Dispose()
    {
        // A file can still be held briefly (SQLite, a scanner); a leftover temporary directory is harmless.
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}

/// <summary>Answers HTTP requests from a script, and remembers what was asked.</summary>
internal sealed class ScriptedHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
{
    public List<HttpRequestMessage> Requests { get; } = [];

    public static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        lock (Requests)
        {
            Requests.Add(request);
        }

        return Task.FromResult(respond(request));
    }
}

internal static class Scans
{
    public static readonly TimeZoneInfo SouthAfrica = TimeZoneInfo.CreateCustomTimeZone("SAST", TimeSpan.FromHours(2), "SAST", "SAST");
    public static readonly DateTimeOffset NovemberStart = new(2026, 11, 1, 0, 0, 0, TimeSpan.FromHours(2));
    public static readonly DateTimeOffset NovemberEnd = new(2026, 12, 1, 0, 0, 0, TimeSpan.FromHours(2));

    public static AgentScanRequest November(AgentKind kind, params string[] paths) => November(new AgentSettings
    {
        Scanners = [new AgentScannerSettings { Kind = kind, Paths = [.. paths] }],
    });

    public static AgentScanRequest November(AgentSettings settings) => new(NovemberStart, NovemberEnd, settings, SouthAfrica);
}
