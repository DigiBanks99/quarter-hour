namespace QuarterHour.AppHost;

/// <summary>
/// Passes the Entra ID app registration for Microsoft Graph to a resource as environment variables. The dashboard asks
/// for the values when they aren't configured under <c>Parameters</c> (user secrets or appsettings). Leave them empty and
/// the app keeps using what is saved in Settings → Calendar.
/// </summary>
public static class GraphParameterExtensions
{
    public static IResourceBuilder<T> WithEntraGraphParameters<T>(this IResourceBuilder<T> resource)
        where T : IResourceWithEnvironment
    {
        IDistributedApplicationBuilder builder = resource.ApplicationBuilder;

        resource.WithEnvironment("QuarterHour__Graph__TenantId", builder.AddOptionalParameter(
            "entra-tenant-id", "Entra tenant ID", "The tenant id or domain. Leave empty to use Settings → Calendar.", secret: false));
        resource.WithEnvironment("QuarterHour__Graph__ClientId", builder.AddOptionalParameter(
            "entra-client-id", "Entra client ID", "The app registration's application (client) id. Leave empty to use Settings → Calendar.", secret: false));
        resource.WithEnvironment("QuarterHour__Graph__ClientSecret", builder.AddOptionalParameter(
            "entra-client-secret", "Entra client secret", "Not used yet: the app signs in as you, which needs no secret. Leave empty.", secret: true));
        return resource;
    }

    private static IResourceBuilder<ParameterResource> AddOptionalParameter(this IDistributedApplicationBuilder builder,
        string name, string label, string description, bool secret)
    {
#pragma warning disable ASPIREINTERACTION001 // Custom input is experimental; it lets the value be left empty.
        return builder.AddParameter(name, secret)
            .WithDescription(description)
            .WithCustomInput(parameter => new InteractionInput
            {
                Name = parameter.Name,
                Label = label,
                Description = parameter.Description,
                InputType = secret ? InputType.SecretText : InputType.Text,
                Placeholder = "Leave empty to use Settings → Calendar",
                Required = false,
            });
#pragma warning restore ASPIREINTERACTION001
    }
}
