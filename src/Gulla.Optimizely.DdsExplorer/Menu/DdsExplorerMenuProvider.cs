using System.Collections.Generic;
using EPiServer.Shell.Navigation;
using EPiServer.Shell.Navigation.Internal;
using Gulla.Optimizely.DdsExplorer.Configuration;
using Gulla.Optimizely.DdsExplorer.Controllers;

namespace Gulla.Optimizely.DdsExplorer.Menu
{
    [MenuProvider]
    public class DdsExplorerMenuProvider : IMenuProvider, IEPiProductMenuProvider
    {
        public IEnumerable<MenuItem> GetMenuItems()
        {
            return
            [
                new SubPathUrlMenuItem("DDS Explorer", MenuPaths.Global + "/cms/admin/tools/ddsexplorer", "/" + DdsExplorerController.RoutePrefix)
                {
                    AuthorizationPolicy = DdsExplorerAuthorizationPolicy.Default,
                    SortIndex = 1337
                }
            ];
        }
    }
}
