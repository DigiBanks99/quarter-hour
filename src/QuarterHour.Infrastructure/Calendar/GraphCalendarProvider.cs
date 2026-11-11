using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using QuarterHour.Core.Configuration;
using QuarterHour.Core.Model;
using QuarterHour.Core.Sources;

namespace QuarterHour.Infrastructure.Calendar;

/// <summary>
/// Reads the signed-in user's calendar view (recurring meetings expanded into occurrences) from Microsoft Graph.
/// Times are requested in UTC; all-day events keep their dates.
/// </summary>
public sealed class GraphCalendarProvider : ICalendarProvider
{
    public const string DefaultBaseUrl = "https://graph.microsoft.com/v1.0";

    private const string Select =
        "id,subject,start,end,isAllDay,showAs,isCancelled,responseStatus,organizer,attendees,categories,location,isOnlineMeeting,seriesMasterId,type,sensitivity,isOrganizer";

    private readonly HttpClient _http;
    private readonly Func<CancellationToken, Task<string>> _accessToken;
    private readonly string _baseUrl;

    public GraphCalendarProvider(HttpClient http, Func<CancellationToken, Task<string>> accessToken, string baseUrl = DefaultBaseUrl)
    {
        _http = http;
        _accessToken = accessToken;
        _baseUrl = baseUrl.TrimEnd('/');
    }

    public CalendarProviderKind Kind => CalendarProviderKind.MicrosoftGraph;

    public async Task<IReadOnlyList<CalendarEvent>> GetEventsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        string token = await _accessToken(cancellationToken);
        string? url = $"{_baseUrl}/me/calendarView" +
                      $"?startDateTime={Uri.EscapeDataString(from.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss'Z'", CultureInfo.InvariantCulture))}" +
                      $"&endDateTime={Uri.EscapeDataString(to.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss'Z'", CultureInfo.InvariantCulture))}" +
                      $"&$select={Select}&$top=250&$orderby=start/dateTime";

        List<CalendarEvent> events = [];
        int pages = 0;
        while (url is not null && pages++ < 200)
        {
            using JsonDocument page = await GetPageAsync(url, token, cancellationToken);
            if (page.RootElement.TryGetProperty("value", out JsonElement values))
            {
                foreach (JsonElement value in values.EnumerateArray())
                {
                    if (Parse(value) is { } calendarEvent)
                    {
                        events.Add(calendarEvent);
                    }
                }
            }

            url = page.RootElement.TryGetProperty("@odata.nextLink", out JsonElement next) ? next.GetString() : null;
        }

        return events;
    }

    private async Task<JsonDocument> GetPageAsync(string url, string token, CancellationToken cancellationToken)
    {
        for (int attempt = 1; ; attempt++)
        {
            using HttpRequestMessage request = new(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Add("Prefer", "outlook.timezone=\"UTC\"");
            using HttpResponseMessage response = await _http.SendAsync(request, cancellationToken);
            if ((response.StatusCode == HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500) && attempt < 4)
            {
                TimeSpan wait = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(2 * attempt);
                await Task.Delay(wait > TimeSpan.FromSeconds(30) ? TimeSpan.FromSeconds(30) : wait, cancellationToken);
                continue;
            }

            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                string detail = await response.Content.ReadAsStringAsync(cancellationToken);
                throw new GraphSignInRequiredException(
                    $"Microsoft Graph refused to read the calendar ({(int)response.StatusCode}). Check the app registration has the delegated " +
                    $"Calendars.ReadBasic (or Calendars.Read) permission with consent, then sign in again. {Shorten(detail)}");
            }

            if (!response.IsSuccessStatusCode)
            {
                string detail = await response.Content.ReadAsStringAsync(cancellationToken);
                throw new HttpRequestException($"Microsoft Graph returned {(int)response.StatusCode}: {Shorten(detail)}", null, response.StatusCode);
            }

            await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        }
    }

    internal static CalendarEvent? Parse(JsonElement value)
    {
        string? id = Text(value, "id");
        if (id is null || !TryTime(value, "start", out DateTimeOffset start) || !TryTime(value, "end", out DateTimeOffset end))
        {
            return null;
        }

        JsonElement organizer = value.TryGetProperty("organizer", out JsonElement organizerElement) && organizerElement.TryGetProperty("emailAddress", out JsonElement email)
            ? email
            : default;
        string type = Text(value, "type") ?? "singleInstance";
        return new CalendarEvent
        {
            Id = id,
            Subject = Text(value, "subject") ?? "",
            Start = start,
            End = end,
            IsAllDay = Flag(value, "isAllDay"),
            ShowAs = Text(value, "showAs") ?? "busy",
            Response = value.TryGetProperty("responseStatus", out JsonElement status) ? Text(status, "response") ?? "none" : "none",
            Organizer = organizer.ValueKind == JsonValueKind.Object ? Text(organizer, "name") : null,
            OrganizerEmail = organizer.ValueKind == JsonValueKind.Object ? Text(organizer, "address") : null,
            IsOrganizer = Flag(value, "isOrganizer"),
            AttendeeCount = value.TryGetProperty("attendees", out JsonElement attendees) && attendees.ValueKind == JsonValueKind.Array ? attendees.GetArrayLength() : 0,
            Location = value.TryGetProperty("location", out JsonElement location) ? Text(location, "displayName") : null,
            Categories = value.TryGetProperty("categories", out JsonElement categories) && categories.ValueKind == JsonValueKind.Array
                ? [.. categories.EnumerateArray().Select(category => category.GetString()).OfType<string>()]
                : [],
            IsOnlineMeeting = Flag(value, "isOnlineMeeting"),
            IsRecurring = type is "occurrence" or "exception",
            SeriesId = Text(value, "seriesMasterId"),
            Sensitivity = Text(value, "sensitivity"),
            IsCancelled = Flag(value, "isCancelled"),
        };
    }

    private static bool TryTime(JsonElement value, string property, out DateTimeOffset time)
    {
        time = default;
        if (!value.TryGetProperty(property, out JsonElement element) || Text(element, "dateTime") is not { } text)
        {
            return false;
        }

        if (!DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out DateTime parsed))
        {
            return false;
        }

        time = new DateTimeOffset(DateTime.SpecifyKind(parsed, DateTimeKind.Utc));
        return true;
    }

    private static string? Text(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool Flag(JsonElement element, string property) =>
        element.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.True;

    private static string Shorten(string text) => text.Length <= 300 ? text : text[..300] + "…";
}
