using System;
using System.Linq;
using Gulla.Optimizely.DdsExplorer.Configuration;
using Microsoft.Extensions.Options;

namespace Gulla.Optimizely.DdsExplorer.Services
{
    /// <summary>
    /// Decides which stores get the "System" badge and the extra warning before they are emptied
    /// or deleted. Purely advisory — nothing is blocked on it.
    /// </summary>
    public class SystemStoreClassifier
    {
        private readonly IOptionsMonitor<DdsExplorerOptions> _options;

        public SystemStoreClassifier(IOptionsMonitor<DdsExplorerOptions> options)
        {
            _options = options;
        }

        public bool IsSystem(string storeName)
        {
            if (string.IsNullOrEmpty(storeName))
            {
                return false;
            }

            var prefixes = _options.CurrentValue.SystemStorePrefixes;
            return prefixes != null && prefixes.Any(p => !string.IsNullOrEmpty(p) && storeName.StartsWith(p, StringComparison.OrdinalIgnoreCase));
        }
    }
}
