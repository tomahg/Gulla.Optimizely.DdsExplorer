using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using EPiServer.Data;
using EPiServer.Data.Dynamic;
using EPiServer.Data.Dynamic.Providers;
using Gulla.Optimizely.DdsExplorer.Configuration;
using Gulla.Optimizely.DdsExplorer.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Gulla.Optimizely.DdsExplorer.Services
{
    public class DdsStoreService : IDdsStoreService
    {
        private const int PreviewColumnCount = 6;

        private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

        private readonly DynamicDataStoreFactory _factory;
        private readonly IDataStoreProviderFactory _providerFactory;
        private readonly RawStoreReader _raw;
        private readonly DdsItemSerializer _serializer;
        private readonly DdsItemConverter _converter;
        private readonly SystemStoreClassifier _classifier;
        private readonly IOptionsMonitor<DdsExplorerOptions> _options;
        private readonly ILogger<DdsStoreService> _logger;

        public DdsStoreService(
            DynamicDataStoreFactory factory,
            IDataStoreProviderFactory providerFactory,
            RawStoreReader raw,
            DdsItemSerializer serializer,
            DdsItemConverter converter,
            SystemStoreClassifier classifier,
            IOptionsMonitor<DdsExplorerOptions> options,
            ILogger<DdsStoreService> logger)
        {
            _factory = factory;
            _providerFactory = providerFactory;
            _raw = raw;
            _serializer = serializer;
            _converter = converter;
            _classifier = classifier;
            _options = options;
            _logger = logger;
        }

        private int PageSize => Math.Clamp(_options.CurrentValue.PageSize, 1, 1000);

        public IReadOnlyList<StoreSummary> ListStores()
        {
            // Names first, then one definition at a time. StoreDefinition.GetAll() loads every
            // definition in a single call, so one store that no longer loads — typically because a
            // property's type has gone with an uninstalled assembly — would take the whole list
            // down with it. Those are exactly the stores someone opens this tool to clean up.
            return _providerFactory.Create().GetStoreNames()
                .Where(name => !string.IsNullOrEmpty(name))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .Select(Summarize)
                .ToList();
        }

        private StoreSummary Summarize(string storeName)
        {
            int? propertyCount = null;
            string error = null;
            long? count = null;
            var broken = false;

            try
            {
                propertyCount = StoreDefinition.Get(storeName)?.ActiveMappings.Count();
                count = TryCount(storeName, out error);
            }
            catch (Exception ex)
            {
                _logger.LogInformation(ex, "DDS Explorer could not load the definition of store {StoreName}.", storeName);
                broken = true;
                error = "The store definition could not be loaded (" + ex.GetType().Name + ": " + ex.Message + "). It can be viewed read-only and deleted.";

                try
                {
                    propertyCount = _raw.GetProperties(storeName)?.Count;
                    count = _raw.Count(storeName);
                }
                catch (Exception rawEx)
                {
                    _logger.LogInformation(rawEx, "DDS Explorer could not read store {StoreName} from the database either.", storeName);
                }
            }

            return new StoreSummary
            {
                Name = storeName,
                IsSystem = _classifier.IsSystem(storeName),
                Broken = broken,
                ItemCount = count,
                PropertyCount = propertyCount,
                Error = error
            };
        }

        public StoreDetails GetStore(string storeName)
        {
            var store = OpenForReading(storeName, out var loadError);
            if (store == null)
            {
                return loadError == null ? null : GetRawStore(storeName, loadError);
            }

            var count = TryCount(storeName, out var error);

            return new StoreDetails
            {
                Name = store.Name,
                IsSystem = _classifier.IsSystem(store.Name),
                ItemCount = count,
                PageSize = PageSize,
                Properties = Describe(store).Select(p => new PropertyInfoModel
                {
                    Name = p.Name,
                    Type = p.TypeName,
                    MapType = p.MapType.ToString(),
                    Editable = p.Editable
                }).ToList(),
                Error = error
            };
        }

        public ItemPage GetItems(string storeName, int page)
        {
            var pageSize = PageSize;
            page = Math.Max(1, page);

            var store = OpenForReading(storeName, out var loadError);
            if (store == null)
            {
                return loadError == null ? null : GetRawItems(storeName, page, pageSize);
            }

            // Both Count() and Skip/Take are translated to SQL by the DDS query provider
            // (ROW_NUMBER() ordered by Id when no order is given), so only one page is loaded.
            var query = store.ItemsAsPropertyBag();
            long total = query.Count();
            var bags = query.Skip((page - 1) * pageSize).Take(pageSize).ToList();

            var columns = Describe(store)
                .Where(p => p.MapType == PropertyMapType.Inline)
                .Take(PreviewColumnCount)
                .Select(p => p.Name)
                .ToList();

            return new ItemPage
            {
                Page = page,
                PageSize = pageSize,
                Total = total,
                Columns = columns,
                Items = bags.Select(bag => new ItemRow
                {
                    Id = bag.Id?.ToString(),
                    Values = columns.ToDictionary(c => c, c => bag.TryGetValue(c, out var v) ? _serializer.Preview(v) : "")
                }).ToList()
            };
        }

        public ItemDetails GetItem(string storeName, string id)
        {
            if (!Identity.TryParse(id, out var identity))
            {
                return null;
            }

            var store = OpenForReading(storeName, out var loadError);
            if (store == null)
            {
                return loadError == null ? null : GetRawItem(storeName, identity, loadError);
            }

            var bag = store.LoadAsPropertyBag(identity);
            if (bag == null)
            {
                return null;
            }

            var properties = Describe(store);
            var document = _serializer.ToJson(bag.Id, AsDictionary(bag), properties);
            var canEdit = CanEdit(store, identity, bag, properties, out _, out var reason);

            return new ItemDetails
            {
                Id = bag.Id?.ToString(),
                Json = document.ToJsonString(Indented),
                Version = DdsItemSerializer.VersionOf(document),
                CanEdit = canEdit,
                CannotEditReason = reason,
                ReadOnlyProperties = properties.Where(p => !p.Editable).Select(p => p.Name).ToList()
            };
        }

        public SaveResult SaveItem(string storeName, string id, string json, string version, string user)
        {
            var store = OpenStore(storeName);
            if (store == null || !Identity.TryParse(id, out var identity))
            {
                return new SaveResult { NotFound = true };
            }

            var bag = store.LoadAsPropertyBag(identity);
            if (bag == null)
            {
                return new SaveResult { NotFound = true };
            }

            var properties = Describe(store);
            var original = _serializer.ToJson(bag.Id, AsDictionary(bag), properties);

            // The edit is compared with the item as stored now, so it must be the item the editor
            // opened: otherwise every value changed meanwhile reads as an edit and is reverted.
            if (!string.Equals(version, DdsItemSerializer.VersionOf(original), StringComparison.Ordinal))
            {
                return new SaveResult
                {
                    Conflict = true,
                    Errors = [new FieldError(null, "The item has changed since it was opened. Copy your edits, then close and reopen the item to edit the current version.")]
                };
            }

            if (!CanEdit(store, identity, bag, properties, out var target, out var reason))
            {
                return Failed(new FieldError(null, reason));
            }

            var conversion = _converter.Convert(json ?? "", original, properties);
            if (!conversion.Success)
            {
                return new SaveResult { Errors = conversion.Errors };
            }

            if (conversion.Changes.Count == 0)
            {
                return new SaveResult { Success = true, ChangedProperties = [] };
            }

            // Save the item as its owner would: loaded as its own CLR type, only the changed inline
            // properties set, then saved. That leaves references and collections exactly as they
            // were. Saving the PropertyBag instead would either throw on references (a PropertyBag
            // sub-object needs a TypeToStoreMapper) or rewrite the item's ItemType.
            if (target is PropertyBag targetBag)
            {
                foreach (var change in conversion.Changes)
                {
                    targetBag[change.Key] = change.Value;
                }
            }
            else
            {
                var missing = conversion.Changes.Keys
                    .Where(k => target.GetType().GetProperty(k)?.SetMethod == null)
                    .ToList();
                if (missing.Count > 0)
                {
                    return Failed(missing.Select(k => new FieldError(k, "Read-only: " + target.GetType().FullName + " has no settable property with this name.")).ToArray());
                }

                var changes = new PropertyBag();
                foreach (var change in conversion.Changes)
                {
                    changes.Add(change.Key, change.Value);
                }

                changes.ToObject(target);
            }

            store.Save(target, identity);

            _logger.LogInformation(
                "DDS Explorer: {User} edited item {ItemId} in store {StoreName}. Changed properties: {ChangedProperties}.",
                user, identity, store.Name, string.Join(", ", conversion.Changes.Keys));

            return new SaveResult { Success = true, ChangedProperties = conversion.Changes.Keys.ToList() };
        }

        public bool DeleteItem(string storeName, string id, string user)
        {
            var store = OpenStore(storeName);
            if (store == null || !Identity.TryParse(id, out var identity) || store.LoadAsPropertyBag(identity) == null)
            {
                return false;
            }

            store.Delete(identity);

            _logger.LogInformation("DDS Explorer: {User} deleted item {ItemId} from store {StoreName}.", user, identity, store.Name);
            return true;
        }

        public int? DeleteItems(string storeName, IReadOnlyCollection<string> ids, string user)
        {
            var store = OpenStore(storeName);
            if (store == null)
            {
                return null;
            }

            var deleted = new List<Identity>();
            foreach (var id in (ids ?? []).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (Identity.TryParse(id, out var identity) && store.LoadAsPropertyBag(identity) != null)
                {
                    store.Delete(identity);
                    deleted.Add(identity);
                }
            }

            if (deleted.Count > 0)
            {
                _logger.LogInformation("DDS Explorer: {User} deleted {Count} items from store {StoreName}: {ItemIds}.",
                    user, deleted.Count, store.Name, string.Join(", ", deleted));
            }

            return deleted.Count;
        }

        public long? EmptyStore(string storeName, string user)
        {
            var store = OpenStore(storeName);
            if (store == null)
            {
                return null;
            }

            long count = store.ItemsAsPropertyBag().Count();
            store.DeleteAll();

            _logger.LogInformation("DDS Explorer: {User} emptied store {StoreName}, deleting {Count} items.", user, store.Name, count);
            return count;
        }

        public bool DeleteStore(string storeName, string user)
        {
            DynamicDataStore store;
            try
            {
                store = OpenStore(storeName);
            }
            catch (Exception ex)
            {
                return DeleteBrokenStore(storeName, user, ex);
            }

            if (store == null)
            {
                return false;
            }

            var count = TryCount(store.Name, out _);
            _factory.DeleteStore(store.Name, deleteObjects: true);

            _logger.LogInformation("DDS Explorer: {User} deleted store {StoreName} and its {Count} items.", user, store.Name, count?.ToString() ?? "(uncounted)");
            return true;
        }

        private DynamicDataStore OpenStore(string storeName)
        {
            if (string.IsNullOrEmpty(storeName))
            {
                return null;
            }

            try
            {
                return _factory.GetStore(storeName);
            }
            catch (ArgumentException ex) when (ex is not ArgumentNullException && ex.ParamName == "storeName")
            {
                // GetStore rejects names containing characters it considers SQL-injection risks.
                // Such a name cannot come from a store that exists, so it is simply not found.
                // Filtered on the parameter: a definition that fails to load also surfaces as an
                // ArgumentException (an ArgumentNullException on "key", from DDS's fallback for
                // unresolvable collection/reference types), and that must not read as "no store".
                return null;
            }
        }

        /// <summary>
        /// Like <see cref="OpenStore"/>, but a store whose definition fails to load comes back as
        /// null with <paramref name="loadError"/> set, so the caller can fall back to raw mode.
        /// Null with no error means there is no such store.
        /// </summary>
        private DynamicDataStore OpenForReading(string storeName, out string loadError)
        {
            loadError = null;
            try
            {
                return OpenStore(storeName);
            }
            catch (Exception ex)
            {
                _logger.LogInformation(ex, "DDS Explorer could not load the definition of store {StoreName}; showing it read-only from the database.", storeName);
                loadError = ex.GetType().Name + ": " + ex.Message;
                return null;
            }
        }

        private StoreDetails GetRawStore(string storeName, string loadError)
        {
            var properties = _raw.GetProperties(storeName);
            if (properties == null)
            {
                return null;
            }

            long? count = null;
            string error = null;
            try
            {
                count = _raw.Count(storeName);
            }
            catch (Exception ex)
            {
                _logger.LogInformation(ex, "DDS Explorer could not count the rows of store {StoreName} in the database.", storeName);
                error = ex.Message;
            }

            return new StoreDetails
            {
                Name = storeName,
                IsSystem = _classifier.IsSystem(storeName),
                ItemCount = count,
                PageSize = PageSize,
                Raw = true,
                LoadError = loadError,
                Properties = properties.Select(p => new PropertyInfoModel
                {
                    Name = p.Name,
                    Type = ShortTypeName(p.TypeName),
                    MapType = p.MapType,
                    Editable = false
                }).ToList(),
                Error = error
            };
        }

        private ItemPage GetRawItems(string storeName, int page, int pageSize)
        {
            var properties = _raw.GetProperties(storeName);
            if (properties == null)
            {
                return null;
            }

            var columns = properties
                .Where(p => p.MapType == nameof(PropertyMapType.Inline))
                .Take(PreviewColumnCount)
                .Select(p => p.Name)
                .ToList();

            var rows = _raw.GetPage(storeName, (page - 1) * pageSize, pageSize);

            return new ItemPage
            {
                Page = page,
                PageSize = pageSize,
                Total = _raw.Count(storeName),
                Columns = columns,
                Items = rows.Select(row => new ItemRow
                {
                    Id = row.TryGetValue("Id", out var id) ? id?.ToString() : null,
                    Values = columns.ToDictionary(c => c, c => row.TryGetValue(c, out var v) ? _serializer.Preview(v) : "")
                }).ToList()
            };
        }

        private ItemDetails GetRawItem(string storeName, Identity identity, string loadError)
        {
            var properties = _raw.GetProperties(storeName);
            var row = properties == null ? null : _raw.GetRow(storeName, identity.StoreId);
            if (row == null)
            {
                return null;
            }

            var references = _raw.GetReferences(identity.StoreId);

            var document = new System.Text.Json.Nodes.JsonObject
            {
                [DdsItemSerializer.IdKey] = row.TryGetValue("Id", out var id) ? id?.ToString() : identity.ToString(),
                ["ItemType"] = row.TryGetValue("ItemType", out var itemType) ? ShortTypeName(itemType?.ToString()) : null
            };

            foreach (var property in properties)
            {
                if (property.MapType == nameof(PropertyMapType.Inline))
                {
                    document[property.Name] = row.TryGetValue(property.Name, out var value) ? _serializer.ToNode(value) : null;
                    continue;
                }

                // Collections and references are rows in tblBigTableReference. They are shown as
                // stored — element type, index, value or the referenced item's Identity — because
                // the types needed to rebuild the original objects are exactly what is missing.
                var elements = references
                    .Where(r => string.Equals(r["PropertyName"] as string, property.Name, StringComparison.Ordinal))
                    .Select(r =>
                    {
                        var element = new System.Text.Json.Nodes.JsonObject();
                        foreach (var column in r.Where(c => c.Key != "PropertyName" && c.Value != null && !(c.Key == "IsKey" && c.Value is false)))
                        {
                            element[column.Key] = column.Key is "ElementType" or "CollectionType"
                                ? ShortTypeName(column.Value.ToString())
                                : _serializer.ToNode(column.Value);
                        }
                        return (System.Text.Json.Nodes.JsonNode)element;
                    })
                    .ToArray();

                document[property.Name] = elements.Length == 0 ? null : new System.Text.Json.Nodes.JsonArray(elements);
            }

            return new ItemDetails
            {
                Id = document[DdsItemSerializer.IdKey]?.GetValue<string>(),
                Json = document.ToJsonString(Indented),
                Raw = true,
                CanEdit = false,
                CannotEditReason = "the store definition cannot be loaded (" + loadError + "), so this item is shown read-only, straight from the database.",
                ReadOnlyProperties = properties.Select(p => p.Name).ToList()
            };
        }

        /// <summary>
        /// Deletes a store whose definition no longer loads. DynamicDataStoreFactory.DeleteStore
        /// loads the definition first and so fails the same way; the provider's own delete works
        /// by name only: the BigTableDeleteAll procedure on the store's view, then the store's
        /// config and info rows, view and save procedure.
        /// </summary>
        private bool DeleteBrokenStore(string storeName, string user, Exception loadError)
        {
            if (_raw.GetProperties(storeName) == null)
            {
                return false;
            }

            long? count = null;
            try
            {
                count = _raw.Count(storeName);
            }
            catch (Exception ex)
            {
                _logger.LogInformation(ex, "DDS Explorer could not count the rows of store {StoreName} before deleting it.", storeName);
            }

            _providerFactory.Create().DeleteStoreDefinition(storeName, deleteObjects: true);
            StoreDefinition.DeleteFromCache(storeName);

            _logger.LogInformation(
                "DDS Explorer: {User} deleted store {StoreName} and its {Count} items. The store definition could not be loaded ({LoadError}), so it was deleted by name.",
                user, storeName, count?.ToString() ?? "(uncounted)", loadError.GetType().Name + ": " + loadError.Message);
            return true;
        }

        private static readonly System.Text.RegularExpressions.Regex AssemblyDetails =
            new(@",\s*(Version|Culture|PublicKeyToken)=[^,\]]*", System.Text.RegularExpressions.RegexOptions.Compiled);

        /// <summary>
        /// "List`1[[X, Asm, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null]], System.Private.CoreLib"
        /// → "List`1[[X, Asm]], System.Private.CoreLib": the assembly name tells what is missing;
        /// version, culture and key only add noise.
        /// </summary>
        internal static string ShortTypeName(string typeName) =>
            string.IsNullOrEmpty(typeName) ? typeName : AssemblyDetails.Replace(typeName, "");

        private long? TryCount(string storeName, out string error)
        {
            error = null;
            try
            {
                return OpenStore(storeName)?.ItemsAsPropertyBag().Count();
            }
            catch (Exception ex)
            {
                _logger.LogInformation(ex, "DDS Explorer could not count the items in store {StoreName}.", storeName);
                error = ex.Message;
                return null;
            }
        }

        internal static IReadOnlyList<DdsPropertyDescriptor> Describe(DynamicDataStore store)
        {
            return store.StoreDefinition.ActiveMappings
                .Select(m => new DdsPropertyDescriptor(m.PropertyName, m.PropertyType, m.PropertyMapType))
                .ToList();
        }

        private static IReadOnlyDictionary<string, object> AsDictionary(PropertyBag bag)
        {
            var result = new Dictionary<string, object>(StringComparer.Ordinal);
            foreach (var pair in bag)
            {
                result[pair.Key] = pair.Value;
            }

            return result;
        }

        /// <param name="native">The item loaded as its own CLR type — what a save writes back.</param>
        private bool CanEdit(DynamicDataStore store, Identity identity, PropertyBag bag, IReadOnlyList<DdsPropertyDescriptor> properties, out object native, out string reason)
        {
            native = null;
            reason = null;

            if (!properties.Any(p => p.Editable))
            {
                reason = "None of this store's properties can be edited here.";
                return false;
            }

            try
            {
                native = store.Load(identity);
            }
            catch (Exception ex)
            {
                reason = "The item's own type could not be loaded, so it cannot be saved safely: " + ex.Message;
                return false;
            }

            if (native == null)
            {
                reason = "The item was deleted while it was being loaded.";
                return false;
            }

            if (native is PropertyBag
                && properties.Any(p => p.MapType == PropertyMapType.Reference && bag.TryGetValue(p.Name, out var v) && v != null))
            {
                reason = "This item is stored as a PropertyBag and holds references to other items. Saving it would require mapping those to stores, which the explorer cannot know.";
                return false;
            }

            return true;
        }

        private static SaveResult Failed(params FieldError[] errors) => new() { Errors = errors };
    }
}
