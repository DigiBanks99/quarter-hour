using System.Text.Json;
using QuarterHour.Core.Configuration;
using QuarterHour.Infrastructure.Storage;

namespace QuarterHour.IntegrationTests.Storage;

public sealed class WorkspaceStoreTests : IDisposable
{
    private readonly TemporaryDirectory _data = new();
    private readonly WorkspaceStore _store;

    public WorkspaceStoreTests()
    {
        _store = new WorkspaceStore(new DataDirectory(_data.Path));
    }

    [Test]
    public void TheAppStartsForTheFirstTime()
    {
        WorkspaceConfig config = _store.LoadConfig();

        ItShouldStartWithTheStarterRules();
        ItShouldSaveThemForNextTime();

        void ItShouldStartWithTheStarterRules() => config.Rules.ShouldContain(rule => rule.Name == "Coffee runs");

        void ItShouldSaveThemForNextTime() => File.Exists(Path.Combine(_data.Path, "config.json")).ShouldBeTrue();
    }

    [Test]
    public void TheConfigurationIsChangedAndReloaded()
    {
        WorkspaceConfig config = _store.LoadConfig();
        config.Locations.Schedule.Add(new LocationScheduleRule { Location = "Entelect", Days = [DayOfWeek.Friday], AnchorDate = new DateOnly(2026, 11, 6) });
        config.Rounding.RoundUpFromMinutes = 8;
        _store.SaveConfig(config);

        WorkspaceConfig reloaded = _store.LoadConfig();
        string json = File.ReadAllText(Path.Combine(_data.Path, "config.json"));

        ItShouldKeepTheChanges();
        ItShouldWriteReadableJson();

        void ItShouldKeepTheChanges() => reloaded.ShouldSatisfyAllConditions(
            () => reloaded.Locations.Schedule.Single().AnchorDate.ShouldBe(new DateOnly(2026, 11, 6)),
            () => reloaded.Locations.Schedule.Single().Days.ShouldBe([DayOfWeek.Friday]),
            () => reloaded.Rounding.RoundUpFromMinutes.ShouldBe(8));

        void ItShouldWriteReadableJson() => json.ShouldSatisfyAllConditions(
            () => json.ShouldContain("\"Friday\""),
            () => json.ShouldContain("\"2026-11-06\""));
    }

    [Test]
    public void TheJournalIsSaved()
    {
        Journal journal = _store.LoadJournal();
        journal.ManualEntries.Add(new ManualEntry { Date = new DateOnly(2026, 11, 9), Start = new TimeOnly(9, 30), Minutes = 90, Description = "Workshop" });
        journal.Overrides["cal:1"] = new ActivityOverride { Excluded = true };
        _store.SaveJournal(journal);

        Journal reloaded = _store.LoadJournal();

        ItShouldComeBackTheSame();

        void ItShouldComeBackTheSame() => reloaded.ShouldSatisfyAllConditions(
            () => reloaded.ManualEntries.Single().Start.ShouldBe(new TimeOnly(9, 30)),
            () => reloaded.Overrides["cal:1"].Excluded.ShouldBe(true));
    }

    [Test]
    public void SomethingElseIsImportedAsConfiguration()
    {
        Exception? exception = null;
        try
        {
            WorkspaceStore.ImportConfig("[1, 2, 3]");
        }
        catch (Exception caught)
        {
            exception = caught;
        }

        ItShouldBeRefused();

        void ItShouldBeRefused() => exception.ShouldBeAssignableTo<JsonException>();
    }

    public void Dispose() => _data.Dispose();
}
