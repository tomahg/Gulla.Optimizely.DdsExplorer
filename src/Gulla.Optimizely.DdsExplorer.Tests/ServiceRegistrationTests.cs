using Gulla.Optimizely.DdsExplorer.Configuration;
using Gulla.Optimizely.DdsExplorer.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Gulla.Optimizely.DdsExplorer.Tests
{
    /// <summary>
    /// The explorer is registered from two places — <c>AddDdsExplorer()</c> and the initialization
    /// module that runs inside <c>AddCms()</c> — in either order, or from the module alone.
    /// <see cref="ServiceCollectionExtensions.AddDdsExplorerServices"/> stands in for the module here.
    /// </summary>
    public class ServiceRegistrationTests
    {
        private static ServiceCollection Services(IConfiguration configuration)
        {
            var services = new ServiceCollection();
            services.AddSingleton(configuration);
            services.AddLogging();
            return services;
        }

        private static IConfigurationRoot Configuration(Dictionary<string, string> values = null) =>
            new ConfigurationBuilder().AddInMemoryCollection(values ?? []).Build();

        private static DdsExplorerOptions Options(IServiceCollection services) =>
            services.BuildServiceProvider().GetRequiredService<IOptionsMonitor<DdsExplorerOptions>>().CurrentValue;

        [Fact]
        public void The_module_alone_registers_everything_the_pages_need()
        {
            var services = Services(Configuration());
            services.AddDdsExplorerServices();
            services.AddDefaultDdsExplorerAuthorizationPolicy();

            Assert.Contains(services, d => d.ServiceType == typeof(IDdsStoreService));
            Assert.Contains(services, d => d.ServiceType == typeof(SystemStoreClassifier));

            var provider = services.BuildServiceProvider();
            Assert.NotNull(provider.GetRequiredService<SystemStoreClassifier>());
            Assert.NotNull(provider.GetRequiredService<IOptions<AuthorizationOptions>>().Value.GetPolicy(DdsExplorerAuthorizationPolicy.Default));
        }

        [Fact]
        public void Registering_from_both_places_registers_each_service_once()
        {
            var services = Services(Configuration());
            services.AddDdsExplorerServices();
            services.AddDdsExplorer();
            services.AddDdsExplorer();

            Assert.Single(services, d => d.ServiceType == typeof(IDdsStoreService));
            Assert.Single(services, d => d.ServiceType == typeof(RawStoreReader));
        }

        [Fact]
        public void Configuration_is_bound_once_whatever_the_order()
        {
            var config = Configuration(new()
            {
                ["Gulla:DdsExplorer:SystemStorePrefixes:0"] = "MyCompany."
            });

            var moduleFirst = Services(config);
            moduleFirst.AddDdsExplorerServices();
            moduleFirst.AddDdsExplorer();

            var explorerFirst = Services(config);
            explorerFirst.AddDdsExplorer();
            explorerFirst.AddDdsExplorerServices();

            Assert.Equal(["MyCompany."], Options(moduleFirst).SystemStorePrefixes);
            Assert.Equal(["MyCompany."], Options(explorerFirst).SystemStorePrefixes);
        }

        [Fact]
        public void Configured_prefixes_replace_the_defaults_just_as_prefixes_set_in_code_do()
        {
            var config = Configuration(new()
            {
                ["Gulla:DdsExplorer:SystemStorePrefixes:0"] = "MyCompany.",
                ["Gulla:DdsExplorer:SystemStorePrefixes:1"] = "Other."
            });

            var services = Services(config);
            services.AddDdsExplorer();

            Assert.Equal(["MyCompany.", "Other."], Options(services).SystemStorePrefixes);
        }

        [Fact]
        public void The_default_prefixes_stay_when_configuration_sets_none()
        {
            var services = Services(Configuration(new() { ["Gulla:DdsExplorer:PageSize"] = "20" }));
            services.AddDdsExplorer();

            Assert.Equal(new DdsExplorerOptions().SystemStorePrefixes, Options(services).SystemStorePrefixes);
        }

        [Fact]
        public void Prefixes_set_in_code_replace_configured_ones()
        {
            var config = Configuration(new() { ["Gulla:DdsExplorer:SystemStorePrefixes:0"] = "FromConfig." });

            var services = Services(config);
            services.AddDdsExplorer(options => options.SystemStorePrefixes = ["FromCode."]);

            Assert.Equal(["FromCode."], Options(services).SystemStorePrefixes);
        }

        [Fact]
        public void Code_overrides_configuration_even_when_the_module_registered_first()
        {
            var config = Configuration(new() { ["Gulla:DdsExplorer:PageSize"] = "20" });

            var services = Services(config);
            services.AddDdsExplorerServices();
            services.AddDdsExplorer(options => options.PageSize = 10);

            Assert.Equal(10, Options(services).PageSize);
        }

        [Fact]
        public void Code_overrides_configuration_when_registered_before_the_module()
        {
            var config = Configuration(new() { ["Gulla:DdsExplorer:PageSize"] = "20" });

            var services = Services(config);
            services.AddDdsExplorer(options => options.PageSize = 10);
            services.AddDdsExplorerServices();

            Assert.Equal(10, Options(services).PageSize);
        }

        [Fact]
        public void A_changed_configuration_reaches_the_options_monitor()
        {
            var config = Configuration(new() { ["Gulla:DdsExplorer:PageSize"] = "20" });

            var services = Services(config);
            services.AddDdsExplorer();
            var monitor = services.BuildServiceProvider().GetRequiredService<IOptionsMonitor<DdsExplorerOptions>>();
            Assert.Equal(20, monitor.CurrentValue.PageSize);

            config["Gulla:DdsExplorer:PageSize"] = "30";
            config.Reload();

            Assert.Equal(30, monitor.CurrentValue.PageSize);
        }

        [Fact]
        public void A_policy_the_site_defines_itself_wins_over_the_default()
        {
            var services = Services(Configuration());
            services.AddDdsExplorerServices();
            services.AddDefaultDdsExplorerAuthorizationPolicy();
            services.AddDdsExplorer(authorization =>
                authorization.AddPolicy(DdsExplorerAuthorizationPolicy.Default, policy => policy.RequireRole("DdsPeople")));

            var policy = services.BuildServiceProvider().GetRequiredService<IOptions<AuthorizationOptions>>().Value
                .GetPolicy(DdsExplorerAuthorizationPolicy.Default);

            var roles = policy.Requirements.OfType<Microsoft.AspNetCore.Authorization.Infrastructure.RolesAuthorizationRequirement>().Single();
            Assert.Equal(["DdsPeople"], roles.AllowedRoles);
        }
    }
}
