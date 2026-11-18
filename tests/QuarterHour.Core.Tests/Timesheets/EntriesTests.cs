using QuarterHour.Core.Configuration;
using QuarterHour.Core.Model;
using QuarterHour.Core.Time;
using QuarterHour.Core.Timesheets;

namespace QuarterHour.Core.Tests.Timesheets;

/// <summary>Agent blocks and manual entries of a day grouped into entries of their own.</summary>
public sealed class EntriesTests
{
    private static readonly TimesheetPeriod November = new(2026, 11);
    private static readonly DateOnly Monday = new(2026, 11, 9);

    private readonly WorkspaceConfig _config = TestData.Workspace();
    private readonly Journal _journal = new();

    public EntriesTests()
    {
        _config.Rules.Add(TestData.Direct("agent:portal", DefaultWorkspace.ClientDevelopment));
        _config.Rules.Add(TestData.Direct("agent:proposals", DefaultWorkspace.ClientDevelopment));
        _config.Rules.Add(TestData.Direct("agent:intranet", DefaultWorkspace.ClientAnalysis));
        _config.Rules.Add(TestData.Heuristic("Client meetings", "title", ConditionOperator.StartsWith, "Stand-up", DefaultWorkspace.ClientMeetings, scope: RuleScope.Calendar));
    }

    [Test]
    public void TwoProjectsAndAManualEntryAreGroupedIntoOneEntry()
    {
        Activity search = TestData.AgentBlock("s1", "portal", At(9), 40, title: "Wire up search");
        Activity prompts = TestData.AgentBlock("s2", "proposals", At(11), 25, title: "Tune the prompts");
        Activity other = TestData.AgentBlock("s3", "portal", At(14), 60, title: "Fix filters");
        string review = Manual("w1", At(13), 20, DefaultWorkspace.ClientDevelopment, "Review the design");
        Group("e1", search.Id, prompts.Id, review);

        PeriodTimesheet sheet = Build(search, prompts, other);
        TimesheetLine entry = sheet.Lines.Single(line => line.EntryId == "e1");

        ItShouldLogTheEntryAsOneLine();
        ItShouldRoundItsTimeOnce();
        ItShouldSayEachActivityOnce();
        ItShouldLeaveTheRestOnTheirUsualLine();
        ItShouldTellEachActivityItsEntry();

        void ItShouldLogTheEntryAsOneLine() => entry.PieceIds.ShouldBe([search.Id, prompts.Id, review], ignoreOrder: true);

        void ItShouldRoundItsTimeOnce() => entry.Minutes.ShouldBe(90);

        void ItShouldSayEachActivityOnce() => entry.Description.ShouldBe("portal: Wire up search\nproposals: Tune the prompts\nReview the design");

        void ItShouldLeaveTheRestOnTheirUsualLine() =>
            sheet.Lines.Where(line => line.EntryId is null).Select(line => line.Description).ShouldBe(["portal: Fix filters"]);

        void ItShouldTellEachActivityItsEntry() =>
            sheet.Activities.Where(item => item.EntryId == "e1").Select(item => item.PieceId).ShouldBe([search.Id, review, prompts.Id], ignoreOrder: true);
    }

    [Test]
    public void AnEntrysWorkWasDoneAtHomeAndAtTheClient()
    {
        Activity search = TestData.AgentBlock("s1", "portal", At(9), 60, title: "Wire up search");
        Activity filters = TestData.AgentBlock("s2", "portal", At(11), 60, title: "Fix filters");
        Activity docs = TestData.AgentBlock("s3", "portal", At(13), 60, title: "Write the docs");
        Group("e1", search.Id, filters.Id, docs.Id);
        _journal.Overrides[filters.Id].Location = "Client";
        _journal.Overrides[docs.Id].Location = "Entelect";

        TimesheetLine entry = Build(search, filters, docs).Lines.Single();

        ItShouldBeLoggedWhereTheEntryOrderSays();

        void ItShouldBeLoggedWhereTheEntryOrderSays() => entry.Location.ShouldBe("Entelect");
    }

