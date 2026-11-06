using System.Reflection;

using PowerBIQualityGate.Cli.Rendering;

using Spectre.Console.Testing;

namespace PowerBIQualityGate.Tests;

public class PbiqConsoleChromeVersionTests
{
    [Theory]
    [InlineData("STRUCTURE SCANNER")]
    [InlineData("CUSTOM MODE")]
    public void BrandUsesCliInformationalVersionWithoutCommitMetadata(string mode)
    {
        using var console = new TestConsole();
        var assembly = typeof(PbiqConsoleChrome).Assembly;
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? throw new InvalidOperationException("CLI informational version metadata is missing.");
        Assert.False(string.IsNullOrWhiteSpace(informational));

        var versionWithoutMetadata = informational.Split('+', 2)[0];
        PbiqConsoleChrome.WriteBrand(console, mode);

        Assert.Contains($"v{versionWithoutMetadata}", console.Output);
        Assert.Contains(mode, console.Output);
        if (informational.Contains('+'))
        {
            Assert.DoesNotContain($"v{informational}", console.Output);
        }
    }

    [Fact]
    public void PreReleaseBannerDoesNotLoseTheSuffix()
    {
        using var console = new TestConsole();
        var assembly = typeof(PbiqConsoleChrome).Assembly;
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? throw new InvalidOperationException("CLI informational version metadata is missing.");
        var expected = informational.Split('+', 2)[0];

        PbiqConsoleChrome.WriteBrand(console, "STRUCTURE SCANNER");

        Assert.Contains($"POWER BI QUALITY GATE  v{expected}", console.Output);
    }
}
