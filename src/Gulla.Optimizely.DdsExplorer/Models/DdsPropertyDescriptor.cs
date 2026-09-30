using System;
using EPiServer.Data.Dynamic;
using Gulla.Optimizely.DdsExplorer.Services;

namespace Gulla.Optimizely.DdsExplorer.Models
{
    /// <summary>
    /// One active property of a store, as the explorer sees it. Decoupled from <see cref="PropertyMap"/>
    /// so the conversion rules can be tested without a database behind them.
    /// </summary>
    public sealed class DdsPropertyDescriptor
    {
        public DdsPropertyDescriptor(string name, Type clrType, PropertyMapType mapType)
        {
            Name = name;
            ClrType = clrType;
            MapType = mapType;
        }

        public string Name { get; }

        /// <summary>
        /// The declared CLR type, or null when the type could not be loaded — typically because the
        /// assembly that owned the store is no longer deployed.
        /// </summary>
        public Type ClrType { get; }

        public PropertyMapType MapType { get; }

        public string TypeName => ClrType == null ? "(unresolved type)" : FriendlyTypeName(ClrType);

        /// <summary>
        /// Only inline properties of a type the converter understands can be edited. Collections and
        /// references live in other rows or stores; re-saving them from JSON would need a
        /// <see cref="TypeToStoreMapper"/> the explorer cannot know, so they are left untouched.
        /// </summary>
        public bool Editable => MapType == PropertyMapType.Inline && ClrType != null && DdsItemConverter.IsSupported(ClrType);

        internal static string FriendlyTypeName(Type type)
        {
            var underlying = Nullable.GetUnderlyingType(type);
            if (underlying != null)
            {
                return FriendlyTypeName(underlying) + "?";
            }

            if (!type.IsGenericType)
            {
                return type.FullName ?? type.Name;
            }

            var name = type.GetGenericTypeDefinition().FullName ?? type.Name;
            var tick = name.IndexOf('`');
            if (tick >= 0)
            {
                name = name.Substring(0, tick);
            }

            return name + "<" + string.Join(", ", Array.ConvertAll(type.GetGenericArguments(), FriendlyTypeName)) + ">";
        }
    }
}
