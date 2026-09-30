using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using EPiServer.Data;
using Gulla.Optimizely.DdsExplorer.Models;

namespace Gulla.Optimizely.DdsExplorer.Services
{
    /// <summary>
    /// Reads an edited item document back into typed values, strictly: the key set must be the
    /// store definition's, the Id cannot change, read-only properties cannot change, and every
    /// changed value must convert to its declared CLR type. Any failure rejects the whole save.
    /// The point is that a value DDS accepts but the owning code cannot load — a string where an
    /// int was declared, say — never reaches the database.
    /// </summary>
    public class DdsItemConverter
    {
        private static readonly HashSet<Type> SupportedTypes =
        [
            typeof(string), typeof(bool), typeof(char),
            typeof(byte), typeof(sbyte), typeof(short), typeof(ushort),
            typeof(int), typeof(uint), typeof(long), typeof(ulong),
            typeof(float), typeof(double), typeof(decimal),
            typeof(Guid), typeof(DateTime), typeof(Identity)
        ];

        public static bool IsSupported(Type type)
        {
            var underlying = Nullable.GetUnderlyingType(type) ?? type;
            return underlying.IsEnum || SupportedTypes.Contains(underlying);
        }

        /// <param name="editedJson">The document as submitted.</param>
        /// <param name="original">The document as <see cref="DdsItemSerializer.ToJson"/> produced it from the stored item.</param>
        /// <param name="properties">The store's active properties.</param>
        public ConversionResult Convert(string editedJson, JsonObject original, IReadOnlyList<DdsPropertyDescriptor> properties)
        {
            var result = new ConversionResult();

            JsonObject edited;
            try
            {
                edited = JsonNode.Parse(editedJson, documentOptions: new JsonDocumentOptions { AllowTrailingCommas = false }) as JsonObject;
            }
            catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException)
            {
                result.AddError(null, "Not valid JSON: " + ex.Message);
                return result;
            }

            if (edited == null)
            {
                result.AddError(null, "The document must be a JSON object.");
                return result;
            }

            // Materialize once: JsonObject throws on duplicate keys lazily, on first enumeration.
            List<string> editedKeys;
            try
            {
                editedKeys = edited.Select(p => p.Key).ToList();
            }
            catch (ArgumentException ex)
            {
                result.AddError(null, "Duplicate property: " + ex.Message);
                return result;
            }

            var expectedKeys = new HashSet<string>(properties.Select(p => p.Name), StringComparer.Ordinal) { DdsItemSerializer.IdKey };

            foreach (var key in editedKeys.Where(k => !expectedKeys.Contains(k)))
            {
                result.AddError(key, "Not a property of this store. Properties cannot be added.");
            }

            foreach (var key in expectedKeys.Where(k => !edited.ContainsKey(k)))
            {
                result.AddError(key, "Missing. Properties cannot be removed — set the value to null instead, if its type allows it.");
            }

            if (edited.ContainsKey(DdsItemSerializer.IdKey)
                && !JsonNode.DeepEquals(edited[DdsItemSerializer.IdKey], original[DdsItemSerializer.IdKey]))
            {
                result.AddError(DdsItemSerializer.IdKey, "The Id cannot be changed.");
            }

            foreach (var property in properties)
            {
                if (!edited.TryGetPropertyValue(property.Name, out var node))
                {
                    continue;
                }

                if (JsonNode.DeepEquals(node, original[property.Name]))
                {
                    continue;
                }

                if (!property.Editable)
                {
                    result.AddError(property.Name, ReadOnlyReason(property));
                    continue;
                }

                if (TryConvertValue(node, property.ClrType, out var value, out var error))
                {
                    result.Changes[property.Name] = value;
                }
                else
                {
                    result.AddError(property.Name, error);
                }
            }

            if (!result.Success)
            {
                result.Changes.Clear();
            }

            return result;
        }

        internal static string ReadOnlyReason(DdsPropertyDescriptor property)
        {
            if (property.ClrType == null)
            {
                return "Read-only: its type could not be loaded.";
            }

            return property.MapType switch
            {
                EPiServer.Data.Dynamic.PropertyMapType.Collection => "Read-only: collections are stored in separate rows and cannot be edited here.",
                EPiServer.Data.Dynamic.PropertyMapType.Reference => "Read-only: references point to items in other stores and cannot be edited here.",
                EPiServer.Data.Dynamic.PropertyMapType.Inline => "Read-only: values of type " + property.TypeName + " cannot be edited here.",
                _ => "Read-only: " + property.MapType + " properties cannot be edited here."
            };
        }

