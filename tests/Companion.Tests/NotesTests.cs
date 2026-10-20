using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnnamedTrackingPlaynite;

internal static partial class Program
{
    private static async Task NotesHttp()
    {
        using var temp = new TemporaryDirectory();
        using var host = new NotesHost();
        var client = new UnnamedTrackingSyncClient(Log, null!);
        var localId = Guid.NewGuid();
        var remoteId = Guid.NewGuid();
        var local = "日本語 🎮\r\nLauncher note";
        var allowApply = true;
        Task<bool> Apply(Guid id, string expected, string value)
        {
            Check(id == localId, "Pull targets the matched Playnite game");
            if (!allowApply || local != expected) return Task.FromResult(false);
            local = value;
            return Task.FromResult(true);
        }
        PlayniteNotesSync Create() => new PlayniteNotesSync(temp.Path, client.SendJsonAsync, Apply);
        Task Sync(PlayniteNotesSync sync) => sync.SyncAsync(host.Url, "utd_notes_secret", remoteId, localId, local);
        var notes = Create();
        await Sync(notes);
        Check(host.Notes["Playnite"] == local, "Initial launcher note appears as the dedicated Markdown note, with exact Unicode and CRLF");
        Check(host.LastExpected == null, "Initial write uses create, not a blind replacement");
        Check(File.ReadAllText(Path.Combine(temp.Path, "note-sync.json")).Contains(PlayniteNotesSync.Digest(local)), "Baseline persists its exact revision");
        Check(!File.ReadAllText(Path.Combine(temp.Path, "note-sync.json")).Contains("Launcher note"), "Baseline stores hashes, not private text");

        notes = Create();
        host.Notes["Unrelated note"] = "Independent";
        host.Notes["Playnite"] = "Changed in web app";
        await Sync(notes);
        Check(local == "Changed in web app", "Remote-only edit pulls after extension restart");
        local = "Edited in launcher";
        await Sync(notes);
        Check(host.Notes["Playnite"] == local && host.LastExpected == PlayniteNotesSync.Digest("Changed in web app"), "Local-only edit sends the exact expected remote revision");

        local = "Simultaneous launcher edit";
        host.Notes["Playnite"] = "Simultaneous web edit";
        await Sync(notes);
        Check(local == "Simultaneous web edit" && host.Notes.Values.Contains("Simultaneous launcher edit"), "Concurrent edits keep a conflict copy and the canonical web edit");
        Check(host.Notes["Unrelated note"] == "Independent", "Other notes remain independent");
        var conflictCount = host.Notes.Count;
        await Sync(Create());
        Check(host.Notes.Count == conflictCount, "Retry after conflict creates no duplicate copy");

        local = "Raced launcher edit";
        host.BeforePut = () => host.Notes["Playnite"] = "Changed after GET";
        await Sync(notes);
        Check(local == "Changed after GET" && host.Notes.Values.Contains("Raced launcher edit"), "A 409 race refetches and preserves both values");

        local = "Edited during pull";
        host.Notes["Playnite"] = "Another remote edit";
        allowApply = false;
        await ThrowsAsync<InvalidOperationException>(() => Sync(notes));
        Check(local == "Edited during pull" && host.Notes.Values.Contains(local), "UI compare-and-set failure keeps local text and conflict copy");
        var countBeforeRetry = host.Notes.Count;
        allowApply = true;
        await Sync(Create());
        Check(local == "Another remote edit" && host.Notes.Count == countBeforeRetry, "Failed local apply does not advance baseline; retry reuses conflict copy");

        host.Notes.Remove("Playnite");
        local = "Local edit when web note deleted";
        await Sync(notes = Create());
        Check(local == "" && !host.Notes.ContainsKey("Playnite") && host.Notes.Values.Contains("Local edit when web note deleted"), "Remote deletion is respected while a local edit is preserved independently");
        await Sync(notes);
        Check(!host.Notes.ContainsKey("Playnite"), "Empty local state does not resurrect a deleted note");
        host.Notes["Playnite"] = "Remote new note";
        await Sync(notes);
        Check(local == "Remote new note", "First sync with an empty launcher note pulls existing remote text");
        local = "";
        await Sync(notes);
        Check(host.Notes["Playnite"] == "", "Explicit local clearing updates the canonical note to empty");
        host.Notes["Playnite"] = "\uFEFFBOM stays part of the exact revision\r\n";
        await Sync(notes);
        Check(local == host.Notes["Playnite"], "UTF-8 BOM and line endings are not stripped by transport");
        local += "Edit";
        await Sync(notes);
        Check(host.Notes["Playnite"] == local, "Conditional write uses exact bytes after a BOM-bearing read");

        local = "Keep on failure";
        host.FailureStatus = 403;
        await ThrowsAsync<UnnamedTrackingSyncApiException>(() => Sync(notes));
        Check(local == "Keep on failure", "Permission denial never changes local notes");
        host.FailureStatus = 500;
        await ThrowsAsync<UnnamedTrackingSyncApiException>(() => Sync(notes));
        Check(local == "Keep on failure", "Server failure never changes local notes");
        host.FailureStatus = 0;
        using (var cancelled = new CancellationTokenSource())
        {
            cancelled.Cancel();
            using (ApiConnection.UseCancellation(cancelled.Token))
                await ThrowsAsync<OperationCanceledException>(() => Sync(notes));
        }
        Check(!Log.Messages.Any(message => message.Contains("utd_notes_secret") || message.Contains("Simultaneous launcher edit")), "Transport does not log credentials or note bodies");

        using var other = new TemporaryDirectory();
        local = "First divergent local text";
        host.Notes["Playnite"] = "Existing remote text";
        await Sync(new PlayniteNotesSync(other.Path, client.SendJsonAsync, Apply));
        Check(local == "Existing remote text" && host.Notes.Values.Contains("First divergent local text"), "First divergent sync preserves both without guessing a winner");
        File.WriteAllText(Path.Combine(other.Path, "note-sync.json"), "broken");
        Throws<SerializationException>(() => new PlayniteNotesSync(other.Path, client.SendJsonAsync, Apply));
        Check(File.ReadAllText(Path.Combine(other.Path, "note-sync.json")) == "broken", "Corrupt baseline is retained for diagnosis");
        await NotesLibraryIntegration();
    }

