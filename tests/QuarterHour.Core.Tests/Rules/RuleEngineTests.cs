using QuarterHour.Core.Configuration;
using QuarterHour.Core.Model;
using QuarterHour.Core.Rules;

namespace QuarterHour.Core.Tests.Rules;

public sealed class RuleEngineTests
{
    private static readonly DateTime Morning = new(2026, 11, 9, 9, 0, 0);
    private readonly Activity _standup = TestData.Meeting("1", "Daily standup", Morning, 15);

    [Test]
    public void AMappedMeetingWasDeclined()
    {
        Activity declined = TestData.Meeting("2", "Daily standup", Morning, 15, response: "declined");
        RuleEngine engine = new([
            TestData.Direct(declined.GroupKey, DefaultWorkspace.ClientMeetings),
            TestData.Filter("Declined", "response", ConditionOperator.Equals, "declined"),
        ]);

        RuleMatch match = engine.Classify(declined);
        RuleMatch ignoringFilters = engine.ClassifyIgnoringFilters(declined);

        ItShouldBeFilteredOut();
        ItShouldStillKnowItsMapping();

        void ItShouldBeFilteredOut() => match.Outcome.ShouldBe(RuleOutcome.Filtered);

        void ItShouldStillKnowItsMapping() => ignoringFilters.Rule!.CategoryId.ShouldBe(DefaultWorkspace.ClientMeetings);
    }

    [Test]
    public void AMeetingMatchesADirectMappingAndAHeuristic()
    {
        RuleEngine engine = new([
            TestData.Heuristic("Anything with standup", "title", ConditionOperator.Contains, "standup", DefaultWorkspace.KnowledgeSharing),
            TestData.Direct(_standup.GroupKey, DefaultWorkspace.ClientMeetings),
        ]);

        RuleMatch match = engine.Classify(_standup);

        ItShouldUseTheDirectMapping();

        void ItShouldUseTheDirectMapping() => match.Rule!.CategoryId.ShouldBe(DefaultWorkspace.ClientMeetings);
    }

    [Test]
    public void TwoHeuristicsMatch()
    {
        RuleEngine engine = new([
            TestData.Heuristic("First", "title", ConditionOperator.Contains, "daily", DefaultWorkspace.ClientMeetings),
            TestData.Heuristic("Second", "title", ConditionOperator.Contains, "standup", DefaultWorkspace.KnowledgeSharing),
        ]);

        RuleMatch match = engine.Classify(_standup);

        ItShouldUseTheFirstOne();

        void ItShouldUseTheFirstOne() => match.Rule!.Name.ShouldBe("First");
    }

    [Test]
    public void TheOnlyMatchingRuleIsDisabled()
    {
        Rule rule = TestData.Heuristic("Off", "title", ConditionOperator.Contains, "standup", DefaultWorkspace.ClientMeetings);
        rule.Enabled = false;

        RuleMatch match = new RuleEngine([rule]).Classify(_standup);

        ItShouldBeUnmapped();

        void ItShouldBeUnmapped() => match.Outcome.ShouldBe(RuleOutcome.Unmapped);
    }

    [Test]
    public void ACalendarRuleMeetsAgentWork()
    {
        Activity agentWork = TestData.AgentBlock("s1", "standup-bot", Morning, 30);
        RuleEngine engine = new([
            TestData.Heuristic("Meetings only", "title", ConditionOperator.Contains, "standup", DefaultWorkspace.ClientMeetings, scope: RuleScope.Calendar),
        ]);

        RuleMatch match = engine.Classify(agentWork);

        ItShouldNotApply();

        void ItShouldNotApply() => match.Outcome.ShouldBe(RuleOutcome.Unmapped);
    }

    [Test]
    public void AnyOneConditionIsEnough()
    {
        Rule rule = new()
        {
            Name = "Either",
            Match = ConditionMatch.Any,
            Conditions =
            [
                new RuleCondition { Field = "title", Operator = ConditionOperator.Contains, Value = "retro" },
                new RuleCondition { Field = "title", Operator = ConditionOperator.Contains, Value = "standup" },
            ],
            CategoryId = DefaultWorkspace.ClientMeetings,
        };

        RuleMatch match = new RuleEngine([rule]).Classify(_standup);

        ItShouldMatch();

        void ItShouldMatch() => match.Outcome.ShouldBe(RuleOutcome.Mapped);
    }

    [Test]
    public void ASpaceBlockerIsMarkedNotRelevant()
    {
        Activity focus = TestData.Meeting("3", "Focus time", Morning, 120, isOrganizer: true, attendees: 0);
        RuleEngine engine = new(DefaultWorkspace.Create().Rules);

        RuleMatch match = engine.Classify(focus);

        ItShouldBeIgnoredByTheStarterRules();

        void ItShouldBeIgnoredByTheStarterRules() => match.Outcome.ShouldBe(RuleOutcome.NotRelevant);
    }
}
