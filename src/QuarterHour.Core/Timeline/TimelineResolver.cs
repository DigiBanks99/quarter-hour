using QuarterHour.Core.Configuration;

namespace QuarterHour.Core.Timeline;

public sealed record TimelineItem(string Id, DateTime Start, DateTime End, int Priority);

/// <summary>How much of an item's time it keeps, and which items it overlapped.</summary>
public sealed record TimelineSlot(string Id, TimeSpan Effective, IReadOnlyList<string> OverlappedBy, IReadOnlyList<(DateTime Start, DateTime End)> Segments)
{
    public bool WasClipped(TimeSpan full) => Effective < full;
}

/// <summary>
/// Makes sure the same minutes aren't logged twice. Higher priorities claim their time first. Within a priority,
/// the earlier (then longer) item wins, except for priorities listed as shared: items there split overlapping time
/// evenly, the way two agents running side by side each get half of that hour.
/// </summary>
public static class TimelineResolver
{
    public static IReadOnlyDictionary<string, TimelineSlot> Resolve(IEnumerable<TimelineItem> items, OverlapPolicy policy, IReadOnlySet<int>? sharedPriorities = null)
    {
        List<TimelineItem> valid = items.Where(item => item.End > item.Start).ToList();
        Dictionary<string, TimelineSlot> slots = new(StringComparer.Ordinal);
        if (policy == OverlapPolicy.CountOverlaps)
        {
            foreach (TimelineItem item in valid)
            {
                List<string> overlapping = valid.Where(other => other.Id != item.Id && Overlaps(item, other)).Select(other => other.Id).ToList();
                slots[item.Id] = new TimelineSlot(item.Id, item.End - item.Start, overlapping, [(item.Start, item.End)]);
            }

            return slots;
        }

        List<TimelineItem> placed = [];
        foreach (IGrouping<int, TimelineItem> tier in valid.GroupBy(item => item.Priority).OrderByDescending(group => group.Key))
        {
            List<TimelineItem> tierItems = tier
                .OrderBy(item => item.Start)
                .ThenByDescending(item => item.End - item.Start)
                .ThenBy(item => item.Id, StringComparer.Ordinal)
                .ToList();
            if (sharedPriorities?.Contains(tier.Key) == true)
            {
                Share(tierItems, placed, slots);
                placed.AddRange(tierItems);
                continue;
            }

            foreach (TimelineItem item in tierItems)
            {
                List<string> overlappedBy = placed.Where(other => Overlaps(item, other)).Select(other => other.Id).ToList();
                List<(DateTime Start, DateTime End)> segments = Subtract((item.Start, item.End), placed.Select(other => (other.Start, other.End)));
                slots[item.Id] = new TimelineSlot(item.Id, Total(segments), overlappedBy, segments);
                placed.Add(item);
            }
        }

        return slots;
    }

    /// <summary>Splits every stretch of time evenly between the items of one tier that are active in it.</summary>
    private static void Share(List<TimelineItem> items, List<TimelineItem> placed, Dictionary<string, TimelineSlot> slots)
    {
        List<DateTime> boundaries = items
            .SelectMany(item => new[] { item.Start, item.End })
            .Concat(placed.SelectMany(other => new[] { other.Start, other.End }))
            .Distinct()
            .Order()
            .ToList();
        Dictionary<string, TimeSpan> effective = items.ToDictionary(item => item.Id, _ => TimeSpan.Zero, StringComparer.Ordinal);
        Dictionary<string, List<(DateTime Start, DateTime End)>> segments = items.ToDictionary(item => item.Id, _ => new List<(DateTime, DateTime)>(), StringComparer.Ordinal);
        Dictionary<string, HashSet<string>> overlapped = items.ToDictionary(
            item => item.Id,
            item => placed.Where(other => Overlaps(item, other)).Select(other => other.Id).ToHashSet(StringComparer.Ordinal),
            StringComparer.Ordinal);

        for (int index = 0; index + 1 < boundaries.Count; index++)
        {
            DateTime from = boundaries[index];
            DateTime to = boundaries[index + 1];
            if (placed.Any(other => other.Start < to && from < other.End))
            {
                continue;
            }

            List<TimelineItem> active = items.Where(item => item.Start <= from && item.End >= to).ToList();
            if (active.Count == 0)
            {
                continue;
            }

            TimeSpan share = (to - from) / active.Count;
            foreach (TimelineItem item in active)
            {
                effective[item.Id] += share;
                List<(DateTime Start, DateTime End)> own = segments[item.Id];
                if (own.Count > 0 && own[^1].End == from)
                {
                    own[^1] = (own[^1].Start, to);
                }
                else
                {
                    own.Add((from, to));
                }

                if (active.Count > 1)
                {
                    overlapped[item.Id].UnionWith(active.Where(other => other.Id != item.Id).Select(other => other.Id));
                }
            }
        }

        foreach (TimelineItem item in items)
        {
            slots[item.Id] = new TimelineSlot(item.Id, effective[item.Id], [.. overlapped[item.Id]], segments[item.Id]);
        }
    }

    private static bool Overlaps(TimelineItem item, TimelineItem other) => other.Start < item.End && item.Start < other.End;

    private static TimeSpan Total(List<(DateTime Start, DateTime End)> segments) =>
        segments.Aggregate(TimeSpan.Zero, (total, segment) => total + (segment.End - segment.Start));

    internal static List<(DateTime Start, DateTime End)> Subtract((DateTime Start, DateTime End) range, IEnumerable<(DateTime Start, DateTime End)> taken)
    {
        List<(DateTime Start, DateTime End)> remaining = [range];
        foreach ((DateTime takenStart, DateTime takenEnd) in taken.OrderBy(interval => interval.Start))
        {
            List<(DateTime Start, DateTime End)> next = [];
            foreach ((DateTime start, DateTime end) in remaining)
            {
                if (takenEnd <= start || takenStart >= end)
                {
                    next.Add((start, end));
                    continue;
                }

                if (takenStart > start)
                {
                    next.Add((start, takenStart));
                }

                if (takenEnd < end)
                {
                    next.Add((takenEnd, end));
                }
            }

            remaining = next;
            if (remaining.Count == 0)
            {
                break;
            }
        }

        return remaining;
    }
}
