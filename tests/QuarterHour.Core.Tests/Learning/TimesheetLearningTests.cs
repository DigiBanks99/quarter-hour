using QuarterHour.Core.Configuration;
using QuarterHour.Core.Learning;
using QuarterHour.Core.Rules;

namespace QuarterHour.Core.Tests.Learning;

public sealed class TimesheetLearningTests
{
    private const string Client = "R - Acme - Portal: Flex Capacity";

    private static readonly IReadOnlyList<TimesheetRow> October = Rows();

    private readonly WorkspaceConfig _config = DefaultWorkspace.Create();

    [Test]
    public void LastMonthsTimesheetIsRead()
    {
        TimesheetLessons lessons = TimesheetLearning.Learn(October, _config);

        ItShouldFindTheClientProjectsCategories();
        ItShouldOfferToRenameThePlaceholderProject();
        ItShouldLearnTheRecurringMeetings();
        ItShouldLearnWhereEachWeekdayIsWorked();
        ItShouldPickTheCategoryThatFillsTheDay();

        void ItShouldFindTheClientProjectsCategories() => lessons.Categories
            .Where(category => category.Project == Client)
            .Select(category => category.Name)
            .ShouldBe(["Analysis", "Meetings"], ignoreOrder: true);

        void ItShouldOfferToRenameThePlaceholderProject() => lessons.Categories
            .Single(category => category is { Project: Client, Name: "Analysis" })
            .PlaceholderId.ShouldBe(DefaultWorkspace.ClientAnalysis);

        void ItShouldLearnTheRecurringMeetings() => lessons.Mappings.ShouldSatisfyAllConditions(
            () => lessons.Mappings.ShouldContain(mapping => mapping.Label == "Scrum and change stand-up" && mapping.Category == "Meetings"),
            () => lessons.Mappings.ShouldNotContain(mapping => mapping.Label == "Coffee Run"),
            () => lessons.Mappings.ShouldNotContain(mapping => mapping.Label == "Lindo PDR discussion"));

        void ItShouldLearnWhereEachWeekdayIsWorked() => lessons.ShouldSatisfyAllConditions(
            () => lessons.DefaultLocation.ShouldBe("Client"),
            () => lessons.Schedule.Single(day => day.Day == DayOfWeek.Wednesday).Location.ShouldBe("Home"));

        void ItShouldPickTheCategoryThatFillsTheDay() => lessons.TopUp.ShouldBe((Client, "Analysis"));
    }

    [Test]
    public void WhatWasLearnedIsApplied()
    {
        TimesheetLessons lessons = TimesheetLearning.Learn(October, _config);

        LearningSummary summary = TimesheetLearning.Apply(lessons, _config, new LearningChoices());
        Category analysis = _config.FindCategory(DefaultWorkspace.ClientAnalysis)!;
        Rule? standUp = DirectMappings.Find(_config.Rules, "cal:scrum and change stand-up");

        ItShouldRenameThePlaceholderInPlace();
        ItShouldMapTheStandUp();
        ItShouldScheduleWednesdaysAtHome();
        ItShouldTopUpWithAnalysis();

        void ItShouldRenameThePlaceholderInPlace() => analysis.ShouldSatisfyAllConditions(
            () => analysis.Group.ShouldBe(Client),
            () => summary.CategoriesRenamed.ShouldBe(2));

        void ItShouldMapTheStandUp() => standUp.ShouldNotBeNull().CategoryId.ShouldBe(DefaultWorkspace.ClientMeetings);

        void ItShouldScheduleWednesdaysAtHome() => _config.Locations.Schedule
            .ShouldContain(rule => rule.Location == "Home" && rule.Days.Contains(DayOfWeek.Wednesday));

        void ItShouldTopUpWithAnalysis() => _config.Targets.TopUpCategoryId.ShouldBe(DefaultWorkspace.ClientAnalysis);
    }

    private static List<TimesheetRow> Rows()
    {
        List<TimesheetRow> rows = [];
        DateOnly start = new(2026, 10, 12);
        for (int week = 0; week < 3; week++)
        {
            for (int weekday = 0; weekday < 5; weekday++)
            {
                DateOnly day = start.AddDays((week * 7) + weekday);
                string location = day.DayOfWeek is DayOfWeek.Wednesday or DayOfWeek.Friday ? "Home" : "Client";
                rows.Add(new TimesheetRow(day, Client, "Meetings", 30, true, "Scrum and change stand-up", null, "Neutral", location));
                rows.Add(new TimesheetRow(day, Client, "Analysis", 420, true, "Product maintenance: search\nProposals: prompts", null, "Neutral", location));
            }

            rows.Add(new TimesheetRow(start.AddDays(week * 7), "Social Events", "Team Building Attendance", 30, false, "Coffee Run", null, "Neutral", "Client"));
        }

        rows.Add(new TimesheetRow(start.AddDays(1), Client, "Meetings", 30, true, "Lindo PDR discussion", null, "Neutral", "Client"));
        return rows;
    }
}
