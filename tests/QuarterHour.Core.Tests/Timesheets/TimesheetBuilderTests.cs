using QuarterHour.Core.Configuration;
using QuarterHour.Core.Locations;
using QuarterHour.Core.Model;
using QuarterHour.Core.Time;
using QuarterHour.Core.Timesheets;

namespace QuarterHour.Core.Tests.Timesheets;

public sealed class TimesheetBuilderTests
{
    private static readonly TimesheetPeriod November = new(2026, 11);
    private static readonly DateOnly Monday = new(2026, 11, 9);
    private static readonly DateOnly Tuesday = new(2026, 11, 10);
    private static readonly DateOnly Friday = new(2026, 11, 13);
    private static readonly DateOnly Saturday = new(2026, 11, 14);

    private readonly WorkspaceConfig _config = TestData.Workspace();
    private readonly Journal _journal = new();
    private readonly TimesheetBuilder _builder = new();

    [Test]
    public void AMeetingLastsFiftyMinutes()
    {
        _config.Rules.Add(MeetingsAreClientMeetings());

        PeriodTimesheet sheet = Build(TestData.Meeting("m1", "Client sync", At(Monday, 10), 50));

        ItShouldLogFortyFiveMinutes();

        void ItShouldLogFortyFiveMinutes() => sheet.Lines.Single().Minutes.ShouldBe(45);
    }

    [Test]
    public void AgentWorkOnAProjectAddsUpToThreeHoursTwentyTwo()
    {
        _config.Rules.Add(TestData.Direct("agent:portal", DefaultWorkspace.ClientDevelopment));

        PeriodTimesheet sheet = Build(
            TestData.AgentBlock("s1", "portal", At(Monday, 8), 120, title: "Fix login"),
            TestData.AgentBlock("s2", "portal", At(Monday, 13), 82, agent: "claude-code", title: "Add tests"));

        TimesheetLine line = sheet.Lines.Single();

        ItShouldLogOneLineForTheDay();
        ItShouldRoundUpToThreeAndAHalfHours();
        ItShouldDescribeTheWork();

        void ItShouldLogOneLineForTheDay() => line.CategoryId.ShouldBe(DefaultWorkspace.ClientDevelopment);

        void ItShouldRoundUpToThreeAndAHalfHours() => line.Minutes.ShouldBe(210);

        void ItShouldDescribeTheWork() => line.Description.ShouldBe("portal: Fix login; Add tests");
    }

    [Test]
    public void TwoRepositoriesShareAFriendlyName()
    {
        _config.Rules.Add(TestData.Direct("agent:portal-api", DefaultWorkspace.ClientDevelopment));
        _config.Rules.Add(TestData.Direct("agent:portal-web", DefaultWorkspace.ClientDevelopment));

        PeriodTimesheet sheet = Build(
            TestData.AgentBlock("s1", "portal-api", At(Monday, 8), 60, title: "Fix login") with { GroupLabel = "Portal" },
            TestData.AgentBlock("s2", "portal-web", At(Monday, 10), 60, title: "Restyle login") with { GroupLabel = "Portal" });

        ItShouldDescribeThemUnderOneName();

        void ItShouldDescribeThemUnderOneName() => sheet.Lines.Single().Description.ShouldBe("Portal: Fix login; Restyle login");
    }

    [Test]
    public void ACoffeeRunIsOnTheCalendar()
    {
        _config.Rules.AddRange(DefaultWorkspace.Create().Rules);

        PeriodTimesheet sheet = Build(TestData.Meeting("m1", "Cofffee run", At(Monday, 15), 30));
        ActivityResult coffee = sheet.Activities.Single();

        ItShouldCountAsATeamBuild();
        ItShouldHappenAtTheClientEvenOnAHomeDay();

        void ItShouldCountAsATeamBuild() => coffee.CategoryId.ShouldBe(DefaultWorkspace.TeamBuild);

        void ItShouldHappenAtTheClientEvenOnAHomeDay() => coffee.Location.ShouldSatisfyAllConditions(
            () => coffee.Location.Name.ShouldBe("Client"),
            () => coffee.Location.Origin.ShouldBe(LocationOrigin.MeetingPin));
    }

    [Test]
    public void TuesdaysAreScheduledAtTheClient()
    {
        _config.Rules.Add(MeetingsAreClientMeetings());
        _config.Locations.Schedule.Add(new LocationScheduleRule { Location = "Client", Days = [DayOfWeek.Tuesday] });

        PeriodTimesheet sheet = Build(
            TestData.Meeting("m1", "Client sync", At(Monday, 10), 60),
            TestData.Meeting("m2", "Client sync", At(Tuesday, 10), 60));

        ItShouldLogMondayAtHome();
        ItShouldLogTuesdayAtTheClient();

        void ItShouldLogMondayAtHome() => sheet.Lines.Single(line => line.Date == Monday).Location.ShouldBe("Home");

        void ItShouldLogTuesdayAtTheClient() => sheet.Lines.Single(line => line.Date == Tuesday).Location.ShouldBe("Client");
    }

