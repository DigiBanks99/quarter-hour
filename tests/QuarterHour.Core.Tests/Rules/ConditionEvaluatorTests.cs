using QuarterHour.Core.Configuration;
using QuarterHour.Core.Model;
using QuarterHour.Core.Rules;

namespace QuarterHour.Core.Tests.Rules;

public sealed class ConditionEvaluatorTests
{
    private static readonly DateTime Morning = new(2026, 11, 9, 9, 0, 0);

    [Test]
    [Arguments("Coffee run", true)]
    [Arguments("coffee run with the team", true)]
    [Arguments("Cofffee run!", true)]
    [Arguments("Tea run", false)]
    public void AMeetingIsCheckedForACoffeeRun(string subject, bool expected)
    {
        RuleCondition condition = new() { Field = "title", Operator = ConditionOperator.Matches, Value = "[Cc]of+ee run" };

        bool matched = ConditionEvaluator.Evaluate(condition, TestData.Meeting("1", subject, Morning, 30));

        ItShouldMatchOnlyCoffeeRuns();

        void ItShouldMatchOnlyCoffeeRuns() => matched.ShouldBe(expected);
    }

    [Test]
    public void APatternIsCaseSensitive()
    {
        RuleCondition condition = new() { Field = "title", Operator = ConditionOperator.Matches, Value = "^Standup$", CaseSensitive = true };

        bool lowerCase = ConditionEvaluator.Evaluate(condition, TestData.Meeting("1", "standup", Morning, 15));
        bool exact = ConditionEvaluator.Evaluate(condition, TestData.Meeting("2", "Standup", Morning, 15));

        ItShouldRespectTheCase();

        void ItShouldRespectTheCase()
        {
            lowerCase.ShouldBeFalse();
            exact.ShouldBeTrue();
        }
    }

    [Test]
    public void APatternIsInvalid()
    {
        Activity meeting = TestData.Meeting("1", "Planning", Morning, 60);

        bool matches = ConditionEvaluator.Evaluate(new RuleCondition { Field = "title", Operator = ConditionOperator.Matches, Value = "([" }, meeting);
        bool doesNotMatch = ConditionEvaluator.Evaluate(new RuleCondition { Field = "title", Operator = ConditionOperator.NotMatches, Value = "([" }, meeting);
        string? problem = ConditionEvaluator.ValidatePattern("([");

        ItShouldNeverMatch();
        ItShouldExplainTheProblem();

        void ItShouldNeverMatch()
        {
            matches.ShouldBeFalse();
            doesNotMatch.ShouldBeFalse();
        }

        void ItShouldExplainTheProblem() => problem.ShouldNotBeNullOrWhiteSpace();
    }

    [Test]
    public void AMeetingHasOutlookCategories()
    {
        Activity meeting = TestData.Meeting("1", "Planning", Morning, 60) with { Categories = ["Blocker", "Client"] };

        bool equals = ConditionEvaluator.Evaluate(new RuleCondition { Field = "categories", Operator = ConditionOperator.Equals, Value = "client" }, meeting);
        bool contains = ConditionEvaluator.Evaluate(new RuleCondition { Field = "categories", Operator = ConditionOperator.Contains, Value = "lock" }, meeting);
        bool notEquals = ConditionEvaluator.Evaluate(new RuleCondition { Field = "categories", Operator = ConditionOperator.NotEquals, Value = "Blocker" }, meeting);
        bool empty = ConditionEvaluator.Evaluate(new RuleCondition { Field = "categories", Operator = ConditionOperator.IsEmpty }, meeting);

        ItShouldMatchAnyOfThem();
        ItShouldOnlyCallItDifferentWhenNoneMatch();
        ItShouldNotBeEmpty();

        void ItShouldMatchAnyOfThem()
        {
            equals.ShouldBeTrue();
            contains.ShouldBeTrue();
        }

        void ItShouldOnlyCallItDifferentWhenNoneMatch() => notEquals.ShouldBeFalse();

        void ItShouldNotBeEmpty() => empty.ShouldBeFalse();
    }

    [Test]
    public void AMeetingHasFiveAttendees()
    {
        Activity meeting = TestData.Meeting("1", "Refinement", Morning, 60, attendees: 5);

        bool more = ConditionEvaluator.Evaluate(new RuleCondition { Field = "attendees", Operator = ConditionOperator.GreaterThan, Value = "3" }, meeting);
        bool fewer = ConditionEvaluator.Evaluate(new RuleCondition { Field = "attendees", Operator = ConditionOperator.LessThan, Value = "3" }, meeting);

        ItShouldCompareAsNumbers();

        void ItShouldCompareAsNumbers()
        {
            more.ShouldBeTrue();
            fewer.ShouldBeFalse();
        }
    }

    [Test]
    public void TheStatusIsOneOfSeveral()
    {
        Activity meeting = TestData.Meeting("1", "Holiday", Morning, 60, showAs: "oof");

        bool matched = ConditionEvaluator.Evaluate(new RuleCondition { Field = "showAs", Operator = ConditionOperator.OneOf, Value = "free, oof" }, meeting);

        ItShouldMatchAnyListedValue();

        void ItShouldMatchAnyListedValue() => matched.ShouldBeTrue();
    }
}
