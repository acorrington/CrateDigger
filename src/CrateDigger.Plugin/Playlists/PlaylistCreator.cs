using CrateDigger.Core.Models;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Playlists;
using MediaBrowser.Model.Logging;

namespace CrateDigger.Plugin.Playlists;

/// <summary>
/// Wraps Emby's IPlaylistManager (verified against 4.10.1.0:
/// Task&lt;PlaylistCreationResult&gt; CreatePlaylist(PlaylistCreationRequest)).
/// </summary>
public sealed class PlaylistCreator
{
    private readonly IPlaylistManager _playlistManager;
    private readonly ILogger _logger;

    public PlaylistCreator(IPlaylistManager playlistManager, ILogger logger)
    {
        _playlistManager = playlistManager;
        _logger = logger;
    }

    /// <summary>Create a native playlist containing <paramref name="tracks"/> in order.</summary>
    public async Task<string> CreateAsync(
        string name,
        IReadOnlyList<LibraryTrack> tracks,
        User owner)
    {
        var ids = new long[tracks.Count];
        for (var i = 0; i < tracks.Count; i++)
        {
            if (long.TryParse(tracks[i].Id, out var internalId))
                ids[i] = internalId;
            else
                throw new InvalidOperationException(
                    $"Track '{tracks[i].Track.Display}' has an invalid library id '{tracks[i].Id}'.");
        }

        var result = await _playlistManager.CreatePlaylist(new PlaylistCreationRequest
        {
            Name = name,
            ItemIdList = ids,
            MediaType = "Audio",
            User = owner,
        }).ConfigureAwait(false);

        _logger.Info("Created playlist '{0}' ({1}) with {2} tracks", result.Name, result.Id, result.ItemAddedCount);
        return result.Id;
    }
}