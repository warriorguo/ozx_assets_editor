using OAE.Core.Resources;

namespace OAE.Tests;

public class PpuClassifierTests
{
    [Theory]
    [InlineData("Assets/Images/Room/Bridge/Bridge1.png", 100, PpuSeverity.Strict)]
    [InlineData("Assets/Images/Room/Door/Door1.png",       68, PpuSeverity.Ok)]
    [InlineData("Assets/Images/Characters/Arack/Walk.png", 128, PpuSeverity.Preferred)]
    [InlineData("Assets/Images/Characters/X.png",          68, PpuSeverity.Ok)]
    [InlineData("Assets/Images/UI/Buttons/Btn.png",        300, PpuSeverity.Ignored)]
    [InlineData("Assets/Images/_Tools/Debug.png",          50, PpuSeverity.Ignored)]
    [InlineData("Assets/Images/Materials/Noise.png",       100, PpuSeverity.Ignored)]
    [InlineData("Assets/Images/Weapons/Pistol.png",        100, PpuSeverity.Preferred)]
    [InlineData("Assets/Images/Effects/Hit.png",           68, PpuSeverity.Ok)]
    [InlineData("Assets/Images/Skills/Icon.png",           96, PpuSeverity.Preferred)]
    [InlineData("Assets/Images/Loot/Coin.png",             100, PpuSeverity.Preferred)]
    [InlineData("Assets/Images/Unknown/Foo.png",           100, PpuSeverity.Ignored)]
    public void Classify_returns_expected_severity(string path, int ppu, PpuSeverity expected)
    {
        var verdict = PpuClassifier.Classify(path, ppu);
        Assert.Equal(expected, verdict.Severity);
    }

    [Fact]
    public void Strict_mismatch_reason_mentions_master_and_actual()
    {
        var v = PpuClassifier.Classify("Assets/Images/Room/X.png", 100);
        Assert.Equal(PpuSeverity.Strict, v.Severity);
        Assert.Contains("68", v.Reason);
        Assert.Contains("100", v.Reason);
    }

    [Fact]
    public void Path_separators_are_normalised()
    {
        var v = PpuClassifier.Classify(@"Assets\Images\Room\Bridge\X.png", 100);
        Assert.Equal(PpuSeverity.Strict, v.Severity);
    }
}
