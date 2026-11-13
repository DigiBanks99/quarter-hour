using QuarterHour.Core.Model;
using QuarterHour.Core.Rules;

namespace QuarterHour.Core.Tests.Model;

public sealed class AgentActivitiesTests
{
    private static readonly TimeZoneInfo SouthAfrica = TimeZoneInfo.CreateCustomTimeZone("SAST", TimeSpan.FromHours(2), "SAST", "SAST");
    private static readonly DateTimeOffset SevenUtc = new(2026, 11, 9, 7, 0, 0, TimeSpan.Zero);

    [Test]
    public void ASessionHasALongPause()
    {
        AgentSession session = Session([0, 5, 10, 50, 60]);

        IReadOnlyList<Activity> blocks = AgentActivities.ToActivities(session, TimeSpan.FromMinutes(15), TimeSpan.FromMinutes(5), SouthAfrica);

        ItShouldSplitIntoTwoBlocks();
        ItShouldUseLocalTime();
        ItShouldPadEachBlock();

        void ItShouldSplitIntoTwoBlocks() => blocks.Count.ShouldBe(2);

        void ItShouldUseLocalTime() => blocks[0].Start.ShouldBe(new DateTime(2026, 11, 9, 9, 0, 0));

        void ItShouldPadEachBlock() => blocks.Select(block => block.Duration).ShouldBe([TimeSpan.FromMinutes(15), TimeSpan.FromMinutes(15)]);
    }

    [Test]
    public void ThePaddingWouldRunIntoTheNextBlock()
    {
        AgentSession session = Session([0, 30]);

        IReadOnlyList<Activity> blocks = AgentActivities.ToActivities(session, TimeSpan.FromMinutes(15), TimeSpan.FromMinutes(45), SouthAfrica);

        ItShouldStopWhereTheNextBlockStarts();

        void ItShouldStopWhereTheNextBlockStarts() => blocks[0].End.ShouldBe(blocks[1].Start);
    }

    [Test]
    public void ASessionHasNoMessages()
    {
        IReadOnlyList<Activity> blocks = AgentActivities.ToActivities(Session([]), TimeSpan.FromMinutes(15), TimeSpan.Zero, SouthAfrica);

        ItShouldProduceNoWork();

        void ItShouldProduceNoWork() => blocks.ShouldBeEmpty();
    }

    [Test]
    public void TheSameRepositoryIsUsedByTwoAgents()
    {
        AgentSession copilot = Session([0]) with { Repository = "acme/portal", ProjectPath = @"C:\work\portal-wt2" };
        AgentSession claude = Session([0]) with { Agent = "claude-code", Repository = null, ProjectPath = @"C:\source\portal\" };

        string copilotGroup = AgentActivities.ToActivities(copilot, TimeSpan.FromMinutes(15), TimeSpan.Zero, SouthAfrica)[0].GroupKey;
        string claudeGroup = AgentActivities.ToActivities(claude, TimeSpan.FromMinutes(15), TimeSpan.Zero, SouthAfrica)[0].GroupKey;

        ItShouldGroupThemAsOneProject();

        void ItShouldGroupThemAsOneProject()
        {
            copilotGroup.ShouldBe("agent:portal");
            claudeGroup.ShouldBe(copilotGroup);
        }
    }

    [Test]
    public void TheProjectHasAFriendlyName()
    {
        AgentSession session = Session([0]) with { ProjectPath = @"C:\source\portal-api", Title = null };
        Dictionary<string, string> names = new(StringComparer.OrdinalIgnoreCase) { ["portal-api"] = "Portal" };

        Activity block = AgentActivities.ToActivities(session, TimeSpan.FromMinutes(15), TimeSpan.Zero, SouthAfrica, names)[0];

        ItShouldShowTheFriendlyName();
        ItShouldKeepTheGroupKeyOnTheRepositoryName();
        ItShouldKeepTheRepositoryNameForRules();

        void ItShouldShowTheFriendlyName()
        {
            block.GroupLabel.ShouldBe("Portal");
            block.Title.ShouldBe("Portal");
            ActivityFields.Get(block, "project").Text.ShouldBe("Portal");
        }

        void ItShouldKeepTheGroupKeyOnTheRepositoryName() => block.GroupKey.ShouldBe("agent:portal-api");

        void ItShouldKeepTheRepositoryNameForRules() => ActivityFields.Get(block, "repo").Text.ShouldBe("portal-api");
    }

    [Test]
    [Arguments("https://github.com/acme/portal.git", "portal")]
    [Arguments("git@github.com:acme/portal.git", "portal")]
    [Arguments("acme/portal", "portal")]
    public void ARepositoryIsNamed(string repository, string expected)
    {
        string? name = ProjectNaming.RepositoryName(repository);

        ItShouldUseTheRepositoryName();

        void ItShouldUseTheRepositoryName() => name.ShouldBe(expected);
    }

    private static AgentSession Session(int[] minutes) => new()
    {
        Agent = "copilot-cli",
        SessionId = "s1",
        Title = "Fix the login page",
        ProjectPath = @"C:\source\portal",
        Timestamps = [.. minutes.Select(minute => SevenUtc.AddMinutes(minute))],
    };
}
