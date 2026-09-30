using System.Collections.Generic;

namespace Gulla.Optimizely.DdsExplorer.Models
{
    public sealed class StoreSummary
    {
        public string Name { get; init; }

        public bool IsSystem { get; init; }

        /// <summary>The store definition does not load; the store opens in read-only raw mode.</summary>
        public bool Broken { get; init; }

        /// <summary>Null when the store could not be counted; <see cref="Error"/> then says why.</summary>
        public long? ItemCount { get; init; }

        /// <summary>Null when the store definition could not be loaded.</summary>
        public int? PropertyCount { get; init; }

        public string Error { get; init; }
    }

    public sealed class StoreDetails
    {
        public string Name { get; init; }

        public bool IsSystem { get; init; }

        public long? ItemCount { get; init; }

        public int PageSize { get; init; }

        /// <summary>
        /// True when the store definition does not load and the store is read straight from the
        /// DDS tables instead: read-only, types shown as stored strings.
        /// </summary>
        public bool Raw { get; init; }

        /// <summary>Why the definition did not load, when <see cref="Raw"/> is true.</summary>
        public string LoadError { get; init; }

        public IReadOnlyList<PropertyInfoModel> Properties { get; init; }

        public string Error { get; init; }
    }

    public sealed class PropertyInfoModel
    {
        public string Name { get; init; }

        public string Type { get; init; }

        public string MapType { get; init; }

        public bool Editable { get; init; }
    }

    public sealed class ItemPage
    {
        public int Page { get; init; }

        public int PageSize { get; init; }

        public long Total { get; init; }

        public IReadOnlyList<string> Columns { get; init; }

        public IReadOnlyList<ItemRow> Items { get; init; }
    }

    public sealed class ItemRow
    {
        public string Id { get; init; }

        public IReadOnlyDictionary<string, string> Values { get; init; }
    }

    public sealed class ItemDetails
    {
        public string Id { get; init; }

        /// <summary>
        /// The item document, pre-serialized. Sent as a string rather than as nested JSON so the
        /// browser never parses it: a JavaScript number cannot hold every long, and a value that
        /// came back rounded would look like an edit to the strict save.
        /// </summary>
        public string Json { get; init; }

        /// <summary>True when the item was read straight from the DDS tables; see <see cref="StoreDetails.Raw"/>.</summary>
        public bool Raw { get; init; }

        /// <summary>
        /// Identifies the stored state <see cref="Json"/> was built from. Sent back with a save, so
        /// a save made from a stale copy is refused instead of reverting what changed meanwhile.
        /// </summary>
        public string Version { get; init; }

        public bool CanEdit { get; init; }

        public string CannotEditReason { get; init; }

        public IReadOnlyList<string> ReadOnlyProperties { get; init; }
    }

    public sealed class SaveResult
    {
        public bool Success { get; init; }

        public bool NotFound { get; init; }

        /// <summary>The item changed after it was opened, so nothing was saved.</summary>
        public bool Conflict { get; init; }

        public IReadOnlyList<string> ChangedProperties { get; init; }

        public IReadOnlyList<Services.FieldError> Errors { get; init; }
    }
}
