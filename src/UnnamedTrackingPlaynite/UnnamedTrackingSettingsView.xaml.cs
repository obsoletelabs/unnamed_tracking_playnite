using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace UnnamedTrackingPlaynite;

public partial class UnnamedTrackingSettingsView : UserControl
{
    private CancellationTokenSource? signIn;

    public UnnamedTrackingSettingsView()
    {
        InitializeComponent();
        Loaded += (_, _) => RefreshAccount();
        DataContextChanged += (_, _) => RefreshAccount();
    }

    // Which account buttons apply: sign in when signed out, sign out when signed in,
    // cancel while waiting for the browser. The API key stays available for older servers.
    private void RefreshAccount()
    {
        var settings = DataContext as UnnamedTrackingSettings;
        var waiting = signIn != null;
        var signedIn = settings?.IsSignedIn == true;
        SignInButton.Visibility = !signedIn && !waiting ? Visibility.Visible : Visibility.Collapsed;
        CancelSignInButton.Visibility = waiting ? Visibility.Visible : Visibility.Collapsed;
        SignOutButton.Visibility = signedIn && !waiting ? Visibility.Visible : Visibility.Collapsed;
        SignInButton.IsEnabled = !string.IsNullOrWhiteSpace(settings?.ApiUrl);
        if (settings != null && !signedIn && !string.IsNullOrEmpty(settings.AuthValue)) ApiKeyExpander.IsExpanded = true;
    }

    private void ServerUrlBox_TextChanged(object sender, TextChangedEventArgs e) => RefreshAccount();

    private async void SignInButton_Click(object sender, RoutedEventArgs e)
    {
        if (!(DataContext is UnnamedTrackingSettings settings)) return;
        signIn = new CancellationTokenSource(TimeSpan.FromMinutes(11));
        RefreshAccount();
        ConnectionStatus.Text = string.Empty;
        try
        {
            await settings.SignInAsync(text => SignInStatus.Text = text, signIn.Token);
        }
        catch (OperationCanceledException) { SignInStatus.Text = "Sign-in cancelled."; }
        catch (Exception ex) { SignInStatus.Text = "Sign-in failed: " + ex.Message; }
        finally
        {
            signIn.Dispose();
            signIn = null;
            RefreshAccount();
        }
    }

    private void CancelSignInButton_Click(object sender, RoutedEventArgs e) => signIn?.Cancel();

    private async void SignOutButton_Click(object sender, RoutedEventArgs e)
    {
        if (!(DataContext is UnnamedTrackingSettings settings)) return;
        SignOutButton.IsEnabled = false;
        try
        {
            await settings.SignOutAsync(CancellationToken.None);
            SignInStatus.Text = "Signed out. Playnite's access was revoked on the server.";
        }
        catch (Exception ex)
        {
            // Signed out locally either way; the grant can still be revoked in Connected apps.
            SignInStatus.Text = "Signed out here, but the server could not be reached to revoke access: " + ex.Message +
                                " Revoke it under Connected apps if needed.";
        }
        finally
        {
            SignOutButton.IsEnabled = true;
            RefreshAccount();
        }
    }

