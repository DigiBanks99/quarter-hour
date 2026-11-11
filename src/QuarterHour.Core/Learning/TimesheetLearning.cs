using QuarterHour.Core.Configuration;
using QuarterHour.Core.Model;
using QuarterHour.Core.Rules;

namespace QuarterHour.Core.Learning;

/// <summary>A row of a timesheet the Timesheet system exported.</summary>
public sealed record TimesheetRow(
    DateOnly Date,
    string Project,
    string Category,
    int Minutes,
    bool Billable,
    string Description,
    string? Ticket = null,
    string? Sentiment = null,
    string? WorkedFrom = null)
{
    public bool IsSingleLine => !Description.Contains('\n') && Description.Trim().Length is > 0 and <= 100;
}

public sealed record LearnedCategory(string Project, string Name, bool Billable, bool IsLeave, int Rows, int Minutes, string? ExistingId, string? PlaceholderId)
{
    public bool IsNew => ExistingId is null;
}

public sealed record LearnedMapping(string GroupKey, string Label, string Project, string Category, int Occurrences, string? LocationPin);

public sealed record LearnedScheduleDay(DayOfWeek Day, string Location, int Agreeing, int Dates);

public sealed record LearnedCatchAll(string Project, string Category, int Rows, int OfRows);

public sealed record TimesheetLessons
{
    public required DateOnly From { get; init; }
    public required DateOnly To { get; init; }
    public required int Rows { get; init; }
    public IReadOnlyList<LearnedCategory> Categories { get; init; } = [];
    public IReadOnlyList<LearnedMapping> Mappings { get; init; } = [];
    public LearnedCatchAll? CatchAll { get; init; }
    public string? DefaultLocation { get; init; }
    public IReadOnlyList<LearnedScheduleDay> Schedule { get; init; } = [];
    public (string Project, string Category)? TopUp { get; init; }
    public IReadOnlyList<string> Locations { get; init; } = [];
    public IReadOnlyList<string> Sentiments { get; init; } = [];
    public string? MostCommonSentiment { get; init; }
}

public sealed class LearningChoices
{
    public bool Categories { get; set; } = true;
    public bool Mappings { get; set; } = true;
    public bool CatchAll { get; set; } = true;
    public bool Schedule { get; set; } = true;
    public bool TopUp { get; set; } = true;
    public bool Sentiments { get; set; } = true;
}

public sealed record LearningSummary(int CategoriesAdded, int CategoriesRenamed, int MappingsAdded, bool CatchAllAdded, int ScheduleRulesAdded, bool TopUpSet);

/// <summary>
/// Learns a starting configuration from a previous timesheet export: its projects and categories, the recurring
/// meetings and where they were logged, the usual location per weekday, and which category tops up each day.
/// </summary>
public static class TimesheetLearning
{
    private const int MaxMeetingMinutes = 180;
    private const double ScheduleAgreement = 0.75;
    private const double CatchAllShare = 0.6;

