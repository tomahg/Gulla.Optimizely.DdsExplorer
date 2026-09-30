using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using EPiServer.Data;
using EPiServer.Data.Dynamic;
using Gulla.Optimizely.DdsExplorer.Models;
using Gulla.Optimizely.DdsExplorer.Services;

namespace Gulla.Optimizely.DdsExplorer.Tests
{
    public class DdsItemConverterTests
    {
        public enum Color { Red, Green, Blue }

        [System.Flags]
        public enum Access { None = 0, Read = 1, Write = 2 }

        private const string ItemId = "42:6F1C0E3A-5B8B-4E1B-9C3E-2A1F0B7D9E11";

        private static readonly DdsPropertyDescriptor[] Properties =
        [
            new("Name", typeof(string), PropertyMapType.Inline),
            new("Count", typeof(int), PropertyMapType.Inline),
            new("Big", typeof(long), PropertyMapType.Inline),
            new("Price", typeof(decimal), PropertyMapType.Inline),
            new("Enabled", typeof(bool), PropertyMapType.Inline),
            new("Created", typeof(DateTime), PropertyMapType.Inline),
            new("Key", typeof(Guid), PropertyMapType.Inline),
            new("Color", typeof(Color), PropertyMapType.Inline),
            new("Optional", typeof(int?), PropertyMapType.Inline),
            new("Tags", typeof(List<string>), PropertyMapType.Collection),
            new("Owner", typeof(object), PropertyMapType.Reference),
            new("Lost", null, PropertyMapType.Inline)
        ];

        private static readonly Dictionary<string, object> Values = new()
        {
            ["Name"] = "Alpha",
            ["Count"] = 3,
            ["Big"] = 9007199254740993L,
            ["Price"] = 12.50m,
            ["Enabled"] = true,
            ["Created"] = new DateTime(2026, 9, 29, 10, 15, 0, DateTimeKind.Utc),
            ["Key"] = new Guid("0f8fad5b-d9cb-469f-a165-70867728950e"),
            ["Color"] = Color.Green,
            ["Optional"] = null,
            ["Tags"] = new List<string> { "a", "b" },
            ["Owner"] = new { Name = "Bob" },
            ["Lost"] = "whatever"
        };

        private readonly DdsItemSerializer _serializer = new();
        private readonly DdsItemConverter _converter = new();

        private JsonObject Original() => _serializer.ToJson(Identity.Parse(ItemId), Values, Properties);

        private ConversionResult Save(Action<JsonObject> edit)
        {
            var original = Original();
            var edited = (JsonObject)original.DeepClone();
            edit(edited);
            return _converter.Convert(edited.ToJsonString(), original, Properties);
        }

        private static string ErrorFor(ConversionResult result, string field) =>
            result.Errors.SingleOrDefault(e => e.Field == field)?.Message;

        [Fact]
        public void Unchanged_document_succeeds_with_no_changes()
        {
            var result = _converter.Convert(Original().ToJsonString(), Original(), Properties);

            Assert.True(result.Success, string.Join("; ", result.Errors.Select(e => e.Field + ": " + e.Message)));
            Assert.Empty(result.Changes);
        }

        [Fact]
        public void Unchanged_document_with_different_formatting_succeeds_with_no_changes()
        {
            var indented = Original().ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true });

            var result = _converter.Convert(indented, Original(), Properties);

