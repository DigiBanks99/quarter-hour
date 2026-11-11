namespace QuarterHour.Infrastructure.Storage;

/// <summary>
/// Where QuarterHour keeps its files: %LOCALAPPDATA%\QuarterHour by default, or QUARTERHOUR_DATA / the configured
/// "QuarterHour:DataDirectory" setting.
/// </summary>
public sealed class DataDirectory
{
    public DataDirectory(string? root = null)
    {
        Root = Path.GetFullPath(string.IsNullOrWhiteSpace(root) ? DefaultRoot() : root);
        Directory.CreateDirectory(Root);
    }

    public string Root { get; }
    public string ConfigFile => Path.Combine(Root, "config.json");
    public string JournalFile => Path.Combine(Root, "journal.json");
    public string CalendarDirectory => Path.Combine(Root, "calendar");
    public string TokenCacheFileName => "msal-token-cache.bin";

    public static string DefaultRoot() =>
        Environment.GetEnvironmentVariable("QUARTERHOUR_DATA") is { Length: > 0 } fromEnvironment
            ? fromEnvironment
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "QuarterHour");
}
