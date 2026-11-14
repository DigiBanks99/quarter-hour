using System.Globalization;
using QuarterHour.Core.Configuration;
using QuarterHour.Core.Locations;
using QuarterHour.Core.Model;
using QuarterHour.Core.Rules;
using QuarterHour.Core.Time;
using QuarterHour.Core.Timeline;

namespace QuarterHour.Core.Timesheets;

/// <summary>
/// Builds a period's timesheet from raw activities:
/// classify (rules) → apply overrides → split per day → resolve overlaps → resolve locations →
/// group into lines (meetings one each, agent work per day/category/location) → round → total.
/// </summary>
public sealed class TimesheetBuilder
{
    private const int ManualPriority = 3;
    private const int CalendarPriority = 2;
    private const int AgentPriority = 1;
    private const int MaxDescriptionLength = 500;

    /// <summary>Agents running side by side split the overlapping time evenly.</summary>
    private static readonly IReadOnlySet<int> SharedPriorities = new HashSet<int> { AgentPriority };

    public PeriodTimesheet Build(TimesheetPeriod period, WorkspaceConfig config, Journal journal, IReadOnlyList<Activity> sourceActivities, DateOnly? today = null)
    {
        WorkCalendar workCalendar = new(config.Targets);
        RuleEngine rules = new(config.Rules);
        LocationResolver locations = new(config.Locations, journal);
        Dictionary<string, Category> categories = config.Categories.ToDictionary(category => category.Id);
        Dictionary<DateOnly, ResolvedLocation> dayLocations = period.Days().ToDictionary(day => day, locations.ForDay);

        List<Piece> pieces = [];
        foreach (Activity activity in sourceActivities.Concat(ManualActivities(journal, period)))
        {
            RuleMatch match = rules.Classify(activity);
            foreach (Piece piece in Split(activity, period, workCalendar, config.Targets))
            {
                Decide(piece, match, rules, journal, categories);
                pieces.Add(piece);
            }
        }

        if (config.Targets.IgnoreMeetingsOnLeaveDays)
        {
            LeaveOutMeetingsOnLeaveDays(pieces, categories, config.Targets);
        }

        ResolveOverlaps(pieces, config.General.OverlapPolicy);
        foreach (Piece piece in pieces)
        {
            piece.Location = LocationFor(piece, dayLocations[piece.Date]);
            piece.Description = DescriptionFor(piece);
        }

        List<TimesheetLine> lines = BuildLines(pieces, config.Rounding, config.Export.DefaultSentiment);
        lines = TopUp(lines, pieces, config, categories, workCalendar, dayLocations, today);
        lines = ApplyLineOverrides(lines, journal);
        List<ActivityResult> results = pieces
            .OrderBy(piece => piece.Start)
            .ThenBy(piece => piece.Id, StringComparer.Ordinal)
            .Select(piece => piece.ToResult())
            .ToList();

        List<DaySummary> days = period.Days()
            .Select(day => Summarise(day, workCalendar, dayLocations[day], lines, pieces, categories, journal, today))
            .ToList();

        return new PeriodTimesheet
        {
            Period = period,
            Activities = results,
            Lines = lines,
            Days = days,
            Totals = Totalise(lines, days, pieces, categories, config.Targets),
        };
    }

    private static IEnumerable<Activity> ManualActivities(Journal journal, TimesheetPeriod period)
    {
        foreach (ManualEntry entry in journal.ManualEntries.Where(entry => period.Contains(entry.Date)))
        {
            DateTime dayStart = entry.Date.ToDateTime(TimeOnly.MinValue);
            DateTime start = entry.Start is { } time ? entry.Date.ToDateTime(time) : dayStart;
            string description = string.IsNullOrWhiteSpace(entry.Description) ? "Manual entry" : entry.Description.Trim();
            yield return new Activity
            {
                Id = $"manual:{entry.Id}",
                Source = ActivitySource.Manual,
                Start = start,
                End = entry.Start is null ? start : start.AddMinutes(Math.Max(0, entry.Minutes)),
                IsTimed = entry.Start is not null,
                FixedMinutes = entry.Start is null ? Math.Max(0, entry.Minutes) : null,
                Title = description,
                GroupKey = "manual",
                GroupLabel = "Manual entries",
                ManualCategoryId = entry.CategoryId,
                ManualLocation = entry.Location,
            };
        }
    }

