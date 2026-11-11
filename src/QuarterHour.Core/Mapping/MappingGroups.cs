using QuarterHour.Core.Configuration;
using QuarterHour.Core.Model;
using QuarterHour.Core.Rules;
using QuarterHour.Core.Timesheets;

namespace QuarterHour.Core.Mapping;

/// <summary>A group of similar activities (one meeting series, one project) as the mapping board shows it.</summary>
public sealed record MappingGroup
{
    public required string Key { get; init; }
    public required string Label { get; init; }
    public required ActivitySource Source { get; init; }
    public int Occurrences { get; init; }
    public TimeSpan RawTime { get; init; }
    public TimeSpan CountedTime { get; init; }
    public IReadOnlyList<string> Variants { get; init; } = [];
    public IReadOnlyList<string> Agents { get; init; } = [];

    /// <summary>The mapping that applies to most of the group, ignoring relevance filters.</summary>
    public required RuleMatch Mapping { get; init; }

    public bool HasMixedMappings { get; init; }
    public int FilteredCount { get; init; }
    public int ExcludedCount { get; init; }
    public Rule? DirectRule { get; init; }
    public DateTime FirstSeen { get; init; }
    public DateTime LastSeen { get; init; }
    public IReadOnlyList<ActivityResult> Items { get; init; } = [];

    /// <summary>The category id, <see cref="DirectMappings.NotRelevantTarget"/>, or null when unmapped.</summary>
    public string? TargetId => Mapping.Outcome switch
    {
        RuleOutcome.Mapped => Mapping.Rule?.CategoryId,
        RuleOutcome.NotRelevant => DirectMappings.NotRelevantTarget,
        _ => null,
    };

    public bool IsDirect => DirectRule is not null && ReferenceEquals(Mapping.Rule, DirectRule);
    public string? LocationPin => Mapping.Rule is { Action: RuleAction.Map } rule ? rule.LocationPin : null;
    public bool IsUnmapped => Mapping.Outcome == RuleOutcome.Unmapped;
    public bool IsEntirelyFiltered => Occurrences > 0 && FilteredCount == Occurrences;
}

public static class MappingGroups
{
    public static IReadOnlyList<MappingGroup> Build(PeriodTimesheet sheet, WorkspaceConfig config)
    {
        RuleEngine engine = new(config.Rules);
        List<MappingGroup> groups = [];
        foreach (IGrouping<string, ActivityResult> group in sheet.Activities
                     .Where(result => result.Activity.Source != ActivitySource.Manual)
                     .GroupBy(result => result.Activity.GroupKey))
        {
            List<ActivityResult> items = group.OrderBy(result => result.Start).ToList();
            List<Activity> distinctActivities = items.Select(result => result.Activity).DistinctBy(activity => activity.Id).ToList();
            List<RuleMatch> mappings = distinctActivities.Select(engine.ClassifyIgnoringFilters).ToList();
            List<IGrouping<string?, RuleMatch>> byRule = mappings.GroupBy(match => match.Rule?.Id).OrderByDescending(ruleGroup => ruleGroup.Count()).ToList();
            RuleMatch dominant = byRule[0].First();
            Activity first = distinctActivities[0];

            groups.Add(new MappingGroup
            {
                Key = group.Key,
                Label = first.GroupLabel,
                Source = first.Source,
                Occurrences = distinctActivities.Count,
                RawTime = items.Aggregate(TimeSpan.Zero, (total, result) => total + result.Raw),
                CountedTime = items.Aggregate(TimeSpan.Zero, (total, result) => total + result.Counted),
                Variants = distinctActivities.Select(activity => activity.Title).Distinct(StringComparer.OrdinalIgnoreCase).Take(6).ToList(),
                Agents = distinctActivities.Select(activity => activity.Agent).OfType<string>().Distinct().Order().ToList(),
                Mapping = dominant,
                HasMixedMappings = byRule.Count > 1,
                FilteredCount = distinctActivities.Count(activity => items.Any(result => result.Activity.Id == activity.Id && result.Status == ActivityStatus.Filtered)),
                ExcludedCount = distinctActivities.Count(activity => items.Any(result => result.Activity.Id == activity.Id && result.Status == ActivityStatus.Excluded)),
                DirectRule = DirectMappings.Find(config.Rules, group.Key),
                FirstSeen = items[0].Start,
                LastSeen = items[^1].Start,
                Items = items,
            });
        }

        return groups
            .OrderBy(group => group.IsEntirelyFiltered)
            .ThenByDescending(group => group.IsUnmapped)
            .ThenByDescending(group => group.RawTime)
            .ThenBy(group => group.Label, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
