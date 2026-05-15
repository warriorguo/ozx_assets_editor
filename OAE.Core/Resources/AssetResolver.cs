using System.Text.Json.Nodes;
using OAE.Core.Schema;

namespace OAE.Core.Resources;

/// <summary>
/// One image asset associated with the open entity, ready to render in the
/// Images tab. <see cref="ImagePath"/> is the file the user actually sees;
/// <see cref="DirectAssetPath"/> is the file the ResourcesDB key points at,
/// which may be a SpriteAnimationData <c>.asset</c> rather than a PNG.
/// </summary>
public sealed record ResolvedAsset(
    string AssetKey,
    string JsonPath,
    string Pipeline,
    string? ImagePath,
    string? DirectAssetPath,
    int? Width,
    int? Height,
    int Hops);

/// <summary>
/// Walks an entity's <see cref="JsonObject"/> + <see cref="SchemaModel"/>
/// and surfaces every field with an <see cref="EditorMeta.AssetKey"/> so the
/// Images tab can render a card per asset.
/// </summary>
public static class AssetResolver
{
    public static IReadOnlyList<ResolvedAsset> Resolve(SchemaModel schema, JsonObject entity, AssetLocator locator)
    {
        var results = new List<ResolvedAsset>();
        Walk(schema.Fields, entity, basePath: "", schema.Type, locator, results);
        return results;
    }

    private static void Walk(
        IReadOnlyList<FieldDescriptor> fields,
        JsonObject? parent,
        string basePath,
        Type rootType,
        AssetLocator locator,
        List<ResolvedAsset> sink)
    {
        if (parent is null) return;
        foreach (var field in fields)
        {
            var jsonPath = string.IsNullOrEmpty(basePath) ? field.Name : $"{basePath}.{field.Name}";
            var node = parent[field.Name];

            switch (field.Kind)
            {
                case FieldKind.String when field.Meta?.AssetKey is { } pipeline:
                    var key = node?.GetValue<string?>();
                    if (!string.IsNullOrEmpty(key))
                        sink.Add(BuildResolved(key, jsonPath, pipeline, locator));
                    break;

                case FieldKind.Object when field.Nested is not null:
                    Walk(field.Nested.Fields, node as JsonObject, jsonPath, rootType, locator, sink);
                    break;

                case FieldKind.Array when field.Element is not null && node is JsonArray arr:
                    var elemPath = jsonPath + "[]";
                    for (var i = 0; i < arr.Count; i++)
                    {
                        var elem = arr[i];
                        if (field.Element.Kind == FieldKind.String
                            && field.Element.Meta?.AssetKey is { } elemPipeline)
                        {
                            var elemKey = elem?.GetValue<string?>();
                            if (!string.IsNullOrEmpty(elemKey))
                                sink.Add(BuildResolved(elemKey, $"{jsonPath}[{i}]", elemPipeline, locator));
                        }
                        else if (field.Element.Kind == FieldKind.Object
                            && field.Element.Nested is not null
                            && elem is JsonObject obj)
                        {
                            Walk(field.Element.Nested.Fields, obj, $"{jsonPath}[{i}]", rootType, locator, sink);
                        }
                    }
                    break;
            }
        }
    }

    private static ResolvedAsset BuildResolved(string key, string jsonPath, string pipeline, AssetLocator locator)
    {
        var located = locator.Resolve(key);
        (int W, int H)? dims = null;
        if (located.ImagePath is not null)
            dims = PngReader.TryReadDimensions(located.ImagePath);
        return new ResolvedAsset(
            AssetKey: key,
            JsonPath: jsonPath,
            Pipeline: pipeline,
            ImagePath: located.ImagePath,
            DirectAssetPath: located.DirectAssetPath,
            Width: dims?.W,
            Height: dims?.H,
            Hops: located.Hops);
    }
}
