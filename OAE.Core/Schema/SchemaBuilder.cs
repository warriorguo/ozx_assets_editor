using System.Collections.Concurrent;
using System.Reflection;

namespace OAE.Core.Schema;

/// <summary>
/// Reflects over a Game.Contracts.Data class and produces a
/// <see cref="SchemaModel"/>. Caches per type so repeated calls are O(1).
/// </summary>
/// <remarks>
/// JsonUtility (Unity) only sees public instance fields, so OAE walks the
/// same surface and ignores properties. This matches what the JSON files on
/// disk actually contain.
/// </remarks>
public static class SchemaBuilder
{
    private static readonly ConcurrentDictionary<Type, SchemaModel> Cache = new();

    public static SchemaModel For<T>() => For(typeof(T));

    public static SchemaModel For(Type type) =>
        Cache.GetOrAdd(type, t => Build(t, jsonPathPrefix: "", rootType: t, seen: new HashSet<Type>()));

    /// <summary>
    /// Build for a <paramref name="type"/>, treating <paramref name="rootType"/>
    /// as the metadata-lookup anchor (the entity type the user is editing).
    /// </summary>
    private static SchemaModel Build(Type type, string jsonPathPrefix, Type rootType, HashSet<Type> seen)
    {
        if (!seen.Add(type))
        {
            // Cycle — return an opaque schema so the caller doesn't recurse forever.
            return new SchemaModel(type, Array.Empty<FieldDescriptor>());
        }

        var fields = new List<FieldDescriptor>();
        foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
        {
            // JsonUtility skips fields with [NonSerialized]; mirror that.
            if (field.IsDefined(typeof(NonSerializedAttribute), inherit: false)) continue;
            fields.Add(BuildField(field.Name, field.FieldType, jsonPathPrefix, rootType, seen));
        }

        seen.Remove(type);
        return new SchemaModel(type, fields);
    }

    private static FieldDescriptor BuildField(
        string name,
        Type fieldType,
        string jsonPathPrefix,
        Type rootType,
        HashSet<Type> seen)
    {
        var jsonPath = string.IsNullOrEmpty(jsonPathPrefix) ? name : $"{jsonPathPrefix}.{name}";
        var meta = EditorMetadata.For(rootType, jsonPath);

        // Arrays
        if (fieldType.IsArray)
        {
            var elementType = fieldType.GetElementType()!;
            var element = BuildField("[]", elementType, jsonPath + "[]", rootType, seen);
            return new FieldDescriptor(name, FieldKind.Array, fieldType)
            {
                Element = element,
                Meta = meta,
            };
        }

        // System.Collections.Generic.List<T>
        if (fieldType.IsGenericType && fieldType.GetGenericTypeDefinition() == typeof(List<>))
        {
            var elementType = fieldType.GetGenericArguments()[0];
            var element = BuildField("[]", elementType, jsonPath + "[]", rootType, seen);
            return new FieldDescriptor(name, FieldKind.Array, fieldType)
            {
                Element = element,
                Meta = meta,
            };
        }

        // Enums
        if (fieldType.IsEnum)
        {
            return new FieldDescriptor(name, FieldKind.Enum, fieldType)
            {
                Enum = Enum.GetNames(fieldType),
                Meta = meta,
            };
        }

        // Primitives
        var kind = ClassifyPrimitive(fieldType);
        if (kind != FieldKind.Object && kind != FieldKind.Unknown)
        {
            return new FieldDescriptor(name, kind, fieldType) { Meta = meta };
        }

        // Nested class (or [Serializable] struct)
        if (fieldType.IsClass || (fieldType.IsValueType && !fieldType.IsPrimitive))
        {
            var nested = Build(fieldType, jsonPath, rootType, seen);
            return new FieldDescriptor(name, FieldKind.Object, fieldType)
            {
                Nested = nested,
                Meta = meta,
            };
        }

        return new FieldDescriptor(name, FieldKind.Unknown, fieldType) { Meta = meta };
    }

    private static FieldKind ClassifyPrimitive(Type t) => Type.GetTypeCode(t) switch
    {
        TypeCode.String  => FieldKind.String,
        TypeCode.Boolean => FieldKind.Bool,
        TypeCode.Int32   => FieldKind.Int,
        TypeCode.Int16   => FieldKind.Int,
        TypeCode.Byte    => FieldKind.Int,
        TypeCode.SByte   => FieldKind.Int,
        TypeCode.UInt16  => FieldKind.Int,
        TypeCode.UInt32  => FieldKind.Long,
        TypeCode.Int64   => FieldKind.Long,
        TypeCode.UInt64  => FieldKind.Long,
        TypeCode.Single  => FieldKind.Float,
        TypeCode.Double  => FieldKind.Double,
        TypeCode.Decimal => FieldKind.Double,
        _                => FieldKind.Object, // means "drill into it as a class"
    };
}
