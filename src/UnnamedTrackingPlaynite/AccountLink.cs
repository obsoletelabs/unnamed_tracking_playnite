using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace UnnamedTrackingPlaynite;

// Signs the extension in through the server's user-approved integration access,
// the same consent flow the Discord Activity uses: the extension asks for access,
// the user approves exactly what it may do in their normal browser session, and the
// extension exchanges a one-time verifier for a revocable, scoped credential (utd_).
// No password or API key passes through Playnite.
//
// Protocol: POST /api/delegation/v1/desktop/playnite/requests starts consent and
// returns verification_path; POST /requests/{id}/exchange is polled with the
// verifier; DELETE /credential signs out (revokes the grant).
internal static class AccountLink
{
    internal const string App = "playnite";
    internal const string CredentialPrefix = "utd_";
    // What the extension needs: who you are (for "Connected as"), library sync and saves.
    internal static readonly string[] Scopes = { "profile.read", "library.sync", "saves.read", "saves.write" };
    /// <summary>Optional: adding screenshots, clips and documents. Only requested from servers that offer it,
    /// so an older server's sign-in is never refused for an unknown capability.</summary>
    internal const string ContentScope = "content.write";
    internal const string NotesReadScope = "notes.read";
    internal const string NotesWriteScope = "notes.write";
    private const string Base = "/api/delegation/v1";

    /// <summary>The scopes to request, given the instance description the server returned.</summary>
    internal static List<string> ScopesFor(string? instanceJson, bool remoteLaunch = false)
    {
        var scopes = new List<string>(Scopes);
        var offered = CapabilitiesFor(instanceJson);
        foreach (var capability in new[] { ContentScope, NotesReadScope, NotesWriteScope })
            if (offered.Contains(capability)) scopes.Add(capability);
        if (remoteLaunch)
        {
            if (!offered.Contains(RemoteLaunchClient.Scope)) throw new InvalidOperationException("Update the server before enabling remote launch.");
            scopes.Add(RemoteLaunchClient.Scope);
        }
        return scopes;
    }

    internal static HashSet<string> CapabilitiesFor(string? instanceJson)
    {
        if (string.IsNullOrWhiteSpace(instanceJson)) return new HashSet<string>(StringComparer.Ordinal);
        var instance = Deserialize<InstanceInfo>(instanceJson!);
        return instance.Capabilities == null ? new HashSet<string>(StringComparer.Ordinal)
            : new HashSet<string>(instance.Capabilities.Keys, StringComparer.Ordinal);
    }

    internal static async Task<HashSet<string>> SupportedCapabilitiesAsync(string apiUrl, CancellationToken token)
    {
        using (ApiConnection.UseCancellation(token))
        {
            try { return CapabilitiesFor(await SendAsync(apiUrl, Base + "/instance", "GET", null, null).ConfigureAwait(false)); }
            catch (AccountLinkException ex) when (ex.StatusCode == 404) { return CapabilitiesFor(null); }
        }
    }

    /// <summary>True when the server offers desktop sign-in (older servers need an API key).</summary>
    internal static async Task<bool> SupportsSignInAsync(string apiUrl, CancellationToken cancellationToken)
    {
        using (ApiConnection.UseCancellation(cancellationToken))
        {
            try
            {
                var instance = Deserialize<InstanceInfo>(await SendAsync(apiUrl, Base + "/instance", "GET", null, null).ConfigureAwait(false));
                return instance.DesktopApps != null && instance.DesktopApps.Contains(App);
            }
            catch (AccountLinkException ex) when (ex.StatusCode == 404) { return false; }
        }
    }

    /// <summary>Starts consent. Open <see cref="PendingSignIn.VerificationUrl"/> in the browser.</summary>
    internal static async Task<PendingSignIn> StartAsync(string apiUrl, string deviceName, string deviceId, CancellationToken cancellationToken, bool remoteLaunch = false)
    {
        var baseUrl = ApiConnection.ValidateBaseUrl(apiUrl);
        var verifier = NewVerifier();
        string? instance = null;
        using (ApiConnection.UseCancellation(cancellationToken))
        {
            try { instance = await SendAsync(baseUrl, Base + "/instance", "GET", null, null).ConfigureAwait(false); }
            catch (AccountLinkException ex) when (ex.StatusCode == 404) { }
        }
        var body = Serialize(new DesktopAccessBody
        {
            Scopes = ScopesFor(instance, remoteLaunch),
            CodeChallenge = Challenge(verifier),
            DeviceName = DeviceLabel(deviceName),
            DeviceId = deviceId
        });
        using (ApiConnection.UseCancellation(cancellationToken))
        {
            var started = Deserialize<DesktopAccessStarted>(
                await SendAsync(baseUrl, $"{Base}/desktop/{App}/requests", "POST", body, null).ConfigureAwait(false));
            if (string.IsNullOrEmpty(started.RequestId) || started.VerificationPath == null || !started.VerificationPath.StartsWith("/", StringComparison.Ordinal))
                throw new InvalidDataException("The server returned an unexpected sign-in request.");
            return new PendingSignIn(
                started.RequestId!,
                baseUrl + started.VerificationPath,
                verifier,
                TimeSpan.FromSeconds(Math.Max(2, Math.Min(30, started.PollIntervalSeconds))),
                DateTimeOffset.FromUnixTimeSeconds(started.ExpiresAt));
        }
    }

