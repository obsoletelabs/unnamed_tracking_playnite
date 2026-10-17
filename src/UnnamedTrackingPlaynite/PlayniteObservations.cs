using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;

namespace UnnamedTrackingPlaynite;

// Versioned observation contract with the host (/api/playnite/*). The extension reports what
// the Playnite library holds, keyed by the stable Playnite game ID; the host decides identity.
// An omitted member is "unknown" on the host and never clears a stored value.
internal static class PlayniteContract
{
    internal const int Version = 1;
    internal const string ContractPath = "/api/playnite/contract";
    internal const string ObservationsPath = "/api/playnite/observations";
    internal const string InventoryPath = "/api/playnite/inventory";
    internal const int BatchSize = 500;
    internal const long MaxPlaytimeSeconds = 2000000000L;

    internal static bool Supports(PlayniteContractInfo? info)
        => info != null && info.SupportedVersions != null && info.SupportedVersions.Contains(Version);

    internal static long? UnixSeconds(DateTime? value)
    {
        if (!value.HasValue || value.Value.Year < 1971) return null;
        var seconds = new DateTimeOffset(value.Value.ToUniversalTime()).ToUnixTimeSeconds();
        return seconds < 0 ? (long?)null : seconds;
    }
}

[DataContract]
internal sealed class PlayniteContractInfo
{
    [DataMember(Name = "supported_versions")] public List<int>? SupportedVersions { get; set; }
    [DataMember(Name = "current_version")] public int CurrentVersion { get; set; }
}

[DataContract]
internal sealed class PlayniteEvidenceGame
{
    [DataMember(Name = "playnite_id")] public Guid PlayniteId { get; set; }
    [DataMember(Name = "name", EmitDefaultValue = false)] public string? Name { get; set; }
    [DataMember(Name = "platforms", EmitDefaultValue = false)] public List<string>? Platforms { get; set; }
    [DataMember(Name = "release_date", EmitDefaultValue = false)] public string? ReleaseDate { get; set; }
    [DataMember(Name = "playtime_seconds")] public long PlaytimeSeconds { get; set; }
    [DataMember(Name = "last_played_at", EmitDefaultValue = false)] public long? LastPlayedAt { get; set; }
    [DataMember(Name = "installed")] public bool Installed { get; set; }
    [DataMember(Name = "owned", EmitDefaultValue = false)] public bool? Owned { get; set; }
    [DataMember(Name = "favorite")] public bool Favorite { get; set; }
    [DataMember(Name = "status", EmitDefaultValue = false)] public string? Status { get; set; }
    [DataMember(Name = "modified_at", EmitDefaultValue = false)] public long? ModifiedAt { get; set; }

    // Built from plain values so the mapping is testable without Playnite database objects.
    // Playnite has no achievement or ownership model of its own: achievement counts are never
    // sent, and `owned` is only reported (false) for a wishlist entry.
    internal static PlayniteEvidenceGame Create(
        Guid id, string? name, IEnumerable<string?> platforms, DateTime? releaseDate, ulong playtime,
        DateTime? lastActivity, bool installed, bool favorite, string? status, DateTime? modified)
    {
        var platformNames = (platforms ?? Enumerable.Empty<string?>())
            .Where(item => !string.IsNullOrWhiteSpace(item)).Select(item => item!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase).Take(20).ToList();
        var normalizedStatus = string.IsNullOrWhiteSpace(status) ? null : status;
        return new PlayniteEvidenceGame
        {
            PlayniteId = id,
            Name = string.IsNullOrWhiteSpace(name) ? null : name!.Trim(),
            Platforms = platformNames.Count == 0 ? null : platformNames,
            ReleaseDate = releaseDate.HasValue && releaseDate.Value.Year > 0 ? releaseDate.Value.Date.ToString("yyyy-MM-dd") : null,
            PlaytimeSeconds = playtime > (ulong)PlayniteContract.MaxPlaytimeSeconds ? PlayniteContract.MaxPlaytimeSeconds : (long)playtime,
            LastPlayedAt = PlayniteContract.UnixSeconds(lastActivity),
            Installed = installed,
            Owned = string.Equals(normalizedStatus, "WISHLIST", StringComparison.Ordinal) ? false : (bool?)null,
            Favorite = favorite,
            Status = normalizedStatus,
            ModifiedAt = PlayniteContract.UnixSeconds(modified)
        };
    }
}

[DataContract]
internal sealed class PlayniteEvidenceBatch
{
    [DataMember(Name = "contract_version")] public int ContractVersion { get; set; } = PlayniteContract.Version;
    [DataMember(Name = "games")] public List<PlayniteEvidenceGame> Games { get; set; } = new List<PlayniteEvidenceGame>();
}

[DataContract]
internal sealed class PlayniteInventoryReport
{
    [DataMember(Name = "contract_version")] public int ContractVersion { get; set; } = PlayniteContract.Version;
    [DataMember(Name = "playnite_ids")] public List<Guid> PlayniteIds { get; set; } = new List<Guid>();
}
