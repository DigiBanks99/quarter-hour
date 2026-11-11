using System.Text.Json;
using System.Text.Json.Serialization;

namespace QuarterHour.Infrastructure.Storage;

/// <summary>Reads and writes JSON files. Writes go to a temporary file first and replace the old one, keeping a .bak.</summary>
public static class JsonFiles
{
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static T? Read<T>(string path)
        where T : class
    {
        if (!File.Exists(path))
        {
            return null;
        }

        using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        return JsonSerializer.Deserialize<T>(stream, Options);
    }

    public static T? Parse<T>(string json)
        where T : class => JsonSerializer.Deserialize<T>(json, Options);

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);

    public static void Write<T>(string path, T value)
    {
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string temporary = path + ".tmp";
        File.WriteAllText(temporary, Serialize(value));
        if (File.Exists(path))
        {
            try
            {
                File.Replace(temporary, path, path + ".bak", ignoreMetadataErrors: true);
                return;
            }
            catch (IOException)
            {
                // Some file systems can't replace atomically; fall back to a plain move.
            }
            catch (PlatformNotSupportedException)
            {
            }
        }

        File.Move(temporary, path, overwrite: true);
    }
}
