using System.Collections.Concurrent;
using System.Globalization;
using System.Text.RegularExpressions;
using QuarterHour.Core.Configuration;
using QuarterHour.Core.Model;

namespace QuarterHour.Core.Rules;

/// <summary>Evaluates one rule condition against an activity.</summary>
public static class ConditionEvaluator
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(250);
    private static readonly ConcurrentDictionary<(string Pattern, bool CaseSensitive), Regex?> Patterns = new();

    public static bool Evaluate(RuleCondition condition, Activity activity)
    {
        FieldValue value = ActivityFields.Get(activity, condition.Field);
        return Evaluate(condition, value);
    }

    public static bool Evaluate(RuleCondition condition, FieldValue value)
    {
        StringComparison comparison = condition.CaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        string expected = condition.Value ?? "";
        switch (condition.Operator)
        {
            case ConditionOperator.IsTrue:
                return value.Flag ?? IsTruthy(value.Text);
            case ConditionOperator.IsFalse:
                return !(value.Flag ?? IsTruthy(value.Text));
            case ConditionOperator.IsEmpty:
                return value.Items is { } emptyItems ? emptyItems.Count == 0 : string.IsNullOrWhiteSpace(value.Text);
            case ConditionOperator.IsNotEmpty:
                return value.Items is { } items ? items.Count > 0 : !string.IsNullOrWhiteSpace(value.Text);
            case ConditionOperator.GreaterThan:
                return TryNumber(value, out double greater) && TryParse(expected, out double greaterThan) && greater > greaterThan;
            case ConditionOperator.LessThan:
                return TryNumber(value, out double less) && TryParse(expected, out double lessThan) && less < lessThan;
            case ConditionOperator.OneOf:
            {
                string[] options = expected.Split([',', ';', '|'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                return Values(value).Any(text => options.Any(option => string.Equals(text, option, comparison)));
            }
            case ConditionOperator.NotEquals:
                return !Values(value).Any(text => string.Equals(text, expected, comparison));
            case ConditionOperator.NotContains:
                return !Values(value).Any(text => text.Contains(expected, comparison));
            case ConditionOperator.NotMatches:
            {
                Regex? regex = Pattern(expected, condition.CaseSensitive);
                return regex is not null && !Values(value).Any(text => SafeMatch(regex, text));
            }
            case ConditionOperator.Equals:
                return Values(value).Any(text => string.Equals(text, expected, comparison));
            case ConditionOperator.Contains:
                return Values(value).Any(text => text.Contains(expected, comparison));
            case ConditionOperator.StartsWith:
                return Values(value).Any(text => text.StartsWith(expected, comparison));
            case ConditionOperator.EndsWith:
                return Values(value).Any(text => text.EndsWith(expected, comparison));
            case ConditionOperator.Matches:
            {
                Regex? regex = Pattern(expected, condition.CaseSensitive);
                return regex is not null && Values(value).Any(text => SafeMatch(regex, text));
            }
            default:
                return false;
        }
    }

    /// <summary>Why a pattern can't be used, or null when it's fine.</summary>
    public static string? ValidatePattern(string pattern)
    {
        try
        {
            _ = new Regex(pattern, RegexOptions.CultureInvariant, RegexTimeout);
            return null;
        }
        catch (ArgumentException exception)
        {
            return exception.Message;
        }
    }

    private static IEnumerable<string> Values(FieldValue value) =>
        value.Items ?? (value.Text is null ? [] : [value.Text]);

    private static Regex? Pattern(string pattern, bool caseSensitive) =>
        Patterns.GetOrAdd((pattern, caseSensitive), static key =>
        {
            try
            {
                RegexOptions options = RegexOptions.CultureInvariant;
                if (!key.CaseSensitive)
                {
                    options |= RegexOptions.IgnoreCase;
                }

                return new Regex(key.Pattern, options, RegexTimeout);
            }
            catch (ArgumentException)
            {
                return null;
            }
        });

    private static bool SafeMatch(Regex regex, string text)
    {
        try
        {
            return regex.IsMatch(text);
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }

    private static bool TryNumber(FieldValue value, out double number)
    {
        if (value.Number is { } known)
        {
            number = known;
            return true;
        }

        return TryParse(value.Text, out number);
    }

    private static bool TryParse(string? text, out double number) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out number);

    private static bool IsTruthy(string? text) =>
        text is not null && (text.Equals("true", StringComparison.OrdinalIgnoreCase) || text == "1" || text.Equals("yes", StringComparison.OrdinalIgnoreCase));
}
