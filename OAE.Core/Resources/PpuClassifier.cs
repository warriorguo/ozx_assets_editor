namespace OAE.Core.Resources;

/// <summary>
/// How strictly OAE cares about the PPU on a given sprite.
/// </summary>
public enum PpuSeverity
{
    /// <summary>PPU matches the rule for this path bucket.</summary>
    Ok,
    /// <summary>Mismatch in a 'preferred 68' bucket. Informational; legacy assets tolerated.</summary>
    Preferred,
    /// <summary>Mismatch in a strict bucket (world-space tile/wall). Must be fixed for clean rendering.</summary>
    Strict,
    /// <summary>Path is in a bucket where PPU is irrelevant (UI, tools, materials).</summary>
    Ignored,
}

public sealed record PpuVerdict(PpuSeverity Severity, string Reason);

/// <summary>
/// Classifies a sprite's PPU against the OZX-367 rule (PPU=68 strict for
/// world-space tile/wall art, preferred for character/enemy/projectile/FX,
/// irrelevant for UI). Pure function; no IO. See OAE-16 ticket.
/// </summary>
public static class PpuClassifier
{
    /// <summary>The single PPU master OZX targets.</summary>
    public const int MasterPpu = 68;

    private static readonly (string Segment, PpuSeverity Bucket, string Label)[] Buckets =
    {
        ("/Room/",       PpuSeverity.Strict,    "world-space tile/wall art"),
        ("/Characters/", PpuSeverity.Preferred, "character art"),
        ("/Weapons/",    PpuSeverity.Preferred, "weapon sprite"),
        ("/Effects/",    PpuSeverity.Preferred, "effect / FX sprite"),
        ("/Skills/",     PpuSeverity.Preferred, "skill sprite"),
        ("/Loot/",       PpuSeverity.Preferred, "loot sprite"),
        ("/UI/",         PpuSeverity.Ignored,   "UI sprite, PPU not enforced"),
        ("/_Tools/",     PpuSeverity.Ignored,   "dev-tools asset"),
        ("/Materials/",  PpuSeverity.Ignored,   "material / texture"),
    };

    /// <summary>
    /// Classify a sprite. <paramref name="relativePath"/> is the asset path as
    /// it appears under the project (e.g. <c>Assets/Images/Room/Bridge/Bridge1.png</c>);
    /// the classifier matches case-insensitive substrings.
    /// </summary>
    public static PpuVerdict Classify(string relativePath, int ppu)
    {
        // Normalise to forward slashes so matching works on both Unix and Win.
        var norm = "/" + relativePath.Replace('\\', '/').Trim('/') + "/";

        foreach (var (segment, bucket, label) in Buckets)
        {
            if (norm.IndexOf(segment, StringComparison.OrdinalIgnoreCase) < 0) continue;
            return bucket switch
            {
                PpuSeverity.Strict when ppu != MasterPpu =>
                    new PpuVerdict(PpuSeverity.Strict,
                        $"{label}, must be PPU {MasterPpu} (got {ppu})"),
                PpuSeverity.Preferred when ppu != MasterPpu =>
                    new PpuVerdict(PpuSeverity.Preferred,
                        $"{label}, PPU {MasterPpu} preferred (got {ppu})"),
                PpuSeverity.Ignored =>
                    new PpuVerdict(PpuSeverity.Ignored, label),
                _ => new PpuVerdict(PpuSeverity.Ok, $"{label}, PPU {ppu} matches master"),
            };
        }

        // Unclassified — be conservative: treat as ignored so we don't nag on
        // folders the user organises in unexpected ways.
        return new PpuVerdict(PpuSeverity.Ignored, "unclassified folder, PPU not enforced");
    }
}
