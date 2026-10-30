using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;

namespace UnnamedTrackingPlaynite;

// Persist-before-execution is deliberate: losing a launch is safer than executing one twice after a crash.
internal sealed class RemoteLaunchJournal : IDisposable
{
    private readonly string path;
    private readonly FileStream lease;
    private readonly List<LaunchReceipt> receipts;
    internal RemoteLaunchJournal(string directory)
    {
        Directory.CreateDirectory(directory);
        path = Path.Combine(directory, "remote-launch.json");
        lease = new FileStream(path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        try
        {
            if (File.Exists(path) && new FileInfo(path).Length > 4 * 1024 * 1024)
                throw new InvalidDataException("The remote launch journal is too large.");
            receipts = File.Exists(path)
                ? AccountLink.Deserialize<JournalDocument>(File.ReadAllText(path, Encoding.UTF8)).Receipts ?? throw new InvalidDataException("Invalid launch journal.")
                : new List<LaunchReceipt>();
            if (receipts.Count > 12000 || receipts.Any(row => row.Id == Guid.Empty || row.SeenAt <= 0))
                throw new InvalidDataException("Invalid launch journal.");
        }
        catch { lease.Dispose(); throw; }
    }
    internal bool Remember(Guid id)
    {
        if (receipts.Any(row => row.Id == id)) return false;
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        receipts.RemoveAll(row => row.Outcome == null && row.SeenAt < now - 7 * 86400);
        if (receipts.Count >= 12000) throw new InvalidDataException("Remote launch history is full. Receiving has stopped.");
        receipts.Add(new LaunchReceipt { Id = id, SeenAt = now });
        Save();
        return true;
    }
    internal void RecordResult(Guid id, RemoteLaunchOutcome outcome)
    { receipts.Single(row => row.Id == id).Outcome = outcome; Save(); }
    internal IEnumerable<LaunchReceipt> Pending => receipts.Where(row => row.Outcome != null).Take(5).ToArray();
    internal void Acknowledged(Guid id)
    { receipts.Single(row => row.Id == id).Outcome = null; Save(); }
    private void Save()
    {
        var temporary = path + ".new";
        var bytes = Encoding.UTF8.GetBytes(AccountLink.Serialize(new JournalDocument { Receipts = receipts }));
        using (var file = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
        { file.Write(bytes, 0, bytes.Length); file.Flush(true); }
        if (File.Exists(path)) File.Replace(temporary, path, null);
        else File.Move(temporary, path);
    }
    public void Dispose() => lease.Dispose();
    [DataContract] private sealed class JournalDocument
    { [DataMember(Name = "receipts")] public List<LaunchReceipt>? Receipts { get; set; } }
}

[DataContract]
internal sealed class LaunchReceipt
{
    [DataMember(Name = "id")] public Guid Id { get; set; }
    [DataMember(Name = "seen_at")] public long SeenAt { get; set; }
    [DataMember(Name = "outcome", EmitDefaultValue = false)] public RemoteLaunchOutcome? Outcome { get; set; }
}
