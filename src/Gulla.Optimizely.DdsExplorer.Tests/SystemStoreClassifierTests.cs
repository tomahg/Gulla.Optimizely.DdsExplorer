using Gulla.Optimizely.DdsExplorer.Configuration;
using Gulla.Optimizely.DdsExplorer.Services;
using Microsoft.Extensions.Options;

namespace Gulla.Optimizely.DdsExplorer.Tests
{
    public class SystemStoreClassifierTests
    {
        private sealed class StaticOptions(DdsExplorerOptions value) : IOptionsMonitor<DdsExplorerOptions>
        {
            public DdsExplorerOptions CurrentValue => value;
            public DdsExplorerOptions Get(string name) => value;
            public IDisposable OnChange(Action<DdsExplorerOptions, string> listener) => null;
        }

        [Theory]
        [InlineData("EPiServer.Shell.Storage.PersonalizedViewSettingsStorage", true)]
        [InlineData("episerver.cms.something", true)]
        [InlineData("Optimizely.Something", true)]
        [InlineData("MySite.Favourites", false)]
        [InlineData("", false)]
        [InlineData(null, false)]
        public void Default_prefixes(string name, bool expected)
        {
            var classifier = new SystemStoreClassifier(new StaticOptions(new DdsExplorerOptions()));

            Assert.Equal(expected, classifier.IsSystem(name));
        }

        [Fact]
        public void Custom_prefixes_replace_the_defaults()
        {
            var classifier = new SystemStoreClassifier(new StaticOptions(new DdsExplorerOptions { SystemStorePrefixes = ["MySite."] }));

            Assert.True(classifier.IsSystem("MySite.Favourites"));
            Assert.False(classifier.IsSystem("EPiServer.Anything"));
        }
    }
}
