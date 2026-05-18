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

    public static IEnumerable<object[]> P1Types() => new[]
    {
        new object[] { "enemies",     typeof(EnemyData)      },
        new object[] { "weapons",     typeof(WeaponData)     },
        new object[] { "projectiles", typeof(ProjectileData) },
        new object[] { "skills",      typeof(SkillData)      },
        new object[] { "items",       typeof(ItemData)       },
    };

    [Theory]
    [MemberData(nameof(P1Types))]
    public void Every_file_in_type_bucket_deserialises(string typeId, Type clrType)
    {
        var ozx = ResolveSiblingOzxBase();
        if (ozx is null) { _out.WriteLine($"{typeId}: ozx_base sibling not found — skipped."); return; }

        // Sanity-check our own map agrees with the type passed in.
        Assert.Equal(clrType, EntityTypes.Map[typeId]);

        var dir = Path.Combine(ozx, "Assets", "StreamingAssets", "GameData", typeId);
        Assert.True(Directory.Exists(dir), $"missing dir: {dir}");
        var files = Directory.EnumerateFiles(dir, "*.json", SearchOption.TopDirectoryOnly).ToList();
        Assert.NotEmpty(files);

        var failures = new List<string>();
        foreach (var path in files)
        {
            var json = File.ReadAllText(path);
            try
            {
                var obj = JsonSerializer.Deserialize(json, clrType, DeserOpts);
                if (obj is null) failures.Add($"{Path.GetFileName(path)}: deserialised to null");
            }
            catch (Exception ex)
            {
                failures.Add($"{Path.GetFileName(path)}: {ex.GetType().Name}: {ex.Message}");
            }
        }

        _out.WriteLine($"{typeId}: deserialised {files.Count - failures.Count}/{files.Count}");
        if (failures.Count > 0)
        {
            _out.WriteLine("--- failures ---");
            foreach (var f in failures) _out.WriteLine("  " + f);
        }
        Assert.Empty(failures);
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
