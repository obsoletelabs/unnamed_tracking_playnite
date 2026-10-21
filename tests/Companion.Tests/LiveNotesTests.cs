using System;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnnamedTrackingPlaynite;

internal static partial class Program
{
    // Opt-in cross-repository check. Supply only an explicitly approved disposable
    // localhost user's game/grant; neither CI nor the ordinary harness runs this.
    private static async Task LiveNotesHttp(string fixturePath)
    {
        using var source = File.OpenRead(fixturePath);
        var fixture = (LiveFixture)new DataContractJsonSerializer(typeof(LiveFixture)).ReadObject(source)!;
        var address = new Uri(ApiConnection.ValidateBaseUrl(fixture.BaseUrl));
        if (!address.IsLoopback || fixture.RemoteId == Guid.Empty || fixture.LocalId == Guid.Empty)
            throw new InvalidOperationException("Live checks require an approved disposable localhost game and grant.");
        using var temp = new TemporaryDirectory();
        var client = new UnnamedTrackingSyncClient(Log, null!);
        var local = "Launcher note: 日本語 🎮\r\nExact line endings.";
        var endpoint = fixture.BaseUrl + "/api/game/" + fixture.RemoteId + "/notes/";
        try
        {
            var previous = await client.SendJsonAsync(endpoint + "Playnite", fixture.Credential, "GET", null);
            await client.SendJsonAsync(endpoint + "Playnite", fixture.Credential, "PUT", LiveBody(local, PlayniteNotesSync.Digest(previous)));
        }
        catch (UnnamedTrackingSyncApiException ex) when (ex.StatusCode == 404) { }
        var notes = new PlayniteNotesSync(temp.Path, client.SendJsonAsync,
            (id, expected, value) =>
            {
                Check(id == fixture.LocalId && local == expected, "Live pull targets the unchanged local note");
                local = value; return Task.FromResult(true);
            });
        Task Sync() => notes.SyncAsync(fixture.BaseUrl, fixture.Credential, fixture.RemoteId, fixture.LocalId, local);
        await Sync();
        Check(await client.SendJsonAsync(endpoint + "Playnite", fixture.Credential, "GET", null) == local,
            "Production transport creates a visible named note on the real host");
        try { await client.SendJsonAsync(endpoint + "Independent", fixture.Credential, "POST", LiveBody("Unrelated app note")); }
        catch (UnnamedTrackingSyncApiException ex) when (ex.StatusCode == 409) { }
        var digest = PlayniteNotesSync.Digest(local);
        await client.SendJsonAsync(endpoint + "Playnite", fixture.Credential, "PUT", LiveBody("Edited in app", digest));
        await Sync();
        Check(local == "Edited in app", "Real host note edit pulls into launcher state");
        local = "Edited in launcher";
        await Sync();
        Check(await client.SendJsonAsync(endpoint + "Playnite", fixture.Credential, "GET", null) == local,
            "Conditional push updates the real host");
        digest = PlayniteNotesSync.Digest(local);
        await client.SendJsonAsync(endpoint + "Playnite", fixture.Credential, "PUT", LiveBody("Concurrent app edit", digest));
        local = "Concurrent launcher edit";
        await Sync();
        Check(local == "Concurrent app edit", "Concurrent real-host edit becomes canonical locally");
        var copy = "Playnite conflict " + PlayniteNotesSync.Digest("Concurrent launcher edit").Substring(0, 16);
        Check(await client.SendJsonAsync(endpoint + Uri.EscapeDataString(copy), fixture.Credential, "GET", null) == "Concurrent launcher edit",
            "Real host retains the exact conflict copy");
        Check(await client.SendJsonAsync(endpoint + "Independent", fixture.Credential, "GET", null) == "Unrelated app note",
            "Real host retains unrelated notes");
        await ThrowsAsync<UnnamedTrackingSyncApiException>(() =>
            client.SendJsonAsync(endpoint + "Playnite", fixture.Credential, "PUT", LiveBody("Stale overwrite", digest)));
        Check(await client.SendJsonAsync(endpoint + "Playnite", fixture.Credential, "GET", null) == "Concurrent app edit",
            "A stale real-host write cannot replace the note");
        try
        {
            await client.SendJsonAsync(fixture.BaseUrl + "/api/game/" + fixture.ForeignId + "/notes/Playnite", fixture.Credential, "GET", null);
            throw new Exception("Foreign note was exposed");
        }
        catch (UnnamedTrackingSyncApiException ex) { Check(ex.StatusCode == 404, "Ordinary delegated user cannot read another user's note"); }
        try
        {
            await client.SendJsonAsync(endpoint + "Playnite", fixture.Credential, "DELETE", null);
            throw new Exception("Note grant allowed deletion");
        }
        catch (UnnamedTrackingSyncApiException ex) { Check(ex.StatusCode == 403, "Note write scope cannot delete notes"); }

        var shot = Path.Combine(temp.Path, "playnite-regression.png");
        File.WriteAllBytes(shot, Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAACAAAAAYCAIAAAAUMWhjAAAAJklEQVR4nGOUi5rGQEvARFPTRy0YtWDUglELRi0YtWDUglELqAYAeKoBPhDfIkMAAAAASUVORK5CYII="));
        var doc = Path.Combine(temp.Path, "playnite-regression.txt");
        File.WriteAllText(doc, "A synthetic document uploaded by the real Playnite transport.", new UTF8Encoding(false));
        var media = await GameContentUploader.UploadAsync(fixture.BaseUrl, fixture.Credential, fixture.RemoteId,
            GameContentKind.Media, new[] { shot }, null, CancellationToken.None);
        Check(media.Saved.Count == 1 && !media.HasProblems, "Actual host screenshot upload: " + media.Summary());
        var document = await GameContentUploader.UploadAsync(fixture.BaseUrl, fixture.Credential, fixture.RemoteId,
            GameContentKind.Document, new[] { doc }, null, CancellationToken.None);
        Check(document.Saved.Count == 1 && !document.HasProblems, "Actual host document upload: " + document.Summary());
        Check(!Log.Messages.Any(message => message.Contains(fixture.Credential) || message.Contains("Concurrent launcher edit")),
            "Live checks do not log the grant or note bodies");
    }

    private static string LiveBody(string content, string? expected = null)
    {
        using var stream = new MemoryStream();
        new DataContractJsonSerializer(typeof(TestNoteWrite)).WriteObject(stream, new TestNoteWrite { Content = content, Expected = expected });
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    [DataContract]
    private sealed class LiveFixture
    {
        [DataMember(Name = "base_url")] public string BaseUrl { get; set; } = "";
        [DataMember(Name = "credential")] public string Credential { get; set; } = "";
        [DataMember(Name = "remote_id")] public Guid RemoteId { get; set; }
        [DataMember(Name = "local_id")] public Guid LocalId { get; set; }
        [DataMember(Name = "foreign_id")] public Guid ForeignId { get; set; }
    }
}
