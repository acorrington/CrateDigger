using CrateDigger.Plugin.Configuration;

namespace CrateDigger.Plugin.Tasks;

/// <summary>
/// One seed queue's identity and pipeline parameters (v0.3.0 dual-mode design).
///
/// Emby playlists are single-media-type, so audio and video seeds are necessarily
/// SEPARATE playlists with separate names/results — both watched by the same
/// debounce event and interval backstop.
/// </summary>
public sealed record SeedMode(
    string PlaylistName,
    string ResultName,
    string[] IncludeItemTypes,
    string MediaType,
    bool IncludeCatalogDetails)
{
    /// <summary>
    /// Enabled modes from config. Audio mode is on when SeedPlaylistName is set;
    /// video mode when SeedPlaylistNameVideo is set (empty = feature off).
    /// </summary>
    public static IReadOnlyList<SeedMode> GetModes(PluginConfiguration config)
    {
        var modes = new List<SeedMode>(2);

        var audioName = config.SeedPlaylistName?.Trim();
        if (!string.IsNullOrEmpty(audioName))
        {
            modes.Add(new SeedMode(
                audioName,
                string.IsNullOrWhiteSpace(config.SeedResultName) ? "CrateDigger Radio" : config.SeedResultName.Trim(),
                new[] { "Audio" },
                "Audio",
                IncludeCatalogDetails: false));
        }

        var videoName = config.SeedPlaylistNameVideo?.Trim();
        if (!string.IsNullOrEmpty(videoName))
        {
            modes.Add(new SeedMode(
                videoName,
                string.IsNullOrWhiteSpace(config.SeedResultNameVideo) ? "CrateDigger Video Radio" : config.SeedResultNameVideo.Trim(),
                new[] { "MusicVideo" },
                "Video",
                IncludeCatalogDetails: true));
        }

        return modes;
    }

    /// <summary>Find the mode whose seed playlist name matches (trimmed, case-insensitive).</summary>
    public static SeedMode? Match(string? playlistName, PluginConfiguration config)
    {
        var name = playlistName?.Trim();
        if (string.IsNullOrEmpty(name))
            return null;
        foreach (var mode in GetModes(config))
        {
            if (string.Equals(name, mode.PlaylistName, StringComparison.OrdinalIgnoreCase))
                return mode;
        }
        return null;
    }
}