using System.Text.Json;
using Game.Contracts.Data;
using OAE.Core.Schema;
using Xunit.Abstractions;

namespace OAE.Tests;

/// <summary>
/// Round-trip every JSON file under the OAE-7 P1 entity types through
/// System.Text.Json into the matching <c>Game.Contracts.Data</c> class.
/// Surfaces real drift between the JSON files in ozx_base and the C# layer.
/// </summary>
public class EntityJsonValidationTests
{
    private readonly ITestOutputHelper _out;
    public EntityJsonValidationTests(ITestOutputHelper output) => _out = output;

    private static readonly JsonSerializerOptions DeserOpts = new()
    {
        IncludeFields = true,
        PropertyNamingPolicy = null,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>
    /// All 22 entity types from <see cref="EntityTypes.Map"/>. Theory-driven so
    /// a failure in one type's bucket is isolated rather than aborting the rest.
    /// </summary>
    public static IEnumerable<object[]> AllTypes() =>
        EntityTypes.Map.Select(kv => new object[] { kv.Key, kv.Value });

    /// <summary>
    /// Known JSON / C# drift in ozx_base, tracked upstream. New drift must
    /// not be silently allowlisted — file a bug against ozx_base instead.
    /// </summary>
    private static readonly HashSet<string> KnownDrift = new(StringComparer.Ordinal)
    {
        // (none currently — OZX-389 removed necromancer.json, the previous drift case)
    };

    [Theory]
    [MemberData(nameof(AllTypes))]
    public void Every_file_in_type_bucket_deserialises(string typeId, Type clrType)
    {
        var ozx = ResolveSiblingOzxBase();
        if (ozx is null) { _out.WriteLine($"{typeId}: ozx_base sibling not found — skipped."); return; }

        // Sanity-check our own map agrees with the type passed in.
        Assert.Equal(clrType, EntityTypes.Map[typeId]);

        var dir = Path.Combine(ozx, "Assets", "StreamingAssets", "GameData", typeId);
        Assert.True(Directory.Exists(dir), $"missing dir: {dir}");
        var files = Directory.EnumerateFiles(dir, "*.json", SearchOption.TopDirectoryOnly).ToList();
        // An empty bucket is legitimate upstream state (e.g. all bosses removed
        // in OZX-389). Skip with a log line — the test is about JSON we *have*
        // failing to deserialize, not about bucket population.
        if (files.Count == 0) { _out.WriteLine($"{typeId}: bucket is empty — skipped."); return; }

        var unexpectedFailures = new List<string>();
        var expectedFailures = new List<string>();
        foreach (var path in files)
        {
            var relKey = $"{typeId}/{Path.GetFileName(path)}";
            var json = File.ReadAllText(path);
            try
            {
                var obj = JsonSerializer.Deserialize(json, clrType, DeserOpts);
                if (obj is null)
                {
                    (KnownDrift.Contains(relKey) ? expectedFailures : unexpectedFailures)
                        .Add($"{Path.GetFileName(path)}: deserialised to null");
                }
            }
            catch (Exception ex)
            {
                (KnownDrift.Contains(relKey) ? expectedFailures : unexpectedFailures)
                    .Add($"{Path.GetFileName(path)}: {ex.GetType().Name}: {ex.Message}");
            }
        }

        _out.WriteLine($"{typeId}: deserialised {files.Count - unexpectedFailures.Count - expectedFailures.Count}/{files.Count}");
        if (expectedFailures.Count > 0)
        {
            _out.WriteLine("--- known drift (tracked upstream) ---");
            foreach (var f in expectedFailures) _out.WriteLine("  " + f);
        }
        if (unexpectedFailures.Count > 0)
        {
            _out.WriteLine("--- unexpected failures ---");
            foreach (var f in unexpectedFailures) _out.WriteLine("  " + f);
        }
        Assert.Empty(unexpectedFailures);
    }

    private static string? ResolveSiblingOzxBase()
    {
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 8 && dir is not null; i++)
        {
            var sibling = Path.Combine(dir, "..", "ozx_base", "Assets", "StreamingAssets", "GameData");
            if (Directory.Exists(sibling)) return Path.GetFullPath(Path.Combine(dir, "..", "ozx_base"));
            dir = Path.GetDirectoryName(dir);
        }
        return null;
    }
}