    [Test]
    public void AnActivityInAnEntryWasMovedToOther()
    {
        Activity search = TestData.AgentBlock("s1", "portal", At(9), 60, title: "Wire up search");
        Activity filters = TestData.AgentBlock("s2", "portal", At(11), 60, title: "Fix filters");
        Activity docs = TestData.AgentBlock("s3", "portal", At(13), 60, title: "Write the docs");
        Group("e1", search.Id, filters.Id, docs.Id);
        _journal.Overrides[docs.Id].Location = "Other";

        PeriodTimesheet sheet = Build(search, filters, docs);

        ItShouldLeaveTheEntry();

        void ItShouldLeaveTheEntry() => sheet.Lines.Select(line => (line.EntryId, line.Location, line.Description)).ShouldBe(
        [
            ("e1", "Home", "portal: Wire up search; Fix filters"),
            (null, "Other", "portal: Write the docs"),
        ]);
    }

    [Test]
    public void AnActivityInAnEntryChangedCategory()
    {
        Activity search = TestData.AgentBlock("s1", "portal", At(9), 60, title: "Wire up search");
        Activity filters = TestData.AgentBlock("s2", "portal", At(11), 60, title: "Fix filters");
        Activity themes = TestData.AgentBlock("s3", "intranet", At(13), 30, title: "Add themes");
        Group("e1", search.Id, filters.Id, themes.Id);

        PeriodTimesheet sheet = Build(search, filters, themes);

        ItShouldKeepTheCategoryWithTheMostTime();
        ItShouldSendTheOddOneBackToItsUsualLine();

        void ItShouldKeepTheCategoryWithTheMostTime() =>
            sheet.Lines.Single(line => line.EntryId == "e1").CategoryId.ShouldBe(DefaultWorkspace.ClientDevelopment);

        void ItShouldSendTheOddOneBackToItsUsualLine() =>
            sheet.Lines.Single(line => line.EntryId is null).Description.ShouldBe("intranet: Add themes");
    }

    [Test]
    public void AnEntryHoldsUnmappedWork()
    {
        Activity search = TestData.AgentBlock("s1", "portal", At(9), 60, title: "Wire up search");
        Activity notes = TestData.AgentBlock("s2", "notes", At(11), 60, title: "Sort the notes");
        Group("e1", search.Id, notes.Id);

        PeriodTimesheet sheet = Build(search, notes);

        ItShouldLeaveTheUnmappedWorkOut();

        void ItShouldLeaveTheUnmappedWorkOut() => sheet.Activities.Single(item => item.PieceId == notes.Id).EntryId.ShouldBeNull();
    }

    [Test]
    public void SomeTitlesAreUnticked()
    {
        Activity search = TestData.AgentBlock("s1", "portal", At(9), 40, title: "Wire up search");
        Activity filters = TestData.AgentBlock("s2", "portal", At(11), 20, title: "Fix filters");
        string review = Manual("w1", At(13), 30, DefaultWorkspace.ClientDevelopment, "Review the design");
        Group("e1", search.Id, filters.Id, review);
        _journal.Entries["e1"] = new EntryOverride { Unticked = [filters.Id] };

        TimesheetLine entry = Build(search, filters).Lines.Single();

        ItShouldLeaveThemOutOfTheDescription();
        ItShouldStillCountTheirTime();

        void ItShouldLeaveThemOutOfTheDescription() => entry.Description.ShouldBe("portal: Wire up search\nReview the design");

        void ItShouldStillCountTheirTime() => entry.Minutes.ShouldBe(90);
    }

    [Test]
    public void EveryTitleIsUnticked()
    {
        Activity search = TestData.AgentBlock("s1", "portal", At(9), 40, title: "Wire up search");
        Activity filters = TestData.AgentBlock("s2", "portal", At(11), 20, title: "Fix filters");
        Group("e1", search.Id, filters.Id);
        _journal.Entries["e1"] = new EntryOverride { Unticked = [search.Id, filters.Id] };

        TimesheetLine entry = Build(search, filters).Lines.Single();

        ItShouldStillSaySomething();

        void ItShouldStillSaySomething() => entry.Description.ShouldBe("portal: Wire up search; Fix filters");
    }

    [Test]
    public void AnEntryIsDescribedInMyOwnWords()
    {
        Activity search = TestData.AgentBlock("s1", "portal", At(9), 40, title: "Wire up search");
        Activity filters = TestData.AgentBlock("s2", "portal", At(11), 20, title: "Fix filters");
        Group("e1", search.Id, filters.Id);
        _journal.Entries["e1"] = new EntryOverride { Unticked = [filters.Id], Description = "  Search for the portal  " };

        TimesheetLine entry = Build(search, filters).Lines.Single();

        ItShouldUseWhatIWrote();

        void ItShouldUseWhatIWrote() => entry.Description.ShouldBe("Search for the portal");
    }