    private static IEnumerable<Piece> Split(Activity activity, TimesheetPeriod period, WorkCalendar workCalendar, TargetSettings targets)
    {
        if (!activity.IsTimed)
        {
            if (activity.IsAllDay)
            {
                DateOnly first = DateOnly.FromDateTime(activity.Start);
                DateOnly afterLast = DateOnly.FromDateTime(activity.End);
                if (afterLast <= first)
                {
                    afterLast = first.AddDays(1);
                }

                bool multiDay = afterLast.DayNumber - first.DayNumber > 1;
                int dayMinutes = (int)Math.Round(targets.AllDayEventHours * 60);
                for (DateOnly day = first; day < afterLast; day = day.AddDays(1))
                {
                    if (!period.Contains(day))
                    {
                        continue;
                    }

                    DateTime dayStart = day.ToDateTime(TimeOnly.MinValue);
                    yield return new Piece(activity, multiDay ? PieceId(activity, day) : activity.Id, day, dayStart, dayStart.AddDays(1), timed: false,
                        TimeSpan.FromMinutes(workCalendar.IsWorkDay(day) ? dayMinutes : 0));
                }

                yield break;
            }

            DateOnly date = DateOnly.FromDateTime(activity.Start);
            if (period.Contains(date))
            {
                yield return new Piece(activity, activity.Id, date, activity.Start, activity.Start, timed: false,
                    TimeSpan.FromMinutes(activity.FixedMinutes ?? 0));
            }

            yield break;
        }

        DateOnly startDate = DateOnly.FromDateTime(activity.Start);
        DateOnly endDate = DateOnly.FromDateTime(activity.End > activity.Start ? activity.End.AddTicks(-1) : activity.Start);
        bool spansDays = endDate > startDate;
        for (DateOnly day = startDate; day <= endDate; day = day.AddDays(1))
        {
            if (!period.Contains(day))
            {
                continue;
            }

            DateTime dayStart = day.ToDateTime(TimeOnly.MinValue);
            DateTime pieceStart = activity.Start > dayStart ? activity.Start : dayStart;
            DateTime pieceEnd = activity.End < dayStart.AddDays(1) ? activity.End : dayStart.AddDays(1);
            if (pieceEnd < pieceStart)
            {
                pieceEnd = pieceStart;
            }

            yield return new Piece(activity, spansDays ? PieceId(activity, day) : activity.Id, day, pieceStart, pieceEnd, timed: true, pieceEnd - pieceStart);
        }
    }

    private static string PieceId(Activity activity, DateOnly day) =>
        $"{activity.Id}@{day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}";

    private static void Decide(Piece piece, RuleMatch match, RuleEngine rules, Journal journal, Dictionary<string, Category> categories)
    {
        Activity activity = piece.Activity;
        ActivityOverride? activityOverride = journal.Overrides.GetValueOrDefault(piece.Id)
            ?? (piece.Id != activity.Id ? journal.Overrides.GetValueOrDefault(activity.Id) : null);
        piece.Match = match;
        piece.Override = activityOverride;

        if (activityOverride?.Excluded == true)
        {
            piece.Status = ActivityStatus.Excluded;
            piece.MappingRule = match.Rule;
            return;
        }

        bool forcedIn = activityOverride?.Excluded == false || activityOverride?.CategoryId is not null;
        RuleMatch mapping = match;
        if (match.Outcome == RuleOutcome.Filtered && forcedIn)
        {
            mapping = rules.ClassifyIgnoringFilters(activity);
        }

        piece.MappingRule = mapping.Outcome is RuleOutcome.Mapped or RuleOutcome.NotRelevant ? mapping.Rule : match.Rule;
        string? categoryId = activityOverride?.CategoryId;
        ActivityStatus status;
        switch (mapping.Outcome)
        {
            case RuleOutcome.Filtered:
                status = ActivityStatus.Filtered;
                break;
            case RuleOutcome.NotRelevant:
                status = forcedIn ? ActivityStatus.Counted : ActivityStatus.NotRelevant;
                break;
            case RuleOutcome.Mapped:
                status = ActivityStatus.Counted;
                categoryId ??= mapping.Rule?.CategoryId;
                break;
            case RuleOutcome.Manual:
                status = ActivityStatus.Counted;
                categoryId ??= activity.ManualCategoryId;
                break;
            default:
                status = ActivityStatus.Counted;
                break;
        }

        if (status == ActivityStatus.Counted && (categoryId is null || !categories.ContainsKey(categoryId)))
        {
            status = ActivityStatus.Unmapped;
            categoryId = null;
        }

        piece.Status = status;
        piece.CategoryId = categoryId;
    }

