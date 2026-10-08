namespace CrateDigger.Core.Models;

/// <summary>
/// Turns a raw model-provided playlist name into a safe, filesystem/Emby-friendly one
/// (v0.3.1: AI-generated result names).
///
/// The name becomes an m3u folder on disk (<c>{name} [playlist]\{name}.m3u</c>), so we
/// strip path separators and control chars, collapse whitespace, and cap length. Returns
/// null when nothing usable remains, letting the caller fall back to the timestamped name.
/// </summary>
public static class PlaylistNameSanitizer
{
    public const int MaxLength = 60;

    /// <summary>Sanitize a model/user-provided name; null if it yields nothing usable.</summary>
    public static string? Sanitize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        // Replace any whitespace run (spaces, tabs, newlines) with a single space first,
        // so later splitting is stable regardless of what the model emitted.
        var s = string.Join(' ', raw.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

        // Turn path/structure separators into spaces BEFORE stripping, so "Vol/1" becomes
        // "Vol 1" (readable) rather than "Vol1" (glued). Then strip the remaining
        // characters invalid in Windows filenames / the "name [playlist]" convention.
        s = s.Replace('/', ' ').Replace('\\', ' ').Replace(':', ' ').Replace('|', ' ');
        var invalid = new HashSet<char>(Path.GetInvalidFileNameChars());
        invalid.UnionWith("[]{}<>\"*?");
        s = new string(s.Where(c => !invalid.Contains(c) && !char.IsControl(c)).ToArray());

        // Collapse again in case removing chars left double spaces.
        s = string.Join(' ', s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

        if (s.Length == 0)
            return null;
        if (s.Length > MaxLength)
            s = s[..MaxLength].Trim();

        // Reject names that are only punctuation/symbols after cleanup.
        if (!s.Any(char.IsLetterOrDigit))
            return null;

        return s;
    }
}