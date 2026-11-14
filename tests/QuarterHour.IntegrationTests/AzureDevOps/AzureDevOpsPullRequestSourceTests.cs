using System.Text.Json;
using QuarterHour.Core.Model;
using QuarterHour.Core.Sources;
using QuarterHour.Infrastructure.AzureDevOps;
using QuarterHour.Infrastructure.Processes;
using QuarterHour.IntegrationTests.Git;

namespace QuarterHour.IntegrationTests.AzureDevOps;

public sealed class AzureDevOpsPullRequestSourceTests : IDisposable
{
    private const string Upstream = "https://dev.azure.com/contoso/Portal/_git/portal";
    private const string Fork = "https://me@dev.azure.com/contoso/Portal/_git/portal-fork";
    private static readonly DateTimeOffset November = new(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset December = new(2026, 12, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly TemporaryDirectory _root = new();
    private readonly FakeAzureCli _az = new();

    [Test]
    [Arguments("https://dev.azure.com/contoso/Portal/_git/portal-api", "contoso/Portal/portal-api", "https://dev.azure.com/contoso")]
    [Arguments("https://me@dev.azure.com/contoso/Portal%20Team/_git/portal-api", "contoso/Portal Team/portal-api", "https://dev.azure.com/contoso")]
    [Arguments("https://contoso.visualstudio.com/DefaultCollection/Portal/_git/portal-api", "contoso/Portal/portal-api", "https://contoso.visualstudio.com")]
    [Arguments("git@ssh.dev.azure.com:v3/contoso/Portal/portal-api", "contoso/Portal/portal-api", "https://dev.azure.com/contoso")]
    [Arguments("contoso@vs-ssh.visualstudio.com:v3/contoso/Portal/portal-api", "contoso/Portal/portal-api", "https://dev.azure.com/contoso")]
    [Arguments("https://dev.azure.com/contoso/_git/portal", "contoso/portal/portal", "https://dev.azure.com/contoso")]
    public void AnAzureDevOpsRemoteIsRead(string url, string expected, string organizationUrl)
    {
        AzureDevOpsRepository? repository = AzureDevOpsRepository.TryParse(url);

        ItShouldNameTheRepository();
        ItShouldKnowTheOrganizationToAsk();

        void ItShouldNameTheRepository() => repository?.ToString().ShouldBe(expected);

        void ItShouldKnowTheOrganizationToAsk() => repository?.OrganizationUrl.ShouldBe(organizationUrl);
    }

    [Test]
    [Arguments("https://github.com/someone/portal.git")]
    [Arguments("git@github.com:someone/portal.git")]
    [Arguments("")]
    public void AnotherHostsRemoteIsRead(string url)
    {
        AzureDevOpsRepository? repository = AzureDevOpsRepository.TryParse(url);

        ItShouldNotBeAzureDevOps();

        void ItShouldNotBeAzureDevOps() => repository.ShouldBeNull();
    }

    [Test]
    public async Task AForkRaisedAPullRequestUpstream()
    {
        string fork = TestGit.Repository(_root, "portal-fork");
        string target = TestGit.Commit(fork, "chore: start", "2026-11-02T08:00:00+02:00");
        TestGit.Run(fork, "switch", "-q", "-c", "feature/sso");
        string first = TestGit.Commit(fork, "feat: add sso login", "2026-11-09T09:10:00+02:00");
        string second = TestGit.Commit(fork, "test: cover sso", "2026-11-09T09:20:00+02:00");
        TestGit.Run(fork, "remote", "add", "origin", Fork);
        TestGit.Run(fork, "remote", "add", "upstream", Upstream);
        TestGit.Run(fork, "remote", "add", "mirror", "https://github.com/someone/portal.git");
        _az.PullRequests["portal"] =
        [
            PullRequestJson(482, "feat: add SSO login", "2026-11-09T07:30:00Z", "feature/sso", "portal-fork", source: second, target: target),
            PullRequestJson(400, "Last month's work", "2026-10-20T07:30:00Z", "feature/old", "portal-fork"),
        ];

        PullRequestScanResult result = await Scan(new ProjectFolders("portal-fork", [fork]));

        ItShouldSearchEveryAzureDevOpsRemote();
        ItShouldAskForMyPullRequests();
        ItShouldKeepThePullRequestsInThePeriod();
        ItShouldKnowThePullRequestsCommits();

        void ItShouldSearchEveryAzureDevOpsRemote() =>
            result.Repositories["portal-fork"].Order().ShouldBe(["contoso/Portal/portal", "contoso/Portal/portal-fork"]);

        void ItShouldAskForMyPullRequests() => _az.Creators.ShouldAllBe(creator => creator == "me@contoso.example");

        void ItShouldKeepThePullRequestsInThePeriod()
        {
            PullRequest pullRequest = result.PullRequests.ShouldHaveSingleItem();
            pullRequest.Id.ShouldBe(482);
            pullRequest.ProjectKey.ShouldBe("portal-fork");
            pullRequest.SourceBranch.ShouldBe("feature/sso");
            pullRequest.CreatedAt.ShouldBe(new DateTimeOffset(2026, 11, 9, 7, 30, 0, TimeSpan.Zero));
        }

        void ItShouldKnowThePullRequestsCommits() => result.PullRequests[0].CommitShas.Order().ShouldBe(new[] { first, second }.Order());
    }

    [Test]
    public async Task TwoProjectsShareAnUpstream()
    {
        string clone = TestGit.Repository(_root, "portal");
        TestGit.Run(clone, "remote", "add", "origin", Upstream);
        string fork = TestGit.Repository(_root, "portal-fork");
        TestGit.Run(fork, "remote", "add", "origin", Fork);
        _az.PullRequests["portal"] =
        [
            PullRequestJson(482, "Add SSO login", "2026-11-09T07:30:00Z", "feature/sso", forkRepository: "portal-fork"),
            PullRequestJson(490, "Fix the build", "2026-11-10T07:30:00Z", "fix/build"),
        ];

        PullRequestScanResult result = await Scan(new ProjectFolders("portal", [clone]), new ProjectFolders("portal-fork", [fork], [Upstream]));

        ItShouldGiveEachPullRequestToTheProjectHoldingItsBranch();
        ItShouldAskEachRepositoryOnce();

        void ItShouldGiveEachPullRequestToTheProjectHoldingItsBranch() =>
            result.PullRequests.Select(pullRequest => (pullRequest.Id, pullRequest.ProjectKey)).ShouldBe([(482, "portal-fork"), (490, "portal")]);

        void ItShouldAskEachRepositoryOnce() => _az.Repositories.Order().ShouldBe(["portal", "portal-fork"]);
    }

    [Test]
    public async Task TheAzureCliIsNotSignedIn()
    {
        string clone = TestGit.Repository(_root, "portal");
        TestGit.Run(clone, "remote", "add", "origin", Upstream);
        _az.SignedIn = false;

        PullRequestScanResult result = await Scan(new ProjectFolders("portal", [clone]));

        ItShouldSayHowToSignIn();
        ItShouldNotAskForPullRequests();

        void ItShouldSayHowToSignIn() => result.Problems.ShouldHaveSingleItem().ShouldContain("az login");

        void ItShouldNotAskForPullRequests() => _az.Repositories.ShouldBeEmpty();
    }

    [Test]
    public async Task ARemoteCantBeRead()
    {
        string fork = TestGit.Repository(_root, "portal-fork");
        TestGit.Run(fork, "remote", "add", "origin", Fork);
        TestGit.Run(fork, "remote", "add", "upstream", Upstream);
        _az.PullRequests["portal"] = [PullRequestJson(490, "Fix the build", "2026-11-10T07:30:00Z", "fix/build", forkRepository: "portal-fork")];
        _az.Failing.Add("portal-fork");

        PullRequestScanResult result = await Scan(new ProjectFolders("portal-fork", [fork]));

        ItShouldSayWhichRemoteFailed();
        ItShouldStillReadTheOthers();

        void ItShouldSayWhichRemoteFailed() => result.Problems.ShouldHaveSingleItem().ShouldStartWith("contoso/Portal/portal-fork: ");

        void ItShouldStillReadTheOthers() => result.PullRequests.Select(pullRequest => pullRequest.Id).ShouldBe([490]);
    }

    [Test]
    public async Task AProjectHasNoAzureDevOpsRemotes()
    {
        string clone = TestGit.Repository(_root, "portal");
        TestGit.Run(clone, "remote", "add", "origin", "https://github.com/someone/portal.git");

        PullRequestScanResult result = await Scan(new ProjectFolders("portal", [clone]));

        ItShouldNotRunTheAzureCliAtAll();

        void ItShouldNotRunTheAzureCliAtAll()
        {
            _az.Calls.ShouldBe(0);
            result.Problems.ShouldBeEmpty();
        }
    }

    private Task<PullRequestScanResult> Scan(params ProjectFolders[] projects) =>
        new AzureDevOpsPullRequestSource(_az, "git", "az").ScanAsync(new PullRequestScanRequest(November, December, projects), CancellationToken.None);

    private static object PullRequestJson(int id, string title, string created, string branch, string? forkRepository = null, string? source = null, string? target = null) => new
    {
        pullRequestId = id,
        title,
        creationDate = created,
        sourceRefName = "refs/heads/" + branch,
        repository = new { name = "portal", project = new { name = "Portal" } },
        forkSource = forkRepository is null ? null : new { name = "refs/heads/" + branch, repository = new { name = forkRepository, project = new { name = "Portal" } } },
        lastMergeSourceCommit = source is null ? null : new { commitId = source },
        lastMergeTargetCommit = target is null ? null : new { commitId = target },
    };

    public void Dispose() => _root.Dispose();

    /// <summary>Answers the Azure CLI's commands from canned pull requests, and runs git for real.</summary>
    private sealed class FakeAzureCli : ICommandRunner
    {
        private readonly CommandLine _git = new();
        private readonly Lock _gate = new();

        public bool SignedIn { get; set; } = true;
        public Dictionary<string, object[]> PullRequests { get; } = [];
        public HashSet<string> Failing { get; } = [];
        public List<string> Repositories { get; } = [];
        public List<string> Creators { get; } = [];
        public int Calls { get; private set; }

        public Task<CommandResult> RunAsync(string fileName, string? workingDirectory, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
        {
            if (fileName == "git")
            {
                return _git.RunAsync(fileName, workingDirectory, arguments, cancellationToken);
            }

            lock (_gate)
            {
                Calls++;
                if (arguments is ["account", "show", ..])
                {
                    return Task.FromResult(SignedIn
                        ? new CommandResult(true, "me@contoso.example\n", "")
                        : new CommandResult(false, "", "ERROR: Please run 'az login' to setup account.\n"));
                }

                string repository = Option(arguments, "--repository");
                Repositories.Add(repository);
                Creators.Add(Option(arguments, "--creator"));
                return Task.FromResult(Failing.Contains(repository)
                    ? new CommandResult(false, "", "ERROR: TF401019: The Git repository with name or identifier portal-fork does not exist.\n")
                    : new CommandResult(true, JsonSerializer.Serialize(PullRequests.GetValueOrDefault(repository) ?? []), ""));
            }
        }

        private static string Option(IReadOnlyList<string> arguments, string name)
        {
            int index = arguments.ToList().IndexOf(name);
            return index >= 0 && index + 1 < arguments.Count ? arguments[index + 1] : "";
        }
    }
}
