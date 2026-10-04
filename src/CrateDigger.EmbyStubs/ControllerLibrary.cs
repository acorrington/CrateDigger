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