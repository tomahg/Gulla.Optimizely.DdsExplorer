using System.Collections.Generic;
using Gulla.Optimizely.DdsExplorer.Models;

namespace Gulla.Optimizely.DdsExplorer.Services
{
    public interface IDdsStoreService
    {
        IReadOnlyList<StoreSummary> ListStores();

        /// <returns>Null when no store has that name.</returns>
        StoreDetails GetStore(string storeName);

        /// <returns>Null when no store has that name.</returns>
        ItemPage GetItems(string storeName, int page);

        /// <returns>Null when the store or the item does not exist.</returns>
        ItemDetails GetItem(string storeName, string id);

        /// <param name="version">The <see cref="ItemDetails.Version"/> of the copy that was edited.</param>
        SaveResult SaveItem(string storeName, string id, string json, string version, string user);

        /// <returns>False when the store or the item does not exist.</returns>
        bool DeleteItem(string storeName, string id, string user);

        /// <returns>The number of items deleted, or null when the store does not exist.</returns>
        int? DeleteItems(string storeName, IReadOnlyCollection<string> ids, string user);

        /// <returns>The number of items the store held, or null when the store does not exist.</returns>
        long? EmptyStore(string storeName, string user);

        /// <returns>False when the store does not exist.</returns>
        bool DeleteStore(string storeName, string user);
    }
}
