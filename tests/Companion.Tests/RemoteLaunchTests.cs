using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnnamedTrackingPlaynite;

internal static partial class Program
{
    private const string LaunchContractJson = "{\"contract\":\"playnite-remote-launch\",\"version\":1,\"scope\":\"games.launch\",\"delivery\":\"at-most-once\"}";
    private static string DeliveryJson(Guid id, Guid gameId, long now) =>
        "{\"request\":{\"id\":\"" + id + "\",\"playnite_id\":\"" + gameId + "\",\"expires_at\":" + (now + 60) + "},\"server_time\":" + now + "}";

    private static async Task RemoteLaunchHttp()
    {
        Check(ApiConnection.CreateRequest("https://example.test").Headers["X-Playnite-Extension-Version"] == typeof(ApiConnection).Assembly.GetName().Version?.ToString(3), "Requests report the built extension version without requiring a new host contract");
        var id = Guid.NewGuid(); var gameId = Guid.NewGuid(); var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var offered = "{\"capabilities\":{\"games.launch\":\"Receive\"}}";
        Check(!AccountLink.ScopesFor(offered).Contains("games.launch"), "Remote launch is not requested by default");
        Check(AccountLink.ScopesFor(offered, true).Contains("games.launch"), "Explicit opt-in requests launch permission");
        Throws<InvalidOperationException>(() => AccountLink.ScopesFor("{}", true));
        Throws<InvalidOperationException>(() => new RemoteLaunchClient("https://example.test", "utk_legacy"));
        Throws<ArgumentException>(() => RemoteLaunchOutcome.Failed("raw exception"));
        using (var temp = new TemporaryDirectory())
        using (var server = new Server(request => request.Url!.AbsolutePath.EndsWith("/contract", StringComparison.Ordinal)
            ? LaunchContractJson : request.Url.AbsolutePath.EndsWith("/claim", StringComparison.Ordinal) ? DeliveryJson(id, gameId, now) : "{}"))
        {
            var calls = 0; var client = new RemoteLaunchClient(server.Url, "utd_remote");
            using (var journal = new RemoteLaunchJournal(temp.Path))
            {
                await client.ConnectAsync(CancellationToken.None);
                var receiver = new RemoteLaunchReceiver(client, journal, (delivery, token) =>
                {
                    Check(delivery.Id == id && delivery.GameId == gameId && delivery.CanExecute, "Only validated IDs reach the dispatcher");
                    Check(File.ReadAllText(Path.Combine(temp.Path, "remote-launch.json")).Contains(id.ToString()), "Receipt reaches disk before the SDK callback");
                    calls++; return Task.FromResult(RemoteLaunchOutcome.Accepted());
                }, _ => { });
                await receiver.PollOnceAsync(CancellationToken.None);
                await receiver.PollOnceAsync(CancellationToken.None);
                Check(calls == 1, "Repeated delivery is not executed twice");
                Throws<IOException>(() => { using var duplicate = new RemoteLaunchJournal(temp.Path); });
            }
            using (var journal = new RemoteLaunchJournal(temp.Path))
            {
                var receiver = new RemoteLaunchReceiver(client, journal, (_, _) =>
                { calls++; return Task.FromResult(RemoteLaunchOutcome.Accepted()); }, _ => { });
                await receiver.PollOnceAsync(CancellationToken.None);
                Check(calls == 1, "Receipt survives a receiver restart");
            }
            var report = server.Requests.Single(request => request.Path.EndsWith("/result", StringComparison.Ordinal));
            Check(report.Body.Contains("accepted") && !report.Body.Contains("command"), "Result contains no command payload");
            Check(server.Requests.All(request => request.Authorization == "Bearer utd_remote"), "Receiver authenticates all requests");
        }
        using (var temp = new TemporaryDirectory())
        {
            var reports = 0; var calls = 0;
            using var server = new Server(request => request.Url!.AbsolutePath.EndsWith("/claim", StringComparison.Ordinal) ? DeliveryJson(id, gameId, now) : "{}",
                statusFor: request => request.Url!.AbsolutePath.EndsWith("/result", StringComparison.Ordinal) && ++reports == 1 ? 500 : 200);
            var client = new RemoteLaunchClient(server.Url, "utd_remote");
            using (var journal = new RemoteLaunchJournal(temp.Path))
            {
                var receiver = new RemoteLaunchReceiver(client, journal, (_, _) =>
                { calls++; return Task.FromResult(RemoteLaunchOutcome.Accepted()); }, _ => { });
                await ThrowsAsync<AccountLinkException>(() => receiver.PollOnceAsync(CancellationToken.None));
                Check(journal.Pending.Count() == 1, "Failed acknowledgement is durable");
            }
            using (var journal = new RemoteLaunchJournal(temp.Path))
            {
                var receiver = new RemoteLaunchReceiver(client, journal, (_, _) =>
                { calls++; return Task.FromResult(RemoteLaunchOutcome.Accepted()); }, _ => { });
                await receiver.PollOnceAsync(CancellationToken.None);
                Check(calls == 1 && !journal.Pending.Any() && reports == 2, "Acknowledgement retry never repeats the launch");
            }
        }
        using (var temp = new TemporaryDirectory())
        {
            long ticks = 0;
            using var server = new Server(_ => { ticks = 61 * Stopwatch.Frequency; return DeliveryJson(id, gameId, now); });
            using var journal = new RemoteLaunchJournal(temp.Path);
            var calls = 0;
            var receiver = new RemoteLaunchReceiver(new RemoteLaunchClient(server.Url, "utd_remote", () => ticks), journal,
                (_, _) => { calls++; return Task.FromResult(RemoteLaunchOutcome.Accepted()); }, _ => { });
            await receiver.PollOnceAsync(CancellationToken.None);
            Check(calls == 0, "HTTP round trip counts against the execution deadline");
        }
        using (var server = new Server(_ => DeliveryJson(id, gameId, now).Replace(gameId.ToString(), "C:/game.exe")))
            await ThrowsAsync<InvalidDataException>(() => new RemoteLaunchClient(server.Url, "utd_remote").ClaimAsync(CancellationToken.None));
        using (var server = new Server(_ => LaunchContractJson.Replace("at-most-once", "retry-forever")))
        {
            await ThrowsAsync<InvalidDataException>(() => new RemoteLaunchClient(server.Url, "utd_remote").ConnectAsync(CancellationToken.None));
            Check(server.Requests.Count == 1, "Unsupported contract never enables receiving");
        }
        using (var temp = new TemporaryDirectory())
        {
            File.WriteAllText(Path.Combine(temp.Path, "remote-launch.json"), "{}");
            Throws<InvalidDataException>(() => { using var journal = new RemoteLaunchJournal(temp.Path); });
            Check(File.ReadAllText(Path.Combine(temp.Path, "remote-launch.json")) == "{}", "Corrupt history is retained and stops receiving");
        }
        using (var temp = new TemporaryDirectory())
        using (var server = new Server(_ => "{}", 401))
        using (var journal = new RemoteLaunchJournal(temp.Path))
        {
            string status = "";
            var receiver = new RemoteLaunchReceiver(new RemoteLaunchClient(server.Url, "utd_revoked"), journal,
                (_, _) => throw new Exception("Revoked request reached SDK"), message => status = message);
            await receiver.RunAsync(CancellationToken.None);
            Check(status.Contains("permissions"), "Revocation stops the loop with actionable status");
        }
    }
}
