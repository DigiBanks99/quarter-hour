using QuarterHour.Core.Configuration;
using QuarterHour.Core.Rules;

namespace QuarterHour.Core.Tests.Rules;

public sealed class ProjectRenamesTests
{
    [Test]
    public void AProjectIsRenamed()
    {
        Rule exact = TestData.Heuristic("Portal work", "project", ConditionOperator.Equals, "portal-api", "dev", scope: RuleScope.Agent);
        Rule loose = TestData.Heuristic("Anything portal", "project", ConditionOperator.Contains, "portal", "dev", scope: RuleScope.Agent);
        Rule byRepo = TestData.Heuristic("By repo", "repo", ConditionOperator.Equals, "portal-api", "dev", scope: RuleScope.Agent);

        IReadOnlyList<Rule> affected = ProjectRenames.AffectedRules([exact, loose, byRepo], "portal-api", "Portal");

        ItShouldFlagOnlyRulesWhoseMatchChanges();

        void ItShouldFlagOnlyRulesWhoseMatchChanges() => affected.ShouldBe([exact]);
    }

    [Test]
    public void TheNameStaysTheSame()
    {
        Rule rule = TestData.Heuristic("Portal work", "project", ConditionOperator.Equals, "Portal", "dev", scope: RuleScope.Agent);

        IReadOnlyList<Rule> affected = ProjectRenames.AffectedRules([rule], "Portal", "Portal");

        ItShouldFlagNothing();

        void ItShouldFlagNothing() => affected.ShouldBeEmpty();
    }
}
