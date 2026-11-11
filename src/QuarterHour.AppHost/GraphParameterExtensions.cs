namespace QuarterHour.AppHost;

/// <summary>
/// Passes the Entra ID app registration for Microsoft Graph to a resource as environment variables. Each value is an
/// optional parameter: fill it in under <c>Parameters</c> (user secrets or appsettings) to inject it, leave it out and the
/// app keeps using what is saved in Settings → Calendar.
/// </summary>
public static class GraphParameterExtensions
{
    private const string TenantId = "entra-tenant-id";
    private const string ClientId = "entra-client-id";
    private const string ClientSecret = "entra-client-secret";

    public static IResourceBuilder<T> WithEntraGraphParameters<T>(this IResourceBuilder<T> resource)
        where T : IResourceWithEnvironment
    {
        IDistributedApplicationBuilder builder = resource.ApplicationBuilder;
        resource.WithOptionalParameter(builder, TenantId, "QuarterHour__Graph__TenantId", secret: false);
        resource.WithOptionalParameter(builder, ClientId, "QuarterHour__Graph__ClientId", secret: false);
        resource.WithOptionalParameter(builder, ClientSecret, "QuarterHour__Graph__ClientSecret", secret: true);
        return resource;
    }

    private static void WithOptionalParameter<T>(this IResourceBuilder<T> resource, IDistributedApplicationBuilder builder,
        string name, string variable, bool secret)
        where T : IResourceWithEnvironment
    {
        // Only declare the parameter when it has a value, so a missing one doesn't block the run waiting for input.
        if (string.IsNullOrWhiteSpace(builder.Configuration[$"Parameters:{name}"]))
        {
            return;
        }

        IResourceBuilder<ParameterResource> parameter = builder.AddParameter(name, secret);
        resource.WithEnvironment(variable, parameter);
    }
}
