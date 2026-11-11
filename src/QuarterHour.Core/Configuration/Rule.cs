namespace QuarterHour.Core.Configuration;

/// <summary>
/// Decides what an activity is. Rules run in three stages: relevance filters first, then direct mappings
/// (made on the mapping board), then heuristic rules in order. The first match wins.
/// </summary>
public sealed class Rule
{
    public string Id { get; set; } = WorkspaceConfig.NewId();
    public string Name { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public RuleStage Stage { get; set; } = RuleStage.Heuristic;
    public RuleScope AppliesTo { get; set; } = RuleScope.Any;
    public ConditionMatch Match { get; set; } = ConditionMatch.All;
    public List<RuleCondition> Conditions { get; set; } = [];
    public RuleAction Action { get; set; } = RuleAction.Map;
    public string? CategoryId { get; set; }

    /// <summary>Where this activity always happens, whatever the day's location is (for example a coffee run at the client).</summary>
    public string? LocationPin { get; set; }

    /// <summary>Optional description, such as "{title} ({organizer})". Empty uses the activity's title.</summary>
    public string? DescriptionTemplate { get; set; }

    public bool AppliesToSource(Model.ActivitySource source) => AppliesTo switch
    {
        RuleScope.Any => source != Model.ActivitySource.Manual,
        RuleScope.Calendar => source == Model.ActivitySource.Calendar,
        RuleScope.Agent => source == Model.ActivitySource.Agent,
        _ => false,
    };
}

public enum RuleStage
{
    /// <summary>Decides relevance from the occurrence itself (declined, cancelled, free). Always ignores.</summary>
    Filter,

    /// <summary>An exact group mapping made on the mapping board.</summary>
    Direct,

    /// <summary>A pattern rule, such as "title matches [Cc]of+ee run".</summary>
    Heuristic,
}

public enum RuleScope
{
    Any,
    Calendar,
    Agent,
}

public enum ConditionMatch
{
    All,
    Any,
}

public enum RuleAction
{
    /// <summary>Count the activity under <see cref="Rule.CategoryId"/>.</summary>
    Map,

    /// <summary>The activity is not relevant (a space blocker, a reminder, a personal appointment).</summary>
    Ignore,
}

public sealed class RuleCondition
{
    public string Field { get; set; } = "title";
    public ConditionOperator Operator { get; set; } = ConditionOperator.Contains;
    public string Value { get; set; } = "";
    public bool CaseSensitive { get; set; }
}

public enum ConditionOperator
{
    Equals,
    NotEquals,
    Contains,
    NotContains,
    StartsWith,
    EndsWith,
    Matches,
    NotMatches,
    GreaterThan,
    LessThan,
    IsTrue,
    IsFalse,
    IsEmpty,
    IsNotEmpty,
    OneOf,
}
