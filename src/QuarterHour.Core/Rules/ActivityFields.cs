using System.Globalization;
using QuarterHour.Core.Configuration;
using QuarterHour.Core.Model;

namespace QuarterHour.Core.Rules;

public enum FieldType
{
    Text,
    Number,
    Boolean,
    List,
}

public sealed record FieldDefinition(string Key, string Label, FieldType Type, RuleScope Scope, string? Hint = null);

/// <summary>A field's value, readable as text, number, flag or list, whichever the operator needs.</summary>
public readonly record struct FieldValue(string? Text, double? Number, bool? Flag, IReadOnlyList<string>? Items)
{
    public static FieldValue OfText(string? text) => new(text, null, null, null);

    public static FieldValue OfNumber(double number) =>
        new(number.ToString(CultureInfo.InvariantCulture), number, null, null);

    public static FieldValue OfFlag(bool flag) => new(flag ? "true" : "false", null, flag, null);

    public static FieldValue OfList(IReadOnlyList<string> items) => new(string.Join("; ", items), null, null, items);
}

/// <summary>The activity fields rules can test, and how to read them.</summary>
public static class ActivityFields
{
    public const string GroupKey = "groupKey";

    public static IReadOnlyList<FieldDefinition> All { get; } =
    [
        new("title", "Title / subject", FieldType.Text, RuleScope.Any, "Meeting subject, or the agent session's title"),
        new("group", "Group (as shown on the board)", FieldType.Text, RuleScope.Any),
        new("duration", "Duration (minutes)", FieldType.Number, RuleScope.Any),
        new("dayOfWeek", "Day of week", FieldType.Text, RuleScope.Any, "Monday … Sunday"),
        new("startTime", "Start time (HH:mm)", FieldType.Text, RuleScope.Any),
        new("organizer", "Organizer name", FieldType.Text, RuleScope.Calendar),
        new("organizerEmail", "Organizer email", FieldType.Text, RuleScope.Calendar),
        new("eventLocation", "Meeting location / room", FieldType.Text, RuleScope.Calendar),
        new("categories", "Outlook categories", FieldType.List, RuleScope.Calendar),
        new("showAs", "Show as", FieldType.Text, RuleScope.Calendar, "free, tentative, busy, oof, workingElsewhere"),
        new("response", "My response", FieldType.Text, RuleScope.Calendar, "none, organizer, tentativelyAccepted, accepted, declined, notResponded"),
        new("isAllDay", "Is all-day", FieldType.Boolean, RuleScope.Calendar),
        new("isOrganizer", "I organised it", FieldType.Boolean, RuleScope.Calendar),
        new("attendees", "Number of attendees", FieldType.Number, RuleScope.Calendar, "Not counting the organiser"),
        new("isOnline", "Is an online meeting", FieldType.Boolean, RuleScope.Calendar),
        new("isRecurring", "Is recurring", FieldType.Boolean, RuleScope.Calendar),
        new("sensitivity", "Sensitivity", FieldType.Text, RuleScope.Calendar, "normal, personal, private, confidential"),
        new("isCancelled", "Is cancelled", FieldType.Boolean, RuleScope.Calendar),
        new("agent", "Agent", FieldType.Text, RuleScope.Agent, "claude-code, copilot-cli, codex, opencode, pi, …"),
        new("project", "Project", FieldType.Text, RuleScope.Agent),
        new("path", "Project folder", FieldType.Text, RuleScope.Agent),
        new("repository", "Repository", FieldType.Text, RuleScope.Agent),
        new("branch", "Git branch", FieldType.Text, RuleScope.Agent),
        new("messages", "Messages in block", FieldType.Number, RuleScope.Agent),
        new(GroupKey, "Group key (exact)", FieldType.Text, RuleScope.Any, "Used by mappings made on the board"),
    ];

    public static FieldDefinition? Find(string key) =>
        All.FirstOrDefault(field => string.Equals(field.Key, key, StringComparison.OrdinalIgnoreCase));

    public static FieldValue Get(Activity activity, string field) => field.ToLowerInvariant() switch
    {
        "title" or "subject" => FieldValue.OfText(activity.Title),
        "group" => FieldValue.OfText(activity.GroupLabel),
        "groupkey" => FieldValue.OfText(activity.GroupKey),
        "source" => FieldValue.OfText(activity.Source.ToString().ToLowerInvariant()),
        "duration" => FieldValue.OfNumber(Math.Round(activity.Duration.TotalMinutes, 2)),
        "dayofweek" => FieldValue.OfText(activity.Start.DayOfWeek.ToString()),
        "starttime" => FieldValue.OfText(activity.Start.ToString("HH:mm", CultureInfo.InvariantCulture)),
        "organizer" => FieldValue.OfText(activity.Organizer),
        "organizeremail" => FieldValue.OfText(activity.OrganizerEmail),
        "eventlocation" => FieldValue.OfText(activity.EventLocation),
        "categories" => FieldValue.OfList(activity.Categories),
        "showas" => FieldValue.OfText(activity.ShowAs),
        "response" => FieldValue.OfText(activity.Response),
        "isallday" => FieldValue.OfFlag(activity.IsAllDay),
        "isorganizer" => FieldValue.OfFlag(activity.IsOrganizer),
        "attendees" => FieldValue.OfNumber(activity.AttendeeCount),
        "isonline" => FieldValue.OfFlag(activity.IsOnlineMeeting),
        "isrecurring" => FieldValue.OfFlag(activity.IsRecurring),
        "sensitivity" => FieldValue.OfText(activity.Sensitivity),
        "iscancelled" => FieldValue.OfFlag(activity.IsCancelled),
        "agent" => FieldValue.OfText(activity.Agent),
        "project" => FieldValue.OfText(activity.Source == ActivitySource.Agent ? activity.GroupLabel : null),
        "path" => FieldValue.OfText(activity.ProjectPath),
        "repository" => FieldValue.OfText(activity.Repository),
        "branch" => FieldValue.OfText(activity.Branch),
        "messages" => FieldValue.OfNumber(activity.MessageCount),
        _ => FieldValue.OfText(null),
    };
}
