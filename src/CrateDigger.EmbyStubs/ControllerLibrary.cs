using MediaBrowser.Controller.Entities;

namespace MediaBrowser.Controller.Library
{
    /// <summary>Read access to the Emby library tree.</summary>
    public interface ILibraryManager
    {
        /// <summary>Execute a query and materialize matching items.</summary>
        List<BaseItem> GetItemList(InternalItemsQuery query);

        /// <summary>Fetch a single item by id, or null when missing.</summary>
        BaseItem? GetItemById(Guid id);
    }
}