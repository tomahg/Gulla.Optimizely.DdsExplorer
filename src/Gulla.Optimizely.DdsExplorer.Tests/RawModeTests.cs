using Gulla.Optimizely.DdsExplorer.Services;

namespace Gulla.Optimizely.DdsExplorer.Tests
{
    public class RawModeTests
    {
        [Fact]
        public void View_name_is_bracket_quoted_with_closing_brackets_doubled()
        {
            Assert.Equal("[dbo].[VW_EPiServer.Find.Framework.BestBets.BestBet]", RawStoreReader.ViewName("EPiServer.Find.Framework.BestBets.BestBet"));
            Assert.Equal("[dbo].[VW_a]]; drop table x;--]", RawStoreReader.ViewName("a]; drop table x;--"));
        }

        [Fact]
        public void Type_names_lose_version_culture_and_key_but_keep_the_assembly()
        {
            var stored = "System.Collections.Generic.List`1[[EPiServer.Find.Framework.BestBets.IBestBetCriterion, EPiServer.Find.Framework, Version=16.3.0.0, Culture=neutral, PublicKeyToken=8fe83dea738b45b7]], System.Private.CoreLib, Version=10.0.0.0, Culture=neutral, PublicKeyToken=7cec85d7bea7798e";

            Assert.Equal(
                "System.Collections.Generic.List`1[[EPiServer.Find.Framework.BestBets.IBestBetCriterion, EPiServer.Find.Framework]], System.Private.CoreLib",
                DdsStoreService.ShortTypeName(stored));
        }

        [Fact]
        public void Plain_type_names_are_unchanged()
        {
            Assert.Equal("System.String", DdsStoreService.ShortTypeName("System.String"));
            Assert.Null(DdsStoreService.ShortTypeName(null));
        }
    }
}
