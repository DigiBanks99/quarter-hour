namespace QuarterHour.Core.Tests;

internal static class Catch
{
    public static Exception? Exception(Action action)
    {
        try
        {
            action();
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }
}
