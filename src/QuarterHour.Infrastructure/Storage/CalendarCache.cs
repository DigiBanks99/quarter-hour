using QuarterHour.Core.Model;

namespace QuarterHour.Infrastructure.Storage;

/// <summary>The calendar events last fetched (or imported) for a period, so reviewing works without going back to the source.</summary>
public sealed class CalendarSnapshot
{
    public DateTimeOffset FetchedAt { get; set; }

    /// <summary>"Microsoft Graph", "Outlook" or "Imported file.ics".</summary>
    public string Source { get; set; } = "";

    public List<CalendarEvent> Events { get; set; } = [];
}

public sealed class CalendarCache
{
    private readonly DataDirectory _directory;

    public CalendarCache(DataDirectory directory)
    {
        _directory = directory;
    }

    public CalendarSnapshot? Load(string periodKey) => JsonFiles.Read<CalendarSnapshot>(PathFor(periodKey));

    public void Save(string periodKey, CalendarSnapshot snapshot) => JsonFiles.Write(PathFor(periodKey), snapshot);

    public void Delete(string periodKey)
    {
        string path = PathFor(periodKey);
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private string PathFor(string periodKey) => Path.Combine(_directory.CalendarDirectory, $"{periodKey}.json");
}
