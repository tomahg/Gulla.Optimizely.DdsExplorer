using System;
using EPiServer.Shell.Navigation;
using Microsoft.AspNetCore.Http;

namespace Gulla.Optimizely.DdsExplorer.Menu
{
    /// <summary>
    /// A <see cref="UrlMenuItem"/> that also counts as selected on any page below its URL.
    /// </summary>
    /// <remarks>
    /// The shell decides which product's left rail a page shows on the server: the
    /// <c>&lt;platform-navigation&gt;</c> tag helper renders <c>data-epi-product-id</c> from the
    /// menu item that <see cref="MenuItem.IsSelected"/> picks, and the base implementation only
    /// accepts an exact path match. On <c>/DdsExplorer/store/{name}</c> nothing matched, the
    /// product id came out empty, and the rail sat on its loading dots forever. Selecting this
    /// item selects its parents too (MenuNode propagates it), so the Settings rail loads with
    /// DDS Explorer highlighted.
    /// </remarks>
    public class SubPathUrlMenuItem : UrlMenuItem
    {
        public SubPathUrlMenuItem(string text, string path, string url)
            : base(text, path, url)
        {
        }

        public override bool IsSelected(HttpContext requestContext)
        {
            if (base.IsSelected(requestContext))
            {
                return true;
            }

            var url = Url?.Trim('/');
            var path = requestContext?.Request?.Path.Value?.Trim('/');

            return !string.IsNullOrEmpty(url)
                && path != null
                && path.StartsWith(url + "/", StringComparison.OrdinalIgnoreCase);
        }
    }
}