    [Test]
    public void ADayIsSpentAtTheOffice()
    {
        _config.Rules.Add(MeetingsAreClientMeetings());
        _journal.Days[Journal.DayKey(Monday)] = new DayOverride { Location = "Entelect" };

        PeriodTimesheet sheet = Build(TestData.Meeting("m1", "Client sync", At(Monday, 10), 60));

        ItShouldLogTheDayAtTheOffice();

        void ItShouldLogTheDayAtTheOffice() => sheet.Lines.Single().Location.ShouldBe("Entelect");
    }

    [Test]
    public void AMeetingIsNotMappedYet()
    {
        PeriodTimesheet sheet = Build(TestData.Meeting("m1", "Mystery meeting", At(Monday, 10), 60));

        ItShouldBeFlaggedAsUnmapped();
        ItShouldStillShowUpInTheTotals();

        void ItShouldBeFlaggedAsUnmapped() => sheet.Activities.Single().Status.ShouldBe(ActivityStatus.Unmapped);

        void ItShouldStillShowUpInTheTotals() => sheet.Totals.ShouldSatisfyAllConditions(
            () => sheet.Totals.UnmappedActivities.ShouldBe(1),
            () => sheet.Totals.UnmappedMinutes.ShouldBe(60),
            () => sheet.Totals.BillableMinutes.ShouldBe(0));
    }

    [Test]
    public void AnInvitationWasDeclined()
    {
        _config.Rules.Add(TestData.Filter("Declined", "response", ConditionOperator.Equals, "declined"));
        _config.Rules.Add(MeetingsAreClientMeetings());

        PeriodTimesheet sheet = Build(TestData.Meeting("m1", "Client sync", At(Monday, 10), 60, response: "declined"));

        ItShouldBeFilteredOut();
        ItShouldNotBeLogged();

        void ItShouldBeFilteredOut() => sheet.Activities.Single().Status.ShouldBe(ActivityStatus.Filtered);

        void ItShouldNotBeLogged() => sheet.Lines.ShouldBeEmpty();
    }

    [Test]
    public void ADeclinedMeetingWasAttendedAfterAll()
    {
        _config.Rules.Add(TestData.Filter("Declined", "response", ConditionOperator.Equals, "declined"));
        _config.Rules.Add(MeetingsAreClientMeetings());
        _journal.Overrides["cal:m1"] = new ActivityOverride { Excluded = false };

        PeriodTimesheet sheet = Build(TestData.Meeting("m1", "Client sync", At(Monday, 10), 60, response: "declined"));

        ItShouldBeCountedUnderItsUsualCategory();

        void ItShouldBeCountedUnderItsUsualCategory() => sheet.Lines.Single().CategoryId.ShouldBe(DefaultWorkspace.ClientMeetings);
    }

    [Test]
    public void OvertimeIsWorkedOnASaturday()
    {
        _config.Rules.Add(TestData.Direct("agent:portal", DefaultWorkspace.ClientDevelopment));

        PeriodTimesheet sheet = Build(TestData.AgentBlock("s1", "portal", At(Saturday, 10), 120));
        DaySummary saturday = sheet.Day(Saturday)!;

        ItShouldRecordTheWeekendWork();
        ItShouldNotExpectWeekendHours();

        void ItShouldRecordTheWeekendWork() => saturday.ShouldSatisfyAllConditions(
            () => saturday.TotalMinutes.ShouldBe(120),
            () => sheet.Totals.WeekendOrHolidayDaysWorked.ShouldBe(1));

        void ItShouldNotExpectWeekendHours() => saturday.ShouldSatisfyAllConditions(
            () => saturday.Kind.ShouldBe(DayKind.Weekend),
            () => saturday.MissingMinutes.ShouldBe(0));
    }

    [Test]
    public void AWorkdayIsShortOfEightHours()
    {
        _config.Rules.Add(MeetingsAreClientMeetings());

        PeriodTimesheet sheet = Build(TestData.Meeting("m1", "Client sync workshop", At(Monday, 9), 180));
        DaySummary monday = sheet.Day(Monday)!;

        ItShouldReportTheMissingTime();

        void ItShouldReportTheMissingTime() => monday.ShouldSatisfyAllConditions(
            () => monday.MissingMinutes.ShouldBe(300),
            () => monday.NeedsAttention.ShouldBeTrue());
    }

