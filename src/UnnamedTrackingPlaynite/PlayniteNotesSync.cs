using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace UnnamedTrackingPlaynite;

// Uses the existing library transport and mutation gate. Only the named Playnite
// note is synchronized; a conflict is another ordinary, independently editable note.
internal sealed class PlayniteNotesSync
{
    private readonly string path;
    private readonly Func<string, string, string, string?, Task<string>> send;
    private readonly Func<Guid, string, string, Task<bool>> applyLocal;
    private readonly Dictionary<string, string> baselines;

    internal PlayniteNotesSync(string directory,
        Func<string, string, string, string?, Task<string>> send,
        Func<Guid, string, string, Task<bool>> applyLocal)
    {
        Directory.CreateDirectory(directory);
        path = Path.Combine(directory, "note-sync.json");
        this.send = send;
        this.applyLocal = applyLocal;
        if (!File.Exists(path)) baselines = new Dictionary<string, string>();
        else
        {
            // A damaged baseline must not turn a known conflict into an overwrite.
            // Preserve it and require an explicit retry after the file is repaired.
            using var stream = File.OpenRead(path);
            baselines = (Dictionary<string, string>?)Serializer.ReadObject(stream)
                ?? throw new InvalidDataException("The note-sync baseline is invalid.");
        }
    }

    private static DataContractJsonSerializer Serializer => new DataContractJsonSerializer(typeof(Dictionary<string, string>));

    internal static string Digest(string value)
    {
        using var sha = SHA256.Create();
        return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(value))).Replace("-", "").ToLowerInvariant();
    }

    internal async Task SyncAsync(string apiUrl, string credential, Guid remoteId, Guid localId, string local)
    {
        var endpoint = ApiConnection.ValidateBaseUrl(apiUrl) + "/api/game/" + remoteId + "/notes/";
        var key = Digest(endpoint + "|" + localId);
        baselines.TryGetValue(key, out var baseline);
        var localDigest = Digest(local);
        for (var attempt = 0; attempt < 3; attempt++)
        {
            ApiConnection.Token.ThrowIfCancellationRequested();
            var remote = await ReadAsync(endpoint + "Playnite", credential).ConfigureAwait(false);
            var remoteDigest = remote == null ? null : Digest(remote);
            if (remote != null && localDigest == remoteDigest)
            {
                Commit(key, localDigest);
                return;
            }
            if (remote == null && baseline == null)
            {
                if (local.Length == 0) return;
                try
                {
                    await send(endpoint + "Playnite", credential, "POST", Body(local)).ConfigureAwait(false);
                    Commit(key, localDigest);
                    return;
                }
                catch (UnnamedTrackingSyncApiException ex) when (ex.StatusCode == 409) { continue; }
            }
            if (remote != null && baseline != null && remoteDigest == baseline)
            {
                try
                {
                    await send(endpoint + "Playnite", credential, "PUT", Body(local, remoteDigest)).ConfigureAwait(false);
                    Commit(key, localDigest);
                    return;
                }
                catch (UnnamedTrackingSyncApiException ex) when (ex.StatusCode == 409) { continue; }
            }

            // A remote deletion is an upstream change too. Never resurrect it or
            // discard a simultaneous local edit. First-time differing notes also
            // keep both: there is no trustworthy baseline to choose a winner.
            if (localDigest != baseline && local.Length != 0)
                await PreserveConflictAsync(endpoint, credential, local).ConfigureAwait(false);
            if (!await applyLocal(localId, local, remote ?? string.Empty).ConfigureAwait(false))
                throw new InvalidOperationException("The Playnite note changed during synchronization. It was kept; sync again to reconcile it.");
            Commit(key, remoteDigest);
            return;
        }
        throw new InvalidOperationException("The shared Playnite note keeps changing. Your local note was kept; sync again later.");
    }

    private async Task<string?> ReadAsync(string endpoint, string credential)
    {
        try { return await send(endpoint, credential, "GET", null).ConfigureAwait(false); }
        catch (UnnamedTrackingSyncApiException ex) when (ex.StatusCode == 404) { return null; }
    }

    private async Task PreserveConflictAsync(string endpoint, string credential, string content)
    {
        var name = "Playnite conflict " + Digest(content).Substring(0, 16);
        for (var index = 0; index < 10; index++)
        {
            var address = endpoint + Uri.EscapeDataString(name + (index == 0 ? "" : " " + (index + 1)));
            try { await send(address, credential, "POST", Body(content)).ConfigureAwait(false); return; }
            catch (UnnamedTrackingSyncApiException ex) when (ex.StatusCode == 409)
            {
                if (await ReadAsync(address, credential).ConfigureAwait(false) == content) return;
            }
        }
        throw new InvalidOperationException("Could not preserve a conflict copy. Your local Playnite note was kept.");
    }

    private static string Body(string content, string? expected = null)
    {
        using var stream = new MemoryStream();
        new DataContractJsonSerializer(typeof(NoteWrite)).WriteObject(stream,
            new NoteWrite { Content = content, ExpectedSha256 = expected });
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private void Commit(string key, string? digest)
    {
        var updated = new Dictionary<string, string>(baselines);
        if (digest == null) updated.Remove(key); else updated[key] = digest;
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                Serializer.WriteObject(stream, updated);
                stream.Flush(true);
            }
            if (File.Exists(path)) File.Replace(temporary, path, path + ".bak");
            else File.Move(temporary, path);
            baselines.Clear();
            foreach (var item in updated) baselines.Add(item.Key, item.Value);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    [DataContract]
    private sealed class NoteWrite
    {
        [DataMember(Name = "content")] public string Content { get; set; } = string.Empty;
        [DataMember(Name = "expected_sha256", EmitDefaultValue = false)] public string? ExpectedSha256 { get; set; }
    }
}
