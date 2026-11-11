# QuarterHour

Builds the monthly Entelect timesheet from your calendar and your AI agent coding sessions, then writes the
`TimesheetExport.xlsx` the Timesheet system takes (Date, Project, Category, Hours, Minutes, Billable, Description,
Ticket Number, Sentiment, Worked From — every cell as text, in a `Timesheet` table).

## Run it

Needs the .NET 10 SDK.

```powershell
aspire start
```

To take it to a machine without the SDK, publish a self-contained build and copy the folder:

```powershell
dotnet publish src/QuarterHour.App -c Release -r win-x64 --self-contained -o dist/QuarterHour
dist/QuarterHour/QuarterHour.exe                # opens the browser itself
```

Data lives in `%LOCALAPPDATA%\QuarterHour` (`config.json`, `journal.json`, cached calendars). Set
`QUARTERHOUR_DATA` to keep it elsewhere. **Settings → Data** downloads/imports the configuration to move it between machines.

## First-time setup

1. **Categories → Learn from a previous timesheet**: upload last month's `TimesheetExport.xlsx`. It renames the
   "Client project" placeholder to your real project, maps recurring meetings, learns your weekday locations,
   and turns on the daily top-up with the category your "what I worked on" rows used (e.g. Analysis).
2. **Settings → Calendar**: choose a source.
   - *Microsoft Graph*: register an app in Entra ID (Mobile and desktop platform, redirect URI `http://localhost`,
     allow public client flows, delegated **Calendars.ReadBasic**), paste its client ID, sign in. Full steps are in the app.
   - *Classic Outlook on this PC*: reads Outlook through COM — no registration needed (not the new Outlook).
   - *.ics import*: Outlook → File → Save Calendar, then import the file for the period.
3. **Settings → Agent sessions**: Claude Code, Copilot CLI, Codex, OpenCode (SQLite and older JSON) and Pi are
   read directly from their usual folders. Turn on **AgentsView** (`agentsview serve`) for everything else.
4. **Mapping**: drag each meeting series / project onto a category (or "Not relevant"), or pull a line from its
   handle. Pin meetings that always happen somewhere (e.g. coffee runs at Client).
5. **Overview → Day review → Export**: fix gaps and unmapped items, then download the .xlsx and upload it.

## How time is counted

- **Rules**: relevance filters (cancelled, declined, free, all-day reminders) → your direct mappings → heuristic
  rules in order (regex, organiser, attendees, Outlook categories, show-as, project, branch…). First match wins.
- **Agent sessions** become blocks of activity; a pause longer than 15 min ends a block, and 5 min is added after each.
- **Overlaps**: manual entries beat meetings, meetings beat agent work; agents running side by side split the
  overlap evenly; double-booked meetings: the earlier one wins.
- **Rounding** to 15 min, rounding up from 7 min past the quarter (50m → 45m, 3h22m → 3h30m), per meeting and per
  day's agent work per category/location. Strict "nearest" is a setting (round up from 8).
- **Location**: entry change → meeting pin → day setting → weekly/fortnightly/monthly schedule → default.
- **Targets**: warns below 160 billable hours (less leave); flags working days short of 8 h; SA public holidays built in.
- On a full leave day, meetings still in the calendar are left out.

## Develop

```powershell
dotnet build QuarterHour.slnx
dotnet test --solution QuarterHour.slnx       # TUnit + Shouldly
python samples/make-sample-calendar.py         # regenerates samples/november-2026.ics
```

`src/QuarterHour.Core` is the pure domain (rules, timeline, rounding, locations, export rows, learning),
`src/QuarterHour.Infrastructure` the readers/writers (Graph, Outlook, .ics, agent stores, AgentsView, ClosedXML),
`src/QuarterHour.App` the Blazor Server + MudBlazor UI (`wwwroot/js/mapping-board.js` draws the board's links).
