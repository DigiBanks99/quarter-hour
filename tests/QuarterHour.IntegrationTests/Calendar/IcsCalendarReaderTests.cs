using QuarterHour.Core.Model;
using QuarterHour.Infrastructure.Calendar;

namespace QuarterHour.IntegrationTests.Calendar;

public sealed class IcsCalendarReaderTests
{
    private const string Export = """
        BEGIN:VCALENDAR
        VERSION:2.0
        PRODID:-//Microsoft Corporation//Outlook 16.0 MIMEDIR//EN
        BEGIN:VTIMEZONE
        TZID:South Africa Standard Time
        BEGIN:STANDARD
        DTSTART:16010101T000000
        TZOFFSETFROM:+0200
        TZOFFSETTO:+0200
        END:STANDARD
        END:VTIMEZONE
        BEGIN:VEVENT
        UID:standup-1
        SUMMARY:Daily standup
        DTSTART;TZID=South Africa Standard Time:20261102T090000
        DTEND;TZID=South Africa Standard Time:20261102T091500
        RRULE:FREQ=WEEKLY;BYDAY=MO,TU,WE,TH,FR;UNTIL=20261231T000000Z
        EXDATE;TZID=South Africa Standard Time:20261103T090000
        ORGANIZER;CN=Thandi:mailto:thandi@example.com
        ATTENDEE;CN=Wilco:mailto:wilco@example.com
        CATEGORIES:Client
        X-MICROSOFT-CDO-BUSYSTATUS:BUSY
        END:VEVENT
        BEGIN:VEVENT
        UID:leave-1
        SUMMARY:Leave
        DTSTART;VALUE=DATE:20261113
        DTEND;VALUE=DATE:20261114
        X-MICROSOFT-CDO-BUSYSTATUS:OOF
        END:VEVENT
        BEGIN:VEVENT
        UID:gone-1
        SUMMARY:Cancelled: Retro
        STATUS:CANCELLED
        DTSTART:20261106T120000Z
        DTEND:20261106T130000Z
        END:VEVENT
        END:VCALENDAR
        """;

    [Test]
    public void AnOutlookExportIsImported()
    {
        IReadOnlyList<CalendarEvent> events = IcsCalendarReader.Read(Export,
            new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.FromHours(2)), new DateTimeOffset(2026, 12, 1, 0, 0, 0, TimeSpan.FromHours(2)));
        List<CalendarEvent> standups = events.Where(calendarEvent => calendarEvent.Subject == "Daily standup").ToList();
        CalendarEvent leave = events.Single(calendarEvent => calendarEvent.Subject == "Leave");

        ItShouldExpandTheRecurringStandup();
        ItShouldConvertTheTimeZone();
        ItShouldKeepOutlooksBusyStatus();
        ItShouldMarkTheCancelledMeeting();

        // 21 weekdays in November 2026, less the one excluded occurrence.
        void ItShouldExpandTheRecurringStandup() => standups.Count.ShouldBe(20);

        void ItShouldConvertTheTimeZone() => standups[0].Start.ShouldBe(new DateTimeOffset(2026, 11, 2, 7, 0, 0, TimeSpan.Zero));

        void ItShouldKeepOutlooksBusyStatus() => leave.ShouldSatisfyAllConditions(
            () => leave.ShowAs.ShouldBe("oof"),
            () => leave.IsAllDay.ShouldBeTrue());

        void ItShouldMarkTheCancelledMeeting() => events.Single(calendarEvent => calendarEvent.Subject.Contains("Retro")).IsCancelled.ShouldBeTrue();
    }
}
