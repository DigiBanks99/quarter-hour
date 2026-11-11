using QuarterHour.Core.Configuration;

namespace QuarterHour.Core.Time;

/// <summary>Rounds durations to the timesheet increment (15 minutes by default).</summary>
public static class Rounding
{
    /// <summary>
    /// Rounds to whole increments. The leftover past the last whole increment rounds up once it reaches
    /// <see cref="RoundingSettings.RoundUpFromMinutes"/>: with 15 and 7, 50m becomes 45m and 3h 22m becomes 3h 30m.
    /// </summary>
    public static int RoundMinutes(TimeSpan duration, RoundingSettings settings) =>
        RoundMinutes(duration, settings.IncrementMinutes, settings.RoundUpFromMinutes);

    public static int RoundMinutes(TimeSpan duration, int incrementMinutes, double roundUpFromMinutes)
    {
        if (duration <= TimeSpan.Zero)
        {
            return 0;
        }

        int increment = Math.Max(1, incrementMinutes);
        long incrementTicks = TimeSpan.FromMinutes(increment).Ticks;
        long thresholdTicks = (long)(Math.Clamp(roundUpFromMinutes, 0, increment) * TimeSpan.TicksPerMinute);
        long wholeIncrements = duration.Ticks / incrementTicks;
        long leftover = duration.Ticks % incrementTicks;
        if (leftover > 0 && leftover >= thresholdTicks)
        {
            wholeIncrements++;
        }

        return (int)(wholeIncrements * increment);
    }

    public static int RoundMinutes(int minutes, RoundingSettings settings) =>
        RoundMinutes(TimeSpan.FromMinutes(minutes), settings);
}
