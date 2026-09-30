using System.Collections.Generic;

namespace Gulla.Optimizely.DdsExplorer.Configuration
{
    public class DdsExplorerOptions
    {
        /// <summary>
        /// Number of items per page in a store's item list.
        /// </summary>
        public int PageSize { get; set; } = 50;

        /// <summary>
        /// Store name prefixes that mark a store as belonging to Optimizely itself. Such stores get
        /// a "System" badge and an extra warning before they are emptied or deleted — deleting one
        /// can break the site. They are never blocked; this is a warning, not a lock.
        /// Matched case-insensitively.
        /// </summary>
        public List<string> SystemStorePrefixes { get; set; } =
        [
            "EPiServer.",
            "Episerver.",
            "Optimizely.",
            "Mediachase.",
            "EPiServer_",
            "Episerver_",
            "Optimizely_",
            "Forms.",
            "Find."
        ];
    }
}
