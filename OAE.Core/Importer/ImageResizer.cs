using System.Diagnostics;

namespace OAE.Core.Importer;

/// <summary>
/// Resizes an image to a target W×H. The result lands in a temp file the
/// caller is responsible for deleting (typically by passing it through to
/// <see cref="AssetImporter"/> and then cleaning up after).
/// </summary>
public interface IImageResizer
{
    Task<string> ResizeAsync(string sourcePath, int width, int height, CancellationToken ct = default);
    bool IsAvailable { get; }
}

/// <summary>
/// macOS-only resizer using the built-in <c>sips</c> tool. Zero extra deps,
/// preserves alpha, and does the right thing for PNG/JPEG. Replace with a
/// SkiaSharp implementation when cross-platform support is needed.
/// </summary>
/// <remarks>
/// <c>sips -z H W src --out dest</c> — note the unusual height-then-width
/// ordering. For a non-uniform target (rare), this stretches; if we ever
/// want letterboxing instead, swap to ImageMagick or SkiaSharp.
/// </remarks>
public sealed class SipsImageResizer : IImageResizer
{
    public bool IsAvailable => OperatingSystem.IsMacOS() && File.Exists("/usr/bin/sips");

    public async Task<string> ResizeAsync(string sourcePath, int width, int height, CancellationToken ct = default)
    {
        if (!IsAvailable) throw new InvalidOperationException("sips not available on this platform");

        var ext = Path.GetExtension(sourcePath);
        var dest = Path.Combine(
            Path.GetTempPath(),
            $"oae-resize-{Guid.NewGuid():N}{(string.IsNullOrEmpty(ext) ? ".png" : ext)}");

        var psi = new ProcessStartInfo
        {
            FileName = "/usr/bin/sips",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add("-z");
        psi.ArgumentList.Add(height.ToString());     // sips wants H then W
        psi.ArgumentList.Add(width.ToString());
        psi.ArgumentList.Add(sourcePath);
        psi.ArgumentList.Add("--out");
        psi.ArgumentList.Add(dest);

        using var proc = new Process { StartInfo = psi };
        proc.Start();
        var errTask = proc.StandardError.ReadToEndAsync(ct);
        await proc.WaitForExitAsync(ct);
        if (proc.ExitCode != 0)
        {
            try { File.Delete(dest); } catch { /* ignore */ }
            var err = await errTask;
            throw new IOException($"sips exited {proc.ExitCode}: {err}");
        }
        return dest;
    }
}
