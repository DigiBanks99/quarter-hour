using System.Net;
using QuarterHour.Core.Model;
using QuarterHour.Infrastructure.Calendar;

namespace QuarterHour.IntegrationTests.Calendar;

public sealed class GraphCalendarProviderTests : IDisposable
{
    private const string FirstPage = """
        {
          "value": [
            {
              "id": "AAA1", "subject": "Daily standup", "isAllDay": false, "showAs": "busy", "isCancelled": false, "isOrganizer": false,
              "type": "occurrence", "seriesMasterId": "SERIES", "sensitivity": "normal", "isOnlineMeeting": true,
              "start": { "dateTime": "2026-11-09T07:00:00.0000000", "timeZone": "UTC" },
              "end": { "dateTime": "2026-11-09T07:15:00.0000000", "timeZone": "UTC" },
              "responseStatus": { "response": "accepted", "time": "2026-11-01T00:00:00Z" },
              "organizer": { "emailAddress": { "name": "Thandi", "address": "thandi@example.com" } },
              "attendees": [ { "type": "required" }, { "type": "required" } ],
              "categories": [ "Client" ],
              "location": { "displayName": "Teams" }
            }
          ],
          "@odata.nextLink": "https://graph.example/v1.0/me/calendarView?$skiptoken=abc"
        }
        """;

    private const string SecondPage = """
        {
          "value": [
            {
              "id": "AAA2", "subject": "Leave", "isAllDay": true, "showAs": "oof",
              "start": { "dateTime": "2026-11-13T00:00:00.0000000", "timeZone": "UTC" },
              "end": { "dateTime": "2026-11-14T00:00:00.0000000", "timeZone": "UTC" }
            }
          ]
        }
        """;

    private readonly ScriptedHandler _graph = new(request => request.RequestUri!.Query.Contains("skiptoken", StringComparison.Ordinal)
        ? ScriptedHandler.Json(SecondPage)
        : ScriptedHandler.Json(FirstPage));

    [Test]
    public async Task TheCalendarHasTwoPages()
    {
        GraphCalendarProvider provider = new(new HttpClient(_graph), _ => Task.FromResult("token-1"), "https://graph.example/v1.0");

        IReadOnlyList<CalendarEvent> events = await provider.GetEventsAsync(
            new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.FromHours(2)), new DateTimeOffset(2026, 12, 1, 0, 0, 0, TimeSpan.FromHours(2)), CancellationToken.None);
        CalendarEvent standup = events[0];
        HttpRequestMessage firstRequest = _graph.Requests[0];

        ItShouldFollowTheNextLink();
        ItShouldAskForUtcTimesAsTheUser();
        ItShouldReadWhatTheRulesNeed();
        ItShouldKeepAllDayDates();

        void ItShouldFollowTheNextLink() => events.Select(calendarEvent => calendarEvent.Id).ShouldBe(["AAA1", "AAA2"]);

        void ItShouldAskForUtcTimesAsTheUser() => firstRequest.ShouldSatisfyAllConditions(
            () => firstRequest.Headers.Authorization!.ToString().ShouldBe("Bearer token-1"),
            () => firstRequest.Headers.GetValues("Prefer").Single().ShouldBe("outlook.timezone=\"UTC\""),
            () => firstRequest.RequestUri!.ToString().ShouldContain("startDateTime=2026-10-31T22%3A00%3A00Z"));

        void ItShouldReadWhatTheRulesNeed() => standup.ShouldSatisfyAllConditions(
            () => standup.Start.ShouldBe(new DateTimeOffset(2026, 11, 9, 7, 0, 0, TimeSpan.Zero)),
            () => standup.Response.ShouldBe("accepted"),
            () => standup.Organizer.ShouldBe("Thandi"),
            () => standup.AttendeeCount.ShouldBe(2),
            () => standup.Categories.ShouldBe(["Client"]),
            () => standup.IsRecurring.ShouldBeTrue(),
            () => standup.IsOnlineMeeting.ShouldBeTrue());

        void ItShouldKeepAllDayDates() => events[1].ShouldSatisfyAllConditions(
            () => events[1].IsAllDay.ShouldBeTrue(),
            () => events[1].Start.Date.ShouldBe(new DateTime(2026, 11, 13)));
    }

    [Test]
    public async Task ConsentHasNotBeenGiven()
    {
        ScriptedHandler forbidden = new(_ => ScriptedHandler.Json("""{"error":{"code":"ErrorAccessDenied"}}""", HttpStatusCode.Forbidden));
        GraphCalendarProvider provider = new(new HttpClient(forbidden), _ => Task.FromResult("token"), "https://graph.example/v1.0");

        Exception? exception = await Catch.ExceptionAsync(() => provider.GetEventsAsync(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(1), CancellationToken.None));

        ItShouldExplainWhatPermissionIsNeeded();

        void ItShouldExplainWhatPermissionIsNeeded() =>
            exception.ShouldBeOfType<GraphSignInRequiredException>().Message.ShouldContain("Calendars.ReadBasic");
    }

    public void Dispose() => _graph.Dispose();
}

internal static class Catch
{
    public static async Task<Exception?> ExceptionAsync(Func<Task> action)
    {
        try
        {
            await action();
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }
}
