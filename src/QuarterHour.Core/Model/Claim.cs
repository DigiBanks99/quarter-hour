using System.Text;
using System.Text.RegularExpressions;

namespace QuarterHour.Core.Model;

public enum ClaimKind
{
    Commit,
    PullRequest,
}

/// <summary>
/// Evidence in a session's tool output that the session made a candidate: git's "[main 1a2b3c4] Add login" after a
/// commit, or a pull request id in what creating one returned.
/// </summary>
/// <param name="Value">A commit SHA (abbreviated or full, lower case) or a pull request id.</param>
/// <param name="At">When the tool call that showed it happened.</param>
public sealed record Claim(ClaimKind Kind, string Value, DateTimeOffset At)
{
    public bool Claims(Commit commit) =>
        Kind == ClaimKind.Commit && commit.Sha.StartsWith(Value, StringComparison.OrdinalIgnoreCase);

    public bool Claims(PullRequest pullRequest) =>
        Kind == ClaimKind.PullRequest && Value == pullRequest.Id.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>
/// Finds claims in tool inputs and outputs, as written in any transcript (plain text or JSON, escaped any number of
/// times). <see cref="MightClaim"/> is a cheap byte search that lets a scanner skip lines before decoding them.
/// </summary>
public static partial class ClaimMatcher
{
    private static readonly byte[][] Markers = [Encoding.UTF8.GetBytes("] "), Encoding.UTF8.GetBytes("pullRequestId"), Encoding.UTF8.GetBytes("/pullrequest/")];

    public static bool MightClaim(ReadOnlySpan<byte> utf8)
    {
        foreach (byte[] marker in Markers)
        {
            if (utf8.IndexOf(marker) >= 0)
            {
                return true;
            }
        }

        return false;
    }

    public static bool MightClaim(string text) =>
        text.Contains("] ", StringComparison.Ordinal)
        || text.Contains("pullRequestId", StringComparison.Ordinal)
        || text.Contains("/pullrequest/", StringComparison.OrdinalIgnoreCase);

    public static IReadOnlyList<Claim> Find(string text, DateTimeOffset at)
    {
        if (!MightClaim(text))
        {
            return [];
        }

        List<Claim> claims = [];
        claims.AddRange(CommitOutput().Matches(text).Select(match => new Claim(ClaimKind.Commit, match.Groups["sha"].Value.ToLowerInvariant(), at)));
        claims.AddRange(PullRequestId().Matches(text).Select(match => new Claim(ClaimKind.PullRequest, match.Groups["id"].Value, at)));
        claims.AddRange(PullRequestUrl().Matches(text).Select(match => new Claim(ClaimKind.PullRequest, match.Groups["id"].Value, at)));
        return claims.DistinctBy(claim => (claim.Kind, claim.Value)).ToList();
    }

    public static IReadOnlyList<Claim> Find(ReadOnlySpan<byte> utf8, DateTimeOffset at) =>
        MightClaim(utf8) ? Find(Encoding.UTF8.GetString(utf8), at) : [];

    /// <summary>git commit prints "[branch sha] subject", "[main (root-commit) sha] subject" or "[detached HEAD sha] subject".</summary>
    [GeneratedRegex(@"\[(?:detached HEAD|[^\]\s""\\]+(?: \(root-commit\))?) (?<sha>[0-9a-f]{7,40})\] ")]
    private static partial Regex CommitOutput();

    /// <summary>What az repos pr create and the Azure DevOps MCP tools return, with the quotes escaped or not.</summary>
    [GeneratedRegex(@"pullRequestId(?:\\*"")?\s*:\s*(?<id>\d+)")]
    private static partial Regex PullRequestId();

    /// <summary>A pull request's web link: https://dev.azure.com/org/project/_git/repo/pullrequest/482.</summary>
    [GeneratedRegex(@"/_git/[^/\s""\\]+/pullrequest/(?<id>\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex PullRequestUrl();
}
