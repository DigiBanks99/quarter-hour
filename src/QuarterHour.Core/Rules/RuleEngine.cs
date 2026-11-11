using QuarterHour.Core.Configuration;
using QuarterHour.Core.Model;

namespace QuarterHour.Core.Rules;

public enum RuleOutcome
{
    /// <summary>A relevance filter removed it (declined, cancelled, free…).</summary>
    Filtered,

    /// <summary>A mapping or heuristic gave it a category.</summary>
    Mapped,

    /// <summary>A mapping or heuristic marked it as not relevant.</summary>
    NotRelevant,

    /// <summary>No rule says what it is yet.</summary>
    Unmapped,

    /// <summary>Manual entries carry their own category; rules don't apply.</summary>
    Manual,
}

public sealed record RuleMatch(RuleOutcome Outcome, Rule? Rule)
{
    public static RuleMatch Unmapped { get; } = new(RuleOutcome.Unmapped, null);
    public static RuleMatch Manual { get; } = new(RuleOutcome.Manual, null);
}

/// <summary>
/// Classifies activities: relevance filters first, then direct mappings, then heuristic rules in order.
/// The first matching rule wins.
/// </summary>
public sealed class RuleEngine
{
    private readonly List<Rule> _filters;
    private readonly List<Rule> _direct;
    private readonly List<Rule> _heuristics;

    public RuleEngine(IEnumerable<Rule> rules)
    {
        List<Rule> enabled = rules.Where(rule => rule.Enabled && rule.Conditions.Count > 0).ToList();
        _filters = enabled.Where(rule => rule.Stage == RuleStage.Filter).ToList();
        _direct = enabled.Where(rule => rule.Stage == RuleStage.Direct).ToList();
        _heuristics = enabled.Where(rule => rule.Stage == RuleStage.Heuristic).ToList();
    }

    public RuleMatch Classify(Activity activity)
    {
        if (activity.Source == ActivitySource.Manual)
        {
            return RuleMatch.Manual;
        }

        foreach (Rule rule in _filters)
        {
            if (Matches(rule, activity))
            {
                return new RuleMatch(RuleOutcome.Filtered, rule);
            }
        }

        RuleMatch? mapped = FirstMapping(_direct, activity) ?? FirstMapping(_heuristics, activity);
        return mapped ?? RuleMatch.Unmapped;
    }

    /// <summary>The mapping that applies when relevance filters are left out, which is what the board shows per group.</summary>
    public RuleMatch ClassifyIgnoringFilters(Activity activity) =>
        FirstMapping(_direct, activity) ?? FirstMapping(_heuristics, activity) ?? RuleMatch.Unmapped;

    public static bool Matches(Rule rule, Activity activity)
    {
        if (!rule.AppliesToSource(activity.Source) || rule.Conditions.Count == 0)
        {
            return false;
        }

        return rule.Match == ConditionMatch.Any
            ? rule.Conditions.Any(condition => ConditionEvaluator.Evaluate(condition, activity))
            : rule.Conditions.All(condition => ConditionEvaluator.Evaluate(condition, activity));
    }

    private static RuleMatch? FirstMapping(List<Rule> rules, Activity activity)
    {
        foreach (Rule rule in rules)
        {
            if (Matches(rule, activity))
            {
                return new RuleMatch(rule.Action == RuleAction.Ignore ? RuleOutcome.NotRelevant : RuleOutcome.Mapped, rule);
            }
        }

        return null;
    }
}
