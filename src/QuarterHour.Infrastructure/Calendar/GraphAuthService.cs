using Microsoft.Identity.Client;
using Microsoft.Identity.Client.Extensions.Msal;
using QuarterHour.Core.Configuration;
using QuarterHour.Infrastructure.Storage;

namespace QuarterHour.Infrastructure.Calendar;

/// <summary>Thrown when a Graph token can't be had without the user signing in.</summary>
public sealed class GraphSignInRequiredException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// Signs in to Microsoft Graph as the user (delegated, public client) with MSAL. Tokens are cached on disk,
/// encrypted with DPAPI on Windows, so signing in is needed only once in a while.
/// </summary>
public sealed class GraphAuthService
{
    private const string GraphResource = "https://graph.microsoft.com/";

    // Entra shows some errors (e.g. AADSTS50105, user not assigned) on its own page and never redirects back, so the
    // browser sign-in would wait forever. Device codes expire after 15 minutes on Entra's side anyway.
    private static readonly TimeSpan BrowserSignInTimeout = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan DeviceCodeSignInTimeout = TimeSpan.FromMinutes(15);

    private readonly DataDirectory _directory;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private CancellationTokenSource? _pendingSignIn;
    private IPublicClientApplication? _application;
    private string? _applicationKey;
    private MsalCacheHelper? _cacheHelper;

    public GraphAuthService(DataDirectory directory)
    {
        _directory = directory;
    }

