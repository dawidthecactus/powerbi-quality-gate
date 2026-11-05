using PowerBIQualityGate.Core.Discovery;
using PowerBIQualityGate.Core.Inspection;
using PowerBIQualityGate.Core.Validation;

namespace PowerBIQualityGate.Tests;

public sealed class TmdlExampleInventoryTests
{
    [Fact]
    public void IncludedSyntheticExample_ProducesExpectedInventory()
    {
        var directory = FindExample();
        var discovery = new PbipProjectDiscovery().Discover(directory);
        Assert.True(discovery.IsSuccess);

        var validation = new ProjectStructureValidator().Validate(discovery.PbipPath!);
        Assert.True(validation.IsValid);
        Assert.Equal(SemanticModelDefinitionFormat.Tmdl, validation.SemanticModelFormat);
        var definition = Path.Combine(validation.SemanticModelDirectory!, "definition");
        var result = new TmdlInventoryReader().Read(definition);

        Assert.Equal(2, result.Tables.Count);
        Assert.Equal(5, result.Columns);
        Assert.Equal(2, result.Measures);
        Assert.Equal(1, result.Relationships);
        Assert.Equal(1, result.Hierarchies);
    }

    private static string FindExample()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "PowerBIQualityGate.slnx")))
            {
                return Path.Combine(directory.FullName, "examples", "valid-pbir-tmdl");
            }
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Solution root not found.");
    }
}
