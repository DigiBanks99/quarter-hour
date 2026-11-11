"""Writes samples/november-2026.ics: a month of typical (made-up) meetings for trying QuarterHour without Graph access."""
import pathlib

TZ = "South Africa Standard Time"
events = []

def event(uid, summary, start, end, rrule=None, busy="BUSY", status=None, allday=False, organizer="Alex Mokoena", extra=()):
    lines = ["BEGIN:VEVENT", f"UID:{uid}", f"SUMMARY:{summary}"]
    if allday:
        lines += [f"DTSTART;VALUE=DATE:{start}", f"DTEND;VALUE=DATE:{end}"]
    else:
        lines += [f"DTSTART;TZID={TZ}:{start}", f"DTEND;TZID={TZ}:{end}"]
    if rrule:
        lines.append(f"RRULE:{rrule}")
    if organizer:
        lines.append(f"ORGANIZER;CN={organizer}:mailto:{organizer.split()[0].lower()}@example.com")
        lines.append("ATTENDEE;CN=Me:mailto:me@example.com")
        lines.append("ATTENDEE;CN=Team:mailto:team@example.com")
    if status:
        lines.append(f"STATUS:{status}")
    lines.append(f"X-MICROSOFT-CDO-BUSYSTATUS:{busy}")
    lines += list(extra)
    lines.append("END:VEVENT")
    events.append("\r\n".join(lines))

event("standup", "Scrum and change stand-up", "20261102T084500", "20261102T091500", "FREQ=WEEKLY;BYDAY=MO,TU,WE,TH,FR;UNTIL=20261231T000000Z")
event("planning", "Sprint planning", "20261103T100000", "20261103T110000", "FREQ=WEEKLY;INTERVAL=2;BYDAY=TU;UNTIL=20261231T000000Z")
event("eng", "Engine-ering team meeting", "20261102T140000", "20261102T150000", "FREQ=WEEKLY;BYDAY=MO;UNTIL=20261231T000000Z")
event("coffee", "W&I Coffee Run", "20261102T103000", "20261102T110000", "FREQ=WEEKLY;BYDAY=MO,TH;UNTIL=20261231T000000Z", organizer="Sam Naidoo")
event("one-on-one", "1:1 Alex / Me", "20261105T150000", "20261105T160000", "FREQ=WEEKLY;BYDAY=TH;UNTIL=20261231T000000Z")
event("focus", "Focus time", "20261104T130000", "20261104T160000", "FREQ=WEEKLY;BYDAY=WE;UNTIL=20261231T000000Z", organizer=None)
event("lunch", "Lunch", "20261102T120000", "20261102T123000", "FREQ=WEEKLY;BYDAY=MO,TU,WE,TH,FR;UNTIL=20261231T000000Z", organizer=None)
event("imagineers", "Wealth Imagineers alignment", "20261110T160000", "20261110T161500", "FREQ=WEEKLY;INTERVAL=2;BYDAY=TU;UNTIL=20261231T000000Z")
event("cnc", "Coffee & Code: November Open Mic", "20261120T120000", "20261120T130000", organizer="Entelect Events")
event("review", "Online review capture for Jordan", "20261112T140000", "20261112T151500")
event("team-lunch", "Team lunch", "20261127T120000", "20261127T140000", organizer="Sam Naidoo")
event("quality", "Quality review", "20261118T110000", "20261118T113000")
event("arch", "Cancelled: Architecture sync", "20261113T110000", "20261113T120000", status="CANCELLED")
event("vendor", "Vendor demo", "20261119T090000", "20261119T100000")
event("leave", "Leave", "20261106", "20261107", busy="OOF", allday=True, organizer=None)
event("birthday", "Birthday: Lindiwe", "20261117", "20261118", busy="FREE", allday=True, organizer=None)
event("hackathon", "Hackathon prep (Saturday)", "20261114T090000", "20261114T110000", organizer=None)

ics = "\r\n".join([
    "BEGIN:VCALENDAR", "VERSION:2.0", "PRODID:-//QuarterHour//Sample//EN",
    "BEGIN:VTIMEZONE", f"TZID:{TZ}", "BEGIN:STANDARD", "DTSTART:16010101T000000", "TZOFFSETFROM:+0200", "TZOFFSETTO:+0200", "END:STANDARD", "END:VTIMEZONE",
    *events,
    "END:VCALENDAR", ""])
path = pathlib.Path(__file__).with_name("november-2026.ics")
path.write_bytes(ics.encode("utf-8"))
print(f"wrote {path} ({len(events)} events)")