    [Test]
    public void TheStandUpStaysInTheCalendarDuringLeave()
    {
        _config.Rules.Add(MeetingsAreClientMeetings());
        _config.Rules.Add(TestData.Heuristic("Out of office", "showAs", ConditionOperator.Equals, "oof", DefaultWorkspace.AnnualLeave));
        _journal.Overrides["cal:m2"] = new ActivityOverride { Excluded = false };

        PeriodTimesheet sheet = Build(
            TestData.AllDay("leave", "Leave", Tuesday, days: 1),
            TestData.Meeting("m1", "Client sync", At(Tuesday, 9), 30),
            TestData.Meeting("m2", "Client sync escalation", At(Tuesday, 15), 30));

        ItShouldLeaveTheMeetingOut();
        ItShouldStillCountAMeetingIChoseToKeep();
        ItShouldSayWhy();

        void ItShouldLeaveTheMeetingOut() => sheet.Activities.Single(result => result.Activity.Id == "cal:m1").Status.ShouldBe(ActivityStatus.Filtered);

        void ItShouldStillCountAMeetingIChoseToKeep() => sheet.Day(Tuesday)!.TotalMinutes.ShouldBe(480 + 30);

        void ItShouldSayWhy() => sheet.Activities.Single(result => result.Activity.Id == "cal:m1").Note.ShouldBe("On leave that day");
    }

    [Test]
    public void LeaveIsTakenInAMonthShortOfBillableHours()
    {
        _config.Rules.Add(TestData.Direct("agent:portal", DefaultWorkspace.ClientDevelopment));
        _config.Rules.Add(TestData.Heuristic("Out of office", "showAs", ConditionOperator.Equals, "oof", DefaultWorkspace.AnnualLeave));

        PeriodTimesheet sheet = Build(
            TestData.AgentBlock("s1", "portal", At(Monday, 8), 480),
            TestData.AllDay("leave", "Leave", Tuesday, days: 1));

        ItShouldCountTheLeave();
        ItShouldTakeTheLeaveOffTheTarget();
        ItShouldWarnAboutTheShortfall();

        void ItShouldCountTheLeave() => sheet.Day(Tuesday)!.LeaveMinutes.ShouldBe(480);

        void ItShouldTakeTheLeaveOffTheTarget() => sheet.Totals.TargetMinutes.ShouldBe((160 * 60) - 480);

        void ItShouldWarnAboutTheShortfall() => sheet.Totals.ShouldSatisfyAllConditions(
            () => sheet.Totals.BelowTarget.ShouldBeTrue(),
            () => sheet.Totals.BillableShortfallMinutes.ShouldBe((160 * 60) - 480 - 480));
    }

    [Test]
    public void LeaveRunsOverAWeekend()
    {
        _config.Rules.Add(TestData.Heuristic("Out of office", "showAs", ConditionOperator.Equals, "oof", DefaultWorkspace.AnnualLeave));

        PeriodTimesheet sheet = Build(TestData.AllDay("leave", "Long weekend", Friday, days: 4));

        ItShouldCountTheWorkingDaysOnly();

        void ItShouldCountTheWorkingDaysOnly() =>
            new[] { Friday, Saturday, Saturday.AddDays(1), Saturday.AddDays(2) }
                .Select(day => sheet.Day(day)!.LeaveMinutes)
                .ShouldBe([480, 0, 0, 480]);
    }

    [Test]
    public void AMeetingInterruptsAgentWork()
    {
        _config.Rules.Add(TestData.Direct("agent:portal", DefaultWorkspace.ClientDevelopment));
        _config.Rules.Add(MeetingsAreClientMeetings());

        PeriodTimesheet sheet = Build(
            TestData.AgentBlock("s1", "portal", At(Monday, 9), 120),
            TestData.Meeting("m1", "Client sync", At(Monday, 10), 30));
        ActivityResult agentWork = sheet.Activities.Single(result => result.Activity.Source == ActivitySource.Agent);

        ItShouldGiveTheMeetingItsTime();
        ItShouldOnlyCountTheRestAsDevelopment();
        ItShouldShowWhatInterrupted();

        void ItShouldGiveTheMeetingItsTime() => sheet.Lines.Single(line => line.CategoryId == DefaultWorkspace.ClientMeetings).Minutes.ShouldBe(30);

        void ItShouldOnlyCountTheRestAsDevelopment() => sheet.Lines.Single(line => line.CategoryId == DefaultWorkspace.ClientDevelopment).Minutes.ShouldBe(90);

        void ItShouldShowWhatInterrupted() => agentWork.ShouldSatisfyAllConditions(
            () => agentWork.WasClipped.ShouldBeTrue(),
            () => agentWork.OverlappedBy.ShouldBe(["cal:m1"]));
    }

