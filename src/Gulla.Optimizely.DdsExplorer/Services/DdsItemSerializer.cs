using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text.Json.Nodes;
using EPiServer.Data;
using EPiServer.Data.Dynamic;
using Gulla.Optimizely.DdsExplorer.Models;

namespace Gulla.Optimizely.DdsExplorer.Services
{
    /// <summary>
    /// Turns DDS values into JSON for display and editing. Hand-rolled rather than handed to
    /// System.Text.Json because DDS items hold arbitrary CLR graphs — cycles, types without
    /// public constructors, properties that throw — and a viewer must never fail on those.
    /// The inline formats written here are the ones <see cref="DdsItemConverter"/> reads back.
    /// </summary>
    public class DdsItemSerializer
    {
        public const string IdKey = "Id";

        private const int MaxDepth = 8;

        /// <summary>
        /// Builds the item document: <c>Id</c> first, then every active property in definition
        /// order. A property the item has no value for is written as null, so the key set always
        /// matches the definition — which is exactly what the strict save expects back.
        /// </summary>
        public JsonObject ToJson(Identity id, IReadOnlyDictionary<string, object> values, IEnumerable<DdsPropertyDescriptor> properties)
        {
            var result = new JsonObject
            {
                [IdKey] = id?.ToString()
            };

            foreach (var property in properties)
            {
                values.TryGetValue(property.Name, out var value);
                result[property.Name] = ToNode(value);
            }

            return result;
        }

        /// <summary>
        /// A hash of the item document. The document holds every active property, collections and
        /// references included, in definition order, so any change to the stored item changes it.
        /// </summary>
        public static string VersionOf(JsonObject document)
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes(document.ToJsonString());
            return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes));
        }

        public JsonNode ToNode(object value)
        {
            return ToNode(value, 0, new HashSet<object>(ReferenceEqualityComparer.Instance));
        }

        /// <summary>
        /// A one-line rendering for table cells, cut to <paramref name="maxLength"/> characters.
        /// </summary>
        public string Preview(object value, int maxLength = 80)
        {
            var node = ToNode(value);
            var text = node switch
            {
                null => "null",
                JsonValue v when v.TryGetValue<string>(out var s) => s,
                _ => node.ToJsonString()
            };

            return text.Length <= maxLength ? text : text.Substring(0, maxLength - 1) + "…";
        }

        private JsonNode ToNode(object value, int depth, HashSet<object> path)
        {
            switch (value)
            {
                case null:
                    return null;
                case string s:
                    return JsonValue.Create(s);
                case bool b:
                    return JsonValue.Create(b);
                case char c:
                    return JsonValue.Create(c.ToString());
                case Guid g:
                    return JsonValue.Create(g.ToString("D"));
                case DateTime dt:
                    return JsonValue.Create(dt.ToString("o", CultureInfo.InvariantCulture));
                case DateTimeOffset dto:
                    return JsonValue.Create(dto.ToString("o", CultureInfo.InvariantCulture));
                case TimeSpan ts:
                    return JsonValue.Create(ts.ToString("c", CultureInfo.InvariantCulture));
                case Identity identity:
                    return JsonValue.Create(identity.ToString());
                case Uri uri:
                    return JsonValue.Create(uri.OriginalString);
                case Enum e:
                    return JsonValue.Create(e.ToString());
                case double d:
                    return double.IsFinite(d) ? JsonValue.Create(d) : JsonValue.Create(d.ToString("R", CultureInfo.InvariantCulture));
                case float f:
                    return float.IsFinite(f) ? JsonValue.Create(f) : JsonValue.Create(f.ToString("R", CultureInfo.InvariantCulture));
                case decimal m:
                    return JsonValue.Create(m);
                case byte or sbyte or short or ushort or int or uint or long:
                    return JsonValue.Create(Convert.ToInt64(value, CultureInfo.InvariantCulture));
                case ulong ul:
                    return JsonValue.Create(ul);
                case byte[] bytes:
                    return JsonValue.Create(Convert.ToBase64String(bytes));
                case Type t:
                    return JsonValue.Create(t.AssemblyQualifiedName);
            }

            if (depth >= MaxDepth)
            {
                return JsonValue.Create("[max depth reached]");
            }

            if (!path.Add(value))
            {
                return JsonValue.Create("[circular reference]");
            }

            try
            {
                switch (value)
                {
                    case PropertyBag bag:
                    {
                        var obj = new JsonObject { [IdKey] = bag.Id?.ToString() };
                        foreach (var pair in bag.Where(p => p.Key != IdKey))
                        {
                            obj[pair.Key] = ToNode(pair.Value, depth + 1, path);
                        }
                        return obj;
                    }
                    case IDictionary dictionary:
                    {
                        var obj = new JsonObject();
                        foreach (DictionaryEntry entry in dictionary)
                        {
                            var key = Convert.ToString(entry.Key, CultureInfo.InvariantCulture) ?? "";
                            if (!obj.ContainsKey(key))
                            {
                                obj[key] = ToNode(entry.Value, depth + 1, path);
                            }
                        }
                        return obj;
                    }
                    case IEnumerable enumerable:
                    {
                        var array = new JsonArray();
                        foreach (var item in enumerable)
                        {
                            array.Add(ToNode(item, depth + 1, path));
                        }
                        return array;
                    }
                    default:
                        return ObjectToNode(value, depth, path);
                }
            }
            finally
            {
                path.Remove(value);
            }
        }

        private JsonObject ObjectToNode(object value, int depth, HashSet<object> path)
        {
            var obj = new JsonObject();
            var properties = value.GetType()
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.CanRead && p.GetIndexParameters().Length == 0);

            foreach (var property in properties)
            {
                if (obj.ContainsKey(property.Name))
                {
                    continue;
                }

                JsonNode node;
                try
                {
                    node = ToNode(property.GetValue(value), depth + 1, path);
                }
                catch (Exception ex)
                {
                    node = JsonValue.Create("[error reading property: " + (ex.InnerException ?? ex).Message + "]");
                }

                obj[property.Name] = node;
            }

            return obj;
        }
    }
}
