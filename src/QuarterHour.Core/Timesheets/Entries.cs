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