            Assert.True(result.Success);
            Assert.Empty(result.Changes);
        }

        [Fact]
        public void Long_beyond_double_precision_round_trips_unchanged()
        {
            Assert.Equal("9007199254740993", Original()["Big"]!.ToJsonString());
        }

        [Fact]
        public void Only_changed_properties_are_returned_with_their_declared_types()
        {
            var result = Save(d =>
            {
                d["Name"] = "Beta";
                d["Count"] = 7;
                d["Price"] = 1.25m;
                d["Enabled"] = false;
                d["Created"] = "2027-01-02T03:04:05.0000000Z";
                d["Key"] = "11111111-2222-3333-4444-555555555555";
                d["Color"] = "Blue";
                d["Optional"] = 5;
            });

            Assert.True(result.Success, string.Join("; ", result.Errors.Select(e => e.Field + ": " + e.Message)));
            Assert.Equal("Beta", result.Changes["Name"]);
            Assert.Equal(7, Assert.IsType<int>(result.Changes["Count"]));
            Assert.Equal(1.25m, Assert.IsType<decimal>(result.Changes["Price"]));
            Assert.False(Assert.IsType<bool>(result.Changes["Enabled"]));
            var created = Assert.IsType<DateTime>(result.Changes["Created"]);
            Assert.Equal(DateTimeKind.Utc, created.Kind);
            Assert.Equal(new DateTime(2027, 1, 2, 3, 4, 5, DateTimeKind.Utc), created);
            Assert.Equal(new Guid("11111111-2222-3333-4444-555555555555"), result.Changes["Key"]);
            Assert.Equal(Color.Blue, result.Changes["Color"]);
            Assert.Equal(5, result.Changes["Optional"]);
            Assert.Equal(8, result.Changes.Count);
        }

        [Fact]
        public void Nullable_can_be_set_to_null()
        {
            var original = _serializer.ToJson(Identity.Parse(ItemId), new Dictionary<string, object>(Values) { ["Optional"] = 1 }, Properties);
            var edited = (JsonObject)original.DeepClone();
            edited["Optional"] = null;

            var result = _converter.Convert(edited.ToJsonString(), original, Properties);

            Assert.True(result.Success);
            Assert.True(result.Changes.ContainsKey("Optional"));
            Assert.Null(result.Changes["Optional"]);
        }

        [Fact]
        public void Null_is_rejected_for_non_nullable_value_type()
        {
            var result = Save(d => d["Count"] = null);

            Assert.False(result.Success);
            Assert.Contains("not nullable", ErrorFor(result, "Count"));
            Assert.Empty(result.Changes);
        }

        [Theory]
        [InlineData("\"7\"")]
        [InlineData("7.5")]
        [InlineData("99999999999")]
        [InlineData("true")]
        [InlineData("[]")]
        public void Int_rejects_wrong_shapes_and_overflow(string json)
        {
            var result = Save(d => d["Count"] = JsonNode.Parse(json));

            Assert.False(result.Success);
            Assert.NotNull(ErrorFor(result, "Count"));
        }

        [Theory]
        [InlineData("\"Purple\"")]
        [InlineData("\"green\"")]
        [InlineData("\"1\"")]
        [InlineData("17")]
        public void Enum_rejects_unknown_names_wrong_case_numeric_strings_and_undefined_numbers(string json)
        {
            var result = Save(d => d["Color"] = JsonNode.Parse(json));

            Assert.False(result.Success);
            Assert.NotNull(ErrorFor(result, "Color"));
        }

        [Fact]
        public void Enum_accepts_defined_number()
        {
            var result = Save(d => d["Color"] = 2);

            Assert.True(result.Success);
            Assert.Equal(Color.Blue, result.Changes["Color"]);
        }

        [Fact]
        public void Flags_enum_accepts_combination_by_name()
        {
            Assert.True(DdsItemConverter.TryConvertValue(JsonValue.Create("Read, Write"), typeof(Access), out var value, out _));
            Assert.Equal(Access.Read | Access.Write, value);
        }

        [Theory]
        [InlineData("not-a-guid")]
        [InlineData("")]
        public void Guid_rejects_invalid_text(string text)
        {
            var result = Save(d => d["Key"] = text);

            Assert.False(result.Success);
            Assert.Contains("Guid", ErrorFor(result, "Key"));
        }

        [Fact]
        public void DateTime_rejects_invalid_text()
        {
            var result = Save(d => d["Created"] = "yesterday");

            Assert.False(result.Success);
            Assert.Contains("ISO 8601", ErrorFor(result, "Created"));
        }

        [Fact]
        public void Adding_a_property_is_rejected()
        {
            var result = Save(d => d["Extra"] = 1);

            Assert.False(result.Success);
            Assert.NotNull(ErrorFor(result, "Extra"));
        }

        [Fact]
        public void Removing_a_property_is_rejected()
        {
            var result = Save(d => d.Remove("Name"));

            Assert.False(result.Success);
            Assert.Contains("Missing", ErrorFor(result, "Name"));
        }

        [Fact]
        public void Keys_are_case_sensitive()
        {
            var result = Save(d =>
            {
                var value = d["Name"]!.DeepClone();
                d.Remove("Name");
                d["name"] = value;
            });

            Assert.False(result.Success);
            Assert.NotNull(ErrorFor(result, "name"));
            Assert.NotNull(ErrorFor(result, "Name"));
        }

        [Fact]
        public void Changing_the_id_is_rejected()
        {
            var result = Save(d => d["Id"] = "42:00000000-0000-0000-0000-000000000000");

            Assert.False(result.Success);
            Assert.NotNull(ErrorFor(result, "Id"));
        }

        [Fact]
        public void Changing_a_collection_is_rejected()
        {
            var result = Save(d => d["Tags"] = new JsonArray("a"));

            Assert.False(result.Success);
            Assert.Contains("collections", ErrorFor(result, "Tags"));
        }

        [Fact]
        public void Changing_a_reference_is_rejected()
        {
            var result = Save(d => d["Owner"] = new JsonObject { ["Name"] = "Eve" });

            Assert.False(result.Success);
            Assert.Contains("references", ErrorFor(result, "Owner"));
        }

        [Fact]
        public void Changing_a_property_of_unresolved_type_is_rejected()
        {
            var result = Save(d => d["Lost"] = "changed");

            Assert.False(result.Success);
            Assert.Contains("could not be loaded", ErrorFor(result, "Lost"));
        }

        [Fact]
        public void One_bad_value_discards_all_changes()
        {
            var result = Save(d =>
            {
                d["Name"] = "Fine";
                d["Count"] = "bad";
            });

            Assert.False(result.Success);
            Assert.Empty(result.Changes);
            Assert.Single(result.Errors);
        }

        [Theory]
        [InlineData("not json")]
        [InlineData("[1,2]")]
        [InlineData("\"text\"")]
        [InlineData("")]
        public void Non_object_documents_are_rejected(string json)
        {
            var result = _converter.Convert(json, Original(), Properties);

            Assert.False(result.Success);
            Assert.Null(result.Errors.Single().Field);
        }

        [Fact]
        public void Duplicate_keys_are_rejected()
        {
            var json = Original().ToJsonString();
            json = json.Insert(json.Length - 1, ",\"Name\":\"Again\"");

            var result = _converter.Convert(json, Original(), Properties);

            Assert.False(result.Success);
        }

        [Fact]
        public void Editable_only_for_inline_properties_of_supported_types()
        {
            Assert.True(Properties.Single(p => p.Name == "Count").Editable);
            Assert.True(Properties.Single(p => p.Name == "Color").Editable);
            Assert.True(Properties.Single(p => p.Name == "Optional").Editable);
            Assert.False(Properties.Single(p => p.Name == "Tags").Editable);
            Assert.False(Properties.Single(p => p.Name == "Owner").Editable);
            Assert.False(Properties.Single(p => p.Name == "Lost").Editable);
            Assert.False(new DdsPropertyDescriptor("X", typeof(TimeSpan), PropertyMapType.Inline).Editable);
        }
    }
}
