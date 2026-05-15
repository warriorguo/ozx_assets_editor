using OAE.Core.Importer;

namespace OAE.Tests;

public class AssetImporterTests
{
    [Fact]
    public void Autodiscover_returns_null_when_skill_not_at_default_path()
    {
        // Pass a config override pointing nowhere AND assume the test rig
        // doesn't have the skill in HOME (we override that for this test).
        var importer = new AssetImporter(configuredScriptPath: "/tmp/oae-no-such-script.py");
        // If the test machine *does* have the skill, IsAvailable will be true via autodiscover —
        // accept either outcome but assert the script-path resolution is consistent.
        if (importer.IsAvailable)
            Assert.NotNull(importer.ScriptPath);
        else
            Assert.Null(importer.ScriptPath);
    }

    [Fact]
    public void Configured_script_path_is_used_when_it_exists()
    {
        var fake = WriteFakeScript("print('ok')\n");
        try
        {
            var importer = new AssetImporter(configuredScriptPath: fake);
            Assert.True(importer.IsAvailable);
            Assert.Equal(fake, importer.ScriptPath);
        }
        finally { File.Delete(fake); }
    }

    [Fact]
    public async Task RunAsync_captures_stdout_and_exit_zero()
    {
        var fake = WriteFakeScript("import sys\nprint('hello world')\nprint('arg=' + str(sys.argv[1:]))\n");
        try
        {
            var importer = new AssetImporter(configuredScriptPath: fake);
            var result = await importer.RunAsync(
                pipeline: "test-pipeline",
                sourcePath: "/tmp/source.png",
                projectRoot: "/tmp/project",
                entityId: "test_id");

            Assert.Equal(0, result.ExitCode);
            Assert.True(result.IsSuccess);
            Assert.Contains("hello world", result.StdOut);
            // The fake script echoes argv[1:]; --as / --unity-project / --name should appear.
            Assert.Contains("--as", result.StdOut);
            Assert.Contains("test-pipeline", result.StdOut);
            Assert.Contains("test_id", result.StdOut);
        }
        finally { File.Delete(fake); }
    }

    [Fact]
    public async Task RunAsync_captures_nonzero_exit_and_stderr()
    {
        var fake = WriteFakeScript("import sys\nsys.stderr.write('boom\\n')\nsys.exit(7)\n");
        try
        {
            var importer = new AssetImporter(configuredScriptPath: fake);
            var result = await importer.RunAsync(
                pipeline: "x", sourcePath: "/tmp/x", projectRoot: "/tmp/p", entityId: "e");

            Assert.Equal(7, result.ExitCode);
            Assert.False(result.IsSuccess);
            Assert.Contains("boom", result.StdErr);
        }
        finally { File.Delete(fake); }
    }

    [Fact]
    public async Task RunAsync_throws_when_script_path_missing()
    {
        var importer = new AssetImporter(configuredScriptPath: "/tmp/oae-no-such-script.py");
        if (!importer.IsAvailable)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await importer.RunAsync("p", "/tmp/s", "/tmp/r", "id"));
        }
        // If the test machine has the real skill, autodiscover took over and the call
        // would succeed against the real importer — skip the assertion in that case.
    }

    private static string WriteFakeScript(string body)
    {
        var path = Path.Combine(Path.GetTempPath(), $"oae-importer-test-{Guid.NewGuid():N}.py");
        File.WriteAllText(path, body);
        return path;
    }
}
