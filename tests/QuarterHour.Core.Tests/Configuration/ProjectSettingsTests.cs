using QuarterHour.Core.Configuration;

namespace QuarterHour.Core.Tests.Configuration;

public sealed class ProjectSettingsTests
{
    [Test]
    public void AHandEditedFileHasMessyProjects()
    {
        WorkspaceConfig config = new()
        {
            Projects =
            [
                new ProjectSettings { Key = " Portal-API ", FriendlyName = " Portal " },
                new ProjectSettings { Key = "portal-api", FriendlyName = "Duplicate" },
                new ProjectSettings { Key = "", FriendlyName = "No key" },
                new ProjectSettings { Key = "scratch", FriendlyName = "  " },
            ],
        };

        config.Normalize();

        ItShouldKeepOneTidyEntryPerProject();
        ItShouldLookUpFriendlyNamesByKey();

        void ItShouldKeepOneTidyEntryPerProject() =>
            config.Projects.Select(project => (project.Key, project.FriendlyName)).ShouldBe([("portal-api", "Portal")]);

        void ItShouldLookUpFriendlyNamesByKey() => config.FriendlyProjectNames()["PORTAL-API"].ShouldBe("Portal");
    }

    [Test]
    public void AForkHasOnlyExtraRemotes()
    {
        WorkspaceConfig config = new()
        {
            Projects =
            [
                new ProjectSettings
                {
                    Key = "portal-fork",
                    ExtraRemotes = [" https://dev.azure.com/contoso/Portal/_git/portal ", "", "https://dev.azure.com/contoso/Portal/_git/PORTAL"],
                },
            ],
        };

        config.Normalize();

        ItShouldKeepTheProjectWithoutAFriendlyName();

        void ItShouldKeepTheProjectWithoutAFriendlyName() =>
            config.FindProject("portal-fork")!.ExtraRemotes.ShouldBe(["https://dev.azure.com/contoso/Portal/_git/portal"]);
    }
}
