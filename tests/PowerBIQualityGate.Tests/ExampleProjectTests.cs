using PowerBIQualityGate.Core.Discovery;
using PowerBIQualityGate.Core.Validation;

using Xunit;

namespace PowerBIQualityGate.Tests;

public sealed class ExampleProjectTests
{
    [Fact]
    public void ValidExample_DirectoryDiscovery_FindsPbip()
    {
        var directory = ExampleDirectory("valid-pbir-tmdl");

        var discovery = new PbipProjectDiscovery().Discover(directory);

        Assert.True(discovery.IsSuccess);
        Assert.NotNull(discovery.PbipPath);
        Assert.EndsWith("Example.pbip", discovery.PbipPath);
    }

    [Fact]
    public void ValidExample_StructureValidation_Passes()
    {
        var path = Path.Combine(ExampleDirectory("valid-pbir-tmdl"), "Example.pbip");

        var result = new ProjectStructureValidator().Validate(path);

        Assert.True(result.IsValid);
        Assert.Empty(result.Diagnostics);
        Assert.Equal(ReportDefinitionFormat.Pbir, result.ReportFormat);
        Assert.Equal(SemanticModelDefinitionFormat.Tmdl, result.SemanticModelFormat);
    }

    [Fact]
    public void InvalidExample_ReportsPbi002()
    {
        var path = Path.Combine(ExampleDirectory("invalid-pbip-shortcut"), "Broken.pbip");

        var result = new ProjectStructureValidator().Validate(path);

        Assert.False(result.IsValid);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "PBI-002");
    }

    private static string ExampleDirectory(string name)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "PowerBIQualityGate.slnx")))
            {
                return Path.Combine(directory.FullName, "examples", name);
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not find solution root for example tests.");
    }
}
