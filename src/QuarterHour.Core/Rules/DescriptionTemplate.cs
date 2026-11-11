using System.Text.RegularExpressions;
using QuarterHour.Core.Model;

namespace QuarterHour.Core.Rules;

/// <summary>Renders description templates such as "{title} ({organizer})" or "{project}: {branch}".</summary>
public static partial class DescriptionTemplate
{
    public static string Render(string template, Activity activity)
    {
        string rendered = Token().Replace(template, match =>
        {
            string field = match.Groups[1].Value;
            return ActivityFields.Get(activity, field).Text ?? "";
        });

        return Whitespace().Replace(rendered, " ").Trim();
    }

    [GeneratedRegex(@"\{(\w+)\}")]
    private static partial Regex Token();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
