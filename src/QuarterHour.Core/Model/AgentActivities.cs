using System.Globalization;

namespace QuarterHour.Core.Model;

/// <summary>
/// Turns an agent session's timestamps into blocks of work. A pause longer than the idle gap starts a new block,
/// and each block gets a little padding after its last message for reading and reviewing the result.
/// </summary>
public static class AgentActivities
{
    public static IReadOnlyList<Activity> ToActivities(AgentSession session, TimeSpan idleGap, TimeSpan padding, TimeZoneInfo timeZone)
    {
        List<DateTime> times = session.Timestamps
            .Select(timestamp => TimeZoneInfo.ConvertTime(timestamp, timeZone).DateTime)
            .Order()
            .ToList();
        if (times.Count == 0)
        {
            return [];
        }

        List<(DateTime Start, DateTime Last, int Count)> blocks = [];
        DateTime blockStart = times[0];
        DateTime last = times[0];
        int count = 1;
        for (int index = 1; index < times.Count; index++)
        {
            DateTime time = times[index];
            if (time - last > idleGap)
            {
                blocks.Add((blockStart, last, count));
                blockStart = time;
                count = 0;
            }

            last = time;
            count++;
        }

        blocks.Add((blockStart, last, count));

        string project = ProjectNaming.Label(session.ProjectPath, session.Repository);
        string groupKey = "agent:" + ProjectNaming.Key(session.ProjectPath, session.Repository);
        string title = string.IsNullOrWhiteSpace(session.Title) ? project : session.Title.Trim();
        List<Activity> activities = new(blocks.Count);
        for (int index = 0; index < blocks.Count; index++)
        {
            (DateTime start, DateTime blockLast, int messages) = blocks[index];
            DateTime end = blockLast + padding;
            if (index + 1 < blocks.Count && end > blocks[index + 1].Start)
            {
                end = blocks[index + 1].Start;
            }

            activities.Add(new Activity
            {
                Id = $"agent:{session.Agent}:{session.SessionId}:{start.ToString("yyyyMMdd'T'HHmmss", CultureInfo.InvariantCulture)}",
                Source = ActivitySource.Agent,
                Start = start,
                End = end,
                Title = title,
                GroupKey = groupKey,
                GroupLabel = project,
                Agent = session.Agent,
                SessionId = session.SessionId,
                SessionTitle = session.Title,
                ProjectPath = session.ProjectPath,
                Repository = session.Repository,
                Branch = session.Branch,
                MessageCount = messages,
            });
        }

        return activities;
    }
}

/// <summary>Names projects consistently across agents: by repository leaf name, else the working folder's name.</summary>
public static class ProjectNaming
{
    public static string Label(string? path, string? repository)
    {
        string? fromRepository = RepositoryName(repository);
        if (fromRepository is not null)
        {
            return fromRepository;
        }

        if (string.IsNullOrWhiteSpace(path))
        {
            return "(no project)";
        }

        string trimmed = path.Trim().TrimEnd('\\', '/');
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile).TrimEnd('\\', '/');
        if (home.Length > 0 && string.Equals(trimmed, home, StringComparison.OrdinalIgnoreCase))
        {
            return "~ (home folder)";
        }

        int separator = trimmed.LastIndexOfAny(['\\', '/']);
        string leaf = separator >= 0 ? trimmed[(separator + 1)..] : trimmed;
        return leaf.Length == 0 ? trimmed : leaf;
    }

    public static string Key(string? path, string? repository) => Label(path, repository).ToLowerInvariant();

    /// <summary>"https://github.com/acme/portal.git", "git@github.com:acme/portal.git" and "acme/portal" are all "portal".</summary>
    public static string? RepositoryName(string? repository)
    {
        if (string.IsNullOrWhiteSpace(repository))
        {
            return null;
        }

        string trimmed = repository.Trim().TrimEnd('/');
        if (trimmed.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed[..^4];
        }

        int separator = trimmed.LastIndexOfAny(['/', ':', '\\']);
        string leaf = separator >= 0 ? trimmed[(separator + 1)..] : trimmed;
        return leaf.Length == 0 ? null : leaf;
    }
}