    /// <summary>Polls until the user approves or denies, or the request expires.</summary>
    internal static async Task<SignInResult> WaitAsync(
        string apiUrl,
        PendingSignIn pending,
        CancellationToken cancellationToken,
        Func<TimeSpan, CancellationToken, Task>? delay = null,
        Func<DateTimeOffset>? clock = null)
    {
        var baseUrl = ApiConnection.ValidateBaseUrl(apiUrl);
        delay ??= Task.Delay;
        clock ??= () => DateTimeOffset.UtcNow;
        var body = Serialize(new ExchangeBody { Verifier = pending.Verifier });
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (clock() >= pending.ExpiresAt) return SignInResult.Expired;
            ExchangeResult result;
            using (ApiConnection.UseCancellation(cancellationToken))
            {
                try
                {
                    result = Deserialize<ExchangeResult>(
                        await SendAsync(baseUrl, $"{Base}/requests/{pending.RequestId}/exchange", "POST", body, null).ConfigureAwait(false));
                }
                catch (AccountLinkException ex) when (ex.StatusCode == 404) { return SignInResult.Expired; }
            }
            switch (result.State)
            {
                case "connected" when !string.IsNullOrEmpty(result.AccessToken) && result.AccessToken!.StartsWith(CredentialPrefix, StringComparison.Ordinal):
                    return SignInResult.Connected(result.AccessToken, result.Scopes ?? new List<string>());
                case "denied":
                    return SignInResult.Denied;
                case "pending":
                    await delay(pending.PollInterval, cancellationToken).ConfigureAwait(false);
                    continue;
                default:
                    throw new InvalidDataException("The server returned an unexpected sign-in state.");
            }
        }
    }

    /// <summary>The signed-in account's display name, shown as "Connected as …".</summary>
    internal static async Task<string> DisplayNameAsync(string apiUrl, string credential, CancellationToken cancellationToken)
    {
        using (ApiConnection.UseCancellation(cancellationToken))
        {
            var profile = Deserialize<ProfileInfo>(await SendAsync(apiUrl, Base + "/profile", "GET", null, credential).ConfigureAwait(false));
            return string.IsNullOrWhiteSpace(profile.DisplayName) ? "your account" : profile.DisplayName!;
        }
    }

    /// <summary>Revokes this app's grant on the server. An already revoked grant is fine.</summary>
    internal static async Task SignOutAsync(string apiUrl, string credential, CancellationToken cancellationToken)
    {
        using (ApiConnection.UseCancellation(cancellationToken))
        {
            try { await SendAsync(apiUrl, Base + "/credential", "DELETE", null, credential).ConfigureAwait(false); }
            catch (AccountLinkException ex) when (ex.StatusCode == 401) { }
        }
    }

    internal static string NewVerifier()
    {
        var bytes = new byte[48];
        using (var random = RandomNumberGenerator.Create()) random.GetBytes(bytes);
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    internal static string Challenge(string verifier)
    {
        using (var sha = SHA256.Create())
        {
            var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(verifier));
            var hex = new StringBuilder(hash.Length * 2);
            foreach (var b in hash) hex.Append(b.ToString("x2"));
            return hex.ToString();
        }
    }

    // The server shows this on the consent page and in Connected apps.
    internal static string DeviceLabel(string? machine)
    {
        var name = string.IsNullOrWhiteSpace(machine) ? "Playnite" : machine!.Trim();
        return name.Length > 100 ? name.Substring(0, 100) : name;
    }

    internal static async Task<string> SendAsync(string apiUrl, string path, string method, string? body, string? credential, int maxCharacters = 1048576)
    {
        var request = ApiConnection.CreateRequest(ApiConnection.ValidateBaseUrl(apiUrl) + path);
        using var registration = ApiConnection.RegisterRequest(request);
        request.Method = method;
        request.Accept = "application/json";
        request.KeepAlive = false;
        if (credential != null) request.Headers[HttpRequestHeader.Authorization] = "Bearer " + credential;
        if (body != null)
        {
            var bytes = Encoding.UTF8.GetBytes(body);
            request.ContentType = "application/json; charset=utf-8";
            request.ContentLength = bytes.Length;
            using (var stream = await request.GetRequestStreamAsync().ConfigureAwait(false))
                await stream.WriteAsync(bytes, 0, bytes.Length, ApiConnection.Token).ConfigureAwait(false);
        }
        try
        {
            using (var response = ApiConnection.EnsureSuccess((HttpWebResponse)await request.GetResponseAsync().ConfigureAwait(false)))
            using (var reader = new StreamReader(response.GetResponseStream() ?? Stream.Null, Encoding.UTF8))
            {
                var text = new StringBuilder();
                var buffer = new char[4096];
                int length;
                while ((length = await reader.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false)) > 0)
                {
                    if (text.Length + length > maxCharacters) throw new InvalidDataException("The server response is too large.");
                    text.Append(buffer, 0, length);
                }
                return text.ToString();
            }
        }
        catch (WebException) when (ApiConnection.Token.IsCancellationRequested) { throw new OperationCanceledException(ApiConnection.Token); }
        catch (WebException ex)
        {
            var status = ex.Response is HttpWebResponse failed ? (int)failed.StatusCode : 0;
            ex.Response?.Dispose();
            throw new AccountLinkException(status, Describe(status), ex);
        }
    }

    private static string Describe(int status) => status switch
    {
        0 => "Could not reach the server. Check the server URL and your connection.",
        401 => "The server no longer accepts this sign-in. Sign in again.",
        403 => "The server owner has turned off desktop app sign-in. Ask them to allow it, or use an API key.",
        404 => "This server does not offer Playnite sign-in yet. Update the server, or use an API key.",
        429 => "Too many sign-in attempts. Wait a minute and try again.",
        _ => "The server returned HTTP " + status + "."
    };

    internal static string Serialize<T>(T value)
    {
        var serializer = new DataContractJsonSerializer(typeof(T));
        using (var stream = new MemoryStream()) { serializer.WriteObject(stream, value); return Encoding.UTF8.GetString(stream.ToArray()); }
    }

    internal static T Deserialize<T>(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) throw new InvalidDataException("The server returned an empty response.");
        var serializer = new DataContractJsonSerializer(typeof(T), new DataContractJsonSerializerSettings { UseSimpleDictionaryFormat = true });
        using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(json))) return (T)serializer.ReadObject(stream)!;
    }

    [DataContract] private sealed class DesktopAccessBody
    {
        [DataMember(Name = "scopes", Order = 0)] public List<string> Scopes { get; set; } = new List<string>();
        [DataMember(Name = "code_challenge", Order = 1)] public string CodeChallenge { get; set; } = string.Empty;
        [DataMember(Name = "device_name", Order = 2)] public string DeviceName { get; set; } = string.Empty;
        [DataMember(Name = "device_id", Order = 3)] public string DeviceId { get; set; } = string.Empty;
    }

    [DataContract] private sealed class DesktopAccessStarted
    {
        [DataMember(Name = "request_id")] public string? RequestId { get; set; }
        [DataMember(Name = "verification_path")] public string? VerificationPath { get; set; }
        [DataMember(Name = "expires_at")] public long ExpiresAt { get; set; }
        [DataMember(Name = "poll_interval_seconds")] public int PollIntervalSeconds { get; set; }
    }

    [DataContract] private sealed class ExchangeBody
    {
        [DataMember(Name = "verifier")] public string Verifier { get; set; } = string.Empty;
    }

    [DataContract] private sealed class ExchangeResult
    {
        [DataMember(Name = "state")] public string? State { get; set; }
        [DataMember(Name = "access_token")] public string? AccessToken { get; set; }
        [DataMember(Name = "scopes")] public List<string>? Scopes { get; set; }
    }

    [DataContract] private sealed class InstanceInfo
    {
        [DataMember(Name = "desktop_apps")] public List<string>? DesktopApps { get; set; }
        [DataMember(Name = "capabilities")] public Dictionary<string, string>? Capabilities { get; set; }
    }

    [DataContract] private sealed class ProfileInfo
    {
        [DataMember(Name = "display_name")] public string? DisplayName { get; set; }
    }
}