        /// <summary>
        /// Converts one JSON value to <paramref name="type"/>. The accepted JSON shapes are the ones
        /// <see cref="DdsItemSerializer"/> writes: numbers as numbers, everything else as strings.
        /// </summary>
        public static bool TryConvertValue(JsonNode node, Type type, out object value, out string error)
        {
            value = null;
            error = null;

            var underlying = Nullable.GetUnderlyingType(type);
            var nullable = underlying != null || !type.IsValueType;
            var target = underlying ?? type;

            if (node == null)
            {
                if (nullable)
                {
                    return true;
                }

                error = "Cannot be null: " + DdsPropertyDescriptor.FriendlyTypeName(type) + " is not nullable.";
                return false;
            }

            if (node is not JsonValue jsonValue)
            {
                error = "Expected " + Describe(target) + ", got a JSON " + (node is JsonArray ? "array" : "object") + ".";
                return false;
            }

            var kind = jsonValue.GetValueKind();

            if (target == typeof(string))
            {
                return Expect(kind == JsonValueKind.String, jsonValue.GetValue<string>(), target, kind, out value, out error);
            }

            if (target == typeof(bool))
            {
                return Expect(kind is JsonValueKind.True or JsonValueKind.False, kind == JsonValueKind.True, target, kind, out value, out error);
            }

            if (target.IsEnum)
            {
                return TryConvertEnum(jsonValue, kind, target, out value, out error);
            }

            if (IsNumeric(target))
            {
                return TryConvertNumber(jsonValue, kind, target, out value, out error);
            }

            if (kind != JsonValueKind.String)
            {
                error = "Expected " + Describe(target) + ", got " + DescribeKind(kind) + ".";
                return false;
            }

            var text = jsonValue.GetValue<string>();

            if (target == typeof(char))
            {
                return Expect(text.Length == 1, text.Length == 1 ? text[0] : default, target, kind, out value, out error,
                    "Expected a string of exactly one character.");
            }

            if (target == typeof(Guid))
            {
                var ok = Guid.TryParse(text, out var guid);
                return Expect(ok, guid, target, kind, out value, out error, "\"" + text + "\" is not a valid Guid.");
            }

            if (target == typeof(DateTime))
            {
                // RoundtripKind keeps the Z / offset / unspecified distinction that the "o" format
                // wrote, so an untouched UTC value never comes back as local time.
                var ok = DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dt);
                return Expect(ok, dt, target, kind, out value, out error,
                    "\"" + text + "\" is not a valid date. Use ISO 8601, for example 2026-09-29T12:00:00.0000000Z.");
            }

            if (target == typeof(Identity))
            {
                var ok = Identity.TryParse(text, out var identity);
                return Expect(ok, identity, target, kind, out value, out error,
                    "\"" + text + "\" is not a valid Identity. Expected storeId:guid.");
            }

