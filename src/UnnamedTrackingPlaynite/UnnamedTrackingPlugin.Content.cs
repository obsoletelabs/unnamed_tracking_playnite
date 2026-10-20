using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Playnite.SDK;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;

namespace UnnamedTrackingPlaynite;

// Everyday edits from Playnite without opening the website. Status, score and notes are Playnite's own
// fields: they are changed here and then sent through normal library sync. The dedicated Playnite
// note synchronizes in both directions and preserves conflicts. Screenshots, clips and documents use the host's
// upload routes and need the "add screenshots, clips and documents" capability.
public sealed partial class UnnamedTrackingPlugin
{
    private const string TrackingMenu = "Unnamed Tracking";
    private const string StatusMenu = "Unnamed Tracking|Set status";
    private readonly SemaphoreSlim contentGate = new SemaphoreSlim(1, 1);
    private readonly SemaphoreSlim accessGate = new SemaphoreSlim(1, 1);

    private async Task<bool> EnsureCapabilitiesAsync(string purpose, params string[] required)
    {
        if (!RequireSignIn()) return false;
        if (required.All(Settings.HasScope)) return true;
        if (!await accessGate.WaitAsync(0).ConfigureAwait(true))
        {
            PlayniteApi.Dialogs.ShowMessage("A connection update is already running. Finish it in your browser first.", "Unnamed Tracking");
            return false;
        }
        try
        {
            var offered = await AccountLink.SupportedCapabilitiesAsync(Settings.ApiUrl, lifetime.Token).ConfigureAwait(true);
            if (required.Any(scope => !offered.Contains(scope)))
            {
                PlayniteApi.Dialogs.ShowErrorMessage("This server does not yet support " + purpose +
                    ". Update the Unnamed Tracking server, then review app permissions in the extension settings.", "Unnamed Tracking");
                return false;
            }
            var answer = PlayniteApi.Dialogs.ShowMessage("This connection has not been allowed to " + purpose +
                ". Review updated permissions in your browser now? Your current connection stays available if you cancel.",
                "Update Unnamed Tracking access", MessageBoxButton.YesNo);
            if (answer != MessageBoxResult.Yes) return false;
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            cancellation.CancelAfter(TimeSpan.FromMinutes(11));
            var connected = await Settings.SignInAsync(text => NotifyEdit(text), cancellation.Token).ConfigureAwait(true);
            if (!connected || !required.All(Settings.HasScope))
            {
                NotifyEdit("The requested access was not approved. Your local files and notes were kept.", true);
                return false;
            }
            return true;
        }
        catch (OperationCanceledException) { NotifyEdit("Access review cancelled. Your local data was kept."); return false; }
        catch (Exception ex)
        {
            PlayniteApi.Dialogs.ShowErrorMessage("Could not check or update app access: " + ex.Message +
                " Your current connection and local data were kept.", "Unnamed Tracking");
            return false;
        }
        finally { accessGate.Release(); }
    }

