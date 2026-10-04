using CrateDigger.Core.Models;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Library;

namespace CrateDigger.Plugin.Library;

/// <summary>
/// Extracts the music catalog from Emby and maps it into Core's server-agnostic shape.
/// <see cref="LibraryTrack.Id"/> carries the item's InternalId (Int64 as string) — the
/// identifier IPlaylistManager's PlaylistCreationRequest expects.
/// </summary>
public sealed class LibraryExtractor
{
    private readonly ILibraryManager _libraryManager;

    public LibraryExtractor(ILibraryManager libraryManager)
    {
        _libraryManager = libraryManager;
    }

    /// <summary>All Audio items in the library as fuzzy-matchable track references.</summary>
    public IReadOnlyList<LibraryTrack> GetTracks()
    {
        BaseItem[] items = _libraryManager.GetItemList(new InternalItemsQuery
        {
            IncludeItemTypes = new[] { nameof(Audio) },
            Recursive = true,
        });

        return items
            .Where(item => !string.IsNullOrWhiteSpace(item.Name))
            .Select(Map)
            .ToList();
    }

    private static LibraryTrack Map(BaseItem item)
    {
        // Artists live on the Audio subtype (IHasArtist / IHasAlbumArtist), not BaseItem.
        string? artist = null;
        if (item is Audio audio)
        {
            artist = audio.Artists?.FirstOrDefault(a => !string.IsNullOrWhiteSpace(a))
                     ?? audio.AlbumArtists?.FirstOrDefault(a => !string.IsNullOrWhiteSpace(a));
        }

        artist ??= "Unknown Artist";

        var genre = item.Genres is { Length: > 0 } g ? string.Join(", ", g) : null;

        var track = new TrackRef(
            artist.Trim(),
            item.Name.Trim(),
            string.IsNullOrWhiteSpace(item.Album) ? null : item.Album.Trim(),
            genre);

        return new LibraryTrack(item.InternalId.ToString(), track);
    }
}