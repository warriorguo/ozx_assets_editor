using System.Diagnostics;

namespace OAE.Core.Importer;

/// <summary>
/// Outcome of a single <see cref="AssetImporter.RunAsync"/> invocation.
/// </summary>
public sealed record ImportResult(
    int ExitCode,
    string StdOut,
    string StdErr,
    long DurationMs)
{
    public bool IsSuccess => ExitCode == 0;
}

/// <summary>
/// Wraps the <c>import-asset</c> Python skill as a subprocess. OAE doesn't
/// re-implement the per-asset-type pipelines — they live in
/// <c>~/.claude/skills/import-asset/pipelines/*.json</c> and run via
/// <c>scripts/import_asset.py</c>. This class owns the path discovery,
/// argv assembly, and stdout/stderr capture.
/// </summary>
public sealed class AssetImporter
{
    private readonly string? _scriptPath;
    private readonly string _pythonExe;

    /// <summary>
    /// Construct with an optional explicit script path (from
    /// <c>OaeConfig.ImportAssetSkillPath</c>). When <c>null</c>, autodiscovers
    /// the conventional location under <c>~/.claude/skills/import-asset/</c>.
    /// </summary>
    public AssetImporter(string? configuredScriptPath = null, string pythonExe = "python3")
    {
        _scriptPath = ResolveScriptPath(configuredScriptPath);
        _pythonExe = pythonExe;
    }

    /// <summary>
    /// Resolved path, or <c>null</c> if neither the config override nor the
    /// default location exists. The drop-zone uses this to disable itself
    /// with a clear message rather than failing on every drop.
    /// </summary>
    public string? ScriptPath => _scriptPath;

    public bool IsAvailable => _scriptPath is not null;

    /// <summary>
    /// Run the import pipeline. <paramref name="extraArgs"/> are appended after
    /// the standard flags so per-pipeline options (e.g. <c>--rows 4</c>) can be
    /// passed through. Cancelling the token kills the subprocess.
    /// </summary>
    public async Task<ImportResult> RunAsync(
        string pipeline,
        string sourcePath,
        string projectRoot,
        string entityId,
        IReadOnlyList<string>? extraArgs = null,
        CancellationToken cancellationToken = default)
    {
        if (_scriptPath is null)
            throw new InvalidOperationException("import-asset script not found; set ImportAssetSkillPath in config.");

        var args = new List<string>
        {
            _scriptPath,
            sourcePath,
            "--as", pipeline,
            "--unity-project", projectRoot,
            "--name", entityId,
        };
        if (extraArgs is not null) args.AddRange(extraArgs);

        var psi = new ProcessStartInfo
        {
            FileName = _pythonExe,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        using var proc = new Process { StartInfo = psi };
        var stopwatch = Stopwatch.StartNew();
        proc.Start();

        var outTask = proc.StandardOutput.ReadToEndAsync(cancellationToken);
        var errTask = proc.StandardError.ReadToEndAsync(cancellationToken);

        try
        {
            await proc.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            try { proc.Kill(entireProcessTree: true); } catch { /* best effort */ }
            throw;
        }

        var stdout = await outTask;
        var stderr = await errTask;
        stopwatch.Stop();
        return new ImportResult(proc.ExitCode, stdout, stderr, stopwatch.ElapsedMilliseconds);
    }

    private static string? ResolveScriptPath(string? configured)
    {
        if (!string.IsNullOrEmpty(configured) && File.Exists(configured))
            return configured;
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var candidate = Path.Combine(home, ".claude", "skills", "import-asset", "scripts", "import_asset.py");
        return File.Exists(candidate) ? candidate : null;
    }
}
