using MediaBrowser.Controller.Entities;
using MediaBrowser.Model.Querying;

namespace MediaBrowser.Controller.Library
{
    /// <summary>Read access to the Emby library tree (verified signatures).</summary>
    public interface ILibraryManager
    {
        /// <summary>Verified: returns BaseItem[] (NOT List<BaseItem>).</summary>
        BaseItem[] GetItemList(InternalItemsQuery query);

        BaseItem? GetItemById(Guid id);

        /// <summary>Verified: used by the seed task to remove the seed playlist.</summary>
        void DeleteItem(BaseItem item, DeleteOptions options);
    }

    /// <summary>Verified members (used by seed-task playlist clearing).</summary>
    public class DeleteOptions
    {
        /// <summary>True deletes the playlist's backing folder/m3u (media files untouched).</summary>
        public bool DeleteFileLocation { get; set; }

        public bool DeleteFromExternalProvider { get; set; }
    }

    /// <summary>User lookup (verified: MediaBrowser.Controller.Library.IUserManager).</summary>
    public interface IUserManager
    {
        /// <summary>CrateDigger uses the internal-id and public-Guid flavors.</summary>
        User? GetUserById(long id);

        User? GetUserById(Guid id);

        User[] GetUserList(UserQuery query);
    }
}