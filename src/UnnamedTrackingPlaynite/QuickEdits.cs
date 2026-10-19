using System;
using System.Globalization;

namespace UnnamedTrackingPlaynite;

/// <summary>Small pure helpers behind the quick edits in the game menu.</summary>
internal static class QuickEdits
{
    /// <summary>Parses "7", "7.5" or "7,5" as a score out of 10 and returns Playnite's 0-100 scale; blank clears.</summary>
    internal static bool TryParseScore(string? text, out int? playniteScore)
    {
        playniteScore = null;
        var value = (text ?? string.Empty).Trim();
        if (value.Length == 0) return true;
        value = value.Replace(',', '.');
        if (!decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var outOfTen) || outOfTen < 0 || outOfTen > 10)
            return false;
        playniteScore = (int)Math.Round(outOfTen * 10m, MidpointRounding.AwayFromZero);
        return true;
    }

    /// <summary>Appends a dated line to the game's notes, keeping everything already written.</summary>
    internal static string AppendNote(string? existing, string note, DateTime today)
    {
        var line = today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ": " + note.Trim();
        return string.IsNullOrWhiteSpace(existing) ? line : existing!.TrimEnd() + "\n" + line;
    }
}