            error = "Values of type " + DdsPropertyDescriptor.FriendlyTypeName(type) + " cannot be edited.";
            return false;
        }

        private static bool TryConvertEnum(JsonValue jsonValue, JsonValueKind kind, Type target, out object value, out string error)
        {
            value = null;
            error = null;

            if (kind == JsonValueKind.String)
            {
                var text = jsonValue.GetValue<string>();

                // Enum.TryParse also accepts "42" and any integer at all; only names are accepted
                // from strings, so a typo can never silently become an undefined numeric value.
                if (text.Length > 0 && !char.IsDigit(text[0]) && text[0] != '-'
                    && Enum.TryParse(target, text, ignoreCase: false, out var parsed)
                    && IsDefinedOrFlags(target, parsed))
                {
                    value = parsed;
                    return true;
                }

                error = "\"" + text + "\" is not a value of " + target.FullName + ". Valid values: " + string.Join(", ", Enum.GetNames(target)) + ".";
                return false;
            }

            if (kind == JsonValueKind.Number && jsonValue.TryGetValue<long>(out var number))
            {
                var parsed = Enum.ToObject(target, number);
                if (IsDefinedOrFlags(target, parsed))
                {
                    value = parsed;
                    return true;
                }

                error = number + " is not a defined value of " + target.FullName + ".";
                return false;
            }

            error = "Expected " + Describe(target) + ", got " + DescribeKind(kind) + ".";
            return false;
        }

        private static bool IsDefinedOrFlags(Type enumType, object value)
        {
            if (Enum.IsDefined(enumType, value))
            {
                return true;
            }

            // A combination of flags is legitimate but not "defined"; its ToString() is then a
            // comma-separated list of names rather than a bare number.
            return enumType.IsDefined(typeof(FlagsAttribute), false)
                && !char.IsDigit(value.ToString()![0]) && value.ToString()![0] != '-';
        }

        private static bool TryConvertNumber(JsonValue jsonValue, JsonValueKind kind, Type target, out object value, out string error)
        {
            value = null;
            error = null;

            if (kind != JsonValueKind.Number)
            {
                error = "Expected " + Describe(target) + ", got " + DescribeKind(kind) + ".";
                return false;
            }

            var ok = target switch
            {
                _ when target == typeof(byte) => Try<byte>(jsonValue, out value),
                _ when target == typeof(sbyte) => Try<sbyte>(jsonValue, out value),
                _ when target == typeof(short) => Try<short>(jsonValue, out value),
                _ when target == typeof(ushort) => Try<ushort>(jsonValue, out value),
                _ when target == typeof(int) => Try<int>(jsonValue, out value),
                _ when target == typeof(uint) => Try<uint>(jsonValue, out value),
                _ when target == typeof(long) => Try<long>(jsonValue, out value),
                _ when target == typeof(ulong) => Try<ulong>(jsonValue, out value),
                _ when target == typeof(float) => Try<float>(jsonValue, out value) && float.IsFinite((float)value),
                _ when target == typeof(double) => Try<double>(jsonValue, out value) && double.IsFinite((double)value),
                _ when target == typeof(decimal) => Try<decimal>(jsonValue, out value),
                _ => false
            };

            if (!ok)
            {
                value = null;
                error = jsonValue.ToJsonString() + " does not fit in " + Describe(target) + ".";
            }

            return ok;
        }

        private static bool Try<T>(JsonValue jsonValue, out object value)
        {
            if (jsonValue.TryGetValue<T>(out var typed))
            {
                value = typed;
                return true;
            }

            value = null;
            return false;
        }

        private static bool Expect(bool ok, object converted, Type target, JsonValueKind kind, out object value, out string error, string message = null)
        {
            value = ok ? converted : null;
            error = ok ? null : message ?? "Expected " + Describe(target) + ", got " + DescribeKind(kind) + ".";
            return ok;
        }

        private static bool IsNumeric(Type type) =>
            type == typeof(byte) || type == typeof(sbyte) || type == typeof(short) || type == typeof(ushort)
            || type == typeof(int) || type == typeof(uint) || type == typeof(long) || type == typeof(ulong)
            || type == typeof(float) || type == typeof(double) || type == typeof(decimal);

        private static string Describe(Type type)
        {
            if (type == typeof(string)) return "a string";
            if (type == typeof(bool)) return "true or false";
            if (type.IsEnum) return "the name of a " + type.FullName + " value";
            if (IsNumeric(type)) return "a number of type " + type.Name;
            return "a string holding a " + type.Name;
        }

        private static string DescribeKind(JsonValueKind kind) => kind switch
        {
            JsonValueKind.String => "a string",
            JsonValueKind.Number => "a number",
            JsonValueKind.True or JsonValueKind.False => "a boolean",
            JsonValueKind.Null => "null",
            JsonValueKind.Array => "an array",
            JsonValueKind.Object => "an object",
            _ => kind.ToString()
        };
    }

    public class ConversionResult
    {
        public Dictionary<string, object> Changes { get; } = new(StringComparer.Ordinal);

        public List<FieldError> Errors { get; } = [];

        public bool Success => Errors.Count == 0;

        internal void AddError(string field, string message) => Errors.Add(new FieldError(field, message));
    }

    /// <param name="Field">The property the error is about, or null for the document as a whole.</param>
    public sealed record FieldError(string Field, string Message);
}
