using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace QuarterHour.EndToEndTests;

/// <summary>Steps through the app's UI the way a person would.</summary>
internal static class Pages
{
    /// <summary>
    /// Waits for Blazor to connect the circuit: until then the page is static prerendered HTML and clicks do nothing.
    /// </summary>
    public static async Task WaitForInteractiveAsync(this IPage page)
    {
        await page.WaitForFunctionAsync("() => window.Blazor !== undefined && document.querySelector('.qh-period-name') !== null");
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
    }

    /// <summary>Moves the period switcher in the app bar to the month given, one month at a time.</summary>
    public static async Task ShowPeriodAsync(this IPage page, int year, int month)
    {
        string target = new DateOnly(year, month, 1).ToString("MMMM yyyy", CultureInfo.InvariantCulture);
        ILocator name = page.Locator(".qh-period-name");
        for (int step = 0; step < 240; step++)
        {
            string shown = (await name.InnerTextAsync()).Trim();
            if (shown == target)
            {
                return;
            }

            DateOnly current = DateOnly.ParseExact("1 " + shown, "d MMMM yyyy", CultureInfo.InvariantCulture);
            string button = current < new DateOnly(year, month, 1) ? "Next period" : "Previous period";
            await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = button }).ClickAsync();
            await Assertions.Expect(name).Not.ToHaveTextAsync(shown);
        }

        throw new InvalidOperationException($"The period switcher never reached {target}.");
    }

    /// <summary>Follows a link in the navigation drawer, keeping the same circuit (and so the same period). A link may carry a count badge, as Mapping does.</summary>
    public static async Task NavigateAsync(this IPage page, string linkText)
    {
        await page.Locator(".mud-navmenu").GetByRole(AriaRole.Link, new LocatorGetByRoleOptions { NameRegex = new Regex($@"^{Regex.Escape(linkText)}(\s+\d+)?$") }).ClickAsync();
    }

    /// <summary>The repository's samples folder, found by walking up from the test binaries to the solution.</summary>
    public static string Sample(string fileName)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "QuarterHour.slnx")))
        {
            directory = directory.Parent;
        }

        return directory is null
            ? throw new DirectoryNotFoundException("Couldn't find QuarterHour.slnx above the test output.")
            : Path.Combine(directory.FullName, "samples", fileName);
    }
}
