using QuarterHour.Core.Configuration;
using QuarterHour.Core.Model;
using QuarterHour.Core.Rules;

namespace QuarterHour.Core.Timesheets;

/// <summary>
/// Turns unattributed commits and pull requests into a manual entry, for work no agent session saw. The entry is a
/// draft for the user to finish: no start time, one rounding increment, the project's usual category.
/// </summary>
public static class CandidateEntries
{
    private const int MaxDescriptionLength = 500;

    /// <summary>Whether the candidates can become one entry: all unattributed, made on one day, in one project.</summary>
    public static bool CanCombine(IReadOnlyList<Placement> candidates, TimeZoneInfo timeZone, IReadOnlyDictionary<string, string> friendlyNames) =>
        candidates.Count > 0
        && candidates.All(candidate => candidate.IsUnattributed)
        && candidates.Select(candidate => candidate.Day(timeZone)).Distinct().Count() == 1
        && candidates.Select(candidate => friendlyNames.GetValueOrDefault(candidate.ProjectKey) ?? candidate.ProjectKey).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 1;

    public static ManualEntry Draft(IReadOnlyList<Placement> candidates, PeriodTimesheet sheet, WorkspaceConfig config, TimeZoneInfo timeZone)
    {
        List<Placement> ordered = [.. candidates.OrderBy(candidate => candidate.At)];
        Placement first = ordered[0];
        string project = ProjectName(first.ProjectKey, sheet, config.FriendlyProjectNames());
        string description = $"{project}: {string.Join("; ", ordered.Select(candidate => candidate.Text).Distinct(StringComparer.OrdinalIgnoreCase))}";
        return new ManualEntry
        {
            Date = first.Day(timeZone),
            Minutes = Math.Max(1, config.Rounding.IncrementMinutes),
            CategoryId = CategoryOf(first.ProjectKey, project, sheet, config),
            Description = description.Length > MaxDescriptionLength ? description[..(MaxDescriptionLength - 1)] + "…" : description,
            Candidates = [.. ordered.Select(candidate => candidate.Key)],
        };
    }

    /// <summary>The friendly name, else the repository name as the project's sessions spell it.</summary>
    public static string ProjectName(string projectKey, PeriodTimesheet sheet, IReadOnlyDictionary<string, string> friendlyNames) =>
        friendlyNames.GetValueOrDefault(projectKey) is { Length: > 0 } friendly
            ? friendly
            : sheet.Activities.FirstOrDefault(item => item.Activity.GroupKey.Equals("agent:" + projectKey, StringComparison.OrdinalIgnoreCase))?.Activity.ProjectName ?? projectKey;

    /// <summary>The category the project's agent work last went to, else the one its rules would give it.</summary>
    private static string? CategoryOf(string projectKey, string project, PeriodTimesheet sheet, WorkspaceConfig config)
    {
        string groupKey = "agent:" + projectKey;
        if (sheet.Activities.LastOrDefault(item => item.Activity.GroupKey.Equals(groupKey, StringComparison.OrdinalIgnoreCase) && item.IsCounting && item.CategoryId is not null)
            is { CategoryId: { } used })
        {
            return used;
        }

        DateTime start = sheet.Period.Start.ToDateTime(TimeOnly.MinValue);
        Activity standIn = new()
        {
            Id = "candidate:" + projectKey,
            Source = ActivitySource.Agent,
            Start = start,
            End = start,
            Title = project,
            GroupKey = groupKey,
            GroupLabel = project,
            ProjectName = projectKey,
        };
        RuleMatch match = new RuleEngine(config.Rules).Classify(standIn);
        return match.Outcome == RuleOutcome.Mapped && config.FindCategory(match.Rule?.CategoryId) is { } category ? category.Id : null;
    }
}
