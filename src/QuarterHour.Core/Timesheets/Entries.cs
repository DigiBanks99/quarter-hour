using QuarterHour.Core.Configuration;
using QuarterHour.Core.Model;

namespace QuarterHour.Core.Timesheets;

/// <summary>
/// What may be grouped into an entry. An entry holds agent blocks and manual entries of one day and one category, at
/// a location in <see cref="Configuration.LocationSettings.EntryOrder"/>; meetings never join.
/// </summary>
public static class Entries
{
    /// <summary>Why an activity can't be in an entry at all, or null when it can.</summary>
    public static string? WhyNot(ActivitySource source, bool isCounting, string? categoryId, string location, IReadOnlyList<string> entryOrder)
    {
        if (source == ActivitySource.Calendar)
        {
            return "Meetings stay on lines of their own";
        }

        if (!isCounting)
        {
            return "It's left out";
        }

        if (categoryId is null)
        {
            return "It needs a category first";
        }

        return Rank(location, entryOrder) < 0 ? $"Work at {location} can't be grouped" : null;
    }

    /// <inheritdoc cref="WhyNot(ActivitySource, bool, string?, string, IReadOnlyList{string})"/>
    public static string? WhyNot(ActivityResult item, IReadOnlyList<string> entryOrder) =>
        WhyNot(item.Activity.Source, item.IsCounting, item.CategoryId, item.Location.Name, entryOrder);

    /// <summary>Why two activities can't share an entry, or null when they can.</summary>
    public static string? WhyNotTogether(ActivityResult item, ActivityResult other, IReadOnlyList<string> entryOrder)
    {
        if (WhyNot(item, entryOrder) is { } reason)
        {
            return reason;
        }

        if (WhyNot(other, entryOrder) is { } otherReason)
        {
            return otherReason;
        }

        if (item.PieceId == other.PieceId)
        {
            return "It's the same activity";
        }

        if (item.Date != other.Date)
        {
            return "Entries stay within one day";
        }

        return item.CategoryId != other.CategoryId ? "Only work of the same category can be grouped" : null;
    }

    /// <summary>An entry's location: the first of its activities' locations in <paramref name="entryOrder"/>.</summary>
    public static string LocationOf(IEnumerable<string> locations, IReadOnlyList<string> entryOrder) =>
        locations.OrderBy(location => Rank(location, entryOrder)).First();

    /// <summary>
    /// Puts <paramref name="item"/> in <paramref name="target"/>'s entry, or in a new entry with it. Check
    /// <see cref="WhyNotTogether"/> first. Leaving another entry may break that one up. Returns the entry's id.
    /// </summary>
    public static string Join(Journal journal, ActivityResult item, ActivityResult target, IEnumerable<ActivityResult> activities)
    {
        List<ActivityResult> day = activities.Where(other => other.Date == item.Date).ToList();
        string entry = target.EntryId ?? WorkspaceConfig.NewId();
        if (item.EntryId == entry)
        {
            return entry;
        }

        if (item.EntryId is not null)
        {
            TakeOut(journal, item, day);
        }

        if (target.EntryId is null)
        {
            SetEntry(journal, target, entry);
        }

        SetEntry(journal, item, entry);
        return entry;
    }

    /// <summary>Takes an activity out of its entry; an entry left with one activity is broken up.</summary>
    public static void TakeOut(Journal journal, ActivityResult item, IEnumerable<ActivityResult> activities)
    {
        if ((item.EntryId ?? item.Override?.Entry) is not { } entry)
        {
            return;
        }

        SetEntry(journal, item, null);
        journal.Entries.GetValueOrDefault(entry)?.Unticked.Remove(item.PieceId);
        List<ActivityResult> rest = Members(entry, activities).Where(other => other.PieceId != item.PieceId).ToList();
        if (rest.Count(other => other.EntryId == entry) <= 1)
        {
            BreakUp(journal, entry, rest);
        }
    }

    /// <summary>Puts every activity of an entry back on its usual line and forgets how the entry was described.</summary>
    public static void BreakUp(Journal journal, string entry, IEnumerable<ActivityResult> activities)
    {
        foreach (ActivityResult member in Members(entry, activities))
        {
            SetEntry(journal, member, null);
        }

        journal.Entries.Remove(entry);
        foreach (string key in journal.Lines.Keys.Where(key => key.EndsWith($":entry:{entry}", StringComparison.Ordinal)).ToList())
        {
            journal.Lines.Remove(key);
        }
    }

    /// <summary>The activities grouped into an entry, including those that no longer qualify for it.</summary>
    private static IEnumerable<ActivityResult> Members(string entry, IEnumerable<ActivityResult> activities) =>
        activities.Where(other => other.EntryId == entry || other.Override?.Entry == entry).DistinctBy(other => other.PieceId);

    /// <summary>Activity overrides are kept per piece, replacing one kept for the whole activity.</summary>
    private static void SetEntry(Journal journal, ActivityResult item, string? entry)
    {
        ActivityOverride current = journal.Overrides.GetValueOrDefault(item.PieceId) ?? journal.Overrides.GetValueOrDefault(item.Activity.Id) ?? new ActivityOverride();
        current.Entry = entry;
        journal.Overrides.Remove(item.Activity.Id);
        if (current.IsEmpty)
        {
            journal.Overrides.Remove(item.PieceId);
        }
        else
        {
            journal.Overrides[item.PieceId] = current;
        }
    }

    private static int Rank(string location, IReadOnlyList<string> entryOrder)
    {
        for (int index = 0; index < entryOrder.Count; index++)
        {
            if (string.Equals(entryOrder[index], location, StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return -1;
    }
}