    /// <summary>
    /// Leave usually doesn't clear the calendar: recurring meetings stay in it. On a day with a full day of leave,
    /// meetings are left out unless the user chose to count them.
    /// </summary>
    private static void LeaveOutMeetingsOnLeaveDays(List<Piece> pieces, Dictionary<string, Category> categories, TargetSettings targets)
    {
        TimeSpan fullDay = TimeSpan.FromHours(targets.StandardDayHours);
        HashSet<DateOnly> leaveDays = pieces
            .Where(piece => piece.Status == ActivityStatus.Counted && piece.Raw >= fullDay && IsLeaveCategory(piece.CategoryId, categories))
            .Select(piece => piece.Date)
            .ToHashSet();
        foreach (Piece piece in pieces)
        {
            bool chosenByUser = piece.Override?.Excluded == false || piece.Override?.CategoryId is not null;
            if (leaveDays.Contains(piece.Date) && piece.Activity.Source == ActivitySource.Calendar && piece.IsCounting
                && !IsLeaveCategory(piece.CategoryId, categories) && !chosenByUser)
            {
                piece.Status = ActivityStatus.Filtered;
                piece.Note = "On leave that day";
            }
        }
    }

    private static bool IsLeaveCategory(string? categoryId, Dictionary<string, Category> categories) =>
        categoryId is not null && categories.TryGetValue(categoryId, out Category? category) && category.IsLeave;

    private static void ResolveOverlaps(List<Piece> pieces, OverlapPolicy policy)
    {
        foreach (IGrouping<DateOnly, Piece> day in pieces.GroupBy(piece => piece.Date))
        {
            List<Piece> onTimeline = day.Where(piece => piece.IsCounting && piece.Timed && piece.Raw > TimeSpan.Zero).ToList();
            IReadOnlyDictionary<string, TimelineSlot> slots = TimelineResolver.Resolve(
                onTimeline.Select(piece => new TimelineItem(piece.Id, piece.Start, piece.End, Priority(piece.Activity.Source))),
                policy,
                SharedPriorities);

            foreach (Piece piece in day)
            {
                if (!piece.IsCounting)
                {
                    piece.Counted = TimeSpan.Zero;
                    continue;
                }

                if (slots.TryGetValue(piece.Id, out TimelineSlot? slot))
                {
                    piece.Counted = slot.Effective;
                    piece.OverlappedBy = slot.OverlappedBy;
                }
                else
                {
                    piece.Counted = piece.Raw;
                }

                if (piece.Override?.Minutes is { } minutes)
                {
                    piece.Counted = TimeSpan.FromMinutes(Math.Max(0, minutes));
                }
            }
        }
    }

    private static int Priority(ActivitySource source) => source switch
    {
        ActivitySource.Manual => ManualPriority,
        ActivitySource.Calendar => CalendarPriority,
        _ => AgentPriority,
    };

    private static ResolvedLocation LocationFor(Piece piece, ResolvedLocation dayLocation)
    {
        if (!string.IsNullOrWhiteSpace(piece.Override?.Location))
        {
            return new ResolvedLocation(piece.Override.Location, LocationOrigin.EntryOverride, "Set on this entry");
        }

        if (piece.Activity.Source == ActivitySource.Manual && !string.IsNullOrWhiteSpace(piece.Activity.ManualLocation))
        {
            return new ResolvedLocation(piece.Activity.ManualLocation, LocationOrigin.ManualEntry, "Set on the manual entry");
        }

        if (piece.MappingRule is { Action: RuleAction.Map, LocationPin: { Length: > 0 } pin } rule)
        {
            return new ResolvedLocation(pin, LocationOrigin.MeetingPin, $"Pinned by \"{rule.Name}\"");
        }

        return dayLocation;
    }

    private static string DescriptionFor(Piece piece)
    {
        if (!string.IsNullOrWhiteSpace(piece.Override?.Description))
        {
            return piece.Override.Description.Trim();
        }

        if (piece.MappingRule is { DescriptionTemplate: { Length: > 0 } template })
        {
            string rendered = DescriptionTemplate.Render(template, piece.Activity);
            if (rendered.Length > 0)
            {
                return rendered;
            }
        }

        return piece.Activity.Source == ActivitySource.Agent ? AgentWork.Describe(piece.Activity) : piece.Activity.Title;
    }