internal sealed class PendingSignIn
{
    internal PendingSignIn(string requestId, string verificationUrl, string verifier, TimeSpan pollInterval, DateTimeOffset expiresAt)
    {
        RequestId = requestId;
        VerificationUrl = verificationUrl;
        Verifier = verifier;
        PollInterval = pollInterval;
        ExpiresAt = expiresAt;
    }

    internal string RequestId { get; }
    internal string VerificationUrl { get; }
    internal string Verifier { get; }
    internal TimeSpan PollInterval { get; }
    internal DateTimeOffset ExpiresAt { get; }
}

internal sealed class SignInResult
{
    private SignInResult(string outcome, string? credential, IReadOnlyCollection<string> scopes)
    {
        Outcome = outcome;
        Credential = credential;
        Scopes = scopes;
    }

    internal static readonly SignInResult Denied = new SignInResult("denied", null, Array.Empty<string>());
    internal static readonly SignInResult Expired = new SignInResult("expired", null, Array.Empty<string>());
    internal static SignInResult Connected(string credential, IReadOnlyCollection<string> scopes) => new SignInResult("connected", credential, scopes);

    internal string Outcome { get; }
    internal string? Credential { get; }
    internal IReadOnlyCollection<string> Scopes { get; }
    internal bool IsConnected => Outcome == "connected";
}

internal sealed class AccountLinkException : Exception
{
    internal AccountLinkException(int statusCode, string message, Exception inner) : base(message, inner) { StatusCode = statusCode; }
    internal int StatusCode { get; }
}
