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

namespace UnnamedTrackingPlaynite;

internal enum GameContentKind
{
    /// <summary>Images become screenshots, videos become clips and audio becomes soundtrack (server decides).</summary>
    Media,
    /// <summary>Any file for the game's Docs tab.</summary>
    Document
}

/// <summary>What happened to each file of an upload; the server may accept some and reject others.</summary>
internal sealed class GameContentUploadResult
{
    public List<string> Saved { get; } = new List<string>();
    public List<string> Rejected { get; } = new List<string>();

    public bool HasProblems => Rejected.Count > 0;

    public string Summary()
    {
        var text = $"{Saved.Count} file(s) uploaded.";
        if (Rejected.Count > 0) text += " Not uploaded: " + string.Join("; ", Rejected) + ".";
        return text;
    }
}

[DataContract]
internal sealed class GameContentResponse
{
    [DataMember(Name = "results")] public List<GameContentResponseItem>? Results { get; set; }
}

[DataContract]
internal sealed class GameContentResponseItem
{
    [DataMember(Name = "filename")] public string? Filename { get; set; }
    [DataMember(Name = "status")] public string? Status { get; set; }
    [DataMember(Name = "reason")] public string? Reason { get; set; }
}

/// <summary>Adds screenshots, clips, soundtracks and documents to a game using the host's own upload
/// routes. Each file is its own request so one unsupported or oversized file never loses the others;
/// the host enforces the type and size limits and the extension reports what it said.</summary>
internal static class GameContentUploader
{
    internal const string MediaFilter =
        "Images, clips and audio|*.png;*.jpg;*.jpeg;*.gif;*.webp;*.bmp;*.mp4;*.webm;*.mkv;*.mov;*.avi;*.mp3;*.ogg;*.flac;*.wav;*.m4a|All files|*.*";
    internal const string DocumentFilter = "All files|*.*";

    internal static string Endpoint(string apiUrl, Guid remoteGameId, GameContentKind kind)
        => ApiConnection.ValidateBaseUrl(apiUrl) + $"/api/game/{remoteGameId:D}/" +
           (kind == GameContentKind.Media ? "screenshots" : "files/doc");