    private IEnumerable<GameMenuItem> GetContentMenuItems(IReadOnlyList<Game> games)
    {
        if (games.Count == 0) yield break;
        var statuses = PlayniteApi.Database.CompletionStatuses.OrderBy(status => status.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        foreach (var status in statuses)
        {
            var chosen = status;
            yield return new GameMenuItem
            {
                MenuSection = StatusMenu,
                Description = chosen.Name,
                Action = menuArgs => { _ = SetStatusAsync(menuArgs.Games.ToList(), chosen); }
            };
        }
        if (games.Count != 1) yield break;
        var game = games[0];
        yield return new GameMenuItem
        {
            MenuSection = TrackingMenu,
            Description = "Rate…",
            Action = menuArgs => { _ = RateAsync(game); }
        };
        yield return new GameMenuItem
        {
            MenuSection = TrackingMenu,
            Description = "Add note…",
            Action = menuArgs => { _ = AddNoteAsync(game); }
        };
        yield return new GameMenuItem
        {
            MenuSection = TrackingMenu,
            Description = "Add screenshots, clips or audio…",
            Action = menuArgs => { _ = AddContentAsync(game, GameContentKind.Media); }
        };
        yield return new GameMenuItem
        {
            MenuSection = TrackingMenu,
            Description = "Add document…",
            Action = menuArgs => { _ = AddContentAsync(game, GameContentKind.Document); }
        };
    }

    private bool RequireSignIn()
    {
        if (HasCredentials) return true;
        PlayniteApi.Dialogs.ShowErrorMessage("Sign in to Unnamed Tracking in Add-ons > Extension settings first.", "Unnamed Tracking");
        return false;
    }

    /// <summary>A quick-edit message under its own notification ID, so it never replaces a sync's messages.</summary>
    private void NotifyEdit(string text, bool error = false)
    {
        if (lifetime.IsCancellationRequested) return;
        PlayniteApi.MainView.UIDispatcher.BeginInvoke(new Action(() =>
        {
            if (lifetime.IsCancellationRequested) return;
            // Playnite keeps the first message added under an ID, so replace the previous one.
            PlayniteApi.Notifications.Remove("unnamed-tracking-edit");
            PlayniteApi.Notifications.Add(new NotificationMessage("unnamed-tracking-edit", text, error ? NotificationType.Error : NotificationType.Info));
        }));
    }

    /// <summary>Pushes the changed games right away and reports the outcome as a notification, not a dialog.</summary>
    private async Task PushChangesAsync(IReadOnlyCollection<Game> games, string what)
    {
        try
        {
            // The startup or a manual sync may be running; it captured the games before this edit, so wait
            // for it to finish and then send the change instead of refusing.
            if (syncCancellation != null)
            {
                _logger.Info("Quick edit: waiting for the running synchronization before syncing: " + what);
                NotifyEdit($"{what}. Waiting for the running synchronization to finish before syncing it…");
                var deadline = DateTime.UtcNow.AddMinutes(10);
                while (syncCancellation != null && DateTime.UtcNow < deadline)
                    await Task.Delay(500, lifetime.Token).ConfigureAwait(true);
            }
            _logger.Info("Quick edit: syncing " + games.Count + " game(s): " + what);
            var result = await UploadGamesAsync(games).ConfigureAwait(true);
            _logger.Info($"Quick edit: synced, {result.SucceededGames} succeeded, {result.FailedGames} failed.");
            if (result.FailedGames > 0)
                NotifyEdit($"{what}, but the sync to Unnamed Tracking failed for {result.FailedGames} game(s). Run a sync again later.", true);
            else if (result.WarningCount > 0)
                NotifyEdit($"{what}. Game metadata synced, but {result.WarningCount} item(s) need attention: " +
                    string.Join("; ", result.Warnings.Select(warning => warning.Operation + ": " + warning.ResponseBody)), true);
            else
                NotifyEdit($"{what} and synced to Unnamed Tracking.");
        }
        catch (OperationCanceledException) { NotifyEdit($"{what}. The sync to Unnamed Tracking was cancelled."); }
        catch (Exception ex)
        {
            _logger.Error("Could not sync a quick edit: " + ex);
            NotifyEdit($"{what}, but it could not be synced to Unnamed Tracking: {ex.Message}", true);
        }
    }

    private async Task SetStatusAsync(IReadOnlyList<Game> games, CompletionStatus status)
    {
        if (games.Count == 0 || !RequireSignIn()) return;
        foreach (var game in games) game.CompletionStatusId = status.Id;
        PlayniteApi.Database.Games.Update(games);
        var server = UnnamedTrackingSyncClient.NormalizeStatus(status.Name, 0);
        await PushChangesAsync(games, games.Count == 1
            ? $"'{games[0].Name}' set to {status.Name} ({server})"
            : $"{games.Count} games set to {status.Name} ({server})").ConfigureAwait(true);
    }

    private async Task RateAsync(Game game)
    {
        if (!RequireSignIn()) return;
        var current = game.UserScore.HasValue ? (game.UserScore.Value / 10m).ToString("0.#", CultureInfo.InvariantCulture) : string.Empty;
        var answer = PlayniteApi.Dialogs.SelectString("Your score from 0 to 10. Leave empty to clear it.", "Rate " + game.Name, current);
        if (answer == null || !answer.Result) return;
        if (!QuickEdits.TryParseScore(answer.SelectedString, out var score))
        {
            PlayniteApi.Dialogs.ShowErrorMessage("Enter a number from 0 to 10, for example 8 or 7.5.", "Unnamed Tracking");
            return;
        }
        game.UserScore = score;
        PlayniteApi.Database.Games.Update(game);
        await PushChangesAsync(new[] { game }, score.HasValue
            ? $"'{game.Name}' rated {(score.Value / 10m).ToString("0.#", CultureInfo.InvariantCulture)}/10"
            : $"The score of '{game.Name}' was cleared").ConfigureAwait(true);
    }

    private async Task AddNoteAsync(Game game)
    {
        if (!await EnsureCapabilitiesAsync("read and update the shared Playnite note",
                AccountLink.NotesReadScope, AccountLink.NotesWriteScope).ConfigureAwait(true)) return;
        var answer = PlayniteApi.Dialogs.SelectString("Add a dated line to the notes of " + game.Name + ".", "Add note", string.Empty);
        if (answer == null || !answer.Result || string.IsNullOrWhiteSpace(answer.SelectedString)) return;
        game.Notes = QuickEdits.AppendNote(game.Notes, answer.SelectedString, DateTime.Now);
        PlayniteApi.Database.Games.Update(game);
        await PushChangesAsync(new[] { game }, $"Note added to '{game.Name}'").ConfigureAwait(true);
    }

    private async Task AddContentAsync(Game game, GameContentKind kind)
    {
        if (!await EnsureCapabilitiesAsync("add screenshots, clips and documents", AccountLink.ContentScope).ConfigureAwait(true)) return;
        if (UnnamedTrackingSyncClient.HasIgnoreTag(game, Settings.IgnoreTag))
        {
            PlayniteApi.Dialogs.ShowMessage("This game has the configured ignore tag. Remove it before adding files.", "Unnamed Tracking");
            return;
        }
        var paths = PlayniteApi.Dialogs.SelectFiles(kind == GameContentKind.Media ? GameContentUploader.MediaFilter : GameContentUploader.DocumentFilter);
        if (paths == null || paths.Count == 0) return;
        _logger.Info($"Content upload: {paths.Count} {(kind == GameContentKind.Media ? "media" : "document")} file(s) for '{game.Name}'.");
        if (!await contentGate.WaitAsync(0).ConfigureAwait(true))
        {
            PlayniteApi.Dialogs.ShowMessage("Another upload is still running. Wait for it to finish.", "Unnamed Tracking");
            return;
        }
        try
        {
            var remoteId = await _saveSync.ResolveRemoteGameIdAsync(Settings.ApiUrl, Settings.Credential, game.Id).ConfigureAwait(true);
            if (remoteId == Guid.Empty)
            {
                if (!ConfirmSyncGame(game)) return;
                var synced = await UploadGamesAsync(new[] { game }).ConfigureAwait(true);
                if (synced.SucceededGames != 1) throw new InvalidOperationException("Could not sync the game to Unnamed Tracking first.");
                remoteId = await _saveSync.ResolveRemoteGameIdAsync(Settings.ApiUrl, Settings.Credential, game.Id).ConfigureAwait(true);
                if (remoteId == Guid.Empty) throw new InvalidOperationException("The game is still not linked after syncing it.");
            }
            var result = await GameContentUploader.UploadAsync(
                Settings.ApiUrl, Settings.Credential, remoteId, kind, paths,
                (done, total, name) => NotifyEdit($"Uploading {done}/{total}: {name}"), lifetime.Token).ConfigureAwait(true);
            var summary = $"'{game.Name}': {result.Summary()}";
            _logger.Info("Content upload finished: " + summary);
            if (result.HasProblems) PlayniteApi.Dialogs.ShowErrorMessage(summary, "Unnamed Tracking");
            else NotifyEdit(summary);
        }
        catch (OperationCanceledException) { NotifyEdit("Upload cancelled."); }
        catch (Exception ex)
        {
            _logger.Error("Could not upload content for '" + game.Name + "': " + ex);
            PlayniteApi.Dialogs.ShowErrorMessage(ex.Message, "Unnamed Tracking");
        }
        finally { contentGate.Release(); }
    }
}