    [Test]
    public void AnEntrysTitlesOutgrowTheDescriptionLimit()
    {
        Activity[] blocks = Enumerable.Range(0, 12)
            .Select(index => TestData.AgentBlock($"s{index}", "portal", At(8).AddMinutes(index * 30), 20, title: $"Session {index:00} " + new string('x', 40)))
            .ToArray();
        Group("e1", [.. blocks.Select(block => block.Id)]);

        PeriodTimesheet sheet = Build(blocks);

        ItShouldStayOneLine();
        ItShouldShortenTheDescriptionToFit();

        void ItShouldStayOneLine() => sheet.Lines.Count.ShouldBe(1);

        void ItShouldShortenTheDescriptionToFit() => sheet.Lines[0].Description.ShouldSatisfyAllConditions(
            description => description.Length.ShouldBe(TimesheetLine.MaxDescriptionLength),
            description => description.ShouldEndWith("…"));
    }

    [Test]
    public void AnEntrysLineWasEditedBeforeEntriesHadTheirOwnLines()
    {
        Activity search = TestData.AgentBlock("s1", "portal", At(9), 40, title: "Wire up search");
        Activity filters = TestData.AgentBlock("s2", "portal", At(11), 20, title: "Fix filters");
        Group("e1", search.Id, filters.Id);
        _journal.Lines[$"line:2026-11-09:agent:{DefaultWorkspace.ClientDevelopment}:Home:entry:e1"] = new LineOverride { Ticket = "UY-42" };

        TimesheetLine entry = Build(search, filters).Lines.Single();

        ItShouldKeepTheEdit();

        void ItShouldKeepTheEdit() => entry.Ticket.ShouldBe("UY-42");
    }

    [Test]
    public void AMeetingIsDroppedOnAgentWork()
    {
        PeriodTimesheet sheet = Build(
            TestData.Meeting("m1", "Stand-up", At(9), 30),
            TestData.AgentBlock("s1", "portal", At(10), 60, title: "Wire up search"));
        ActivityResult meeting = sheet.Activities[0];
        ActivityResult block = sheet.Activities[1];

        ItShouldRefuse();

        void ItShouldRefuse() => Entries.WhyNotTogether(block, meeting, _config.Locations.EntryOrder).ShouldBe("Meetings stay on lines of their own");
    }

    [Test]
    public void WorkOfAnotherCategoryIsDroppedOnAgentWork()
    {
        PeriodTimesheet sheet = Build(
            TestData.AgentBlock("s1", "portal", At(9), 60, title: "Wire up search"),
            TestData.AgentBlock("s2", "intranet", At(11), 60, title: "Add themes"));

        string? reason = Entries.WhyNotTogether(sheet.Activities[0], sheet.Activities[1], _config.Locations.EntryOrder);

        ItShouldRefuse();

        void ItShouldRefuse() => reason.ShouldBe("Only work of the same category can be grouped");
    }

    [Test]
    public void TwoBlocksOfTheSameCategoryMeet()
    {
        PeriodTimesheet sheet = Build(
            TestData.AgentBlock("s1", "portal", At(9), 60, title: "Wire up search"),
            TestData.AgentBlock("s2", "proposals", At(11), 60, title: "Tune the prompts"));

        string? reason = Entries.WhyNotTogether(sheet.Activities[0], sheet.Activities[1], _config.Locations.EntryOrder);

        ItShouldAllowThem();

        void ItShouldAllowThem() => reason.ShouldBeNull();
    }

    private void Group(string entry, params string[] pieceIds)
    {
        foreach (string pieceId in pieceIds)
        {
            _journal.Overrides[pieceId] = new ActivityOverride { Entry = entry };
        }
    }

    private string Manual(string id, DateTime start, int minutes, string categoryId, string description)
    {
        _journal.ManualEntries.Add(new ManualEntry
        {
            Id = id,
            Date = DateOnly.FromDateTime(start),
            Start = TimeOnly.FromDateTime(start),
            Minutes = minutes,
            CategoryId = categoryId,
            Description = description,
        });
        return $"manual:{id}";
    }

    private PeriodTimesheet Build(params Activity[] activities) =>
        new TimesheetBuilder().Build(November, _config.Normalize(), _journal.Normalize(), activities, today: new DateOnly(2026, 11, 30));

    private static DateTime At(int hour) => Monday.ToDateTime(new TimeOnly(hour, 0));
}
