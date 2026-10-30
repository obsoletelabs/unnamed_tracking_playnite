using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Playnite.SDK;

namespace UnnamedTrackingPlaynite;

public sealed partial class UnnamedTrackingPlugin
{
    private readonly SemaphoreSlim remoteLaunchControl = new SemaphoreSlim(1, 1);
    private CancellationTokenSource? remoteLaunchCancellation;
    private int remoteLaunchGeneration;
    private Task remoteLaunchTask = Task.CompletedTask;

    internal Task StopRemoteLaunchAsync() => RestartRemoteLaunchAsync(true);
    internal Task RefreshRemoteLaunchAsync() => RestartRemoteLaunchAsync(false);

    private async Task RestartRemoteLaunchAsync(bool stopOnly)
    {
        await remoteLaunchControl.WaitAsync().ConfigureAwait(true);
        try
        {
            var generation = ++remoteLaunchGeneration;
            remoteLaunchCancellation?.Cancel();
            try { await remoteLaunchTask.ConfigureAwait(true); }
            catch (Exception ex) { _logger.Warn("Remote launch stopped: " + ex.GetType().Name); }
            remoteLaunchCancellation?.Dispose();
            remoteLaunchCancellation = null;
            if (stopOnly || lifetime.IsCancellationRequested || !Settings.RemoteLaunchEnabled) return;
            if (!Settings.IsSignedIn || !Settings.HasScope(RemoteLaunchClient.Scope))
            { Settings.RemoteLaunchStatus = "Sign in and grant remote launch permission to receive requests."; return; }
            var server = ApiConnection.ValidateBaseUrl(Settings.ApiUrl);
            var credential = Settings.Credential;
            var directory = System.IO.Path.Combine(GetPluginUserDataPath(), "remote-launch", AccountLink.Challenge(server));
            remoteLaunchCancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            var token = remoteLaunchCancellation.Token;
            Settings.RemoteLaunchStatus = "Connecting remote launch…";
            remoteLaunchTask = Task.Run(async () =>
            {
                try
                {
                    using var journal = new RemoteLaunchJournal(directory);
                    var client = new RemoteLaunchClient(server, credential);
                    var receiver = new RemoteLaunchReceiver(client, journal,
                        (delivery, cancellation) => StartRemoteGameAsync(delivery, server, credential, cancellation),
                        text => RemoteLaunchStatus(text, generation));
                    await receiver.RunAsync(token).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.Error("Remote launch receiver stopped: " + ex.GetType().Name);
                    RemoteLaunchStatus("Receiving stopped. Launch history or the connection could not be opened safely. Check the extension log.", generation);
                }
            });
        }
        finally { remoteLaunchControl.Release(); }
    }

    private void RemoteLaunchStatus(string text, int generation)
    {
        if (lifetime.IsCancellationRequested) return;
        PlayniteApi.MainView.UIDispatcher.BeginInvoke(new Action(() =>
        { if (generation == remoteLaunchGeneration) Settings.RemoteLaunchStatus = text; }));
    }

    private async Task<RemoteLaunchOutcome> StartRemoteGameAsync(
        RemoteLaunchDelivery delivery, string server, string credential, CancellationToken token)
    {
        // Database access, confirmation and StartGame all stay on Playnite's UI dispatcher.
        return await PlayniteApi.MainView.UIDispatcher.InvokeAsync(() =>
        {
            bool Allowed() => !token.IsCancellationRequested && delivery.CanExecute &&
                Settings.RemoteLaunchEnabled && Settings.IsSignedIn && Settings.HasScope(RemoteLaunchClient.Scope) &&
                ApiConnection.ValidateBaseUrl(Settings.ApiUrl) == server && Settings.Credential == credential;
            if (!Allowed()) return RemoteLaunchOutcome.Failed("declined");
            var game = PlayniteApi.Database.Games.Get(delivery.GameId);
            if (game == null) return RemoteLaunchOutcome.Failed("game_not_found");
            if (UnnamedTrackingSyncClient.HasIgnoreTag(game, Settings.IgnoreTag)) return RemoteLaunchOutcome.Failed("declined");
            if (game.IsRunning) return RemoteLaunchOutcome.Failed("already_running");
            if (!game.IsInstalled) return RemoteLaunchOutcome.Failed("not_installed");
            if (Settings.ConfirmRemoteLaunch && PlayniteApi.Dialogs.ShowMessage(
                "Launch '" + game.Name + "' on this computer? This request came from your Unnamed Tracking account.",
                "Remote game launch", MessageBoxButton.YesNo) != MessageBoxResult.Yes)
                return RemoteLaunchOutcome.Failed("declined");
            // Confirmation can remain open beyond the delivery deadline or while access changes.
            if (!Allowed()) return RemoteLaunchOutcome.Failed("declined");
            if (game.IsRunning) return RemoteLaunchOutcome.Failed("already_running");
            try
            {
                PlayniteApi.Notifications.Remove("unnamed-tracking-remote-launch");
                PlayniteApi.Notifications.Add(new NotificationMessage("unnamed-tracking-remote-launch",
                    "Remote launch requested for '" + game.Name + "'.", NotificationType.Info));
                PlayniteApi.StartGame(game.Id);
                return RemoteLaunchOutcome.Accepted();
            }
            catch (Exception ex)
            {
                _logger.Error("Playnite remote launch failed: " + ex.GetType().Name);
                return RemoteLaunchOutcome.Failed("launch_failed");
            }
        });
    }
}
