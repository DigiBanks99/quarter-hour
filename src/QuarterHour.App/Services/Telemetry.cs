using System.Diagnostics;

namespace QuarterHour.App.Services;

/// <summary>
/// The app's OpenTelemetry trace sources. Backend work (calendar sync, agent scans, timesheet builds) gets a span
/// that records any exception escaping it; UI actions get a parent span so a failure shows up in the dashboard as
/// one trace, from the button the user pressed down to the HTTP call that failed.
/// </summary>
public static class Telemetry
{
    public const string BackendSource = "QuarterHour.Workspace";
    public const string UiSource = "QuarterHour.Ui";

    private static readonly ActivitySource Backend = new(BackendSource);
    private static readonly ActivitySource UiActions = new(UiSource);

    /// <summary>Starts a span for a piece of backend work. Dispose it when the work ends.</summary>
    public static Activity? StartWork(string name) => Backend.StartActivity(name);

    /// <summary>Starts a span for something the user did in the UI. Dispose it when the action ends.</summary>
    /// <remarks>
    /// It nests under Blazor's own circuit span; any backend spans and HTTP calls made during the action become its
    /// children.
    /// </remarks>
    public static Activity? StartUiAction(string name) => UiActions.StartActivity($"ui: {name}");

    /// <summary>Marks the span as failed and attaches the exception (type, message and stack trace).</summary>
    public static void Fail(this Activity? activity, Exception exception)
    {
        if (activity is null)
        {
            return;
        }

        activity.SetStatus(ActivityStatusCode.Error, exception.Message);
        activity.AddException(exception);
    }

    /// <summary>Runs backend work inside a span, recording the exception if it fails.</summary>
    public static async Task<T> TraceAsync<T>(string name, Func<Activity?, Task<T>> work)
    {
        using Activity? activity = StartWork(name);
        try
        {
            return await work(activity);
        }
        catch (Exception exception)
        {
            activity.Fail(exception);
            throw;
        }
    }

    /// <summary>Runs backend work inside a span, recording the exception if it fails.</summary>
    public static T Trace<T>(string name, Func<Activity?, T> work)
    {
        using Activity? activity = StartWork(name);
        try
        {
            return work(activity);
        }
        catch (Exception exception)
        {
            activity.Fail(exception);
            throw;
        }
    }
}