    private static async Task NotesLibraryIntegration()
    {
        using var temp = new TemporaryDirectory();
        using var host = new NotesHost();
        var guid = Guid.NewGuid();
        host.Library = "[" + Remote(Guid.NewGuid(), guid) + "]";
        var local = "Note from normal sync";
        var granted = true;
        var client = new UnnamedTrackingSyncClient(Log, null!, temp.Path,
            (id, expected, content) => { local = content; return Task.FromResult(true); }, () => granted);
        SyncGameSnapshot Game() { var snapshot = Snapshot(guid); snapshot.Payload.Notes = local; return snapshot; }
        var result = await client.UploadSnapshotsAsync(host.Url, "utd_notes_secret", new[] { Game() });
        Check(result.SucceededGames == 1 && result.WarningCount == 0 && host.Notes["Playnite"] == local, "Normal library sync includes the named note");
        host.Notes["Playnite"] = "Pulled through game-stop update";
        Check(await client.UpdateSnapshotAsync(host.Url, "utd_notes_secret", Game()) && local == host.Notes["Playnite"], "Game-stop update includes note pull");
        granted = false;
        local = "Unapproved local change";
        result = await client.UploadSnapshotsAsync(host.Url, "utd_notes_secret", new[] { Game() });
        Check(result.SucceededGames == 1 && result.WarningCount == 1 && result.Warnings[0].ResponseBody.Contains("Review app permissions"), "Old grant keeps library sync and reports explicit permission renewal");
        Check(host.Notes["Playnite"] != local, "Missing scope never silently expands access");
        granted = true;
        host.OfferNotes = false;
        result = await client.UploadSnapshotsAsync(host.Url, "utd_notes_secret", new[] { Game() });
        Check(result.SucceededGames == 1 && result.WarningCount == 1 && result.Warnings[0].ResponseBody.Contains("Update the server"), "Older host retains original library sync with a clear note warning");
    }

    private sealed class NotesHost : IDisposable
    {
        private readonly Server server;
        private int status;
        public Dictionary<string, string> Notes { get; } = new Dictionary<string, string>();
        public string Url => server.Url;
        public string Library = "[]";
        public bool OfferNotes = true;
        public int FailureStatus;
        public string? LastExpected;
        public Action? BeforePut;
        public NotesHost() { server = new Server(Respond, statusFor: _ => status); }
        private string Respond(HttpListenerRequest request)
        {
            status = FailureStatus == 0 ? 200 : FailureStatus;
            if (FailureStatus != 0) return "{}";
            var path = Uri.UnescapeDataString(request.Url!.AbsolutePath);
            if (path == "/api/delegation/v1/instance")
                return OfferNotes ? "{\"capabilities\":{\"notes.read\":\"Read\",\"notes.write\":\"Write\"}}" : "{}";
            if (path == PlayniteContract.ContractPath) return "{}";
            if (path == "/api/game/list") return Library;
            if (!path.Contains("/notes/")) return "{}";
            var name = path.Substring(path.IndexOf("/notes/", StringComparison.Ordinal) + 7);
            if (request.HttpMethod == "GET")
            {
                if (Notes.TryGetValue(name, out var value)) return value;
                status = 404; return "{}";
            }
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(server.Requests.Last().Body));
            var body = (TestNoteWrite)new DataContractJsonSerializer(typeof(TestNoteWrite)).ReadObject(stream)!;
            LastExpected = body.Expected;
            if (request.HttpMethod == "POST")
            {
                if (Notes.ContainsKey(name)) { status = 409; return "{}"; }
                status = 201;
            }
            else
            {
                var race = BeforePut; BeforePut = null; race?.Invoke();
                if (!Notes.TryGetValue(name, out var current) || PlayniteNotesSync.Digest(current) != body.Expected)
                { status = 409; return "{}"; }
            }
            Notes[name] = body.Content;
            return "{}";
        }
        public void Dispose() => server.Dispose();
    }

    [DataContract]
    private sealed class TestNoteWrite
    {
        [DataMember(Name = "content")] public string Content = "";
        [DataMember(Name = "expected_sha256")] public string? Expected { get; set; }
    }
}
