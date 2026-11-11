using QuarterHour.Core.Configuration;
using QuarterHour.Core.Model;
using QuarterHour.Core.Time;
using QuarterHour.Core.Timesheets;

namespace QuarterHour.Core.Tests.Timesheets;

/// <summary>The day-level touches of <see cref="TimesheetBuilder"/>: topping days up and editing whole lines.</summary>
public sealed class TopUpAndLineOverrideTests
{
    private static readonly TimesheetPeriod November = new(2026, 11);
    private static readonly DateOnly Monday = new(2026, 11, 9);
    private static readonly DateOnly Tuesday = new(2026, 11, 10);

    private readonly WorkspaceConfig _config = TestData.Workspace();
    private readonly Journal _journal = new();

    public TopUpAndLineOverrideTests()
    {
        _config.Rules.Add(TestData.Direct("agent:portal", DefaultWorkspace.ClientAnalysis));
        _config.Rules.Add(TestData.Direct("agent:proposals", DefaultWorkspace.ClientAnalysis));
        _config.Rules.Add(TestData.Heuristic("Client meetings", "title", ConditionOperator.StartsWith, "Stand-up", DefaultWorkspace.ClientMeetings, scope: RuleScope.Calendar));
    }

    [Test]
    public void ADayOfMeetingsAndAgentWorkIsToppedUp()
    {
        _config.Targets.TopUpCategoryId = DefaultWorkspace.ClientAnalysis;

        PeriodTimesheet sheet = Build(
            TestData.Meeting("m1", "Stand-up", At(Monday, 9), 30),
            TestData.AgentBlock("s1", "portal", At(Monday, 10), 202, title: "Wire up search"));
        TimesheetLine analysis = sheet.Lines.Single(line => line.CategoryId == DefaultWorkspace.ClientAnalysis);

        ItShouldFillTheDayToEightHours();
        ItShouldGrowTheAgentWorkLine();
        ItShouldShowHowMuchWasAdded();

        void ItShouldFillTheDayToEightHours() => sheet.Day(Monday)!.TotalMinutes.ShouldBe(480);

        void ItShouldGrowTheAgentWorkLine() => analysis.ShouldSatisfyAllConditions(
            () => analysis.Minutes.ShouldBe(450),
            () => analysis.Description.ShouldBe("portal: Wire up search"));

        void ItShouldShowHowMuchWasAdded() => analysis.TopUpMinutes.ShouldBe(240);
    }

    [Test]
    public void ADayHasNothingRecorded()
    {
        _config.Targets.TopUpCategoryId = DefaultWorkspace.ClientAnalysis;

        PeriodTimesheet sheet = Build(TestData.Meeting("m1", "Stand-up", At(Monday, 9), 30));

        ItShouldNotInventAWorkingDay();

        void ItShouldNotInventAWorkingDay() => sheet.Day(Tuesday)!.TotalMinutes.ShouldBe(0);
    }

    [Test]
    public void ADayHasOnlyMeetings()
    {
        _config.Targets.TopUpCategoryId = DefaultWorkspace.ClientAnalysis;

        PeriodTimesheet sheet = Build(TestData.Meeting("m1", "Stand-up", At(Monday, 9), 60));
        TimesheetLine topUp = sheet.Lines.Single(line => line.CategoryId == DefaultWorkspace.ClientAnalysis);

        ItShouldAddATopUpLine();

        void ItShouldAddATopUpLine() => topUp.ShouldSatisfyAllConditions(
            () => topUp.Minutes.ShouldBe(420),
            () => topUp.Description.ShouldBe("Analysis"),
            () => topUp.Location.ShouldBe("Home"));
    }

    [Test]
    public void AgentWorkSpansTwoProjects()
    {
        PeriodTimesheet sheet = Build(
            TestData.AgentBlock("s1", "portal", At(Monday, 9), 60, title: "Wire up search"),
            TestData.AgentBlock("s2", "portal", At(Monday, 11), 30, title: "Fix filters"),
            TestData.AgentBlock("s3", "proposals", At(Monday, 13), 45, title: "Tune the prompts"));

        string description = sheet.Lines.Single().Description;

        ItShouldWriteAWorkstreamPerLine();

        void ItShouldWriteAWorkstreamPerLine() => description.ShouldBe("portal: Wire up search; Fix filters\nproposals: Tune the prompts");
    }

    [Test]
    public void TheDaysAnalysisLineIsRewritten()
    {
        PeriodTimesheet first = Build(TestData.AgentBlock("s1", "portal", At(Monday, 9), 120, title: "Wire up search"));
        string lineId = first.Lines.Single().Id;
        _journal.Lines[lineId] = new LineOverride { Description = "Product maintenance: search", Sentiment = "Happy", Ticket = "UY-42", Minutes = 150 };

        TimesheetLine line = Build(TestData.AgentBlock("s1", "portal", At(Monday, 9), 120, title: "Wire up search")).Lines.Single();

        ItShouldUseWhatWasWritten();

        void ItShouldUseWhatWasWritten() => line.ShouldSatisfyAllConditions(
            () => line.Description.ShouldBe("Product maintenance: search"),
            () => line.Sentiment.ShouldBe("Happy"),
            () => line.Ticket.ShouldBe("UY-42"),
            () => line.Minutes.ShouldBe(150));
    }

    private PeriodTimesheet Build(params Activity[] activities) =>
        new TimesheetBuilder().Build(November, _config.Normalize(), _journal, activities, today: new DateOnly(2026, 11, 30));

    private static DateTime At(DateOnly day, int hour) => day.ToDateTime(new TimeOnly(hour, 0));
}
