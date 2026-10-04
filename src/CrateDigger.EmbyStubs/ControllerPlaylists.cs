using MediaBrowser.Controller.Entities;

namespace MediaBrowser.Controller.Playlists
{
    /// <summary>
    /// Verified against 4.10.1.0: a single async factory taking a request DTO
    /// (the old Guid/userId signature was an early guess, corrected by probe).
    /// </summary>
    public interface IPlaylistManager
    {
        Task<PlaylistCreationResult> CreatePlaylist(PlaylistCreationRequest options);
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