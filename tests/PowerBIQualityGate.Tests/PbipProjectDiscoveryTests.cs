using PowerBIQualityGate.Core.Discovery;

using Xunit;

namespace PowerBIQualityGate.Tests;

public class PbipProjectDiscoveryTests
{
    private readonly PbipProjectDiscovery _discovery = new();

    [Fact]
    public void Discover_ExistingPbipFile_ReturnsFound()
    {
        WithTempDirectory(directory =>
        {
            var file = Path.Combine(directory, "Demo.pbip");
            File.WriteAllText(file, "{}");

            var result = _discovery.Discover(file);

            Assert.True(result.IsSuccess);
            Assert.Equal(DiscoveryStatus.Found, result.Status);
            Assert.Equal(file, result.PbipPath);
        });
    }

    [Fact]
    public void Discover_DirectoryWithOnePbip_ReturnsFound()
    {
        WithTempDirectory(directory =>
        {
            var file = Path.Combine(directory, "Demo.pbip");
            File.WriteAllText(file, "{}");

            var result = _discovery.Discover(directory);

            Assert.Equal(DiscoveryStatus.Found, result.Status);
            Assert.Equal(file, result.PbipPath);
        });
    }

    [Fact]
    public void Discover_EmptyDirectory_ReturnsNotFound()
    {
        WithTempDirectory(directory =>
        {
            var result = _discovery.Discover(directory);

            Assert.Equal(DiscoveryStatus.NotFound, result.Status);
        });
    }

    [Fact]
    public void Discover_MultiplePbipFiles_ReturnsMultipleProjects()
    {
        WithTempDirectory(directory =>
        {
            File.WriteAllText(Path.Combine(directory, "A.pbip"), "{}");
            File.WriteAllText(Path.Combine(directory, "B.pbip"), "{}");

            var result = _discovery.Discover(directory);

            Assert.Equal(
                DiscoveryStatus.MultipleProjects,
                result.Status);
        });
    }

    [Fact]
    public void Discover_NonexistentPath_ReturnsNotFound()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"pbiq-missing-{Guid.NewGuid():N}");

        var result = _discovery.Discover(path);

        Assert.Equal(DiscoveryStatus.NotFound, result.Status);
    }

    [Fact]
    public void Discover_EmptyPath_ReturnsInvalidPath()
    {
        var result = _discovery.Discover("");

        Assert.Equal(DiscoveryStatus.InvalidPath, result.Status);
    }

    private static void WithTempDirectory(Action<string> action)
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            $"pbiq-test-{Guid.NewGuid():N}");

        Directory.CreateDirectory(directory);

        try
        {
            action(directory);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
