using QuarterHour.Core.Configuration;
using QuarterHour.Core.Model;

namespace QuarterHour.Core.Tests;

/// <summary>Builders for activities and rules, so tests only say what matters to them.</summary>
internal static class TestData
{
    public static Activity Meeting(string id, string subject, DateTime start, int minutes, string showAs = "busy", string response = "accepted",
        bool isOrganizer = false, int attendees = 3) =>
        CalendarActivities.ToActivity(new CalendarEvent
        {
            Id = id,
            Subject = subject,
            Start = new DateTimeOffset(start, TimeSpan.Zero),
            End = new DateTimeOffset(start.AddMinutes(minutes), TimeSpan.Zero),
            ShowAs = showAs,
            Response = response,
            IsOrganizer = isOrganizer,
            AttendeeCount = attendees,
        }, TimeZoneInfo.Utc);

    public static Activity AllDay(string id, string subject, DateOnly first, int days, string showAs = "oof") =>
        CalendarActivities.ToActivity(new CalendarEvent
        {
            Id = id,
            Subject = subject,
            Start = new DateTimeOffset(first.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
            End = new DateTimeOffset(first.AddDays(days).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
            IsAllDay = true,
            ShowAs = showAs,
        }, TimeZoneInfo.Utc);

    public static Activity AgentBlock(string sessionId, string project, DateTime start, int minutes, string agent = "copilot-cli", string? title = null) =>
        new()
        {
            Id = $"agent:{agent}:{sessionId}:{start:yyyyMMddTHHmmss}",
            Source = ActivitySource.Agent,
            Start = start,
            End = start.AddMinutes(minutes),
            Title = title ?? project,
            GroupKey = "agent:" + project.ToLowerInvariant(),
            GroupLabel = project,
            Agent = agent,
            SessionId = sessionId,
            SessionTitle = title,
            ProjectPath = @"C:\source\" + project,
        };

    public static Rule Heuristic(string name, string field, ConditionOperator op, string value, string? categoryId, RuleAction action = RuleAction.Map,
        RuleScope scope = RuleScope.Any, string? pin = null) =>
        new()
        {
            Name = name,
            Stage = RuleStage.Heuristic,
            AppliesTo = scope,
            Conditions = [new RuleCondition { Field = field, Operator = op, Value = value }],
            Action = action,
            CategoryId = categoryId,
            LocationPin = pin,
        };

    public static Rule Direct(string groupKey, string? categoryId, RuleAction action = RuleAction.Map, string? pin = null) =>
        new()
        {
            Name = groupKey,
            Stage = RuleStage.Direct,
            Conditions = [new RuleCondition { Field = "groupKey", Operator = ConditionOperator.Equals, Value = groupKey }],
            Action = action,
            CategoryId = categoryId,
            LocationPin = pin,
        };

    public static Rule Filter(string name, string field, ConditionOperator op, string value = "") =>
        new()
        {
            Name = name,
            Stage = RuleStage.Filter,
            Action = RuleAction.Ignore,
            Conditions = [new RuleCondition { Field = field, Operator = op, Value = value }],
        };

    /// <summary>A workspace with a few categories, no rules, Home as the default location and Mon–Fri working days.</summary>
    public static WorkspaceConfig Workspace()
    {
        WorkspaceConfig config = DefaultWorkspace.Create();
        config.Rules.Clear();
        config.Locations.Default = "Home";
        config.Targets.SouthAfricanPublicHolidays = false;
        return config;
    }
}
