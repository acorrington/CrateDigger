namespace MediaBrowser.Controller.Entities
{
    /// <summary>
    /// Query filter for library item lookups (shape modeled after InternalItemsQuery — verify).
    /// </summary>
    public class InternalItemsQuery
    {
        /// <summary>e.g. new[] { nameof(Audio) } to fetch only music tracks.</summary>
        public string[]? IncludeItemTypes { get; set; }

        public Guid? UserId { get; set; }

        public int? Limit { get; set; }

        public bool Recursive { get; set; } = true;
    }

    /// <summary>Base type for everything the library manages.</summary>
    public class BaseItem
    {
        public Guid Id { get; set; }

        public string Name { get; set; } = string.Empty;

        /// <summary>Album title (Audio/Album items).</summary>
        public string Album { get; set; } = string.Empty;

        /// <summary>Primary album artist display string.</summary>
        public string AlbumArtist { get; set; } = string.Empty;

        /// <summary>Per-track artist list.</summary>
        public List<string> Artists { get; set; } = new();

        /// <summary>Genre tags.</summary>
        public List<string> Genres { get; set; } = new();

        public Guid ParentId { get; set; }

        public bool IsFolder { get; set; }
    }

    /// <summary>A music track.</summary>
    public class Audio : BaseItem
    {
        /// <summary>Track length in ticks (100ns), if known.</summary>
        public long RunTimeTicks { get; set; }
    }
}