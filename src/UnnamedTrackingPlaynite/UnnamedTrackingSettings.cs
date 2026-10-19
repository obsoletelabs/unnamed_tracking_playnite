using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Playnite.SDK;
using Playnite.SDK.Data;

namespace UnnamedTrackingPlaynite;

public sealed class UnnamedTrackingSettings : ObservableObject, ISettings
{
    private readonly UnnamedTrackingPlugin? plugin;
    private string apiUrl = string.Empty;
    private string authValue = string.Empty;
    private string protectedCredential = string.Empty;
    private string signedInServer = string.Empty;
    private string connectedAs = string.Empty;
    private string grantedScopes = string.Empty;
    private string deviceId = string.Empty;
    private string editingApiUrl = string.Empty;
    private string editingAuthValue = string.Empty;
    private string ignoreTag = "trackingapp_ignore";
    private string editingIgnoreTag = "trackingapp_ignore";
    private bool syncOnStartup;
    private bool syncOnGameStopped;
    private bool editingSyncOnStartup;
    private bool editingSyncOnGameStopped;

    public string ApiUrl
    {
        get => apiUrl;
        set
        {
            SetValue(ref apiUrl, value ?? string.Empty);
            OnPropertyChanged(nameof(IsSignedIn));
            OnPropertyChanged(nameof(AccountSummary));
        }
    }

    /// <summary>Legacy user API key (utk_) for servers without Playnite sign-in.</summary>
    public string AuthValue
    {
        get => authValue;
        set => SetValue(ref authValue, value ?? string.Empty);
    }

    /// <summary>The capabilities the user approved at sign-in, space separated. An API key has no such list.</summary>
    public string GrantedScopes
    {
        get => grantedScopes;
        set => SetValue(ref grantedScopes, value ?? string.Empty);
    }

    /// <summary>True when this sign-in may use the capability. A legacy API key is not limited by capability.</summary>
    public bool HasScope(string scope) =>
        !IsSignedIn || GrantedScopes.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Contains(scope, StringComparer.Ordinal);

    /// <summary>The sign-in credential (utd_), DPAPI-encrypted for this Windows user.</summary>
    public string ProtectedCredential
    {
        get => protectedCredential;
        set
        {
            SetValue(ref protectedCredential, value ?? string.Empty);
            OnPropertyChanged(nameof(IsSignedIn));
            OnPropertyChanged(nameof(AccountSummary));
        }
    }

    /// <summary>The server the sign-in belongs to; a different server URL needs a new sign-in.</summary>
    public string SignedInServer
    {
        get => signedInServer;
        set => SetValue(ref signedInServer, value ?? string.Empty);
    }

    public string ConnectedAs
    {
        get => connectedAs;
        set
        {
            SetValue(ref connectedAs, value ?? string.Empty);
            OnPropertyChanged(nameof(AccountSummary));
        }
    }

    /// <summary>A stable random ID for this Playnite install, shown in Connected apps.</summary>
    public string DeviceId
    {
        get => deviceId;
        set => SetValue(ref deviceId, value ?? string.Empty);
    }

    public string IgnoreTag
    {
        get => ignoreTag;
        set => SetValue(ref ignoreTag, value ?? string.Empty);
    }

    public bool SyncOnStartup
    {
        get => syncOnStartup;
        set => SetValue(ref syncOnStartup, value);
    }

    public bool SyncOnGameStopped
    {
        get => syncOnGameStopped;
        set => SetValue(ref syncOnGameStopped, value);
    }

    /// <summary>Signed in through the browser, for the server currently configured.</summary>
    [DontSerialize]
    public bool IsSignedIn =>
        !string.IsNullOrEmpty(ProtectedCredential) &&
        string.Equals(SignedInServer, NormalizedUrl(ApiUrl), StringComparison.OrdinalIgnoreCase);

    [DontSerialize]
    public string AccountSummary => IsSignedIn
        ? $"Connected as {(string.IsNullOrWhiteSpace(ConnectedAs) ? "your account" : ConnectedAs)}."
        : "Not signed in.";

    /// <summary>What every request authenticates with: the sign-in, else a legacy API key.</summary>
    [DontSerialize]
    public string Credential => (IsSignedIn ? CredentialProtection.Unprotect(ProtectedCredential) : null) ?? AuthValue;

    public UnnamedTrackingSettings()
    {
    }

    public UnnamedTrackingSettings(UnnamedTrackingPlugin plugin)
    {
        this.plugin = plugin;
        try
        {
            var savedSettings = plugin.LoadPluginSettings<UnnamedTrackingSettings>();
            if (savedSettings != null)
            {
                ApiUrl = savedSettings.ApiUrl;
                AuthValue = savedSettings.AuthValue;
                ProtectedCredential = savedSettings.ProtectedCredential;
                SignedInServer = savedSettings.SignedInServer;
                ConnectedAs = savedSettings.ConnectedAs;
                GrantedScopes = savedSettings.GrantedScopes;
                DeviceId = savedSettings.DeviceId;
                IgnoreTag = savedSettings.IgnoreTag ?? "trackingapp_ignore";
                SyncOnStartup = savedSettings.SyncOnStartup;
                SyncOnGameStopped = savedSettings.SyncOnGameStopped;
            }
        }
        catch (Exception ex)
        {
            // A corrupt/stale Playnite settings record must never prevent the entire
            // extension from loading. Start from safe defaults and let the user reconfigure.
            LogManager.GetLogger().Error($"Could not load Unnamed Tracking plugin settings; using defaults: {ex}");
        }
        if (string.IsNullOrWhiteSpace(DeviceId)) DeviceId = "playnite-" + Guid.NewGuid().ToString("N");
    }

