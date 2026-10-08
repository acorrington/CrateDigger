namespace CrateDigger.Core.Models;

/// <summary>
/// Central defaults for playlist/queue names so config fallbacks, the settings page
/// placeholders, and startup auto-create all agree (v0.3.1).
///
/// Seed names are verb-forward self-documenting UI copy: they appear verbatim in every
/// Emby client's "Add to playlist" picker, so the name IS the instruction.
/// </summary>
public static class PlaylistNames
{
    public const string SeedAudio = "🎵 CrateDigger – Add Songs Here";

    public const string SeedVideo = "🎬 CrateDigger – Add Videos Here";

    /// <summary>Timestamped fallback base when the model doesn't return a name.</summary>
    public const string ResultAudio = "CrateDigger Radio";

    public const string ResultVideo = "CrateDigger Video Radio";
}