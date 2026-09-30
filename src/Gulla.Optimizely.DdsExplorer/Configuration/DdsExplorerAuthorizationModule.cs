using EPiServer.Framework;
using EPiServer.Framework.Initialization;
using EPiServer.ServiceLocation;

namespace Gulla.Optimizely.DdsExplorer.Configuration
{
    /// <summary>
    /// Makes the explorer work even in a site that never calls <c>AddDdsExplorer()</c>, or that
    /// calls it after <c>AddCms()</c>. Two things depend on it:
    /// <list type="bullet">
    /// <item><see cref="DdsExplorerAuthorizationPolicy.Default"/> must resolve. Anything that refers to
    /// the policy by name — the menu item, the controllers — throws <c>InvalidOperationException:
    /// No policy found: DdsExplorerAdmin.</c> when it is missing, and that surfaces during startup
    /// rather than on the page, which makes it hard to diagnose. The fallback runs as a
    /// <c>PostConfigure</c>, so a policy the site defines itself always wins.</item>
    /// <item>The services must be registered, because the menu item shows up either way and
    /// opening it would otherwise fail on DI. Registration is idempotent, so a later
    /// <c>AddDdsExplorer()</c> only adds its own options and policy.</item>
    /// </list>
    /// </summary>
    [InitializableModule]
    public class DdsExplorerAuthorizationModule : IConfigurableModule
    {
        public void ConfigureContainer(ServiceConfigurationContext context)
        {
            context.Services.AddDdsExplorerServices();
            context.Services.AddDefaultDdsExplorerAuthorizationPolicy();
        }

        public void Initialize(InitializationEngine context)
        {
        }

        public void Uninitialize(InitializationEngine context)
        {
        }
    }
}
