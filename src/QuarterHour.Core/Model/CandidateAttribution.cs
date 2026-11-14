namespace QuarterHour.Core.Model;

/// <param name="Activities">Every activity, agent blocks carrying the candidates attributed to them.</param>
/// <param name="Orphans">Commits no block could take.</param>
/// <param name="OrphanPullRequests">Pull requests no block could take.</param>
public sealed record AttributionResult(IReadOnlyList<Activity> Activities, IReadOnlyList<Commit> Orphans, IReadOnlyList<PullRequest> OrphanPullRequests);

/// <summary>
/// Attributes candidates (commits and pull requests) to the blocks of agent work they describe. A candidate belongs to a
/// block of the same project that was active when it was made, or that ended less than an idle gap before it (it was
/// the block's last act). Of several such blocks, the one active at the time wins, else the one that ended most
/// recently. A pull request first looks for blocks on its source branch, and takes the nearest of those at any distance.
/// A claim beats all of that: a block whose transcript shows it made the candidate takes it (the earliest claim, if
/// several do). A pull request id only claims when it was seen around the pull request's creation, since later
/// sessions list and review pull requests too. Candidates no block can take are orphans. Candidates only describe time; they never add or move it.
/// </summary>
public static class CandidateAttribution
{
    public static AttributionResult Attribute(IReadOnlyList<Activity> activities, IReadOnlyList<Commit> commits, TimeSpan idleGap, TimeZoneInfo timeZone,
        IReadOnlyList<PullRequest>? pullRequests = null)
    {
        Dictionary<string, List<Activity>> blocksByProject = activities
            .Where(activity => activity.Source == ActivitySource.Agent && ProjectNaming.KeyOfGroup(activity.GroupKey) is not null)
            .GroupBy(activity => ProjectNaming.KeyOfGroup(activity.GroupKey)!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);

        Dictionary<string, List<Commit>> commitsByBlock = [];
        List<Commit> orphans = [];
        foreach (Commit commit in commits
                     .Where(commit => !CommitSubjects.IsNoise(commit.Subject))
                     .DistinctBy(commit => commit.Sha)
                     .OrderBy(commit => commit.AuthoredAt))
        {
            DateTime at = TimeZoneInfo.ConvertTime(commit.AuthoredAt, timeZone).DateTime;
            List<Activity>? blocks = blocksByProject.GetValueOrDefault(commit.ProjectKey);
            Activity? claimant = Claimant(blocks, claim => claim.Claims(commit));
            Activity? block = claimant ?? Active(blocks, at, idleGap);
            if (block is null)
            {
                orphans.Add(commit);
            }
            else
            {
                Add(commitsByBlock, block.Id, commit with { Claimed = claimant is not null });
            }
        }

        Dictionary<string, List<PullRequest>> pullRequestsByBlock = [];
        List<PullRequest> orphanPullRequests = [];
        foreach (PullRequest pullRequest in (pullRequests ?? []).DistinctBy(pullRequest => pullRequest.Id).OrderBy(pullRequest => pullRequest.CreatedAt))
        {
            DateTime at = TimeZoneInfo.ConvertTime(pullRequest.CreatedAt, timeZone).DateTime;
            List<Activity>? blocks = blocksByProject.GetValueOrDefault(pullRequest.ProjectKey);
            Activity? claimant = Claimant(blocks, claim => claim.Claims(pullRequest)
                && claim.At >= pullRequest.CreatedAt - PullRequestClaimLead && claim.At <= pullRequest.CreatedAt + PullRequestClaimWindow);
            Activity? block = claimant ?? OnBranch(blocks, pullRequest.SourceBranch, at) ?? Active(blocks, at, idleGap);
            if (block is null)
            {
                orphanPullRequests.Add(pullRequest);
            }
            else
            {
                Add(pullRequestsByBlock, block.Id, pullRequest with { Claimed = claimant is not null });
            }
        }

        List<Activity> result = activities
            .Select(activity => commitsByBlock.ContainsKey(activity.Id) || pullRequestsByBlock.ContainsKey(activity.Id)
                ? activity with
                {
                    Commits = commitsByBlock.GetValueOrDefault(activity.Id) ?? [],
                    PullRequests = pullRequestsByBlock.GetValueOrDefault(activity.Id) ?? [],
                }
                : activity)
            .ToList();
        return new AttributionResult(result, orphans, orphanPullRequests);
    }

    /// <summary>How long after a pull request's creation its id still claims it; later mentions are reviews and lookups.</summary>
    public static readonly TimeSpan PullRequestClaimWindow = TimeSpan.FromMinutes(30);

    /// <summary>Slack for clocks: a claim may be stamped a little before Azure DevOps stamped the creation.</summary>
    private static readonly TimeSpan PullRequestClaimLead = TimeSpan.FromMinutes(5);

    private static Activity? Claimant(List<Activity>? blocks, Func<Claim, bool> claims) =>
        blocks?
            .Select(block => (Block: block, First: block.Claims.Where(claims).Select(claim => (DateTimeOffset?)claim.At).Min()))
            .Where(candidate => candidate.First is not null)
            .OrderBy(candidate => candidate.First)
            .Select(candidate => candidate.Block)
            .FirstOrDefault();

    private static Activity? Active(List<Activity>? blocks, DateTime at, TimeSpan idleGap) =>
        blocks?
            .Where(candidate => candidate.Start <= at && at <= candidate.End + idleGap)
            .OrderBy(candidate => Distance(candidate, at))
            .ThenByDescending(candidate => candidate.Start)
            .FirstOrDefault();

    private static Activity? OnBranch(List<Activity>? blocks, string? branch, DateTime at) =>
        branch is null
            ? null
            : blocks?
                .Where(candidate => string.Equals(candidate.Branch, branch, StringComparison.OrdinalIgnoreCase))
                .OrderBy(candidate => Distance(candidate, at))
                .ThenByDescending(candidate => candidate.Start)
                .FirstOrDefault();

    private static TimeSpan Distance(Activity block, DateTime at) =>
        at < block.Start ? block.Start - at : at > block.End ? at - block.End : TimeSpan.Zero;

    private static void Add<T>(Dictionary<string, List<T>> byBlock, string blockId, T candidate)
    {
        if (!byBlock.TryGetValue(blockId, out List<T>? list))
        {
            byBlock[blockId] = list = [];
        }

        list.Add(candidate);
    }
}

/// <summary>What a block of agent work did, in timesheet words: its pull requests and commits, else its session's title.</summary>
public static class AgentWork
{
    public static IReadOnlyList<string> PullRequests(Activity activity) =>
        activity.PullRequests.Select(pullRequest => pullRequest.Describe()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>The block's commit subjects, leaving out the commits of its pull requests, whose titles already say it.</summary>
    public static IReadOnlyList<string> Commits(Activity activity)
    {
        HashSet<string> inPullRequests = new(activity.PullRequests.SelectMany(pullRequest => pullRequest.CommitShas), StringComparer.OrdinalIgnoreCase);
        return activity.Commits
            .Where(commit => !inPullRequests.Contains(commit.Sha))
            .Select(commit => CommitSubjects.Clean(commit.Subject))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static IReadOnlyList<string> Parts(Activity activity)
    {
        List<string> parts = [.. PullRequests(activity), .. Commits(activity)];
        return parts.Count > 0 ? parts : [activity.Title];
    }

    public static string Describe(Activity activity) => string.Join("; ", Parts(activity));
}
