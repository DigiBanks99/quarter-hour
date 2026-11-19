using System.Globalization;
using QuarterHour.Core.Time;

namespace QuarterHour.Core.Export;

/// <summary>
/// The consecutive days, both included, an export is narrowed to. Both days fall in one calendar month inside the
/// period being exported.
/// </summary>
public readonly record struct ExportRange(DateOnly From, DateOnly To)
{
    public int Days => To.DayNumber - From.DayNumber + 1;

    /// <summary>The days as a file name suffix: "03–14", or "03" for a single day.</summary>
    public string Label => From == To
        ? From.ToString("dd", CultureInfo.InvariantCulture)
        : $"{From.ToString("dd", CultureInfo.InvariantCulture)}–{To.ToString("dd", CultureInfo.InvariantCulture)}";

    /// <summary>The days for people to read: "3–14 Nov", or "3 Nov" for a single day.</summary>
    public string DisplayLabel => From == To
        ? From.ToString("d MMM", CultureInfo.InvariantCulture)
        : $"{From.ToString("%d", CultureInfo.InvariantCulture)}–{To.ToString("d MMM", CultureInfo.InvariantCulture)}";

    /// <summary>The query string the export endpoint reads the range from: "?from=2026-11-03&amp;to=2026-11-14".</summary>
    public string Query =>
        $"?from={From.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}&to={To.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}";

    public bool Contains(DateOnly day) => day >= From && day <= To;

    /// <summary>Whether the range is every day of the period, which exports the same as no range.</summary>
    public bool Covers(TimesheetPeriod period) => From == period.Start && To == period.LastDay;

    /// <summary>Why the range can't be used to export the period, or null when it can.</summary>
    public string? Problem(TimesheetPeriod period)
    {
        if (From > To)
        {
            return "The from day is after the to day.";
        }

        if (From.Year != To.Year || From.Month != To.Month)
        {
            return "The from and to days must be in the same calendar month.";
        }

        return period.Contains(From) && period.Contains(To) ? null : $"The days must fall within {period.Name}.";
    }

    /// <summary>
    /// Reads an optional from and to day for the period. Neither gives no range (the whole period); only one, or a
    /// range with a <see cref="Problem"/>, gives the reason it is refused.
    /// </summary>
    public static bool TryCreate(TimesheetPeriod period, DateOnly? from, DateOnly? to, out ExportRange? range, out string? problem)
    {
        range = null;
        problem = null;
        if (from is null && to is null)
        {
            return true;
        }

        if (from is null || to is null)
        {
            problem = "Give both a from and a to day, or neither.";
            return false;
        }

        ExportRange candidate = new(from.Value, to.Value);
        problem = candidate.Problem(period);
        if (problem is not null)
        {
            return false;
        }

        range = candidate;
        return true;
    }
}
