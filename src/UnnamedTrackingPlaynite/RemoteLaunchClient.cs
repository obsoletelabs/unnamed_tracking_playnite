using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace UnnamedTrackingPlaynite;

internal sealed class RemoteLaunchClient
{
    internal const string Scope = "games.launch";
    internal const string Base = "/api/playnite/launch/v1";
    private readonly string server;
    private readonly string credential;
    private readonly Func<long> clock;

    internal RemoteLaunchClient(string server, string credential, Func<long>? clock = null)
    {
        this.server = ApiConnection.ValidateBaseUrl(server);
        if (string.IsNullOrWhiteSpace(credential) || !credential.StartsWith("utd_", StringComparison.Ordinal))
            throw new InvalidOperationException("Remote launch requires browser sign-in and explicit permission; API keys cannot receive launches.");
        ApiConnection.ValidateKey(credential);
        this.credential = credential;
        this.clock = clock ?? Stopwatch.GetTimestamp;
    }

    private async Task<string> Send(string path, string method, string? body, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        using (ApiConnection.UseCancellation(timeout.Token))
            return await AccountLink.SendAsync(server, Base + path, method, body, credential, 32768).ConfigureAwait(false);
    }

    internal async Task ConnectAsync(CancellationToken token)
    {
        var contract = AccountLink.Deserialize<LaunchContract>(await Send("/contract", "GET", null, token).ConfigureAwait(false));
        if (contract.Name != "playnite-remote-launch" || contract.Version != 1 || contract.Scope != Scope || contract.Delivery != "at-most-once")
            throw new InvalidDataException("The server uses an unsupported remote launch contract. Update the server or extension.");
        await ConfigureAsync(true, token).ConfigureAwait(false);
    }

    internal Task<string> ConfigureAsync(bool enabled, CancellationToken token)
        => Send("/device", "PUT", enabled ? "{\"enabled\":true}" : "{\"enabled\":false}", token);

    internal async Task<RemoteLaunchDelivery?> ClaimAsync(CancellationToken token)
    {
        var started = clock();
        var response = AccountLink.Deserialize<ClaimResponse>(await Send("/requests/claim", "POST", "{}", token).ConfigureAwait(false));
        if (response.Request == null) return null;
        if (!Guid.TryParseExact(response.Request.Id, "D", out var id) || id == Guid.Empty ||
            !Guid.TryParseExact(response.Request.GameId, "D", out var gameId) || gameId == Guid.Empty ||
            response.ServerTime <= 0 || response.Request.ExpiresAt - response.ServerTime > 60)
            throw new InvalidDataException("The server returned an invalid launch request.");
        return new RemoteLaunchDelivery(id, gameId, response.Request.ExpiresAt - response.ServerTime, started, clock);
    }

    internal Task<string> ReportAsync(Guid requestId, RemoteLaunchOutcome outcome, CancellationToken token)
        => Send("/requests/" + requestId.ToString("D") + "/result", "POST", AccountLink.Serialize(outcome), token);

    [DataContract] private sealed class LaunchContract
    {
        [DataMember(Name = "contract")] public string? Name { get; set; }
        [DataMember(Name = "version")] public int Version { get; set; }
        [DataMember(Name = "scope")] public string? Scope { get; set; }
        [DataMember(Name = "delivery")] public string? Delivery { get; set; }
    }
    [DataContract] private sealed class ClaimResponse
    {
        [DataMember(Name = "request")] public ClaimRequest? Request { get; set; }
        [DataMember(Name = "server_time")] public long ServerTime { get; set; }
    }
    [DataContract] private sealed class ClaimRequest
    {
        [DataMember(Name = "id")] public string? Id { get; set; }
        [DataMember(Name = "playnite_id")] public string? GameId { get; set; }
        [DataMember(Name = "expires_at")] public long ExpiresAt { get; set; }
    }
}

internal sealed class RemoteLaunchDelivery
{
    private readonly double budget;
    private readonly long started;
    private readonly Func<long> clock;
    internal RemoteLaunchDelivery(Guid id, Guid gameId, double budget, long started, Func<long> clock)
    { Id = id; GameId = gameId; this.budget = budget; this.started = started; this.clock = clock; }
    internal Guid Id { get; }
    internal Guid GameId { get; }
    // The whole HTTP round trip counts against the remaining server budget. No wall-clock sync is needed.
    internal bool CanExecute => budget > 0 && (clock() - started) / (double)Stopwatch.Frequency < budget;
}

[DataContract]
internal sealed class RemoteLaunchOutcome
{
    [DataMember(Name = "state")] public string State { get; private set; } = "failed";
    [DataMember(Name = "failure_code", EmitDefaultValue = false)] public string? FailureCode { get; private set; }
    internal static RemoteLaunchOutcome Accepted() => new RemoteLaunchOutcome { State = "accepted" };
    internal static RemoteLaunchOutcome Failed(string code)
    {
        switch (code)
        {
            case "game_not_found": case "not_installed": case "already_running": case "declined": case "launch_failed":
                return new RemoteLaunchOutcome { FailureCode = code };
            default: throw new ArgumentException("Unknown launch failure code.", nameof(code));
        }
    }
}
