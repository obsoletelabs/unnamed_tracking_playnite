using System;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace UnnamedTrackingPlaynite;

internal sealed class RemoteLaunchReceiver
{
    private readonly RemoteLaunchClient client;
    private readonly RemoteLaunchJournal journal;
    private readonly Func<RemoteLaunchDelivery, CancellationToken, Task<RemoteLaunchOutcome>> launch;
    private readonly Action<string> status;
    internal RemoteLaunchReceiver(RemoteLaunchClient client, RemoteLaunchJournal journal,
        Func<RemoteLaunchDelivery, CancellationToken, Task<RemoteLaunchOutcome>> launch, Action<string> status)
    { this.client = client; this.journal = journal; this.launch = launch; this.status = status; }

    internal async Task PollOnceAsync(CancellationToken token)
    {
        foreach (var receipt in journal.Pending)
        {
            try { await client.ReportAsync(receipt.Id, receipt.Outcome!, token).ConfigureAwait(false); }
            catch (AccountLinkException ex) when (ex.StatusCode == 404 || ex.StatusCode == 409) { }
            journal.Acknowledged(receipt.Id);
        }
        var request = await client.ClaimAsync(token).ConfigureAwait(false);
        if (request == null || !request.CanExecute) return;
        token.ThrowIfCancellationRequested();
        if (!journal.Remember(request.Id)) return;
        RemoteLaunchOutcome result;
        try { result = await launch(request, token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { return; }
        catch (Exception) { result = RemoteLaunchOutcome.Failed("launch_failed"); }
        journal.RecordResult(request.Id, result);
        try
        {
            await client.ReportAsync(request.Id, result, token).ConfigureAwait(false);
            journal.Acknowledged(request.Id);
        }
        catch (AccountLinkException ex) when (ex.StatusCode == 404 || ex.StatusCode == 409)
        { journal.Acknowledged(request.Id); }
    }

    internal async Task RunAsync(CancellationToken token)
    {
        var connected = false;
        var failures = 0;
        try
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    if (!connected) { await client.ConnectAsync(token).ConfigureAwait(false); connected = true; }
                    await PollOnceAsync(token).ConfigureAwait(false);
                    failures = 0;
                    status("Ready to receive launch requests while Playnite is open.");
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
                catch (AccountLinkException ex) when (ex.StatusCode == 401 || ex.StatusCode == 403)
                { status("Receiving stopped. Review app permissions or sign in again."); break; }
                catch (AccountLinkException ex) when (ex.StatusCode == 404 || ex.StatusCode == 409)
                { status("Receiving stopped. Update the server or disable and re-enable remote launch."); break; }
                catch (InvalidDataException)
                { status("Receiving stopped because the launch contract or local history is invalid. Check the extension log."); break; }
                catch (IOException)
                { status("Receiving stopped because launch history could not be saved safely."); break; }
                catch (Exception ex) when (ex is AccountLinkException || ex is WebException || ex is OperationCanceledException)
                { failures = Math.Min(failures + 1, 4); status("Server unavailable. Receiving will reconnect automatically."); }
                await Task.Delay(TimeSpan.FromSeconds(failures == 0 ? 5 : Math.Min(60, 5 * (1 << failures))), token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        finally
        {
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                try { await client.ConfigureAsync(false, timeout.Token).ConfigureAwait(false); }
                catch (Exception) { } // Opt-out is already enforced locally; server requests also expire.
            }
        }
    }
}
