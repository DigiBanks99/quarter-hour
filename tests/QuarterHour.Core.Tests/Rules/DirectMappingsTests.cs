using QuarterHour.Core.Configuration;
using QuarterHour.Core.Model;
using QuarterHour.Core.Rules;

namespace QuarterHour.Core.Tests.Rules;

public sealed class DirectMappingsTests
{
    private readonly List<Rule> _rules =
    [
        TestData.Filter("Declined", "response", ConditionOperator.Equals, "declined"),
        TestData.Heuristic("Standups", "title", ConditionOperator.Contains, "standup", DefaultWorkspace.KnowledgeSharing),
    ];

    [Test]
    public void AGroupIsDroppedOnACategoryTwice()
    {
        DirectMappings.Link(_rules, "cal:planning", "Planning", ActivitySource.Calendar, DefaultWorkspace.ClientMeetings);
        Rule relinked = DirectMappings.Link(_rules, "cal:planning", "Planning", ActivitySource.Calendar, DefaultWorkspace.KnowledgeSharing);

        ItShouldKeepOneMapping();
        ItShouldUseTheLatestCategory();
        ItShouldSitBeforeTheHeuristics();

        void ItShouldKeepOneMapping() => _rules.Count(rule => rule.Stage == RuleStage.Direct).ShouldBe(1);

        void ItShouldUseTheLatestCategory() => relinked.CategoryId.ShouldBe(DefaultWorkspace.KnowledgeSharing);

        void ItShouldSitBeforeTheHeuristics() => _rules.IndexOf(relinked).ShouldBe(1);
    }

    [Test]
    public void AGroupIsMarkedNotRelevant()
    {
        DirectMappings.Link(_rules, "cal:planning", "Planning", ActivitySource.Calendar, DefaultWorkspace.ClientMeetings);
        DirectMappings.Pin(_rules, "cal:planning", "Planning", ActivitySource.Calendar, "Client", null);
        Rule rule = DirectMappings.Link(_rules, "cal:planning", "Planning", ActivitySource.Calendar, DirectMappings.NotRelevantTarget);

        ItShouldIgnoreTheGroup();
        ItShouldDropThePin();

        void ItShouldIgnoreTheGroup() => rule.ShouldSatisfyAllConditions(
            () => rule.Action.ShouldBe(RuleAction.Ignore),
            () => rule.CategoryId.ShouldBeNull());

        void ItShouldDropThePin() => rule.LocationPin.ShouldBeNull();
    }

    [Test]
    public void AGroupMappedByAHeuristicIsPinned()
    {
        Rule heuristic = _rules[1];

        Rule? pinned = DirectMappings.Pin(_rules, "cal:daily standup", "Daily standup", ActivitySource.Calendar, "Client", heuristic);

        ItShouldMakeTheMappingExplicit();
        ItShouldLeaveTheHeuristicAlone();

        void ItShouldMakeTheMappingExplicit() => pinned.ShouldNotBeNull().ShouldSatisfyAllConditions(
            () => pinned!.Stage.ShouldBe(RuleStage.Direct),
            () => pinned!.CategoryId.ShouldBe(DefaultWorkspace.KnowledgeSharing),
            () => pinned!.LocationPin.ShouldBe("Client"));

        void ItShouldLeaveTheHeuristicAlone() => heuristic.LocationPin.ShouldBeNull();
    }

    [Test]
    public void AnUnmappedGroupIsPinned()
    {
        Rule? pinned = DirectMappings.Pin(_rules, "cal:planning", "Planning", ActivitySource.Calendar, "Client", null);

        ItShouldNotInventAMapping();

        void ItShouldNotInventAMapping()
        {
            pinned.ShouldBeNull();
            _rules.Count.ShouldBe(2);
        }
    }

    [Test]
    public void AGroupIsUnlinked()
    {
        DirectMappings.Link(_rules, "agent:portal", "portal", ActivitySource.Agent, DefaultWorkspace.ClientDevelopment);

        bool removed = DirectMappings.Unlink(_rules, "agent:portal");

        ItShouldForgetTheMapping();

        void ItShouldForgetTheMapping()
        {
            removed.ShouldBeTrue();
            DirectMappings.Find(_rules, "agent:portal").ShouldBeNull();
        }
    }
}
