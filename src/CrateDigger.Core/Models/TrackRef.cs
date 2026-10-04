namespace CrateDigger.Core.Models;

/// <summary>
/// A human-facing reference to a track exactly as it appears in text,
/// prompt output, or LLM suggestions — artist/title pair, nothing Emby-specific.
/// </summary>
public sealed record TrackRef(string Artist, string Title, string? Album = null, string? Genre = null)
{
    /// <summary>Canonical "Artist - Title" rendering used for prompts and logs.</summary>
    public string Display => string.IsNullOrWhiteSpace(Artist) ? Title : $"{Artist} - {Title}";
}