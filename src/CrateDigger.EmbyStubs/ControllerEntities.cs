namespace MediaBrowser.Controller.Entities
{
    /// <summary>Query filter for library item lookups (verified members).</summary>
    public class InternalItemsQuery
    {
        /// <summary>e.g. new[] { nameof(Audio.Audio) } to fetch only music tracks.</summary>
        public string[]? IncludeItemTypes { get; set; }

        public bool Recursive { get; set; } = true;

        public int? Limit { get; set; }

        public bool? IsVirtualItem { get; set; }

        /// <summary>Verified: InternalItemsQuery.ParentIds (Int64[]) — no singular ParentId property.</summary>
        public long[] ParentIds { get; set; } = Array.Empty<long>();

        /// <summary>
        /// Verified: direct-parent object reference — THIS is what the working REST
        /// ?ParentId= form populates (ParentIds[] has different semantics and returns
        /// nothing for direct playlist children).
        /// </summary>
        public BaseItem? Parent { get; set; }

        /// <summary>
        /// Verified referenced by LibraryManager.SetUserAndParents; true keeps a direct
        /// Parent constraint from being rewritten into an ancestor-based filter.
        /// </summary>
        public bool SkipAncestorNormalization { get; set; }

        /// <summary>Verified: exact-name filter used to locate the seed playlist.</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>Verified member of InternalItemsQuery (used by diagnostic query modes).</summary>
        public bool EnableTotalRecordCount { get; set; }
    }

    /// <summary>Base type for everything the library manages (verified members).</summary>
    public class BaseItem
    {
        public Guid Id { get; set; }

        /// <summary>
        /// Int64 internal database id — the identifier PlaylistCreationRequest.ItemIdList
        /// expects (NOT the public Guid).
        /// </summary>
        public long InternalId { get; set; }

        public string Name { get; set; } = string.Empty;

        public string Album { get; set; } = string.Empty;

        /// <summary>Verified as string[] (not List<string>).</summary>
        public string[] Genres { get; set; } = Array.Empty<string>();

        public string Path { get; set; } = string.Empty;

        public Guid ParentId { get; set; }

        public long? RunTimeTicks { get; set; }

        /// <summary>
        /// Playlist entry id (row id within a playlist) — populated when the item is
        /// queried as a playlist child; this is what IPlaylistManager.RemoveFromPlaylist
        /// expects. Verified: BaseItem.ListItemEntryId : Int64 (probe round 17).
        /// </summary>
        public long ListItemEntryId { get; set; }
    }

    /// <summary>Owning/acting user entity (PlaylistCreationRequest.User).</summary>
    public class User
    {
        public Guid Id { get; set; }

        public long InternalId { get; set; }

        public string Name { get; set; } = string.Empty;
    }
}

namespace MediaBrowser.Controller.Entities.Audio
{
    /// <summary>
    /// A music track with artist metadata.
    /// Verified namespace: MediaBrowser.Controller.Entities.Audio.Audio —
    /// Artists/AlbumArtists live here (IHasArtist/IHasAlbumArtist), NOT on BaseItem.
    /// </summary>
    public class Audio : MediaBrowser.Controller.Entities.BaseItem
    {
        public string[] Artists { get; set; } = Array.Empty<string>();

        public string[] AlbumArtists { get; set; } = Array.Empty<string>();
    }
}