    public static TimesheetLessons Learn(IReadOnlyList<TimesheetRow> rows, WorkspaceConfig config)
    {
        if (rows.Count == 0)
        {
            throw new InvalidOperationException("That timesheet has no rows.");
        }

        List<LearnedCategory> categories = rows
            .GroupBy(row => (Project: row.Project.Trim(), Category: row.Category.Trim()), Comparer)
            .Where(group => group.Key.Project.Length > 0 || group.Key.Category.Length > 0)
            .Select(group =>
            {
                Category? existing = Find(config, group.Key.Project, group.Key.Category);
                Category? placeholder = existing is null && group.Any(row => row.Billable)
                    ? config.Categories.FirstOrDefault(category => category.Group == DefaultWorkspace.ClientProject
                                                                   && string.Equals(category.Name, group.Key.Category, StringComparison.OrdinalIgnoreCase))
                    : null;
                return new LearnedCategory(group.Key.Project, group.Key.Category, group.Count(row => row.Billable) * 2 >= group.Count(),
                    IsLeave(group.Key.Project, group.Key.Category), group.Count(), group.Sum(row => row.Minutes), existing?.Id, placeholder?.Id);
            })
            .OrderByDescending(category => category.Minutes)
            .ToList();

        Dictionary<DateOnly, string> dayLocations = rows
            .Where(row => !string.IsNullOrWhiteSpace(row.WorkedFrom))
            .GroupBy(row => row.Date)
            .ToDictionary(day => day.Key, day => day.GroupBy(row => row.WorkedFrom!.Trim()).OrderByDescending(location => location.Sum(row => row.Minutes)).First().Key);

        string? defaultLocation = dayLocations.Values.GroupBy(location => location).OrderByDescending(group => group.Count()).FirstOrDefault()?.Key;
        List<LearnedScheduleDay> schedule = [];
        foreach (IGrouping<DayOfWeek, KeyValuePair<DateOnly, string>> weekday in dayLocations.Where(pair => config.Targets.WorkDays.Contains(pair.Key.DayOfWeek)).GroupBy(pair => pair.Key.DayOfWeek))
        {
            IGrouping<string, KeyValuePair<DateOnly, string>> top = weekday.GroupBy(pair => pair.Value).OrderByDescending(group => group.Count()).First();
            int dates = weekday.Count();
            if (dates >= 2 && top.Count() >= ScheduleAgreement * dates)
            {
                schedule.Add(new LearnedScheduleDay(weekday.Key, top.Key, top.Count(), dates));
            }
        }

        RuleEngine engine = new(config.Rules);
        List<TimesheetRow> meetings = rows.Where(row => row.IsSingleLine && row.Minutes <= MaxMeetingMinutes).ToList();
        List<LearnedMapping> mappings = [];
        foreach (IGrouping<string, TimesheetRow> group in meetings.GroupBy(row => CalendarActivities.GroupKeyFor(row.Description)))
        {
            if (group.Select(row => row.Date).Distinct().Count() < 2)
            {
                continue;
            }

            IGrouping<(string Project, string Category), TimesheetRow> usual = group
                .GroupBy(row => (row.Project.Trim(), row.Category.Trim()), Comparer)
                .OrderByDescending(byCategory => byCategory.Count())
                .First();
            string label = CalendarActivities.CleanSubject(group.First().Description);
            Activity probe = CalendarActivities.ToActivity(new CalendarEvent
            {
                Id = "probe",
                Subject = label,
                Start = DateTimeOffset.UnixEpoch,
                End = DateTimeOffset.UnixEpoch.AddMinutes(30),
                Response = "accepted",
                AttendeeCount = 2,
            }, TimeZoneInfo.Utc);
            RuleMatch current = engine.ClassifyIgnoringFilters(probe);
            Category? currentCategory = current.Outcome == RuleOutcome.Mapped ? config.FindCategory(current.Rule?.CategoryId) : null;
            if (currentCategory is not null
                && string.Equals(currentCategory.Group, usual.Key.Project, StringComparison.OrdinalIgnoreCase)
                && string.Equals(currentCategory.Name, usual.Key.Category, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (DirectMappings.Find(config.Rules, group.Key) is not null)
            {
                continue;
            }

            List<string> locations = group.Select(row => row.WorkedFrom?.Trim()).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            string? pin = locations.Count == 1 && group.Any(row => dayLocations.TryGetValue(row.Date, out string? day) && !string.Equals(day, locations[0], StringComparison.OrdinalIgnoreCase))
                ? locations[0]
                : null;
            mappings.Add(new LearnedMapping(group.Key, label, usual.Key.Project, usual.Key.Category, group.Select(row => row.Date).Distinct().Count(), pin));
        }

        LearnedCatchAll? catchAll = null;
        if (meetings.Count >= 5)
        {
            IGrouping<(string Project, string Category), TimesheetRow> biggest = meetings
                .GroupBy(row => (row.Project.Trim(), row.Category.Trim()), Comparer)
                .OrderByDescending(group => group.Count())
                .First();
            if (biggest.Count() >= CatchAllShare * meetings.Count && biggest.Any(row => row.Billable))
            {
                catchAll = new LearnedCatchAll(biggest.Key.Project, biggest.Key.Category, biggest.Count(), meetings.Count);
            }
        }

        (string Project, string Category)? topUp = rows
            .Where(row => !row.IsSingleLine || row.Minutes > MaxMeetingMinutes)
            .GroupBy(row => (row.Project.Trim(), row.Category.Trim()), Comparer)
            .OrderByDescending(group => group.Sum(row => row.Minutes))
            .Select(group => ((string, string)?)group.Key)
            .FirstOrDefault();

        List<string> sentiments = rows.Select(row => row.Sentiment?.Trim()).OfType<string>().Where(sentiment => sentiment.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return new TimesheetLessons
        {
            From = rows.Min(row => row.Date),
            To = rows.Max(row => row.Date),
            Rows = rows.Count,
            Categories = categories,
            Mappings = [.. mappings.OrderByDescending(mapping => mapping.Occurrences).ThenBy(mapping => mapping.Label)],
            CatchAll = catchAll,
            DefaultLocation = defaultLocation,
            Schedule = [.. schedule.OrderBy(day => ((int)day.Day + 6) % 7)],
            TopUp = topUp,
            Locations = [.. rows.Select(row => row.WorkedFrom?.Trim()).OfType<string>().Where(location => location.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase)],
            Sentiments = sentiments,
            MostCommonSentiment = rows.Select(row => row.Sentiment?.Trim()).OfType<string>().Where(sentiment => sentiment.Length > 0)
                .GroupBy(sentiment => sentiment, StringComparer.OrdinalIgnoreCase).OrderByDescending(group => group.Count()).FirstOrDefault()?.Key,
        };
    }

    public static LearningSummary Apply(TimesheetLessons lessons, WorkspaceConfig config, LearningChoices choices)
    {
        int added = 0;
        int renamed = 0;
        if (choices.Categories)
        {
            HashSet<string> placeholdersTaken = [];
            foreach (LearnedCategory learned in lessons.Categories.Where(category => category.IsNew))
            {
                if (Find(config, learned.Project, learned.Name) is not null)
                {
                    continue;
                }

                Category? placeholder = learned.PlaceholderId is { } id && placeholdersTaken.Add(id) ? config.FindCategory(id) : null;
                if (placeholder is not null)
                {
                    placeholder.Group = learned.Project;
                    placeholder.Name = learned.Name;
                    placeholder.Billable = learned.Billable;
                    renamed++;
                    continue;
                }

                config.Categories.Add(new Category
                {
                    Group = learned.Project,
                    Name = learned.Name,
                    Billable = learned.Billable,
                    IsLeave = learned.IsLeave,
                });
                added++;
            }
        }

        int mappings = 0;
        if (choices.Mappings)
        {
            foreach (LearnedMapping mapping in lessons.Mappings)
            {
                if (Find(config, mapping.Project, mapping.Category) is not { } category || DirectMappings.Find(config.Rules, mapping.GroupKey) is not null)
                {
                    continue;
                }

                Rule rule = DirectMappings.Link(config.Rules, mapping.GroupKey, mapping.Label, ActivitySource.Calendar, category.Id);
                rule.LocationPin = mapping.LocationPin;
                mappings++;
            }
        }

        bool catchAllAdded = false;
        if (choices.CatchAll && lessons.CatchAll is { } catchAll && Find(config, catchAll.Project, catchAll.Category) is { } catchAllCategory
            && config.Rules.All(rule => rule.Name != CatchAllName))
        {
            config.Rules.Add(new Rule
            {
                Name = CatchAllName,
                AppliesTo = RuleScope.Calendar,
                Conditions = [new RuleCondition { Field = "title", Operator = ConditionOperator.IsNotEmpty }],
                CategoryId = catchAllCategory.Id,
            });
            catchAllAdded = true;
        }

        foreach (string location in lessons.Locations.Where(location => !config.Locations.Names.Contains(location, StringComparer.OrdinalIgnoreCase)))
        {
            config.Locations.Names.Add(location);
        }

        int scheduleRules = 0;
        if (choices.Schedule && lessons.DefaultLocation is { } defaultLocation)
        {
            config.Locations.Default = config.Locations.Names.First(name => string.Equals(name, defaultLocation, StringComparison.OrdinalIgnoreCase));
            foreach (IGrouping<string, LearnedScheduleDay> location in lessons.Schedule
                         .Where(day => !string.Equals(day.Location, defaultLocation, StringComparison.OrdinalIgnoreCase))
                         .GroupBy(day => day.Location, StringComparer.OrdinalIgnoreCase))
            {
                List<DayOfWeek> days = [.. location.Select(day => day.Day)];
                bool exists = config.Locations.Schedule.Any(rule =>
                    string.Equals(rule.Location, location.Key, StringComparison.OrdinalIgnoreCase) && rule.Days.Order().SequenceEqual(days.Order()));
                if (!exists)
                {
                    config.Locations.Schedule.Add(new LocationScheduleRule
                    {
                        Location = config.Locations.Names.First(name => string.Equals(name, location.Key, StringComparison.OrdinalIgnoreCase)),
                        Days = days,
                        Note = "Learned from a previous timesheet",
                    });
                    scheduleRules++;
                }
            }
        }

        bool topUpSet = false;
        if (choices.TopUp && lessons.TopUp is { } topUp && Find(config, topUp.Project, topUp.Category) is { } topUpCategory)
        {
            config.Targets.TopUpCategoryId = topUpCategory.Id;
            topUpSet = true;
        }

        if (choices.Sentiments)
        {
            foreach (string sentiment in lessons.Sentiments.Where(sentiment => !config.Export.Sentiments.Contains(sentiment, StringComparer.OrdinalIgnoreCase)))
            {
                config.Export.Sentiments.Add(sentiment);
            }

            if (lessons.MostCommonSentiment is { } common)
            {
                config.Export.DefaultSentiment = config.Export.Sentiments.First(sentiment => string.Equals(sentiment, common, StringComparison.OrdinalIgnoreCase));
            }
        }

        config.Normalize();
        return new LearningSummary(added, renamed, mappings, catchAllAdded, scheduleRules, topUpSet);
    }

    public const string CatchAllName = "Any other meeting";

    private static Category? Find(WorkspaceConfig config, string project, string name) =>
        config.Categories.FirstOrDefault(category =>
            string.Equals(category.Group.Trim(), project.Trim(), StringComparison.OrdinalIgnoreCase)
            && string.Equals(category.Name.Trim(), name.Trim(), StringComparison.OrdinalIgnoreCase));

    private static bool IsLeave(string project, string category) =>
        project.Contains("leave", StringComparison.OrdinalIgnoreCase) || category.Contains("leave", StringComparison.OrdinalIgnoreCase);

    private static readonly IEqualityComparer<(string Project, string Category)> Comparer = new CategoryKeyComparer();

    private sealed class CategoryKeyComparer : IEqualityComparer<(string Project, string Category)>
    {
        public bool Equals((string Project, string Category) x, (string Project, string Category) y) =>
            string.Equals(x.Project, y.Project, StringComparison.OrdinalIgnoreCase) && string.Equals(x.Category, y.Category, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((string Project, string Category) key) =>
            HashCode.Combine(key.Project.ToUpperInvariant(), key.Category.ToUpperInvariant());
    }
}