    internal static async Task<GameContentUploadResult> UploadAsync(
        string apiUrl,
        string credential,
        Guid remoteGameId,
        GameContentKind kind,
        IEnumerable<string> paths,
        Action<int, int, string>? progress,
        CancellationToken cancellationToken)
    {
        ApiConnection.ValidateKey(credential);
        var endpoint = Endpoint(apiUrl, remoteGameId, kind);
        var files = (paths ?? Enumerable.Empty<string>()).Where(path => !string.IsNullOrWhiteSpace(path)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var result = new GameContentUploadResult();
        using (ApiConnection.UseCancellation(cancellationToken))
        {
            for (var index = 0; index < files.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var path = files[index];
                var name = SafeFileName(path);
                progress?.Invoke(index + 1, files.Count, name);
                var problem = Problem(path);
                if (problem != null) { result.Rejected.Add(name + " (" + problem + ")"); continue; }
                try
                {
                    var response = await UploadOneAsync(endpoint, credential, path, name).ConfigureAwait(false);
                    var item = response?.Results?.FirstOrDefault();
                    if (item == null || string.Equals(item.Status, "saved", StringComparison.OrdinalIgnoreCase)) result.Saved.Add(name);
                    else result.Rejected.Add(name + " (" + (string.IsNullOrWhiteSpace(item.Reason) ? "rejected by the server" : item.Reason) + ")");
                }
                catch (WebException ex) when (!cancellationToken.IsCancellationRequested)
                {
                    result.Rejected.Add(name + " (" + Describe(ex) + ")");
                }
                catch (WebException) when (cancellationToken.IsCancellationRequested) { throw new OperationCanceledException(cancellationToken); }
            }
        }
        return result;
    }

    private static string? Problem(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists) return "file not found";
            if (info.Length == 0) return "file is empty";
            return null;
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is NotSupportedException || ex is ArgumentException)
        {
            return "cannot be read";
        }
    }

    private static async Task<GameContentResponse?> UploadOneAsync(string endpoint, string credential, string path, string fileName)
    {
        var info = new FileInfo(path);
        var boundary = "----------------" + Guid.NewGuid().ToString("N");
        var modified = new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero).ToUnixTimeMilliseconds();
        var head = Encoding.UTF8.GetBytes(
            $"--{boundary}\r\nContent-Disposition: form-data; name=\"last_modified\"\r\n\r\n{modified}\r\n" +
            $"--{boundary}\r\nContent-Disposition: form-data; name=\"files\"; filename=\"{fileName}\"\r\n" +
            $"Content-Type: {ContentType(fileName)}\r\n\r\n");
        var tail = Encoding.UTF8.GetBytes($"\r\n--{boundary}--\r\n");

        var request = ApiConnection.CreateRequest(endpoint);
        using var registration = ApiConnection.RegisterRequest(request);
        request.Method = "POST";
        request.Headers[HttpRequestHeader.Authorization] = "Bearer " + credential;
        request.ContentType = "multipart/form-data; boundary=" + boundary;
        request.ContentLength = head.LongLength + info.Length + tail.LongLength;
        request.AllowWriteStreamBuffering = false;

        using (var requestStream = await request.GetRequestStreamAsync().ConfigureAwait(false))
        {
            await requestStream.WriteAsync(head, 0, head.Length).ConfigureAwait(false);
            using (var input = File.OpenRead(path))
            {
                var buffer = new byte[1024 * 1024];
                int read;
                while ((read = await input.ReadAsync(buffer, 0, buffer.Length, ApiConnection.Token).ConfigureAwait(false)) > 0)
                    await requestStream.WriteAsync(buffer, 0, read).ConfigureAwait(false);
            }
            await requestStream.WriteAsync(tail, 0, tail.Length).ConfigureAwait(false);
        }

        using (var response = ApiConnection.EnsureSuccess((HttpWebResponse)await request.GetResponseAsync().ConfigureAwait(false)))
        using (var stream = response.GetResponseStream() ?? Stream.Null)
        using (var memory = new MemoryStream())
        {
            await stream.CopyToAsync(memory).ConfigureAwait(false);
            if (memory.Length == 0) return null;
            memory.Position = 0;
            try { return (GameContentResponse?)new DataContractJsonSerializer(typeof(GameContentResponse)).ReadObject(memory); }
            catch (SerializationException) { return null; }
        }
    }

    /// <summary>A header-safe file name: no path, quotes or line breaks.</summary>
    internal static string SafeFileName(string path)
    {
        var name = Path.GetFileName(path ?? string.Empty);
        var cleaned = new string(name.Where(c => c >= ' ' && c != '"' && c != '\\' && c != '/').ToArray()).Trim();
        return string.IsNullOrEmpty(cleaned) ? "file" : cleaned;
    }

    internal static string ContentType(string fileName)
    {
        switch (Path.GetExtension(fileName)?.ToLowerInvariant())
        {
            case ".png": return "image/png";
            case ".jpg":
            case ".jpeg": return "image/jpeg";
            case ".gif": return "image/gif";
            case ".webp": return "image/webp";
            case ".bmp": return "image/bmp";
            case ".mp4":
            case ".m4v": return "video/mp4";
            case ".webm": return "video/webm";
            case ".mkv": return "video/x-matroska";
            case ".mov": return "video/quicktime";
            case ".avi": return "video/x-msvideo";
            case ".mp3": return "audio/mpeg";
            case ".ogg": return "audio/ogg";
            case ".flac": return "audio/flac";
            case ".wav": return "audio/wav";
            case ".m4a": return "audio/mp4";
            case ".pdf": return "application/pdf";
            case ".txt": return "text/plain";
            default: return "application/octet-stream";
        }
    }

    private static string Describe(WebException ex)
    {
        if (ex.Response is HttpWebResponse response)
        {
            var code = (int)response.StatusCode;
            response.Dispose();
            if (code == 401 || code == 403) return "the server did not allow this app to add files; sign in again and allow adding screenshots, clips and documents";
            if (code == 404) return "the game was not found on the server";
            if (code == 413) return "too large for the server or a proxy in front of it";
            return "the server answered HTTP " + code;
        }
        return "could not reach the server";
    }
}
