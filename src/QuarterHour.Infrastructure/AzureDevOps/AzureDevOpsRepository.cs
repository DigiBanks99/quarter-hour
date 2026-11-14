using System.Text.RegularExpressions;

namespace QuarterHour.Infrastructure.AzureDevOps;

/// <summary>An Azure DevOps git repository, as named by one of its remote URLs.</summary>
/// <param name="OrganizationUrl">What the Azure CLI's --org takes, such as "https://dev.azure.com/contoso".</param>
public sealed partial record AzureDevOpsRepository(string OrganizationUrl, string Organization, string Project, string Name)
{
    /// <summary>Reads the HTTPS, legacy visualstudio.com and SSH remote forms; anything else isn't Azure DevOps.</summary>
    public static AzureDevOpsRepository? TryParse(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        string trimmed = url.Trim();
        if (DevAzure().Match(trimmed) is { Success: true } modern)
        {
            string organization = Decode(modern.Groups["org"].Value);
            return Create($"https://dev.azure.com/{modern.Groups["org"].Value}", organization, modern.Groups["project"], modern.Groups["repo"]);
        }

        if (VisualStudio().Match(trimmed) is { Success: true } legacy)
        {
            string organization = legacy.Groups["org"].Value;
            return Create($"https://{organization}.visualstudio.com", organization, legacy.Groups["project"], legacy.Groups["repo"]);
        }

        if (Ssh().Match(trimmed) is { Success: true } ssh)
        {
            string organization = ssh.Groups["org"].Value;
            return Create(
                $"https://dev.azure.com/{organization}", Decode(organization), ssh.Groups["project"], ssh.Groups["repo"]);
        }

        return null;
    }

    public override string ToString() => $"{Organization}/{Project}/{Name}";

    /// <summary>"https://dev.azure.com/contoso/Portal/_git/portal/pullrequest/482".</summary>
    public string PullRequestUrl(int id) =>
        $"{OrganizationUrl.TrimEnd('/')}/{Uri.EscapeDataString(Project)}/_git/{Uri.EscapeDataString(Name)}/pullrequest/{id}";

    public bool SameAs(AzureDevOpsRepository other) =>
        string.Equals(Organization, other.Organization, StringComparison.OrdinalIgnoreCase)
        && string.Equals(Project, other.Project, StringComparison.OrdinalIgnoreCase)
        && string.Equals(Name, other.Name, StringComparison.OrdinalIgnoreCase);

    private static AzureDevOpsRepository Create(string organizationUrl, string organization, Group project, Group repository)
    {
        string name = Decode(repository.Value);
        if (name.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
        {
            name = name[..^4];
        }

        // https://dev.azure.com/contoso/_git/portal names a repository in the project of the same name.
        string projectName = project.Success && project.Value.Length > 0 ? Decode(project.Value) : name;
        return new AzureDevOpsRepository(organizationUrl, organization, projectName, name);
    }

    private static string Decode(string value) => Uri.UnescapeDataString(value);

    [GeneratedRegex(@"^https?://(?:[^@/]+@)?dev\.azure\.com/(?<org>[^/]+)/(?:(?<project>[^/]+)/)?_git/(?<repo>[^/?#]+)/?$", RegexOptions.IgnoreCase)]
    private static partial Regex DevAzure();

    [GeneratedRegex(@"^https?://(?:[^@/]+@)?(?<org>[^./@]+)\.visualstudio\.com/(?:DefaultCollection/)?(?:(?<project>[^/]+)/)?_git/(?<repo>[^/?#]+)/?$", RegexOptions.IgnoreCase)]
    private static partial Regex VisualStudio();

    [GeneratedRegex(@"^(?:ssh://)?[^@]+@(?:ssh\.dev\.azure\.com|vs-ssh\.visualstudio\.com)[:/]v3/(?<org>[^/]+)/(?<project>[^/]+)/(?<repo>[^/]+?)/?$", RegexOptions.IgnoreCase)]
    private static partial Regex Ssh();
}
