using EPiServer.Data;
using EPiServer.Data.Dynamic;
using Gulla.Optimizely.DdsExplorer.Models;
using Gulla.Optimizely.DdsExplorer.Services;

namespace Gulla.Optimizely.DdsExplorer.Tests
{
    /// <summary>
    /// A save is refused unless the version it was edited from is the stored item's current
    /// version. These pin down that the version tracks every part of the stored item — so a save
    /// from a stale copy is caught instead of reverting what changed in the meantime.
    /// </summary>
    public class ItemVersionTests
    {
        private static readonly Identity Id = Identity.Parse("42:6F1C0E3A-5B8B-4E1B-9C3E-2A1F0B7D9E11");

        private static readonly DdsPropertyDescriptor[] Properties =
        [
            new("Name", typeof(string), PropertyMapType.Inline),
            new("Count", typeof(int), PropertyMapType.Inline),
            new("Tags", typeof(List<string>), PropertyMapType.Collection)
        ];

        private readonly DdsItemSerializer _serializer = new();

        private string VersionOf(Dictionary<string, object> values) =>
            DdsItemSerializer.VersionOf(_serializer.ToJson(Id, values, Properties));

        private static Dictionary<string, object> Stored() => new()
        {
            ["Name"] = "Alpha",
            ["Count"] = 3,
            ["Tags"] = new List<string> { "a", "b" }
        };

        [Fact]
        public void The_same_stored_item_always_has_the_same_version()
        {
            Assert.Equal(VersionOf(Stored()), VersionOf(Stored()));
        }

        [Fact]
        public void A_change_to_a_property_the_editor_did_not_touch_changes_the_version()
        {
            // The lost-update case: the editor opens the item, something else changes Count, and
            // the editor saves a change to Name. The version the editor holds must no longer match.
            var opened = VersionOf(Stored());

            var changedMeanwhile = Stored();
            changedMeanwhile["Count"] = 4;

            Assert.NotEqual(opened, VersionOf(changedMeanwhile));
        }

        [Fact]
        public void A_change_to_a_collection_changes_the_version()
        {
            var changed = Stored();
            changed["Tags"] = new List<string> { "a", "b", "c" };

            Assert.NotEqual(VersionOf(Stored()), VersionOf(changed));
        }

        [Fact]
        public void A_value_set_to_null_changes_the_version()
        {
            var changed = Stored();
            changed["Name"] = null;

            Assert.NotEqual(VersionOf(Stored()), VersionOf(changed));
        }
    }
}
