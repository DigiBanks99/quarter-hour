using System.Text.RegularExpressions;

namespace QuarterHour.Core.Model;

/// <summary>A commit the user authored in a project: a candidate for describing a block of agent work.</summary>
public sealed record Commit
{
    public required string Sha { get; init; }
    public required string Subject { get; init; }

    /// <summary>The author date, which survives rebase and cherry-pick, unlike the committer date.</summary>
    public required DateTimeOffset AuthoredAt { get; init; }

    /// <summary>The project the commit was found in, keyed like its agent group (repository name, lower case).</summary>
    public required string ProjectKey { get; init; }

    public string ShortSha => Sha.Length > 7 ? Sha[..7] : Sha;
}

/// <summary>Turns commit subjects into timesheet wording, and recognises commits that say nothing new about the work.</summary>
public static partial class CommitSubjects
{
    /// <summary>"feat(ui): follow the browser colour scheme" is "Follow the browser colour scheme".</summary>
    public static string Clean(string subject)
    {
        string trimmed = subject.Trim();
        string cleaned = ConventionalPrefix().Replace(trimmed, "").Trim();
        if (cleaned.Length == 0)
        {
            return trimmed;
        }

        return char.ToUpperInvariant(cleaned[0]) + cleaned[1..];
    }

    /// <summary>Fixups, squashes and merge commits repeat other commits rather than describe new work.</summary>
    public static bool IsNoise(string subject)
    {
        string trimmed = subject.TrimStart();
        return trimmed.Length == 0
               || trimmed.StartsWith("fixup!", StringComparison.OrdinalIgnoreCase)
               || trimmed.StartsWith("squash!", StringComparison.OrdinalIgnoreCase)
               || trimmed.StartsWith("amend!", StringComparison.OrdinalIgnoreCase)
               || trimmed.StartsWith("Merge branch ", StringComparison.Ordinal)
               || trimmed.StartsWith("Merge remote-tracking branch ", StringComparison.Ordinal)
               || trimmed.StartsWith("Merge pull request ", StringComparison.Ordinal)
               || trimmed.StartsWith("Merged PR ", StringComparison.Ordinal);
    }

    [GeneratedRegex(@"^[A-Za-z]+(\([^)]*\))?!?:\s*")]
    private static partial Regex ConventionalPrefix();
}
