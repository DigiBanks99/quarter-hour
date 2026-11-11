using System.Globalization;
using MudBlazor;
using QuarterHour.Core.Configuration;
using QuarterHour.Core.Locations;
using QuarterHour.Core.Model;
using QuarterHour.Core.Timesheets;

namespace QuarterHour.App.Services;

/// <summary>Formatting and presentation helpers shared by the pages.</summary>
public static class Ui
{
    private static readonly string[] Palette =
    [
        "#2563eb", "#0891b2", "#7c3aed", "#db2777", "#ea580c", "#16a34a", "#ca8a04", "#4f46e5", "#0d9488", "#be123c", "#9333ea", "#65a30d",
    ];

    public static readonly CultureInfo Culture = CultureInfo.InvariantCulture;

    public static MudTheme Theme { get; } = new()
    {
        PaletteLight = new PaletteLight
        {
            Primary = "#4f46e5",
            Secondary = "#0ea5e9",
            Tertiary = "#f59e0b",
            AppbarBackground = "#1e1b4b",
            AppbarText = "#ffffff",
            Background = "#f5f6fa",
            Surface = "#ffffff",
            DrawerBackground = "#ffffff",
            DrawerText = "#334155",
            TextPrimary = "#1f2937",
            TextSecondary = "#64748b",
            Success = "#16a34a",
            Warning = "#d97706",
            Error = "#dc2626",
            Info = "#0284c7",
            LinesDefault = "#e5e7eb",
            TableLines = "#eef0f4",
        },
        PaletteDark = new PaletteDark
        {
            Primary = "#818cf8",
            Secondary = "#38bdf8",
            Tertiary = "#fbbf24",
            AppbarBackground = "#111827",
            Background = "#0b1020",
            Surface = "#151b2e",
            DrawerBackground = "#111827",
            TextPrimary = "#e5e7eb",
            TextSecondary = "#94a3b8",
            LinesDefault = "#273047",
            TableLines = "#1f2840",
        },
        Typography = new Typography
        {
            Default = new DefaultTypography { FontFamily = ["Segoe UI", "Inter", "system-ui", "-apple-system", "Roboto", "Helvetica", "Arial", "sans-serif"] },
        },
        LayoutProperties = new LayoutProperties { DefaultBorderRadius = "10px" },
    };

    /// <summary>Timesheet-style hours: "7.75 h".</summary>
    public static string Hours(int minutes) => (minutes / 60.0).ToString("0.##", Culture) + " h";

    public static string HoursNumber(int minutes) => (minutes / 60.0).ToString("0.00", Culture);

    /// <summary>A measured duration: "1h 22m".</summary>
    public static string Duration(TimeSpan duration)
    {
        int minutes = (int)Math.Round(duration.TotalMinutes);
        if (minutes < 60)
        {
            return $"{minutes}m";
        }

        return minutes % 60 == 0 ? $"{minutes / 60}h" : $"{minutes / 60}h {minutes % 60:D2}m";
    }

    public static string Time(DateTime time) => time.ToString("HH:mm", Culture);

    public static string Day(DateOnly date) => date.ToString("ddd d MMM", Culture);

    public static string LongDay(DateOnly date) => date.ToString("dddd d MMMM yyyy", Culture);

    public static string DateKey(DateOnly date) => date.ToString("yyyy-MM-dd", Culture);

    public static string ColorOf(Category? category)
    {
        if (category is null)
        {
            return "#94a3b8";
        }

        if (!string.IsNullOrWhiteSpace(category.Color))
        {
            return category.Color;
        }

        int hash = 17;
        foreach (char character in category.Group + category.Name)
        {
            hash = unchecked((hash * 31) + character);
        }

        return Palette[Math.Abs(hash % Palette.Length)];
    }

    public static string NextColor(IEnumerable<Category> categories)
    {
        HashSet<string> used = categories.Select(category => category.Color ?? "").ToHashSet(StringComparer.OrdinalIgnoreCase);
        return Palette.FirstOrDefault(color => !used.Contains(color)) ?? Palette[used.Count % Palette.Length];
    }

    public static string SourceIcon(ActivitySource? source) => source switch
    {
        ActivitySource.Calendar => Icons.Material.Outlined.Event,
        ActivitySource.Agent => Icons.Material.Outlined.SmartToy,
        ActivitySource.Manual => Icons.Material.Outlined.EditNote,
        _ => Icons.Material.Outlined.Layers,
    };

    public static string SourceLabel(ActivitySource? source) => source switch
    {
        ActivitySource.Calendar => "Calendar",
        ActivitySource.Agent => "Agent session",
        ActivitySource.Manual => "Manual entry",
        _ => "Several sources",
    };

    public static string LocationIcon(string? location) => location?.ToLowerInvariant() switch
    {
        "client" => Icons.Material.Outlined.Business,
        "entelect" => Icons.Material.Outlined.Apartment,
        "home" => Icons.Material.Outlined.Home,
        "other" => Icons.Material.Outlined.Groups,
        _ => Icons.Material.Outlined.Place,
    };

    public static string OriginLabel(LocationOrigin origin) => origin switch
    {
        LocationOrigin.Default => "default",
        LocationOrigin.Schedule => "schedule",
        LocationOrigin.DayOverride => "set for the day",
        LocationOrigin.MeetingPin => "pinned",
        LocationOrigin.ManualEntry => "manual entry",
        LocationOrigin.EntryOverride => "set on entry",
        _ => origin.ToString(),
    };

    public static (string Label, Color Color, string Icon) Status(ActivityStatus status) => status switch
    {
        ActivityStatus.Counted => ("Counted", Color.Success, Icons.Material.Filled.CheckCircle),
        ActivityStatus.Unmapped => ("Unmapped", Color.Warning, Icons.Material.Filled.HelpOutline),
        ActivityStatus.NotRelevant => ("Not relevant", Color.Default, Icons.Material.Outlined.Block),
        ActivityStatus.Filtered => ("Filtered", Color.Default, Icons.Material.Outlined.FilterAlt),
        ActivityStatus.Excluded => ("Excluded", Color.Default, Icons.Material.Outlined.RemoveCircleOutline),
        _ => (status.ToString(), Color.Default, Icons.Material.Outlined.Circle),
    };

    /// <summary>A deep copy, so dialogs can edit without touching the live configuration until saved.</summary>
    public static T Clone<T>(T value)
        where T : class => QuarterHour.Infrastructure.Storage.JsonFiles.Parse<T>(QuarterHour.Infrastructure.Storage.JsonFiles.Serialize(value))!;

    public static string Plural(int count, string singular, string? plural = null) =>
        $"{count} {(count == 1 ? singular : plural ?? singular + "s")}";
}