    /// <summary>The signed-in account's user name, or null when nobody is signed in.</summary>
    public async Task<string?> SignedInAsAsync(GraphSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.ClientId))
        {
            return null;
        }

        IPublicClientApplication application = await ApplicationAsync(settings);
        IAccount? account = (await application.GetAccountsAsync()).FirstOrDefault();
        return account?.Username;
    }

    /// <summary>
    /// Gets an access token, silently when possible. With <paramref name="interactive"/> it opens the browser (or shows a
    /// device code through <paramref name="deviceCode"/>) when the user has to sign in.
    /// </summary>
    public async Task<string> GetAccessTokenAsync(GraphSettings settings, bool interactive, Func<string, Task>? deviceCode, CancellationToken cancellationToken)
    {
        IPublicClientApplication application = await ApplicationAsync(settings);
        string[] scopes = Scopes(settings);
        IAccount? account = (await application.GetAccountsAsync()).FirstOrDefault();
        if (account is not null)
        {
            try
            {
                AuthenticationResult silent = await application.AcquireTokenSilent(scopes, account).ExecuteAsync(cancellationToken);
                return silent.AccessToken;
            }
            catch (MsalUiRequiredException exception) when (!interactive)
            {
                throw new GraphSignInRequiredException("Sign in to Microsoft Graph again (Settings → Calendar).", exception);
            }
            catch (MsalUiRequiredException)
            {
                // Fall through to an interactive sign-in.
            }
        }
        else if (!interactive)
        {
            throw new GraphSignInRequiredException("Sign in to Microsoft Graph first (Settings → Calendar).");
        }

        return await SignInAsync(application, scopes, settings, deviceCode, cancellationToken);
    }

    /// <summary>
    /// Runs one interactive sign-in. A new attempt cancels one that is still pending (for example, one whose browser tab
    /// showed an error and was closed), and every attempt gives up after a time limit instead of waiting forever.
    /// </summary>
    private async Task<string> SignInAsync(IPublicClientApplication application, string[] scopes, GraphSettings settings,
        Func<string, Task>? deviceCode, CancellationToken cancellationToken)
    {
        using CancellationTokenSource attempt = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        TimeSpan timeout = settings.SignInMode == GraphSignInMode.DeviceCode ? DeviceCodeSignInTimeout : BrowserSignInTimeout;
        attempt.CancelAfter(timeout);
        CancelPendingSignIn(Interlocked.Exchange(ref _pendingSignIn, attempt));

        try
        {
            await _lock.WaitAsync(attempt.Token);
            try
            {
                return await AcquireInteractiveAsync(application, scopes, settings, deviceCode, attempt.Token);
            }
            finally
            {
                _lock.Release();
            }
        }
        catch (Exception exception) when (attempt.IsCancellationRequested && !cancellationToken.IsCancellationRequested
            && exception is OperationCanceledException or MsalClientException { ErrorCode: MsalError.AuthenticationCanceledError })
        {
            throw new GraphSignInRequiredException(
                $"Sign-in didn't finish within {timeout.TotalMinutes:0} minutes, or a newer sign-in replaced it. If the browser showed " +
                "an error (such as AADSTS50105), fix that first, then sign in again.", exception);
        }
        finally
        {
            Interlocked.CompareExchange(ref _pendingSignIn, null, attempt);
        }
    }

    private static void CancelPendingSignIn(CancellationTokenSource? pending)
    {
        try
        {
            pending?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // That attempt finished while this one was starting.
        }
    }

    private static async Task<string> AcquireInteractiveAsync(IPublicClientApplication application, string[] scopes, GraphSettings settings,
        Func<string, Task>? deviceCode, CancellationToken cancellationToken)
    {
        AuthenticationResult result = settings.SignInMode == GraphSignInMode.DeviceCode
            ? await application.AcquireTokenWithDeviceCode(scopes, async code =>
            {
                if (deviceCode is not null)
                {
                    await deviceCode(code.Message);
                }
            }).ExecuteAsync(cancellationToken)
            : await application.AcquireTokenInteractive(scopes)
                .WithUseEmbeddedWebView(false)
                .WithPrompt(Prompt.SelectAccount)
                .WithSystemWebViewOptions(new SystemWebViewOptions
                {
                    HtmlMessageSuccess = "<html><body style='font-family:sans-serif'><h2>Signed in to QuarterHour</h2><p>You can close this tab.</p></body></html>",
                })
                .ExecuteAsync(cancellationToken);
        return result.AccessToken;
    }

    public async Task SignOutAsync(GraphSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.ClientId))
        {
            return;
        }

        IPublicClientApplication application = await ApplicationAsync(settings);
        foreach (IAccount account in await application.GetAccountsAsync())
        {
            await application.RemoveAsync(account);
        }
    }

    private static string[] Scopes(GraphSettings settings)
    {
        string scope = string.IsNullOrWhiteSpace(settings.Scope) ? "Calendars.ReadBasic" : settings.Scope.Trim();
        return [scope.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ? scope : GraphResource + scope];
    }

    private async Task<IPublicClientApplication> ApplicationAsync(GraphSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.ClientId))
        {
            throw new GraphSignInRequiredException("Enter the app registration's client id in Settings → Calendar first.");
        }

        string tenant = string.IsNullOrWhiteSpace(settings.TenantId) ? "organizations" : settings.TenantId.Trim();
        string key = $"{tenant}|{settings.ClientId.Trim()}";
        if (_application is not null && _applicationKey == key)
        {
            return _application;
        }

        IPublicClientApplication application = PublicClientApplicationBuilder
            .Create(settings.ClientId.Trim())
            .WithAuthority(AzureCloudInstance.AzurePublic, tenant)
            .WithRedirectUri("http://localhost")
            .WithClientName("QuarterHour")
            .Build();

        // Windows encrypts the file with DPAPI; macOS and Linux keep the key in the Keychain / keyring, which need naming.
        _cacheHelper ??= await MsalCacheHelper.CreateAsync(
            new StorageCreationPropertiesBuilder(_directory.TokenCacheFileName, _directory.Root)
                .WithMacKeyChain("QuarterHour", "MsalTokenCache")
                .WithLinuxKeyring(
                    "com.quarterhour.tokencache",
                    MsalCacheHelper.LinuxKeyRingDefaultCollection,
                    "QuarterHour Microsoft Graph token cache",
                    new KeyValuePair<string, string>("Version", "1"),
                    new KeyValuePair<string, string>("ProductGroup", "QuarterHour"))
                .Build());
        _cacheHelper.RegisterCache(application.UserTokenCache);
        _application = application;
        _applicationKey = key;
        return application;
    }
}
