namespace QuarterHour.Core.Model;

public sealed record CommitAttributionResult(IReadOnlyList<Activity> Activities, IReadOnlyList<Commit> Orphans);

/// <summary>
/// Attributes commits to the blocks of agent work they describe. A commit belongs to a block of the same project that
/// was active when it was authored, or that ended less than an idle gap before it (the commit was the block's last
/// act). Of several such blocks, the one active at the time wins, else the one that ended most recently. Commits no
/// block can take are orphans. Commits only describe time; they never add or move it.
/// </summary>
public static class CommitAttribution
{
    public static CommitAttributionResult Attribute(IReadOnlyList<Activity> activities, IReadOnlyList<Commit> commits, TimeSpan idleGap, TimeZoneInfo timeZone)
    {
        Dictionary<string, List<Activity>> blocksByProject = activities
            .Where(activity => activity.Source == ActivitySource.Agent && ProjectNaming.KeyOfGroup(activity.GroupKey) is not null)
            .GroupBy(activity => ProjectNaming.KeyOfGroup(activity.GroupKey)!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);

        Dictionary<string, List<Commit>> attributed = [];
        List<Commit> orphans = [];
        foreach (Commit commit in commits
                     .Where(commit => !CommitSubjects.IsNoise(commit.Subject))
                     .DistinctBy(commit => commit.Sha)
                     .OrderBy(commit => commit.AuthoredAt))
        {
            DateTime at = TimeZoneInfo.ConvertTime(commit.AuthoredAt, timeZone).DateTime;
            Activity? block = blocksByProject.GetValueOrDefault(commit.ProjectKey)?
                .Where(candidate => candidate.Start <= at && at <= candidate.End + idleGap)
                .OrderBy(candidate => at <= candidate.End ? TimeSpan.Zero : at - candidate.End)
                .ThenByDescending(candidate => candidate.Start)
                .FirstOrDefault();
            if (block is null)
            {
                orphans.Add(commit);
                continue;
            }

            if (!attributed.TryGetValue(block.Id, out List<Commit>? list))
            {
                attributed[block.Id] = list = [];
            }

            list.Add(commit);
        }

        List<Activity> result = activities
            .Select(activity => attributed.TryGetValue(activity.Id, out List<Commit>? list) ? activity with { Commits = list } : activity)
            .ToList();
        return new CommitAttributionResult(result, orphans);
    }
}

/// <summary>What a block of agent work did, in timesheet words: its commits, else its session's title.</summary>
public static class AgentWork
{
    public static IReadOnlyList<string> Commits(Activity activity) =>
        activity.Commits
            .Select(commit => CommitSubjects.Clean(commit.Subject))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    public static IReadOnlyList<string> Parts(Activity activity)
    {
        IReadOnlyList<string> commits = Commits(activity);
        return commits.Count > 0 ? commits : [activity.Title];
    }

    public static string Describe(Activity activity) => string.Join("; ", Parts(activity));
}
