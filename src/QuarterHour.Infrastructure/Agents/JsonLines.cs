using System.Globalization;
using System.Text;
using System.Text.Json;

namespace QuarterHour.Infrastructure.Agents;

/// <summary>
/// Fast, forgiving JSONL reading: files are opened without blocking the agent that is writing them, and only the
/// wanted top-level properties of each line are read (large nested tool output is skipped, not parsed).
/// </summary>
internal static class JsonLines
{
    public static byte[] ReadShared(string path)
    {
        using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16);
        using MemoryStream buffer = new(stream.CanSeek ? (int)Math.Min(stream.Length, int.MaxValue) : 1 << 16);
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    public static IEnumerable<ReadOnlyMemory<byte>> Lines(byte[] data)
    {
        int start = 0;
        while (start < data.Length)
        {
            int newline = Array.IndexOf(data, (byte)'\n', start);
            int end = newline < 0 ? data.Length : newline;
            int length = end - start;
            if (length > 0 && data[end - 1] == (byte)'\r')
            {
                length--;
            }

            if (length > 0)
            {
                yield return new ReadOnlyMemory<byte>(data, start, length);
            }

            start = end + 1;
        }
    }

    /// <summary>Reads the wanted top-level properties of a JSON object as text (true/false and numbers too). Null when it isn't JSON.</summary>
    public static Dictionary<string, string?>? TopLevel(ReadOnlySpan<byte> line, IReadOnlyList<string> wanted)
    {
        Utf8JsonReader reader = new(line, new JsonReaderOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
        try
        {
            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
            {
                return null;
            }

            Dictionary<string, string?> values = new(StringComparer.Ordinal);
            while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
            {
                string? name = null;
                foreach (string candidate in wanted)
                {
                    if (reader.ValueTextEquals(candidate))
                    {
                        name = candidate;
                        break;
                    }
                }

                if (!reader.Read())
                {
                    break;
                }

                if (name is null)
                {
                    reader.Skip();
                    continue;
                }

                switch (reader.TokenType)
                {
                    case JsonTokenType.String:
                        values[name] = reader.GetString();
                        break;
                    case JsonTokenType.True:
                        values[name] = "true";
                        break;
                    case JsonTokenType.False:
                        values[name] = "false";
                        break;
                    case JsonTokenType.Number:
                        values[name] = Encoding.UTF8.GetString(reader.ValueSpan);
                        break;
                    case JsonTokenType.Null:
                        values[name] = null;
                        break;
                    default:
                        reader.Skip();
                        break;
                }
            }

            return values;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static JsonDocument? Document(ReadOnlyMemory<byte> line)
    {
        try
        {
            return JsonDocument.Parse(line);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>ISO 8601 text, or Unix time in seconds or milliseconds.</summary>
    public static bool TryTimestamp(string? text, out DateTimeOffset timestamp)
    {
        timestamp = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out long number))
        {
            return TryUnix(number, out timestamp);
        }

        return DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out timestamp);
    }

    public static bool TryUnix(long value, out DateTimeOffset timestamp)
    {
        timestamp = default;
        if (value <= 0)
        {
            return false;
        }

        try
        {
            timestamp = value > 100_000_000_000 ? DateTimeOffset.FromUnixTimeMilliseconds(value) : DateTimeOffset.FromUnixTimeSeconds(value);
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }

    public static string? Text(this JsonElement element, params string[] path)
    {
        JsonElement current = element;
        foreach (string segment in path)
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(segment, out current))
            {
                return null;
            }
        }

        return current.ValueKind switch
        {
            JsonValueKind.String => current.GetString(),
            JsonValueKind.Number => current.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null,
        };
    }
}
