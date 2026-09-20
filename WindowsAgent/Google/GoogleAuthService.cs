using Google.Apis.Auth.OAuth2;
using Google.Apis.Calendar.v3;
using Google.Apis.Gmail.v1;
using Google.Apis.Util.Store;
using WindowsAgent.Configuration;

namespace WindowsAgent.Google;

/// <summary>
/// Google has no app-only/service-account path into a personal Gmail account the
/// way Microsoft Graph does for M365 — access has to come from an interactive
/// OAuth consent, once, after which a refresh token (persisted via FileDataStore
/// under %LOCALAPPDATA%\WindowsAgent\google-tokens) lets the app silently refresh
/// access tokens on every subsequent call.
///
/// ConnectAsync launches the user's default browser and a temporary local HTTP
/// listener to catch Google's OAuth redirect (GoogleWebAuthorizationBroker's
/// standard "installed app" flow) — that only works because this app runs on the
/// same desktop the user is sitting at. It would NOT work if this were ever
/// hosted remotely.
/// </summary>
public class GoogleAuthService
{
    // Read-only scopes only: this integration is for looking at mail/calendar to
    // surface things needing follow-up, not for sending or modifying anything.
    private static readonly string[] Scopes = { GmailService.Scope.GmailReadonly, CalendarService.Scope.CalendarReadonly };

    private readonly AppSettingsStore _settings;
    private readonly string _tokenStorePath;

    public GoogleAuthService(AppSettingsStore settings)
    {
        _settings = settings;
        _tokenStorePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WindowsAgent", "google-tokens");
    }

    public bool IsConnected => _settings.Current.Google.Connected;

    /// <summary>Runs the one-time interactive consent flow. Call from a user-initiated
    /// action (the Settings page's "Connect Google Account" button) — never from
    /// inside a tool call, since it opens a browser window and blocks until the user
    /// completes (or abandons) the consent screen.</summary>
    public async Task ConnectAsync(CancellationToken ct)
    {
        await AuthorizeAsync(ct);

        var settings = _settings.Current;
        settings.Google.Connected = true;
        _settings.Save(settings);
    }

    /// <summary>Gets a credential for an already-connected account. Fails fast
    /// without touching Google at all if ConnectAsync has never succeeded, rather
    /// than risk silently popping a browser window mid-tool-call.</summary>
    public async Task<UserCredential> GetCredentialAsync(CancellationToken ct)
    {
        if (!IsConnected)
            throw new InvalidOperationException(
                "Google account not connected. Go to Settings and click \"Connect Google Account\" first.");

        return await AuthorizeAsync(ct);
    }

    private async Task<UserCredential> AuthorizeAsync(CancellationToken ct)
    {
        var settings = _settings.Current.Google;
        var secrets = new ClientSecrets { ClientId = settings.ClientId, ClientSecret = settings.ClientSecret };
        var dataStore = new FileDataStore(_tokenStorePath, fullPath: true);

        // With a token already in dataStore (from a prior ConnectAsync), this loads
        // and silently refreshes it — no browser, no user interaction. Only a
        // missing/invalid stored token triggers the interactive flow.
        return await GoogleWebAuthorizationBroker.AuthorizeAsync(secrets, Scopes, "user", ct, dataStore);
    }
}
