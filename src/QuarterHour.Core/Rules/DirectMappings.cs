using QuarterHour.Core.Configuration;
using QuarterHour.Core.Model;

namespace QuarterHour.Core.Rules;

/// <summary>The mapping board's operations: link a group to a category (or "not relevant"), unlink it, pin a location.</summary>
public static class DirectMappings
{
    public const string NotRelevantTarget = "ignore";

    public static Rule? Find(IEnumerable<Rule> rules, string groupKey) =>
        rules.FirstOrDefault(rule => rule.Stage == RuleStage.Direct && IsFor(rule, groupKey));

    public static bool IsFor(Rule rule, string groupKey) =>
        rule.Conditions.Count == 1
        && string.Equals(rule.Conditions[0].Field, ActivityFields.GroupKey, StringComparison.OrdinalIgnoreCase)
        && rule.Conditions[0].Operator == ConditionOperator.Equals
        && string.Equals(rule.Conditions[0].Value, groupKey, StringComparison.OrdinalIgnoreCase);

    /// <summary>Maps a group to a category id, or to <see cref="NotRelevantTarget"/>. Returns the direct rule.</summary>
    public static Rule Link(List<Rule> rules, string groupKey, string groupLabel, ActivitySource source, string target)
    {
        Rule? rule = Find(rules, groupKey);
        if (rule is null)
        {
            rule = new Rule
            {
                Stage = RuleStage.Direct,
                AppliesTo = source == ActivitySource.Agent ? RuleScope.Agent : RuleScope.Calendar,
                Conditions = [new RuleCondition { Field = ActivityFields.GroupKey, Operator = ConditionOperator.Equals, Value = groupKey }],
            };

            int firstHeuristic = rules.FindIndex(existing => existing.Stage == RuleStage.Heuristic);
            rules.Insert(firstHeuristic < 0 ? rules.Count : firstHeuristic, rule);
        }

        rule.Name = groupLabel;
        rule.Enabled = true;
        if (target == NotRelevantTarget)
        {
            rule.Action = RuleAction.Ignore;
            rule.CategoryId = null;
            rule.LocationPin = null;
        }
        else
        {
            rule.Action = RuleAction.Map;
            rule.CategoryId = target;
        }

        return rule;
    }

    public static bool Unlink(List<Rule> rules, string groupKey)
    {
        Rule? rule = Find(rules, groupKey);
        return rule is not null && rules.Remove(rule);
    }

    /// <summary>
    /// Pins (or unpins with null) a group's location. A group mapped only by a heuristic gets a direct mapping to the
    /// same category so the pin doesn't leak into everything else the heuristic matches.
    /// </summary>
    public static Rule? Pin(List<Rule> rules, string groupKey, string groupLabel, ActivitySource source, string? location, Rule? currentMapping)
    {
        Rule? rule = Find(rules, groupKey);
        if (rule is null)
        {
            if (location is null || currentMapping is null || currentMapping.Action != RuleAction.Map || currentMapping.CategoryId is null)
            {
                return null;
            }

            rule = Link(rules, groupKey, groupLabel, source, currentMapping.CategoryId);
        }

        if (rule.Action != RuleAction.Map)
        {
            return rule;
        }

        rule.LocationPin = location;
        return rule;
    }
}