    private void ApiKeyBox_Loaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is UnnamedTrackingSettings settings) ApiKeyBox.Password = settings.AuthValue;
    }

    private void ApiKeyBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (ApiKeyBox.IsLoaded && DataContext is UnnamedTrackingSettings settings) settings.AuthValue = ApiKeyBox.Password;
    }

    private async void TestButton_Click(object sender, RoutedEventArgs e)
    {
        if (!(DataContext is UnnamedTrackingSettings settings))
        {
            ConnectionStatus.Text = "Unable to access plugin settings.";
            return;
        }

        TestButton.IsEnabled = false;
        ConnectionStatus.Text = "Testing...";
        try
        {
            await settings.TestConnectionAsync();
            ConnectionStatus.Text = "Connection successful.";
        }
        catch (Exception ex)
        {
            ConnectionStatus.Text = $"Connection failed: {ex.Message}";
        }
        finally
        {
            TestButton.IsEnabled = true;
        }
    }

    private async void UploadButton_Click(object sender, RoutedEventArgs e)
    {
        if (!(DataContext is UnnamedTrackingSettings settings))
        {
            UploadStatus.Text = "Unable to access plugin settings.";
            return;
        }

        UploadButton.IsEnabled = false;
        UploadStatus.Text = "Syncing Playnite library...";
        CancelButton.IsEnabled = true;
        PreviewButton.IsEnabled = false;

        try
        {
            var result = await settings.UploadLibraryAsync((completed, total, game) => Dispatcher.BeginInvoke(new Action(() => UploadStatus.Text = $"Syncing {completed}/{total}: {game}")));
            UploadStatus.Text = FormatResult(result);
        }
        catch (OperationCanceledException)
        {
            UploadStatus.Text = "Library sync cancelled.";
        }
        catch (Exception ex)
        {
            UploadStatus.Text = $"Library sync could not be started: {ex.Message}";
        }
        finally
        {
            UploadButton.IsEnabled = true;
            PreviewButton.IsEnabled = true;
            CancelButton.IsEnabled = false;
        }
    }

    private async void PreviewButton_Click(object sender, RoutedEventArgs e)
    {
        if (!(DataContext is UnnamedTrackingSettings settings))
        {
            UploadStatus.Text = "Unable to access plugin settings.";
            return;
        }

        UploadButton.IsEnabled = false;
        PreviewButton.IsEnabled = false;
        UploadStatus.Text = "Building synchronization preview...";
        CancelButton.IsEnabled = true;
        try
        {
            var result = await settings.PreviewLibraryAsync();
            var builder = new StringBuilder();
            builder.AppendLine("Synchronization preview");
            builder.AppendLine($"Total library entries: {result.TotalGames}");
            builder.AppendLine($"Would create: {result.WouldCreate}");
            builder.AppendLine($"Would update: {result.WouldUpdate}");
            builder.AppendLine($"Ignored: {result.Ignored}");
            if (result.Creates.Count > 0) builder.AppendLine("Creates: " + string.Join(", ", result.Creates));
            if (result.Updates.Count > 0) builder.AppendLine("Updates: " + string.Join(", ", result.Updates));
            if (result.IgnoredGames.Count > 0) builder.AppendLine("Ignored games: " + string.Join(", ", result.IgnoredGames));
            UploadStatus.Text = builder.ToString();
        }
        catch (OperationCanceledException) { UploadStatus.Text = "Preview cancelled."; }
        catch (Exception ex)
        {
            UploadStatus.Text = $"Preview failed: {ex.Message}";
        }
        finally
        {
            UploadButton.IsEnabled = true;
            PreviewButton.IsEnabled = true;
            CancelButton.IsEnabled = false;
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is UnnamedTrackingSettings settings)
        {
            settings.CancelSync();
            UploadStatus.Text = "Cancelling library sync...";
        }
    }

    private static string FormatResult(UnnamedTrackingUploadResult result)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Library sync complete");
        builder.AppendLine();
        builder.AppendLine($"Total: {result.TotalGames}");
        builder.AppendLine($"Succeeded: {result.SucceededGames}");
        builder.AppendLine($"Failed: {result.FailedGames}");
        builder.AppendLine($"Artwork warnings: {result.WarningCount}");
        foreach (var warning in result.Warnings) builder.AppendLine($"  {warning.GameName}: {warning.Operation} — {warning.ResponseBody}");

        if (result.FailedGames == 0)
        {
            return builder.ToString();
        }

        builder.AppendLine();
        builder.AppendLine("Failed:");

        foreach (var failure in result.Failures)
        {
            builder.AppendLine($"  {failure.GameName}");
            if (failure.GameId.HasValue)
            {
                builder.AppendLine($"    Playnite ID: {failure.GameId.Value}");
            }

            var status = failure.StatusCode > 0 ? $"HTTP {failure.StatusCode}" : "No HTTP status";
            builder.AppendLine($"    {failure.Operation} failed ({status}):");
            var responseBody = string.IsNullOrWhiteSpace(failure.ResponseBody)
                ? "<empty server response>"
                : failure.ResponseBody.Trim();
            builder.AppendLine($"      {responseBody}");
        }

        return builder.ToString();
    }
}
