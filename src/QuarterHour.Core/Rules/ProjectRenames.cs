using QuarterHour.Core.Configuration;

namespace QuarterHour.Core.Rules;

/// <summary>
/// The "project" rule field follows a project's friendly name, so renaming a project can change what a rule matches.
/// This finds the rules whose verdict on the project's name changes with the rename.
/// </summary>
public static class ProjectRenames
{
    public static IReadOnlyList<Rule> AffectedRules(IEnumerable<Rule> rules, string oldName, string newName)
    {
        if (string.Equals(oldName, newName, StringComparison.Ordinal))
        {
            return [];
        }

        FieldValue before = FieldValue.OfText(oldName);
        FieldValue after = FieldValue.OfText(newName);
        return rules
            .Where(rule => rule.Conditions.Any(condition =>
                string.Equals(condition.Field, "project", StringComparison.OrdinalIgnoreCase)
                && ConditionEvaluator.Evaluate(condition, before) != ConditionEvaluator.Evaluate(condition, after)))
            .ToList();
    }
}
