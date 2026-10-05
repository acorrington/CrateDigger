using MediaBrowser.Controller.Entities;

namespace MediaBrowser.Controller.Playlists
{
    /// <summary>
    /// Verified against 4.10.1.0: a single async factory taking a request DTO
    /// (the old Guid/userId signature was an early guess, corrected by probe).
    /// Verified events: PlaylistItemsAdded/Removed/Moved with event-args carrying
    /// the Playlist + entry ids (probe round 15).
    /// </summary>
    public interface IPlaylistManager
    {
        Task<PlaylistCreationResult> CreatePlaylist(PlaylistCreationRequest options);

        event EventHandler<PlaylistItemsAddedEventArgs> PlaylistItemsAdded;

        event EventHandler<PlaylistItemsRemovedEventArgs> PlaylistItemsRemoved;
    }

    /// <summary>Verified shape (Controller.Playlists) — carries ONLY the Playlist.</summary>
    public class PlaylistItemsAddedEventArgs : EventArgs
    {
        public Playlist Playlist { get; set; } = new();
    }

    /// <summary>Verified shape (Controller.Playlists).</summary>
    public class PlaylistItemsRemovedEventArgs : EventArgs
    {
        public Playlist Playlist { get; set; } = new();

        public long[] ListItemEntryIds { get; set; } = Array.Empty<long>();
    }

    /// <summary>
    /// Playlist entity (real type: MediaBrowser.Controller.Playlists.Playlist derives
    /// from Folder/BaseItem — members used: Name via BaseItem).
    /// </summary>
    public class Playlist : BaseItem
    {
        public string PlaylistMediaType { get; set; } = string.Empty;
    }

    /// <summary>Verified members.</summary>
    public class PlaylistCreationRequest
    {
        public string Name { get; set; } = string.Empty;

        /// <summary>Item keys are Int64 internal ids (BaseItem.InternalId).</summary>
        public long[] ItemIdList { get; set; } = Array.Empty<long>();

        /// <summary>"Audio" for music playlists.</summary>
        public string? MediaType { get; set; }

        /// <summary>Owning user — required.</summary>
        public User? User { get; set; }

        public bool IsPublic { get; set; }
    }

    /// <summary>Verified members: Id is a string.</summary>
    public class PlaylistCreationResult
    {
        public string Id { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        public int ItemAddedCount { get; set; }
    }
}