    [Test]
    public void AMeetingIsExcludedByHand()
    {
        _config.Rules.Add(MeetingsAreClientMeetings());
        _journal.Overrides["cal:m1"] = new ActivityOverride { Excluded = true };

        PeriodTimesheet sheet = Build(TestData.Meeting("m1", "Client sync", At(Monday, 10), 60));

        ItShouldNotBeLogged();

        void ItShouldNotBeLogged() => sheet.ShouldSatisfyAllConditions(
            () => sheet.Activities.Single().Status.ShouldBe(ActivityStatus.Excluded),
            () => sheet.Lines.ShouldBeEmpty());
    }

    [Test]
    public void AMeetingIsMovedToAnotherCategory()
    {
        _config.Rules.Add(MeetingsAreClientMeetings());
        _journal.Overrides["cal:m1"] = new ActivityOverride { CategoryId = DefaultWorkspace.KnowledgeSharing, Description = "Guild sync" };

        PeriodTimesheet sheet = Build(TestData.Meeting("m1", "Client sync", At(Monday, 10), 60));
        TimesheetLine line = sheet.Lines.Single();

        ItShouldUseTheChosenCategory();
        ItShouldUseTheChosenDescription();

        void ItShouldUseTheChosenCategory() => line.CategoryId.ShouldBe(DefaultWorkspace.KnowledgeSharing);

        void ItShouldUseTheChosenDescription() => line.Description.ShouldBe("Guild sync");
    }

    [Test]
    public void AManualEntryFillsTheDay()
    {
        _journal.ManualEntries.Add(new ManualEntry { Date = Monday, Minutes = 300, CategoryId = DefaultWorkspace.ClientDevelopment, Description = "Pairing" });

        PeriodTimesheet sheet = Build();
        TimesheetLine line = sheet.Lines.Single();

        ItShouldBeLoggedAsEntered();

        void ItShouldBeLoggedAsEntered() => line.ShouldSatisfyAllConditions(
            () => line.Minutes.ShouldBe(300),
            () => line.Source.ShouldBe(ActivitySource.Manual),
            () => line.Location.ShouldBe("Home"));
    }

    [Test]
    [Arguments(RoundingScope.EachLine, 30)]
    [Arguments(RoundingScope.DailyTotalPerCategory, 45)]
    public void TwoShortMeetingsShareACategory(RoundingScope scope, int expectedMinutes)
    {
        _config.Rounding.Scope = scope;
        _config.Rules.Add(MeetingsAreClientMeetings());

        PeriodTimesheet sheet = Build(
            TestData.Meeting("m1", "Client sync", At(Monday, 9), 20),
            TestData.Meeting("m2", "Client sync follow-up", At(Monday, 14), 20));

        ItShouldRoundAsConfigured();

        void ItShouldRoundAsConfigured() => sheet.Lines.Sum(line => line.Minutes).ShouldBe(expectedMinutes);
    }

    [Test]
    public void AMeetingRunsPastMidnight()
    {
        _config.Rules.Add(MeetingsAreClientMeetings());

        PeriodTimesheet sheet = Build(TestData.Meeting("m1", "Client sync release", At(Monday, 23), 120));

        ItShouldBeSplitAcrossBothDays();

        void ItShouldBeSplitAcrossBothDays() => sheet.Lines.Select(line => (line.Date, line.Minutes)).ShouldBe([(Monday, 60), (Tuesday, 60)]);
    }

    [Test]
    public void ThePeriodIsStillUnderway()
    {
        PeriodTimesheet sheet = Build(today: new DateOnly(2026, 11, 11));

        ItShouldOnlyCountFinishedWorkdaysAsMissing();

        void ItShouldOnlyCountFinishedWorkdaysAsMissing() => sheet.Totals.DaysMissingTime.ShouldBe(7);
    }

    private PeriodTimesheet Build(params Activity[] activities) => Build(activities, null);

    private PeriodTimesheet Build(DateOnly today) => Build([], today);

    private PeriodTimesheet Build(Activity[] activities, DateOnly? today) =>
        _builder.Build(November, _config.Normalize(), _journal, activities, today);

    private static Rule MeetingsAreClientMeetings() =>
        TestData.Heuristic("Client meetings", "title", ConditionOperator.StartsWith, "Client sync", DefaultWorkspace.ClientMeetings, scope: RuleScope.Calendar);

    private static DateTime At(DateOnly day, int hour) => day.ToDateTime(new TimeOnly(hour, 0));
}
