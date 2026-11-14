using System.Diagnostics;
using System.Text;

namespace QuarterHour.Infrastructure.Processes;

internal sealed record CommandResult(bool Succeeded, string Output, string Error);

/// <summary>Runs command-line tools such as git and the Azure CLI.</summary>
internal interface ICommandRunner
{
    Task<CommandResult> RunAsync(string fileName, string? workingDirectory, IReadOnlyList<string> arguments, CancellationToken cancellationToken);
}

/// <summary>
/// Runs a tool without a window or a prompt it could wait on, and gives up on it after a timeout. A tool that can't be
/// started fails like one that ran and failed, so callers report both the same way.
/// </summary>
internal sealed class CommandLine : ICommandRunner
{
    private readonly TimeSpan _timeout;

    public CommandLine(TimeSpan? timeout = null)
    {
        _timeout = timeout ?? TimeSpan.FromSeconds(30);
    }

    /// <summary>
    /// The full path of a tool on the PATH, or its bare name when it isn't there (so starting it fails as not installed).
    /// Windows batch wrappers such as az.cmd must be started by full path: started by bare name, they can't find the
    /// files next to them.
    /// </summary>
    public static string Find(string name)
    {
        foreach (string folder in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                string candidate = Path.Combine(folder.Trim().Trim('"'), name);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
            catch (ArgumentException)
            {
                // A malformed PATH entry can't hold the tool.
            }
        }

        return name;
    }

    public async Task<CommandResult> RunAsync(string fileName, string? workingDirectory, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        ProcessStartInfo start = new(fileName)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        if (workingDirectory is not null)
        {
            start.WorkingDirectory = workingDirectory;
        }

        foreach (string argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        // Never wait on a credential or pager prompt.
        start.Environment["GIT_TERMINAL_PROMPT"] = "0";
        start.Environment["GIT_PAGER"] = "cat";
        start.Environment["AZURE_CORE_ONLY_SHOW_ERRORS"] = "true";
        start.Environment["AZURE_CORE_NO_COLOR"] = "true";
        start.Environment["AZURE_EXTENSION_USE_DYNAMIC_INSTALL"] = "no";

        using Process process = new() { StartInfo = start };
        try
        {
            process.Start();
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return new CommandResult(false, "", $"{fileName} couldn't be started: {exception.Message}");
        }

        // Anything that still asks a question reads end-of-input and fails instead of waiting.
        process.StandardInput.Close();

        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_timeout);
        Task<string> output = process.StandardOutput.ReadToEndAsync(timeout.Token);
        Task<string> error = process.StandardError.ReadToEndAsync(timeout.Token);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
            return new CommandResult(process.ExitCode == 0, await output, await error);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            process.Kill(entireProcessTree: true);
            return new CommandResult(false, "", $"{fileName} took longer than {_timeout.TotalSeconds:0} seconds.");
        }
    }
}
