using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using EPiServer.Data;
using EPiServer.ServiceLocation;

namespace Gulla.Optimizely.DdsExplorer.Services
{
    /// <summary>
    /// Reads a store straight from the DDS tables, for stores whose definition no longer loads —
    /// typically because a property's type came from an assembly that is gone (Search &amp;
    /// Navigation's stores on a CMS 13 site). DDS resolves every property type while loading a
    /// definition and, for a collection or reference property whose type cannot be found, crashes
    /// with "Value cannot be null. (Parameter 'key')" instead of falling back.
    /// </summary>
    /// <remarks>
    /// Read-only by design. Relies on the SQL Server layout DDS itself uses:
    /// <list type="bullet">
    /// <item><c>tblBigTableStoreConfig</c> / <c>tblBigTableStoreInfo</c> — the definition, with property types as strings.</item>
    /// <item><c>[dbo].[VW_{store}]</c> — one view per store: Id, StoreId, ExternalId, ItemType, then one column per inline property.</item>
    /// <item><c>tblBigTableReference</c> — collection elements and references, keyed by the item's StoreId and property name.</item>
    /// </list>
    /// Values are always passed as parameters. The one identifier that has to be spliced in, the
    /// view name, is bracket-quoted with <c>]</c> doubled.
    /// </remarks>
    public class RawStoreReader
    {
        private readonly ServiceAccessor<IDatabaseExecutor> _database;

        public RawStoreReader(ServiceAccessor<IDatabaseExecutor> database)
        {
            _database = database;
        }

        /// <returns>The active properties in definition order, or null when no store has that name.</returns>
        public IReadOnlyList<RawProperty> GetProperties(string storeName)
        {
            var db = _database();
            return db.Execute(() =>
            {
                using var command = db.CreateCommand();
                command.CommandType = CommandType.Text;
                command.CommandText = @"
declare @storeId bigint;
select @storeId = pkId from tblBigTableStoreConfig where StoreName = @StoreName;
select @storeId;
select PropertyName, PropertyMapType, PropertyType from tblBigTableStoreInfo
where fkStoreId = @storeId and Active = 1 order by PropertyIndex;";
                AddParameter(command, "StoreName", storeName);

                using var reader = command.ExecuteReader();
                if (!reader.Read() || reader.IsDBNull(0))
                {
                    return (IReadOnlyList<RawProperty>)null;
                }

                reader.NextResult();
                var properties = new List<RawProperty>();
                while (reader.Read())
                {
                    properties.Add(new RawProperty(reader.GetString(0), reader.GetString(1), reader.GetString(2)));
                }

                return properties;
            });
        }

        public long Count(string storeName)
        {
            var db = _database();
            return db.Execute(() =>
            {
                using var command = db.CreateCommand();
                command.CommandType = CommandType.Text;
                command.CommandText = "select count_big(*) from " + ViewName(storeName);
                return Convert.ToInt64(command.ExecuteScalar());
            });
        }

        public IReadOnlyList<IReadOnlyDictionary<string, object>> GetPage(string storeName, int skip, int take)
        {
            var db = _database();
            return db.Execute(() =>
            {
                using var command = db.CreateCommand();
                command.CommandType = CommandType.Text;
                command.CommandText = "select * from " + ViewName(storeName)
                    + " order by StoreId offset @Skip rows fetch next @Take rows only";
                AddParameter(command, "Skip", skip);
                AddParameter(command, "Take", take);

                using var reader = command.ExecuteReader();
                var rows = new List<IReadOnlyDictionary<string, object>>();
                while (reader.Read())
                {
                    rows.Add(ReadRow(reader));
                }

                return (IReadOnlyList<IReadOnlyDictionary<string, object>>)rows;
            });
        }

        /// <returns>The item's view row, or null when the store has no item with that StoreId.</returns>
        public IReadOnlyDictionary<string, object> GetRow(string storeName, long storeId)
        {
            var db = _database();
            return db.Execute(() =>
            {
                using var command = db.CreateCommand();
                command.CommandType = CommandType.Text;
                command.CommandText = "select * from " + ViewName(storeName) + " where StoreId = @StoreId";
                AddParameter(command, "StoreId", storeId);

                using var reader = command.ExecuteReader();
                return reader.Read() ? ReadRow(reader) : null;
            });
        }

        /// <summary>
        /// The item's collection elements and references, in stored order. <c>Ref</c> is the
        /// referenced item's Identity (storeId:GUID) when the row points at another item.
        /// </summary>
        public IReadOnlyList<IReadOnlyDictionary<string, object>> GetReferences(long storeId)
        {
            var db = _database();
            return db.Execute(() =>
            {
                using var command = db.CreateCommand();
                command.CommandType = CommandType.Text;
                command.CommandText = @"
select r.PropertyName, r.[Index], r.IsKey, r.CollectionType, r.ElementType, r.ElementStoreName,
       r.BooleanValue, r.IntegerValue, r.LongValue, r.DateTimeValue, r.GuidValue, r.FloatValue,
       r.StringValue, r.BinaryValue, r.DecimalValue, r.ExternalIdValue,
       case when r.RefIdValue is null then null
            else cast(r.RefIdValue as varchar(50)) + ':' + upper(cast(i.Guid as varchar(50))) end as Ref
from tblBigTableReference r
left outer join tblBigTableIdentity i on r.RefIdValue = i.pkId
where r.pkId = @StoreId
order by r.PropertyName, r.[Index], r.IsKey desc";
                AddParameter(command, "StoreId", storeId);

                using var reader = command.ExecuteReader();
                var rows = new List<IReadOnlyDictionary<string, object>>();
                while (reader.Read())
                {
                    rows.Add(ReadRow(reader));
                }

                return (IReadOnlyList<IReadOnlyDictionary<string, object>>)rows;
            });
        }

        internal static string ViewName(string storeName) => "[dbo].[VW_" + storeName.Replace("]", "]]") + "]";

        private static Dictionary<string, object> ReadRow(DbDataReader reader)
        {
            var row = new Dictionary<string, object>(StringComparer.Ordinal);
            for (var i = 0; i < reader.FieldCount; i++)
            {
                row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
            }

            return row;
        }

        private static void AddParameter(DbCommand command, string name, object value)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = "@" + name;
            parameter.Value = value ?? DBNull.Value;
            command.Parameters.Add(parameter);
        }
    }

    /// <param name="TypeName">The .NET type as DDS stored it: an assembly-qualified name that may no longer resolve.</param>
    public sealed record RawProperty(string Name, string MapType, string TypeName);
}
