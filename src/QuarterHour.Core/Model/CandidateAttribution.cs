using QuarterHour.Core.Configuration;

namespace QuarterHour.Core.Model;

/// <param name="Activities">Every activity, agent blocks carrying the candidates attributed to them.</param>
/// <param name="Orphans">Commits no block could take.</param>
/// <param name="OrphanPullRequests">Pull requests no block could take.</param>
/// <param name="Placements">Where every candidate went, including those marked not relevant or made into entries.</param>
public sealed record AttributionResult(IReadOnlyList<Activity> Activities, IReadOnlyList<Commit> Orphans, IReadOnlyList<PullRequest> OrphanPullRequests,
    IReadOnlyList<Placement> Placements);

/// <summary>What the user decided about candidates: where some go instead, and which became manual entries.</summary>
public sealed record AttributionChoices
{
    public static AttributionChoices None { get; } = new();

    /// <summary>Moves and exclusions, keyed by <see cref="CandidateKeys"/>.</summary>
    public IReadOnlyDictionary<string, CandidateOverride> Overrides { get; init; } = new Dictionary<string, CandidateOverride>();

    /// <summary>Candidate key to the id of the manual entry made from it.</summary>
    public IReadOnlyDictionary<string, string> Entries { get; init; } = new Dictionary<string, string>();

    /// <summary>Project key to friendly name: candidates may move between repositories that share one.</summary>
    public IReadOnlyDictionary<string, string> FriendlyNames { get; init; } = new Dictionary<string, string>();

    public static AttributionChoices From(Journal journal, IReadOnlyDictionary<string, string> friendlyNames)
    {
        Dictionary<string, string> entries = new(StringComparer.OrdinalIgnoreCase);
        foreach (ManualEntry entry in journal.ManualEntries)
        {
            foreach (string key in entry.Candidates ?? [])
            {
                entries.TryAdd(key, entry.Id);
            }
        }

        return new AttributionChoices
        {
            Overrides = new Dictionary<string, CandidateOverride>(journal.Candidates, StringComparer.OrdinalIgnoreCase),
            Entries = entries,
            FriendlyNames = friendlyNames,
        };
    }
}

