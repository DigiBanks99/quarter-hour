namespace QuarterHour.Core.Configuration;

/// <summary>
/// The starting configuration: the Timesheet system's internal projects and categories, a placeholder client
/// project (rename it, or learn the real one from a previous timesheet export) and the rules most calendars need.
/// </summary>
public static class DefaultWorkspace
{
    public const string ClientProject = "Client project";

    public const string ClientMeetings = "client-meetings";
    public const string ClientAnalysis = "client-analysis";
    public const string ClientDevelopment = "client-development";
    public const string ClientSupport = "client-support";
    public const string OneOnOneAttendance = "reviews-one-on-one-attendance";
    public const string OneOnOneFacilitation = "reviews-one-on-one-facilitation";
    public const string OnlineReviewCapture = "reviews-online-review-capture";
    public const string KnowledgeSharing = "reviews-knowledge-sharing";
    public const string TeamBuild = "social-team-building";
    public const string TeamLunch = "social-team-lunch";
    public const string CoffeeAndCode = "training-coffee-and-code";
    public const string SolutionsArchitecture = "solutions-architecture-general";
    public const string AnnualLeave = "leave-annual";

    private const string OneOnOnePattern = @"\b(1\s*[:-]\s*1|one[ -]on[ -]one)\b";

    public static WorkspaceConfig Create() => new WorkspaceConfig
    {
        Categories =
        [
            new Category { Id = ClientMeetings, Group = ClientProject, Name = "Meetings", Billable = true, Color = "#0891b2" },
            new Category { Id = ClientAnalysis, Group = ClientProject, Name = "Analysis", Billable = true, Color = "#2563eb" },
            new Category { Id = ClientDevelopment, Group = ClientProject, Name = "Development", Billable = true, Color = "#4f46e5" },
            new Category { Id = ClientSupport, Group = ClientProject, Name = "Support", Billable = true, Color = "#0d9488" },
            new Category { Id = OneOnOneAttendance, Group = "Meetings and Reviews", Name = "One On One Attendance", Color = "#7c3aed" },
            new Category { Id = OneOnOneFacilitation, Group = "Meetings and Reviews", Name = "One On One Facilitation", Color = "#9333ea" },
            new Category { Id = OnlineReviewCapture, Group = "Meetings and Reviews", Name = "Online Review Capture", Color = "#a855f7" },
            new Category { Id = KnowledgeSharing, Group = "Meetings and Reviews", Name = "Entelect Knowledge Sharing Meeting", Color = "#c026d3" },
            new Category { Id = TeamBuild, Group = "Social Events", Name = "Team Building Attendance", Color = "#ea580c" },
            new Category { Id = TeamLunch, Group = "Social Events", Name = "Team Lunch Attendance", Color = "#f97316" },
            new Category { Id = CoffeeAndCode, Group = "Training", Name = "Coffee & Code Attendance", Color = "#ca8a04" },
            new Category { Id = SolutionsArchitecture, Group = "Solutions Architecture", Name = "General", Color = "#db2777" },
            new Category { Id = AnnualLeave, Group = "Leave", Name = "Annual Leave", IsLeave = true, Color = "#16a34a" },
        ],
        Rules =
        [
            Filter("Cancelled meetings", Condition("isCancelled", ConditionOperator.IsTrue)),
            Filter("Invitations I declined", Condition("response", ConditionOperator.Equals, "declined")),
            Filter("Shown as free", Condition("showAs", ConditionOperator.Equals, "free")),
            Filter("All-day reminders (not out of office)",
                Condition("isAllDay", ConditionOperator.IsTrue),
                Condition("showAs", ConditionOperator.NotEquals, "oof")),
            Heuristic("Coffee & Code", CoffeeAndCode, Condition("title", ConditionOperator.Matches, @"coffee\s*(&|and|n)\s*code")),
            new Rule
            {
                Name = "Coffee runs",
                AppliesTo = RuleScope.Calendar,
                Conditions = [Condition("title", ConditionOperator.Matches, "[Cc]of+ee run")],
                CategoryId = TeamBuild,
                LocationPin = "Client",
            },
            Heuristic("Team lunches", TeamLunch, Condition("title", ConditionOperator.Matches, @"team\s+lunch")),
            Heuristic("One-on-ones I run", OneOnOneFacilitation,
                Condition("isOrganizer", ConditionOperator.IsTrue),
                Condition("title", ConditionOperator.Matches, OneOnOnePattern)),
            Heuristic("One-on-ones", OneOnOneAttendance, Condition("title", ConditionOperator.Matches, OneOnOnePattern)),
            Heuristic("Online review capture", OnlineReviewCapture, Condition("title", ConditionOperator.Contains, "review capture")),
            Heuristic("Knowledge sharing", KnowledgeSharing, Condition("title", ConditionOperator.Contains, "knowledge sharing")),
            Heuristic("Out of office", AnnualLeave, Condition("showAs", ConditionOperator.Equals, "oof")),
            new Rule
            {
                Name = "Space blockers",
                AppliesTo = RuleScope.Calendar,
                Conditions = [Condition("title", ConditionOperator.Matches, @"^(focus( ?time)?|lunch|block(ed|er)?|hold|placeholder|do not book|busy)\b")],
                Action = RuleAction.Ignore,
            },
            new Rule
            {
                Name = "Events with nobody else invited",
                Enabled = false,
                AppliesTo = RuleScope.Calendar,
                Conditions =
                [
                    Condition("isOrganizer", ConditionOperator.IsTrue),
                    Condition("attendees", ConditionOperator.Equals, "0"),
                ],
                Action = RuleAction.Ignore,
            },
        ],
    }.Normalize();

    private static Rule Filter(string name, params RuleCondition[] conditions) => new()
    {
        Name = name,
        Stage = RuleStage.Filter,
        AppliesTo = RuleScope.Calendar,
        Action = RuleAction.Ignore,
        Conditions = [.. conditions],
    };

    private static Rule Heuristic(string name, string categoryId, params RuleCondition[] conditions) => new()
    {
        Name = name,
        AppliesTo = RuleScope.Calendar,
        Conditions = [.. conditions],
        CategoryId = categoryId,
    };

    private static RuleCondition Condition(string field, ConditionOperator op, string value = "") =>
        new() { Field = field, Operator = op, Value = value };
}
