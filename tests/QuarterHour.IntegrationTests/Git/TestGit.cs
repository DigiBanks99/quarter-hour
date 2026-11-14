using System.Diagnostics;

namespace QuarterHour.IntegrationTests.Git;

/// <summary>Builds real git repositories for tests, kept apart from the developer's own git configuration.</summary>
internal static class TestGit
{
    public static string Repository(TemporaryDirectory root, string name)
    {
        string folder = root.Folder(name);
        Run(folder, "init", "-q", "-b", "main");
        Run(folder, "config", "user.email", "me@example.com");
        Run(folder, "config", "user.name", "Me");
        Run(folder, "config", "commit.gpgsign", "false");
        return folder;
    }

    public static string Commit(string folder, string subject, string date, string email = "me@example.com")
    {
        Run(folder, new Dictionary<string, string>
        {
            ["GIT_AUTHOR_DATE"] = date,
            ["GIT_COMMITTER_DATE"] = date,
            ["GIT_AUTHOR_EMAIL"] = email,
            ["GIT_COMMITTER_EMAIL"] = email,
        }, "commit", "-q", "--allow-empty", "-m", subject);
        return Run(folder, "rev-parse", "HEAD").Trim();
    }

    public static string Run(string folder, params string[] arguments) => Run(folder, [], arguments);

    public static string Run(string folder, Dictionary<string, string> environment, params string[] arguments)
    {
        ProcessStartInfo start = new("git") { RedirectStandardError = true, RedirectStandardOutput = true, UseShellExecute = false };
        start.ArgumentList.Add("-C");
        start.ArgumentList.Add(folder);
        foreach (string argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        // Keep the developer's global hooks and templates out of the test repositories.
        start.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
        start.Environment["GIT_CONFIG_GLOBAL"] = Path.Combine(Path.GetTempPath(), "quarter-hour-tests", "empty.gitconfig");
        foreach ((string key, string value) in environment)
        {
            start.Environment[key] = value;
        }

        using Process process = Process.Start(start)!;
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        process.ExitCode.ShouldBe(0, $"git {string.Join(' ', arguments)} failed: {error}");
        return output.Result;
    }
}
