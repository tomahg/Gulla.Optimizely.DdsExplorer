using System;
using System.Collections.Generic;
using System.Linq;
using Gulla.Optimizely.DdsExplorer.Configuration;
using Gulla.Optimizely.DdsExplorer.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Gulla.Optimizely.DdsExplorer
{
    public static class ServiceCollectionExtensions
    {
        internal const string ConfigurationSection = "Gulla:DdsExplorer";

        /// <summary>
        /// Adds the DDS Explorer with default authorization (CmsAdmins, Administrators or WebAdmins).
        /// </summary>
        public static IServiceCollection AddDdsExplorer(this IServiceCollection services)
        {
            return AddDdsExplorer(services, null, null);
        }

        /// <summary>
        /// Adds the DDS Explorer with custom options and default authorization.
        /// </summary>
        public static IServiceCollection AddDdsExplorer(
            this IServiceCollection services,
            Action<DdsExplorerOptions> setupAction)
        {
            return AddDdsExplorer(services, setupAction, null);
        }

        /// <summary>
        /// Adds the DDS Explorer with default options and custom authorization.
        /// </summary>
        public static IServiceCollection AddDdsExplorer(
            this IServiceCollection services,
            Action<AuthorizationOptions> authorizationOptions)
        {
            return AddDdsExplorer(services, null, authorizationOptions);
        }

        /// <summary>
        /// Adds the DDS Explorer with custom options and custom authorization.
        /// </summary>
        public static IServiceCollection AddDdsExplorer(
            this IServiceCollection services,
            Action<DdsExplorerOptions> setupAction,
            Action<AuthorizationOptions> authorizationOptions)
        {
            services.AddDdsExplorerServices();

            // Registered after the configuration binding in AddDdsExplorerServices, so code always
            // overrides configuration — whether this call comes before or after AddCms().
            if (setupAction != null)
            {
                services.Configure(setupAction);
            }

            if (authorizationOptions != null)
            {
                services.AddAuthorization(authorizationOptions);
            }

            services.AddDefaultDdsExplorerAuthorizationPolicy();

            return services;
        }

        /// <summary>
        /// Registers everything the explorer needs to run, once however often it is called. Called
        /// from <c>AddDdsExplorer()</c> and from <see cref="DdsExplorerAuthorizationModule"/>: the menu
        /// item appears even in a site that never calls <c>AddDdsExplorer()</c>, so opening it must
        /// not then fail on a service that was never registered.
        /// </summary>
        internal static IServiceCollection AddDdsExplorerServices(this IServiceCollection services)
        {
            if (services.Any(d => d.ServiceType == typeof(DdsExplorerServicesMarker)))
            {
                return services;
            }

            services.AddSingleton<DdsExplorerServicesMarker>();

            services.AddOptions<DdsExplorerOptions>().Configure<IConfiguration>((options, configuration) =>
            {
                var section = configuration.GetSection(ConfigurationSection);
                section.Bind(options);

                // Bind adds configured list items to the ones already there, so configured prefixes
                // would come on top of the defaults. Configuration replaces the list, like code does.
                var prefixes = section.GetSection(nameof(DdsExplorerOptions.SystemStorePrefixes));
                if (prefixes.GetChildren().Any())
                {
                    options.SystemStorePrefixes = prefixes.Get<List<string>>();
                }
            });

            // Configure<IConfiguration>(…) registers no change token of its own, so without this
            // IOptionsMonitor would never see an edited appsettings.json.
            services.AddSingleton<IOptionsChangeTokenSource<DdsExplorerOptions>>(sp =>
                new ConfigurationChangeTokenSource<DdsExplorerOptions>(sp.GetRequiredService<IConfiguration>().GetSection(ConfigurationSection)));

            services.AddSingleton<SystemStoreClassifier>();
            services.AddSingleton<DdsItemSerializer>();
            services.AddSingleton<DdsItemConverter>();
            // Transient, not singleton: it resolves IDatabaseExecutor per call, which must come
            // from the request scope rather than be captured from the root provider.
            services.AddTransient<RawStoreReader>();
            services.AddTransient<IDdsStoreService, DdsStoreService>();

            return services;
        }

        /// <summary>
        /// Registers the default authorization policy unless one with the same name already exists.
        /// Runs as a <c>PostConfigure</c>, so a policy the site defines itself — through the
        /// <see cref="AuthorizationOptions"/> overload above, or through its own <c>AddAuthorization</c>
        /// call, whether registered before or after this one — always takes precedence.
        /// </summary>
        internal static IServiceCollection AddDefaultDdsExplorerAuthorizationPolicy(this IServiceCollection services)
        {
            services.AddAuthorization();
            services.PostConfigure<AuthorizationOptions>(options =>
            {
                if (options.GetPolicy(DdsExplorerAuthorizationPolicy.Default) != null)
                {
                    return;
                }

                options.AddPolicy(DdsExplorerAuthorizationPolicy.Default, policy =>
                {
                    policy.RequireRole("CmsAdmins", "Administrators", "WebAdmins");
                });
            });

            return services;
        }

        private sealed class DdsExplorerServicesMarker
        {
        }
    }
}