    private static List<TimesheetLine> BuildLines(List<Piece> pieces, RoundingSettings rounding, string sentiment)
    {
        List<Piece> counting = pieces.Where(piece => piece.IsCounting).OrderBy(piece => piece.Start).ToList();
        List<List<Piece>> groups = [];
        if (rounding.Scope == RoundingScope.DailyTotalPerCategory)
        {
            groups.AddRange(counting
                .GroupBy(piece => (piece.Date, piece.CategoryId, piece.Location.Name))
                .Select(group => group.ToList()));
        }
        else
        {
            groups.AddRange(counting.Where(piece => piece.Activity.Source != ActivitySource.Agent).Select(piece => new List<Piece> { piece }));
            groups.AddRange(counting
                .Where(piece => piece.Activity.Source == ActivitySource.Agent)
                .GroupBy(piece => (piece.Date, piece.CategoryId, piece.Location.Name))
                .Select(group => group.ToList()));
        }

        List<TimesheetLine> lines = [];
        foreach (List<Piece> group in groups)
        {
            Piece first = group[0];
            TimeSpan raw = group.Aggregate(TimeSpan.Zero, (total, piece) => total + piece.Counted);
            List<Piece> timed = group.Where(piece => piece.Timed).ToList();
            bool single = group.Count == 1 && first.Activity.Source != ActivitySource.Agent;
            string id = single
                ? "line:" + first.Id
                : $"line:{first.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}:{(group.All(piece => piece.Activity.Source == ActivitySource.Agent) ? "agent" : "all")}:{first.CategoryId ?? "unmapped"}:{first.Location.Name}";

            TimesheetLine line = new()
            {
                Id = id,
                Date = first.Date,
                CategoryId = first.CategoryId,
                Location = first.Location.Name,
                Description = CombinedDescription(group),
                Source = group.Select(piece => piece.Activity.Source).Distinct().Count() == 1 ? first.Activity.Source : null,
                Raw = raw,
                Minutes = Rounding.RoundMinutes(raw, rounding),
                PieceIds = group.Select(piece => piece.Id).ToList(),
                Start = timed.Count > 0 ? timed.Min(piece => piece.Start) : null,
                End = timed.Count > 0 ? timed.Max(piece => piece.End) : null,
                Sentiment = sentiment,
            };

            foreach (Piece piece in group)
            {
                piece.LineId = line.Id;
            }

            lines.Add(line);
        }

        return lines
            .OrderBy(line => line.Date)
            .ThenBy(line => line.Start ?? line.Date.ToDateTime(TimeOnly.MaxValue))
            .ThenBy(line => line.Id, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// One line per workstream, like the Timesheet system's own rows: "portal: Fix login; Add tests", where each part is
    /// a block's commit subjects or, without commits, its session's title.
    /// Activities whose mapping has a description template keep their rendered description as its own line.
    /// </summary>
    private static string CombinedDescription(List<Piece> group)
    {
        if (group.Count == 1 && group[0].Activity.Source != ActivitySource.Agent)
        {
            return group[0].Description;
        }

        List<string> parts = [];
        List<Piece> agentPieces = group.Where(piece => piece.Activity.Source == ActivitySource.Agent).ToList();
        parts.AddRange(agentPieces
            .Where(piece => piece.Override?.Description is not null || piece.MappingRule?.DescriptionTemplate is { Length: > 0 })
            .Select(piece => piece.Description));
        foreach (IGrouping<string, Piece> project in agentPieces
                     .Where(piece => piece.Override?.Description is null && piece.MappingRule?.DescriptionTemplate is not { Length: > 0 })
                     .GroupBy(piece => piece.Activity.GroupLabel))
        {
            List<string> titles = project
                .SelectMany(piece => AgentWork.Parts(piece.Activity))
                .Where(title => !string.Equals(title, project.Key, StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            parts.Add(titles.Count == 0 ? project.Key : $"{project.Key}: {string.Join("; ", titles)}");
        }

        parts.AddRange(group
            .Where(piece => piece.Activity.Source != ActivitySource.Agent)
            .Select(piece => piece.Description));

        string combined = string.Join("\n", parts.Where(part => part.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase));
        return combined.Length <= MaxDescriptionLength ? combined : combined[..(MaxDescriptionLength - 1)] + "…";
    }

    /// <summary>
    /// Brings each past working day that has something recorded up to the standard hours, by adding the missing time
    /// to that day's line for the top-up category (or a new one).
    /// </summary>
    private static List<TimesheetLine> TopUp(List<TimesheetLine> lines, List<Piece> pieces, WorkspaceConfig config, Dictionary<string, Category> categories,
        WorkCalendar workCalendar, Dictionary<DateOnly, ResolvedLocation> dayLocations, DateOnly? today)
    {
        if (config.Targets.TopUpCategoryId is not { } topUpId || !categories.TryGetValue(topUpId, out Category? topUpCategory))
        {
            return lines;
        }

        HashSet<DateOnly> recorded = pieces.Where(piece => piece.IsCounting && piece.Counted > TimeSpan.Zero).Select(piece => piece.Date).ToHashSet();
        List<TimesheetLine> result = [.. lines];
        foreach (DateOnly day in dayLocations.Keys.Order())
        {
            if (!workCalendar.IsWorkDay(day) || (today is { } now && day >= now) || !recorded.Contains(day))
            {
                continue;
            }

            int missing = workCalendar.ExpectedMinutes(day) - result.Where(line => line.Date == day).Sum(line => line.Minutes);
            missing = Rounding.RoundMinutes(TimeSpan.FromMinutes(missing), config.Rounding);
            if (missing <= 0)
            {
                continue;
            }

            string location = dayLocations[day].Name;
            int index = result.FindIndex(line => line.Date == day && line.CategoryId == topUpId && line.Source == ActivitySource.Agent && line.Location == location);
            if (index < 0)
            {
                index = result.FindIndex(line => line.Date == day && line.CategoryId == topUpId && line.Location == location);
            }

            if (index >= 0)
            {
                result[index] = result[index] with { Minutes = result[index].Minutes + missing, TopUpMinutes = missing };
                continue;
            }

            result.Add(new TimesheetLine
            {
                Id = $"line:{day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}:topup:{topUpId}:{location}",
                Date = day,
                CategoryId = topUpId,
                Location = location,
                Description = topUpCategory.Name,
                Source = null,
                Minutes = missing,
                TopUpMinutes = missing,
                Sentiment = config.Export.DefaultSentiment,
            });
        }

        return result
            .OrderBy(line => line.Date)
            .ThenBy(line => line.Start ?? line.Date.ToDateTime(TimeOnly.MaxValue))
            .ThenBy(line => line.Id, StringComparer.Ordinal)
            .ToList();
    }

    private static List<TimesheetLine> ApplyLineOverrides(List<TimesheetLine> lines, Journal journal)
    {
        if (journal.Lines.Count == 0)
        {
            return lines;
        }

        return lines.Select(line => journal.Lines.TryGetValue(line.Id, out LineOverride? change) && !change.IsEmpty
            ? line with
            {
                Description = string.IsNullOrWhiteSpace(change.Description) ? line.Description : change.Description.Trim(),
                Sentiment = string.IsNullOrWhiteSpace(change.Sentiment) ? line.Sentiment : change.Sentiment,
                Ticket = string.IsNullOrWhiteSpace(change.Ticket) ? line.Ticket : change.Ticket.Trim(),
                Minutes = change.Minutes is { } minutes ? Math.Max(0, minutes) : line.Minutes,
                Override = change,
            }
            : line).ToList();
    }

    private static DaySummary Summarise(DateOnly day, WorkCalendar workCalendar, ResolvedLocation location, List<TimesheetLine> lines,
        List<Piece> pieces, Dictionary<string, Category> categories, Journal journal, DateOnly? today)
    {
        List<TimesheetLine> dayLines = lines.Where(line => line.Date == day).ToList();
        List<Piece> dayPieces = pieces.Where(piece => piece.Date == day && piece.IsCounting).ToList();
        return new DaySummary
        {
            Date = day,
            Kind = workCalendar.KindOf(day),
            HolidayName = workCalendar.HolidayName(day),
            Location = location,
            ExpectedMinutes = workCalendar.ExpectedMinutes(day),
            TotalMinutes = dayLines.Sum(line => line.Minutes),
            BillableMinutes = dayLines.Where(line => IsBillable(line, categories)).Sum(line => line.Minutes),
            LeaveMinutes = dayLines.Where(line => IsLeave(line, categories)).Sum(line => line.Minutes),
            UnmappedMinutes = dayLines.Where(line => line.CategoryId is null).Sum(line => line.Minutes),
            UnmappedCount = dayPieces.Count(piece => piece.Status == ActivityStatus.Unmapped),
            OverlapCount = dayPieces.Count(piece => piece.OverlappedBy.Count > 0),
            ActivityCount = dayPieces.Count,
            IsFuture = today is { } now && day > now,
            IsToday = today is { } current && day == current,
            Note = journal.FindDay(day)?.Note,
        };
    }

    private static PeriodTotals Totalise(List<TimesheetLine> lines, List<DaySummary> days, List<Piece> pieces,
        Dictionary<string, Category> categories, TargetSettings targets)
    {
        int total = lines.Sum(line => line.Minutes);
        int billable = lines.Where(line => IsBillable(line, categories)).Sum(line => line.Minutes);
        int leave = lines.Where(line => IsLeave(line, categories)).Sum(line => line.Minutes);
        int unmapped = lines.Where(line => line.CategoryId is null).Sum(line => line.Minutes);
        int target = (int)Math.Round(targets.BillableTargetHours * 60) - (targets.ReduceTargetByLeave ? leave : 0);
        List<DaySummary> pastDays = days.Where(day => !day.IsFuture && !day.IsToday).ToList();

        return new PeriodTotals
        {
            TotalMinutes = total,
            BillableMinutes = billable,
            LeaveMinutes = leave,
            UnmappedMinutes = unmapped,
            NonBillableMinutes = total - billable - leave - unmapped,
            TargetMinutes = Math.Max(0, target),
            ExpectedMinutes = days.Sum(day => day.ExpectedMinutes),
            WorkDays = days.Count(day => day.Kind == DayKind.WorkDay),
            DaysMissingTime = pastDays.Count(day => day.MissingMinutes > 0),
            MissingMinutes = pastDays.Sum(day => day.MissingMinutes),
            UnmappedActivities = pieces.Count(piece => piece.Status == ActivityStatus.Unmapped),
            OverlappingActivities = pieces.Count(piece => piece.IsCounting && piece.OverlappedBy.Count > 0),
            WeekendOrHolidayDaysWorked = days.Count(day => day.Kind != DayKind.WorkDay && day.TotalMinutes > 0),
            ByCategory = lines
                .GroupBy(line => line.CategoryId)
                .Select(group => new CategoryTotal(group.Key, group.Sum(line => line.Minutes)))
                .Where(categoryTotal => categoryTotal.Minutes > 0)
                .OrderByDescending(categoryTotal => categoryTotal.Minutes)
                .ToList(),
            ByLocation = lines
                .Where(line => line.Minutes > 0)
                .GroupBy(line => line.Location)
                .Select(group => new LocationTotal(group.Key, group.Sum(line => line.Minutes), group.Select(line => line.Date).Distinct().Count()))
                .OrderByDescending(locationTotal => locationTotal.Minutes)
                .ToList(),
        };
    }

    private static bool IsBillable(TimesheetLine line, Dictionary<string, Category> categories) =>
        line.CategoryId is not null && categories.TryGetValue(line.CategoryId, out Category? category) && category.Billable && !category.IsLeave;

    private static bool IsLeave(TimesheetLine line, Dictionary<string, Category> categories) =>
        line.CategoryId is not null && categories.TryGetValue(line.CategoryId, out Category? category) && category.IsLeave;

    private sealed class Piece(Activity activity, string id, DateOnly date, DateTime start, DateTime end, bool timed, TimeSpan raw)
    {
        public Activity Activity { get; } = activity;
        public string Id { get; } = id;
        public DateOnly Date { get; } = date;
        public DateTime Start { get; } = start;
        public DateTime End { get; } = end;
        public bool Timed { get; } = timed;
        public TimeSpan Raw { get; } = raw;
        public RuleMatch Match { get; set; } = RuleMatch.Unmapped;
        public Rule? MappingRule { get; set; }
        public ActivityOverride? Override { get; set; }
        public ActivityStatus Status { get; set; }
        public string? CategoryId { get; set; }
        public TimeSpan Counted { get; set; }
        public IReadOnlyList<string> OverlappedBy { get; set; } = [];
        public ResolvedLocation Location { get; set; } = new("", LocationOrigin.Default);
        public string Description { get; set; } = "";
        public string? LineId { get; set; }
        public string? Note { get; set; }

        public bool IsCounting => Status is ActivityStatus.Counted or ActivityStatus.Unmapped;

        public ActivityResult ToResult() => new()
        {
            Activity = Activity,
            PieceId = Id,
            Date = Date,
            Start = Start,
            End = End,
            Status = Status,
            Match = Match,
            MappingRule = MappingRule,
            CategoryId = CategoryId,
            Location = Location,
            Description = Description,
            Raw = Raw,
            Counted = Counted,
            OverlappedBy = OverlappedBy,
            Override = Override,
            LineId = LineId,
            Note = Note,
        };
    }
}
