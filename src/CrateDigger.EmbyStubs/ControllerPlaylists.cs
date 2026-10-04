namespace MediaBrowser.Controller.Playlists
{
    /// <summary>
    /// Playlist creation/management.
    /// SIGNATURE VERIFY: Emby versions differ (sync vs async, folder parameter).
    /// </summary>
    public interface IPlaylistManager
    {
        /// <summary>
        /// Create a playlist owned by <paramref name="userId"/> containing
        /// <paramref name="itemIds"/> in order; returns the new playlist's id.
        /// </summary>
        Guid CreatePlaylist(string name, Guid userId, IEnumerable<Guid> itemIds, string? parentId = null);
    }
}