using System.Collections.Generic;
using System.Text.Json.Nodes;
using EPiServer.Data;
using EPiServer.Data.Dynamic;
using Gulla.Optimizely.DdsExplorer.Models;
using Gulla.Optimizely.DdsExplorer.Services;

namespace Gulla.Optimizely.DdsExplorer.Tests
{
    public class DdsItemSerializerTests
    {
        private readonly DdsItemSerializer _serializer = new();

        public class Node
        {
            public string Name { get; set; }
            public Node Next { get; set; }
            public string Broken => throw new InvalidOperationException("boom");
        }

        [Fact]
        public void Document_has_id_first_and_every_property_even_when_missing()
        {
            var properties = new[]
            {
                new DdsPropertyDescriptor("B", typeof(string), PropertyMapType.Inline),
                new DdsPropertyDescriptor("A", typeof(int), PropertyMapType.Inline)
            };

            var json = _serializer.ToJson(Identity.Parse("1:6F1C0E3A-5B8B-4E1B-9C3E-2A1F0B7D9E11"), new Dictionary<string, object> { ["A"] = 1 }, properties);

            Assert.Equal("{\"Id\":\"1:6F1C0E3A-5B8B-4E1B-9C3E-2A1F0B7D9E11\",\"B\":null,\"A\":1}", json.ToJsonString());
        }

        [Fact]
        public void DateTime_keeps_its_kind()
        {
            Assert.Equal("\"2026-09-29T10:00:00.0000000Z\"", _serializer.ToNode(new DateTime(2026, 9, 29, 10, 0, 0, DateTimeKind.Utc)).ToJsonString());
            Assert.Equal("\"2026-09-29T10:00:00.0000000\"", _serializer.ToNode(new DateTime(2026, 9, 29, 10, 0, 0, DateTimeKind.Unspecified)).ToJsonString());
        }

        [Fact]
        public void Non_finite_doubles_become_strings()
        {
            Assert.Equal("\"NaN\"", _serializer.ToNode(double.NaN).ToJsonString());
        }

        [Fact]
        public void Cycles_are_cut_and_throwing_properties_are_reported()
        {
            var a = new Node { Name = "a" };
            a.Next = a;

            var node = (JsonObject)_serializer.ToNode(a);

            Assert.Equal("[circular reference]", node["Next"]!.GetValue<string>());
            Assert.StartsWith("[error reading property", node["Broken"]!.GetValue<string>());
        }

        [Fact]
        public void The_same_object_twice_is_not_a_cycle()
        {
            var shared = new Node { Name = "shared" };

            var node = (JsonArray)_serializer.ToNode(new List<Node> { shared, shared });

            Assert.Equal("shared", node[1]!["Name"]!.GetValue<string>());
        }

        [Fact]
        public void PropertyBag_is_written_as_object_with_id()
        {
            var bag = new PropertyBag { { "X", 1 } };

            var node = (JsonObject)_serializer.ToNode(bag);

            Assert.Equal(1, node["X"]!.GetValue<long>());
            Assert.True(node.ContainsKey("Id"));
        }

        [Fact]
        public void Preview_truncates()
        {
            Assert.Equal("abc…", _serializer.Preview("abcdef", 4));
            Assert.Equal("null", _serializer.Preview(null));
        }
    }
}
