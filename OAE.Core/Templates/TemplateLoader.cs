using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace OAE.Core.Templates;

/// <summary>
/// One asset slot declared by a template — the wizard renders one drop zone
/// per slot, the pipeline name is passed to <c>import_asset.py</c> when the
/// new entity is saved. See OAE-15 for the wizard side.
/// </summary>
public sealed record AssetSlot(string Name, string Pipeline, string? Hint);

/// <summary>
/// One creation template for an entity type. The body is the partial JSON
/// the form pre-fills when the user clicks 'New' and picks this template.
/// Slots are surfaced as drop zones in the wizard (OAE-15 follow-up).
/// </summary>
public sealed record TemplateDescriptor(
    string Id,
    string Label,
    string? Description,
    string Body,
    IReadOnlyList<AssetSlot> AssetSlots);

/// <summary>
/// Loads JSON templates embedded under <c>OAE.Core/Templates/&lt;type&gt;/*.json</c>.
/// Each file is a wrapper with <c>id</c>, <c>label</c>, <c>description</c>,
/// optional <c>assetSlots</c>, and <c>body</c>; <see cref="TemplateDescriptor.Body"/>
/// is the body re-serialised.
/// </summary>
public static class TemplateLoader
{
    private const string ResourcePrefix = "OAE.Core.Templates.";
    private static readonly Assembly Assembly = typeof(TemplateLoader).Assembly;
    private static readonly Lazy<IReadOnlyDictionary<string, IReadOnlyList<TemplateDescriptor>>> Index =
        new(BuildIndex);

    public static IReadOnlyList<TemplateDescriptor> For(string typeId) =>
        Index.Value.TryGetValue(typeId, out var list) ? list : Array.Empty<TemplateDescriptor>();

    public static TemplateDescriptor? Get(string typeId, string templateId)
    {
        foreach (var t in For(typeId))
            if (t.Id == templateId) return t;
        return null;
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<TemplateDescriptor>> BuildIndex()
    {
        var byType = new Dictionary<string, List<TemplateDescriptor>>();
        var jsonOpts = new JsonSerializerOptions { WriteIndented = true };

        foreach (var name in Assembly.GetManifestResourceNames())
        {
            if (!name.StartsWith(ResourcePrefix, StringComparison.Ordinal)) continue;
            if (!name.EndsWith(".json", StringComparison.Ordinal)) continue;

            // OAE.Core.Templates.<typeId>.<templateId>.json — split on the last two dots.
            var middle = name.Substring(ResourcePrefix.Length, name.Length - ResourcePrefix.Length - ".json".Length);
            var lastDot = middle.LastIndexOf('.');
            if (lastDot < 0) continue;
            var typeId = middle[..lastDot];
            var templateId = middle[(lastDot + 1)..];

            using var stream = Assembly.GetManifestResourceStream(name)!;
            using var reader = new StreamReader(stream);
            var raw = reader.ReadToEnd();
            var node = JsonNode.Parse(raw)?.AsObject();
            if (node is null) continue;

            var label = node["label"]?.GetValue<string>() ?? templateId;
            var description = node["description"]?.GetValue<string>();
            var body = node["body"];
            var bodyJson = body is null ? "{}" : body.ToJsonString(jsonOpts);

            var slots = ParseAssetSlots(node["assetSlots"] as JsonArray);

            if (!byType.TryGetValue(typeId, out var list))
                byType[typeId] = list = new List<TemplateDescriptor>();
            list.Add(new TemplateDescriptor(templateId, label, description, bodyJson, slots));
        }

        foreach (var list in byType.Values)
            list.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));

        return byType.ToDictionary(
            kv => kv.Key,
            kv => (IReadOnlyList<TemplateDescriptor>)kv.Value);
    }

    private static IReadOnlyList<AssetSlot> ParseAssetSlots(JsonArray? arr)
    {
        if (arr is null || arr.Count == 0) return Array.Empty<AssetSlot>();
        var slots = new List<AssetSlot>(arr.Count);
        foreach (var item in arr)
        {
            if (item is not JsonObject obj) continue;
            var slotName = obj["name"]?.GetValue<string?>();
            var pipeline = obj["pipeline"]?.GetValue<string?>();
            if (string.IsNullOrEmpty(slotName) || string.IsNullOrEmpty(pipeline)) continue;
            var hint = obj["hint"]?.GetValue<string?>();
            slots.Add(new AssetSlot(slotName!, pipeline!, hint));
        }
        return slots;
    }
}
