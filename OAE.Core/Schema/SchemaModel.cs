namespace OAE.Core.Schema;

/// <summary>
/// Kind of a field as observed by reflection over a Game.Contracts.Data
/// class. Mirrors what the form layer needs to render a control.
/// </summary>
public enum FieldKind
{
    String,
    Int,
    Long,
    Float,
    Double,
    Bool,
    Enum,
    Object,
    Array,
    Unknown,
}

/// <summary>
/// One field of a class, post-reflection. The form renderer reads this to
/// pick a control; the cross-reference picker (OAE-6) reads <see cref="Meta"/>
/// to decide whether to render a dropdown vs a free-text input.
/// </summary>
public sealed record FieldDescriptor(
    string Name,
    FieldKind Kind,
    Type ClrType)
{
    /// <summary>
    /// Set when <see cref="Kind"/> is <see cref="FieldKind.Object"/> — the
    /// schema for the nested class.
    /// </summary>
    public SchemaModel? Nested { get; init; }

    /// <summary>
    /// Set when <see cref="Kind"/> is <see cref="FieldKind.Array"/> — the
    /// descriptor for one element. Nested arrays are supported by recursion.
    /// </summary>
    public FieldDescriptor? Element { get; init; }

    /// <summary>
    /// Set when <see cref="Kind"/> is <see cref="FieldKind.Enum"/> — the
    /// allowed string values.
    /// </summary>
    public IReadOnlyList<string>? Enum { get; init; }

    /// <summary>
    /// Editor-only metadata supplied by <see cref="EditorMetadata"/>,
    /// resolved against the *root* schema type plus the dotted JSON path
    /// that reaches this field. <c>null</c> means no metadata registered.
    /// </summary>
    public EditorMeta? Meta { get; init; }
}

/// <summary>
/// Reflected schema for one C# class. Field order matches declaration order
/// so the form preserves the structure designers see in the source.
/// </summary>
public sealed record SchemaModel(
    Type Type,
    IReadOnlyList<FieldDescriptor> Fields);
