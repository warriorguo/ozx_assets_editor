using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace OAE.Core.Templates;

/// <summary>
/// One creation template for an entity type. The body is the partial JSON
/// the form pre-fills when the user clicks 'New' and picks this template.
/// </summary>
public sealed record TemplateDescriptor(
    string Id,
    string Label,
    string? Description,
    string Body);

/// <summary>
/// Loads JSON templates embedded under <c>OAE.Core/Templates/&lt;type&gt;/*.json</c>.
/// Each file is a wrapper with <c>id</c>, <c>label</c>, <c>description</c>,
/// <c>body</c>; <see cref="TemplateDescriptor.Body"/> is the body re-serialised.
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

            if (!byType.TryGetValue(typeId, out var list))
                byType[typeId] = list = new List<TemplateDescriptor>();
            list.Add(new TemplateDescriptor(templateId, label, description, bodyJson));
        }

        foreach (var list in byType.Values)
            list.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));

        return byType.ToDictionary(
            kv => kv.Key,
            kv => (IReadOnlyList<TemplateDescriptor>)kv.Value);
    }
}
