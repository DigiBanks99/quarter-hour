using QuarterHour.Core.Configuration;
using QuarterHour.Core.Export;
using QuarterHour.Core.Model;
using QuarterHour.Core.Time;
using QuarterHour.Core.Timesheets;

namespace QuarterHour.Core.Tests.Export;

public sealed class ExportRangeTests
{
    private static readonly TimesheetPeriod November = new(2026, 11);

    private readonly WorkspaceConfig _config = TestData.Workspace();
    private readonly PeriodTimesheet _sheet;

    public ExportRangeTests()
    {
        _config.Rules.Add(TestData.Heuristic("Sync", "title", ConditionOperator.Contains, "sync", DefaultWorkspace.ClientMeetings));
        _sheet = new TimesheetBuilder().Build(November, _config.Normalize(), new Journal(),
        [
            Sync(1),
            Sync(2),
            Sync(13),
            Sync(14),
            Sync(15),
            Sync(29),
            Sync(30),
        ]);
    }

    [Test]
    public void TheWholePeriodIsPicked()
    {
        IReadOnlyList<TimesheetLine> withoutRange = ExportRows.LinesToExport(_sheet, _config);
        IReadOnlyList<TimesheetLine> wholePeriod = ExportRows.LinesToExport(_sheet, _config, new ExportRange(November.Start, November.LastDay));

        ItShouldExportTheSameAsNoRange();

        void ItShouldExportTheSameAsNoRange() => wholePeriod.ShouldBe(withoutRange);
    }

    [Test]
    public void ASingleDayIsPicked()
    {
        IReadOnlyList<TimesheetLine> lines = ExportRows.LinesToExport(_sheet, _config, new ExportRange(Day(14), Day(14)));

        ItShouldOnlyExportThatDay();

        void ItShouldOnlyExportThatDay() => Dates(lines).ShouldBe([Day(14)]);
    }

    [Test]
    public void TheFirstFortnightIsPicked()
    {
        IReadOnlyList<TimesheetLine> lines = ExportRows.LinesToExport(_sheet, _config, new ExportRange(Day(1), Day(14)));

        ItShouldIncludeBothEndsAndNothingAfter();

        void ItShouldIncludeBothEndsAndNothingAfter() => Dates(lines).ShouldBe([Day(1), Day(2), Day(13), Day(14)]);
    }

    [Test]
    public void TheLastDaysOfTheMonthArePicked()
    {
        IReadOnlyList<TimesheetLine> lines = ExportRows.LinesToExport(_sheet, _config, new ExportRange(Day(15), Day(30)));

        ItShouldIncludeTheLastDayOfTheMonth();

        void ItShouldIncludeTheLastDayOfTheMonth() => Dates(lines).ShouldBe([Day(15), Day(29), Day(30)]);
    }

    [Test]
    [Arguments("2026-11-14", "2026-11-03", "The from day is after the to day.")]
    [Arguments("2026-11-20", "2026-12-04", "The from and to days must be in the same calendar month.")]
    [Arguments("2026-10-28", "2026-11-03", "The from and to days must be in the same calendar month.")]
    [Arguments("2026-12-01", "2026-12-04", "The days must fall within November 2026.")]
    public void AnUnusableRangeIsPicked(string from, string to, string reason)
    {
        ExportRange range = new(DateOnly.Parse(from), DateOnly.Parse(to));

        string? problem = range.Problem(November);
        Exception? exception = Catch.Exception(() => ExportRows.LinesToExport(_sheet, _config, range));
        bool created = ExportRange.TryCreate(November, range.From, range.To, out ExportRange? parsed, out string? refusal);

        ItShouldSayWhatIsWrong();
        ItShouldNotBeExported();
        ItShouldBeRefusedWhenAskedFor();

        void ItShouldSayWhatIsWrong() => problem.ShouldBe(reason);

        void ItShouldNotBeExported() => exception.ShouldBeOfType<ArgumentOutOfRangeException>().Message.ShouldStartWith(reason);

        void ItShouldBeRefusedWhenAskedFor() => created.ShouldSatisfyAllConditions(
            () => created.ShouldBeFalse(),
            () => parsed.ShouldBeNull(),
            () => refusal.ShouldBe(reason));
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public void OnlyOneEndIsAskedFor(bool fromOnly)
    {
        bool created = ExportRange.TryCreate(November, fromOnly ? Day(3) : null, fromOnly ? null : Day(14), out ExportRange? range, out string? problem);

        ItShouldBeRefused();

        void ItShouldBeRefused() => created.ShouldSatisfyAllConditions(
            () => created.ShouldBeFalse(),
            () => range.ShouldBeNull(),
            () => problem.ShouldBe("Give both a from and a to day, or neither."));
    }

    [Test]
    public void NoRangeIsAskedFor()
    {
        bool created = ExportRange.TryCreate(November, null, null, out ExportRange? range, out string? problem);

        ItShouldExportTheWholePeriod();

        void ItShouldExportTheWholePeriod() => created.ShouldSatisfyAllConditions(
            () => created.ShouldBeTrue(),
            () => range.ShouldBeNull(),
            () => problem.ShouldBeNull());
    }

    [Test]
    public void ARangeIsAskedFor()
    {
        bool created = ExportRange.TryCreate(November, Day(3), Day(14), out ExportRange? range, out string? problem);

        ItShouldBeAccepted();
        ItShouldCountAndLabelItsDays();

        void ItShouldBeAccepted() => created.ShouldSatisfyAllConditions(
            () => created.ShouldBeTrue(),
            () => range.ShouldBe(new ExportRange(Day(3), Day(14))),
            () => problem.ShouldBeNull());

        void ItShouldCountAndLabelItsDays() => range!.Value.ShouldSatisfyAllConditions(
            () => range.Value.Days.ShouldBe(12),
            () => range.Value.Label.ShouldBe("03–14"),
            () => range.Value.DisplayLabel.ShouldBe("3–14 Nov"),
            () => range.Value.Query.ShouldBe("?from=2026-11-03&to=2026-11-14"),
            () => range.Value.Covers(November).ShouldBeFalse());
    }

    private static DateOnly Day(int day) => new(2026, 11, day);

    private static Activity Sync(int day) => TestData.Meeting($"m{day}", "Client sync", Day(day).ToDateTime(new TimeOnly(10, 0)), 60);

    private static IEnumerable<DateOnly> Dates(IReadOnlyList<TimesheetLine> lines) => lines.Select(line => line.Date).Distinct().Order();
}