    // Sign-in and sign-out are saved when they happen, so cancelling the settings
    // dialog never loses (or resurrects) a credential; only the editable fields revert.
    public void BeginEdit()
    {
        editingApiUrl = ApiUrl;
        editingAuthValue = AuthValue;
        editingIgnoreTag = IgnoreTag;
        editingSyncOnStartup = SyncOnStartup;
        editingSyncOnGameStopped = SyncOnGameStopped;
    }

    public void CancelEdit()
    {
        ApiUrl = editingApiUrl;
        AuthValue = editingAuthValue;
        IgnoreTag = editingIgnoreTag;
        SyncOnStartup = editingSyncOnStartup;
        SyncOnGameStopped = editingSyncOnGameStopped;
    }

    public void EndEdit()
    {
        ApiUrl = ApiUrl.Trim().TrimEnd('/');
        AuthValue = AuthValue.Trim();
        IgnoreTag = IgnoreTag.Trim();
        plugin?.SavePluginSettings(this);
    }

    /// <summary>
    /// Signs in through the browser. <paramref name="status"/> receives progress text
    /// (including the address to open if the browser did not start).
    /// </summary>
    public async Task<bool> SignInAsync(Action<string> status, CancellationToken cancellationToken)
    {
        var server = ApiConnection.ValidateBaseUrl(ApiUrl);
        status("Checking the server...");
        if (!await AccountLink.SupportsSignInAsync(server, cancellationToken).ConfigureAwait(true))
            throw new InvalidOperationException("This server does not offer Playnite sign-in yet. Update the server, or use an API key below.");
        var pending = await AccountLink.StartAsync(server, Environment.MachineName, DeviceId, cancellationToken).ConfigureAwait(true);
        // The page address is not secret (the verifier never leaves Playnite); log it so
        // people without a default browser can still finish signing in.
        LogManager.GetLogger().Info("Opening the Unnamed Tracking sign-in page: " + pending.VerificationUrl);
        OpenInBrowser(pending.VerificationUrl);
        status("Approve the sign-in in your browser. If it did not open, visit: " + pending.VerificationUrl);
        var result = await AccountLink.WaitAsync(server, pending, cancellationToken).ConfigureAwait(true);
        if (!result.IsConnected)
        {
            status(result.Outcome == "denied" ? "Sign-in was denied in the browser." : "The sign-in request expired. Try again.");
            return false;
        }
        ProtectedCredential = CredentialProtection.Protect(result.Credential!);
        GrantedScopes = string.Join(" ", result.Scopes);
        SignedInServer = server;
        try { ConnectedAs = await AccountLink.DisplayNameAsync(server, result.Credential!, cancellationToken).ConfigureAwait(true); }
        catch (Exception) { ConnectedAs = string.Empty; } // profile.read may have been unticked
        plugin?.SavePluginSettings(this);
        status(AccountSummary);
        return true;
    }

    /// <summary>Signs out here and revokes this Playnite's access on the server.</summary>
    public async Task SignOutAsync(CancellationToken cancellationToken)
    {
        var credential = CredentialProtection.Unprotect(ProtectedCredential);
        var server = SignedInServer;
        ProtectedCredential = string.Empty;
        SignedInServer = string.Empty;
        ConnectedAs = string.Empty;
        GrantedScopes = string.Empty;
        plugin?.SavePluginSettings(this);
        if (credential != null && !string.IsNullOrEmpty(server))
            await AccountLink.SignOutAsync(server, credential, cancellationToken).ConfigureAwait(true);
    }

    private static void OpenInBrowser(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception ex) { LogManager.GetLogger().Warn($"Could not open the browser for sign-in: {ex.Message}"); }
    }

    private static string NormalizedUrl(string value)
    {
        try { return ApiConnection.ValidateBaseUrl(value); }
        catch (InvalidOperationException) { return string.Empty; }
    }

    public Task<bool> TestConnectionAsync()
    {
        if (plugin == null)
        {
            throw new InvalidOperationException("The plugin is not initialized.");
        }

        return plugin.TestConnectionAsync();
    }

    public Task<UnnamedTrackingUploadResult> UploadLibraryAsync(Action<int, int, string>? progress = null)
    {
        if (plugin == null)
        {
            throw new InvalidOperationException("The plugin is not initialized.");
        }

        return plugin.UploadLibraryAsync(progress);
    }

    public void CancelSync()
    {
        plugin?.CancelSync();
    }

    public Task<UnnamedTrackingSyncPreviewResult> PreviewLibraryAsync()
    {
        if (plugin == null) throw new InvalidOperationException("The plugin is not initialized.");
        return plugin.PreviewLibraryAsync();
    }

    public bool VerifySettings(out List<string> errors)
    {
        errors = new List<string>();

        if (!string.IsNullOrWhiteSpace(ApiUrl))
        {
            try { ApiConnection.ValidateBaseUrl(ApiUrl); }
            catch (InvalidOperationException ex) { errors.Add(ex.Message); }
        }

        if (!string.IsNullOrWhiteSpace(AuthValue))
        {
            try { ApiConnection.ValidateKey(AuthValue); }
            catch (InvalidOperationException ex) { errors.Add(ex.Message); }
        }

        if ((SyncOnStartup || SyncOnGameStopped) &&
            (string.IsNullOrWhiteSpace(ApiUrl) || string.IsNullOrWhiteSpace(Credential)))
        {
            errors.Add("Sign in (or enter an API key) before turning on automatic synchronization.");
        }

        return errors.Count == 0;
    }
}
