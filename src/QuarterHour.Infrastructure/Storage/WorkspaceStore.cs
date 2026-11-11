using System.Text.Json;
using QuarterHour.Core.Configuration;

namespace QuarterHour.Infrastructure.Storage;

/// <summary>Loads and saves the configuration (config.json) and the user's corrections (journal.json).</summary>
public sealed class WorkspaceStore
{
    private readonly DataDirectory _directory;

    public WorkspaceStore(DataDirectory directory)
    {
        _directory = directory;
    }

    public string DataRoot => _directory.Root;

    /// <summary>Loads the configuration, creating the starter configuration on first run.</summary>
    public WorkspaceConfig LoadConfig()
    {
        WorkspaceConfig? config = JsonFiles.Read<WorkspaceConfig>(_directory.ConfigFile);
        if (config is not null)
        {
            return config.Normalize();
        }

        WorkspaceConfig created = DefaultWorkspace.Create();
        SaveConfig(created);
        return created;
    }

    public void SaveConfig(WorkspaceConfig config) => JsonFiles.Write(_directory.ConfigFile, config);

    public Journal LoadJournal() => (JsonFiles.Read<Journal>(_directory.JournalFile) ?? new Journal()).Normalize();

    public void SaveJournal(Journal journal) => JsonFiles.Write(_directory.JournalFile, journal);

    public static string ExportConfig(WorkspaceConfig config) => JsonFiles.Serialize(config);

    /// <summary>Reads a configuration exported from another machine. Throws <see cref="JsonException"/> when it isn't one.</summary>
    public static WorkspaceConfig ImportConfig(string json)
    {
        WorkspaceConfig? config = JsonFiles.Parse<WorkspaceConfig>(json);
        if (config is null || config.SchemaVersion < 1)
        {
            throw new JsonException("That file isn't a QuarterHour configuration.");
        }

        return config.Normalize();
    }
}
