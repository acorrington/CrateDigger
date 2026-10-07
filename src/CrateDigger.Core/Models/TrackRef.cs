namespace CrateDigger.Core.Models;

/// <summary>
/// A human-facing reference to a track exactly as it appears in text,
/// prompt output, or LLM suggestions — artist/title pair, nothing Emby-specific.
/// <c>Year</c> (v0.3.0) is optional enrichment for video catalogs, where genre
/// metadata is typically absent (music videos, per Reel's installed library).
/// </summary>
public sealed record TrackRef(
    string Artist,
    string Title,
    string? Album = null,
    string? Genre = null,
    int? Year = null)
{
    /// <summary>Canonical "Artist - Title" rendering used for prompts and logs.</summary>
    public string Display => string.IsNullOrWhiteSpace(Artist) ? Title : $"{Artist} - {Title}";

    /// <summary>Detail suffix for prompts: " (1983 · synth pop)" — only fields that exist.</summary>
    public string DetailSuffix
    {
        get
        {
            var parts = new List<string>();
            if (Year is > 1800 and < 2200) parts.Add(Year.Value.ToString());
            if (!string.IsNullOrWhiteSpace(Genre)) parts.Add(Genre!);
            return parts.Count == 0 ? string.Empty : $" ({string.Join(" · ", parts)})";
        }
    }
}