/// <summary>
/// Attributes candidates (commits and pull requests) to the blocks of agent work they describe. A candidate belongs to a
/// block of the same project that was active when it was made, or that ended less than an idle gap before it (it was
/// the block's last act). Of several such blocks, the one active at the time wins, else the one that ended most
/// recently. A pull request first looks for blocks on its source branch, and takes the nearest of those at any distance.
/// A claim beats all of that: a block whose transcript shows it made the candidate takes it (the earliest claim, if
/// several do). A pull request id only claims when it was seen around the pull request's creation, since later
/// sessions list and review pull requests too. The user's choices beat everything: a candidate made into a manual
/// entry, or marked not relevant, describes no block, and one moved to a block goes there while that block exists and
/// may take it (<see cref="CanTake"/>). Candidates no block can take are orphans. Candidates only describe time; they
/// never add or move it.
/// </summary>
public static class CandidateAttribution
{
    public static AttributionResult Attribute(IReadOnlyList<Activity> activities, IReadOnlyList<Commit> commits, TimeSpan idleGap, TimeZoneInfo timeZone,
        IReadOnlyList<PullRequest>? pullRequests = null, AttributionChoices? choices = null)
    {
        choices ??= AttributionChoices.None;
        Dictionary<string, List<Activity>> blocksByProject = activities
            .Where(activity => activity.Source == ActivitySource.Agent && ProjectNaming.KeyOfGroup(activity.GroupKey) is not null)
            .GroupBy(activity => ProjectNaming.KeyOfGroup(activity.GroupKey)!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);
        Dictionary<string, Activity> blocksById = blocksByProject.Values
            .SelectMany(blocks => blocks)
            .DistinctBy(block => block.Id)
            .ToDictionary(block => block.Id, StringComparer.Ordinal);
        List<Placement> placements = [];

        (CandidatePlace Place, Activity? Block, string? EntryId) Decide(string key, string projectKey, DateTimeOffset at, Func<(Activity? Block, bool Claimed)> automatic)
        {
            if (choices.Entries.TryGetValue(key, out string? entryId))
            {
                return (CandidatePlace.InEntry, null, entryId);
            }

            CandidateOverride? choice = choices.Overrides.GetValueOrDefault(key);
            if (choice?.NotRelevant == true)
            {
                return (CandidatePlace.NotRelevant, null, null);
            }

            if (choice?.BlockId is { } blockId && blocksById.TryGetValue(blockId, out Activity? chosen) && CanTake(chosen, projectKey, at, timeZone, choices.FriendlyNames))
            {
                return (CandidatePlace.Moved, chosen, null);
            }

            (Activity? block, bool claimed) = automatic();
            return block is null ? (CandidatePlace.Unattributed, null, null) : (claimed ? CandidatePlace.Claimed : CandidatePlace.ByTime, block, null);
        }

        Dictionary<string, List<Commit>> commitsByBlock = [];
        List<Commit> orphans = [];
        foreach (Commit commit in commits
                     .Where(commit => !CommitSubjects.IsNoise(commit.Subject))
                     .DistinctBy(commit => commit.Sha)
                     .OrderBy(commit => commit.AuthoredAt))
        {
            DateTime at = TimeZoneInfo.ConvertTime(commit.AuthoredAt, timeZone).DateTime;
            List<Activity>? blocks = blocksByProject.GetValueOrDefault(commit.ProjectKey);
            (CandidatePlace place, Activity? block, string? entryId) = Decide(CandidateKeys.Of(commit), commit.ProjectKey, commit.AuthoredAt, () =>
            {
                Activity? claimant = Claimant(blocks, claim => claim.Claims(commit));
                return (claimant ?? Active(blocks, at, idleGap), claimant is not null);
            });
            Commit placed = commit with { Claimed = place == CandidatePlace.Claimed };
            placements.Add(new Placement { Key = CandidateKeys.Of(commit), Commit = placed, Place = place, BlockId = block?.Id, EntryId = entryId });
            if (block is not null)
            {
                Add(commitsByBlock, block.Id, placed);
            }
            else if (place == CandidatePlace.Unattributed)
            {
                orphans.Add(placed);
            }
        }

        Dictionary<string, List<PullRequest>> pullRequestsByBlock = [];
        List<PullRequest> orphanPullRequests = [];
        foreach (PullRequest pullRequest in (pullRequests ?? []).DistinctBy(pullRequest => pullRequest.Id).OrderBy(pullRequest => pullRequest.CreatedAt))
        {
            DateTime at = TimeZoneInfo.ConvertTime(pullRequest.CreatedAt, timeZone).DateTime;
            List<Activity>? blocks = blocksByProject.GetValueOrDefault(pullRequest.ProjectKey);
            (CandidatePlace place, Activity? block, string? entryId) = Decide(CandidateKeys.Of(pullRequest), pullRequest.ProjectKey, pullRequest.CreatedAt, () =>
            {
                Activity? claimant = Claimant(blocks, claim => claim.Claims(pullRequest)
                    && claim.At >= pullRequest.CreatedAt - PullRequestClaimLead && claim.At <= pullRequest.CreatedAt + PullRequestClaimWindow);
                return (claimant ?? OnBranch(blocks, pullRequest.SourceBranch, at) ?? Active(blocks, at, idleGap), claimant is not null);
            });
            PullRequest placed = pullRequest with { Claimed = place == CandidatePlace.Claimed };
            placements.Add(new Placement { Key = CandidateKeys.Of(pullRequest), PullRequest = placed, Place = place, BlockId = block?.Id, EntryId = entryId });
            if (block is not null)
            {
                Add(pullRequestsByBlock, block.Id, placed);
            }
            else if (place == CandidatePlace.Unattributed)
            {
                orphanPullRequests.Add(placed);
            }
        }

        List<Activity> result = activities
            .Select(activity => commitsByBlock.ContainsKey(activity.Id) || pullRequestsByBlock.ContainsKey(activity.Id)
                ? activity with
                {
                    Commits = [.. (commitsByBlock.GetValueOrDefault(activity.Id) ?? []).OrderBy(commit => commit.AuthoredAt)],
                    PullRequests = [.. (pullRequestsByBlock.GetValueOrDefault(activity.Id) ?? []).OrderBy(pullRequest => pullRequest.CreatedAt)],
                }
                : activity)
            .ToList();
        return new AttributionResult(result, orphans, orphanPullRequests, placements);
    }

    /// <summary>
    /// Whether a block may describe a candidate the user moves onto it: the block is agent work in the same project (or
    /// one sharing its friendly name), on the candidate's day or the day either side of it.
    /// </summary>
    public static bool CanTake(Activity block, string projectKey, DateTimeOffset at, TimeZoneInfo timeZone, IReadOnlyDictionary<string, string>? friendlyNames = null)
    {
        if (block.Source != ActivitySource.Agent || ProjectNaming.KeyOfGroup(block.GroupKey) is not { } blockKey
            || !string.Equals(ProjectName(blockKey, friendlyNames), ProjectName(projectKey, friendlyNames), StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        int day = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(at, timeZone).DateTime).DayNumber;
        return day >= DateOnly.FromDateTime(block.Start).DayNumber - 1 && day <= DateOnly.FromDateTime(block.End).DayNumber + 1;
    }

    private static string ProjectName(string key, IReadOnlyDictionary<string, string>? friendlyNames) =>
        friendlyNames?.GetValueOrDefault(key) is { Length: > 0 } name ? name : key;

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
